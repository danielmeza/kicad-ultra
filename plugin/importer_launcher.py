"""Entry point KiCad runs for the "Import from UltraLibrarian" action.

KiCad loads this file through the IPC API plugin system and runs it as its own process. All this
script does is start the .NET importer - and, the first time, fetch it.

**Why it is fetched rather than shipped (#128).** A self-contained build of the importer is 453 MB,
341 MB of which is CEF, the browser the Web Browser tab runs inside. That cannot go inside a Plugin
and Content Manager package, so the package carries only this Python side and the application is
published as a `Velopack <https://velopack.io/>`_ release on this repository's GitHub Releases. The
first run downloads the one asset for this platform, checks it against a SHA-256 published in the
same release, unpacks it, and starts it. Every later run starts it straight away, offline, and the
application updates itself from then on.

Only the standard library is used, on purpose: KiCad installs ``requirements.txt`` into the
interpreter that loads this plugin, and nothing may be added to it.

**The environment matters more than anything else here.** KiCad exports the API socket path, the API
token and the open project's directory into this process's environment, and KiCadSharp on the .NET
side reads them from there - there is no other channel. Passing ``os.environ`` through to the child
is what makes the importer able to talk to KiCad at all.
"""

import hashlib
import http.client
import json
import os
import platform
import shutil
import stat
import subprocess
import sys
import time
import urllib.error
import urllib.request
import zipfile

# The published apphost. Windows is the only platform that gets an extension; the name follows the
# assembly's, which is what `dotnet publish` writes.
#
# It changes with the rename in #132, together with `--mainExe` in .github/workflows/release.yml -
# the two have to name the same file, and the rename tool rewrites both because both are plain
# strings. The one visible consequence is on Windows and macOS, where this name is what is looked
# for inside an already-unpacked application: an installed copy from before the rename stops being
# found and is downloaded once more. Linux is unaffected, because there the AppImage is named after
# PACK_ID rather than the assembly.
EXE_NAME = "UltraLibrarianImporter.UI.exe" if os.name == "nt" else "UltraLibrarianImporter.UI"

# The Velopack package id, which is what every asset name is built from. Keep it in step with
# .github/workflows/release.yml, which passes it to `vpk pack --packId`.
#
# KiCadUltra rather than UltraLibrarianImporter because this id is permanent in a way the rest of
# the naming is not: Velopack matches an installed copy to a feed by it, so changing it later would
# cut every installed copy off from updates. The project is no longer only an UltraLibrarian
# importer - EasyEDA / LCSC and Octopart are providers of their own - and the id says so from the
# first release rather than after one.
PACK_ID = "KiCadUltra"

# Where the releases live. GitHub *Packages* answers 401 to an anonymous request, so it cannot serve
# plugin users; GitHub *Releases* answers 200. Both are overridable so the bootstrap can be
# exercised against a local server without touching the network or this repository.
REPOSITORY = os.environ.get("KICAD_ULTRA_REPOSITORY", "danielmeza/kicad-ultra")
API_BASE_URL = os.environ.get("KICAD_ULTRA_API", "https://api.github.com").rstrip("/")

# The release asset that carries a SHA-256 for every other asset, in `sha256sum` format. A release
# without it is refused rather than trusted: an unverified 450 MB download is not something to run.
CHECKSUM_ASSET = "SHA256SUMS.txt"

# Honest, and the same identity the .NET side sends.
USER_AGENT = "kicad-ultra/1.0"

NETWORK_TIMEOUT_SECONDS = 60
DOWNLOAD_CHUNK_BYTES = 1024 * 1024

# How long to wait for another copy of this script that is already downloading, and after how long
# its lock is treated as abandoned (a KiCad that was killed mid-download leaves one behind).
LOCK_WAIT_SECONDS = 900
LOCK_STALE_SECONDS = 3600


def _say(message):
    """Report progress. stderr, never stdout: KiCad reads neither, but the importer's own `--mcp`
    mode speaks JSON-RPC over stdout and the habit is worth keeping in one direction only."""
    sys.stderr.write("UltraLibrarian importer: {0}\n".format(message))
    sys.stderr.flush()


class BootstrapError(Exception):
    """Something that stops the importer being installed, already phrased for the user."""


# ---------------------------------------------------------------------------------------------
# Where things live
# ---------------------------------------------------------------------------------------------


def data_dir():
    """The directory the application is installed into.

    Deliberately **not** inside the plugin directory. The Plugin and Content Manager owns that
    directory and replaces its contents on every update of the plugin, which would throw away a
    450 MB download and the updates applied to it since; on a system-wide KiCad it is not even
    writable by the user. A per-user data directory survives both. The choice per platform is the
    ordinary one for application data that is not configuration:

    * Windows: ``%LOCALAPPDATA%``
    * macOS: ``~/Library/Application Support``
    * everything else: ``$XDG_DATA_HOME``, else ``~/.local/share``
    """
    override = os.environ.get("KICAD_ULTRA_HOME")
    if override:
        return os.path.abspath(override)

    if os.name == "nt":
        base = os.environ.get("LOCALAPPDATA") or os.path.join(os.path.expanduser("~"), "AppData", "Local")
    elif sys.platform == "darwin":
        base = os.path.join(os.path.expanduser("~"), "Library", "Application Support")
    else:
        base = os.environ.get("XDG_DATA_HOME") or os.path.join(os.path.expanduser("~"), ".local", "share")

    return os.path.join(base, "kicad-ultra")


def _app_dir():
    return os.path.join(data_dir(), "app")


def _marker_path():
    return os.path.join(data_dir(), "install.json")


def target_asset():
    """``(asset name, kind)`` for this platform, or ``None`` if no release is built for it.

    The names are Velopack's own, from ``DefaultName`` in its packaging code: on Linux the portable
    artifact is ``{packId}.AppImage`` for the default ``linux`` channel, and everywhere else it is
    ``{packId}-{channel}-Portable.zip``. release.yml packs exactly these four channels.
    """
    machine = platform.machine().lower()
    is_arm = machine in ("arm64", "aarch64")

    if os.name == "nt":
        return (PACK_ID + "-win-Portable.zip", "zip") if not is_arm else None
    if sys.platform == "darwin":
        return (PACK_ID + ("-osx-arm64" if is_arm else "-osx-x64") + "-Portable.zip", "zip")
    if sys.platform.startswith("linux"):
        return (PACK_ID + ".AppImage", "appimage") if not is_arm else None
    return None


def _find_executable(root, kind):
    """The importer inside an unpacked application, or ``None``.

    Found rather than assumed, because each platform's layout is Velopack's, not ours: the Windows
    portable archive puts the application in ``current/`` beside the ``Update.exe`` that replaces it,
    and macOS puts it inside an ``.app`` bundle whose name follows the package title.
    """
    if kind == "appimage":
        candidate = os.path.join(root, PACK_ID + ".AppImage")
        return candidate if os.path.isfile(candidate) else None

    if os.name == "nt":
        candidate = os.path.join(root, "current", EXE_NAME)
        return candidate if os.path.isfile(candidate) else None

    if not os.path.isdir(root):
        return None
    for name in sorted(os.listdir(root)):
        if name.endswith(".app"):
            candidate = os.path.join(root, name, "Contents", "MacOS", EXE_NAME)
            if os.path.isfile(candidate):
                return candidate
    return None


def find_importer():
    """The importer executable this machine should run, or ``None`` if it has to be fetched.

    ``plugin/bin`` is looked at first and is the developer's override: publishing into it is how the
    application is run from a working tree, and it keeps working exactly as before. Nothing outside
    the plugin directory and the data directory above is ever searched - an earlier version of this
    file searched a development machine's absolute path, which an installed bundle would have
    reached.
    """
    local = os.path.join(os.path.dirname(os.path.abspath(__file__)), "bin", EXE_NAME)
    if os.path.isfile(local):
        return local

    target = target_asset()
    if target is None:
        return None
    return _find_executable(_app_dir(), target[1])


# ---------------------------------------------------------------------------------------------
# Fetching the application
# ---------------------------------------------------------------------------------------------


def _open(url, extra_headers=None):
    headers = {"User-Agent": USER_AGENT}
    headers.update(extra_headers or {})
    request = urllib.request.Request(url, headers=headers)
    return urllib.request.urlopen(request, timeout=NETWORK_TIMEOUT_SECONDS)


def _latest_release():
    url = "{0}/repos/{1}/releases/latest".format(API_BASE_URL, REPOSITORY)
    _say("asking {0} for the latest release".format(url))
    try:
        with _open(url, {"Accept": "application/vnd.github+json"}) as response:
            return json.loads(response.read().decode("utf-8"))
    except (urllib.error.URLError, OSError, ValueError) as error:
        raise BootstrapError(
            "could not read the latest release from {0} ({1}).\n"
            "The first run needs network access; afterwards the importer starts offline.".format(url, error))


def _asset_url(release, name):
    for asset in release.get("assets") or []:
        if asset.get("name") == name:
            url = asset.get("browser_download_url")
            if url:
                return url
    return None


def _expected_checksum(release, asset_name):
    """The SHA-256 the release publishes for *asset_name*, from its ``SHA256SUMS.txt``."""
    url = _asset_url(release, CHECKSUM_ASSET)
    if url is None:
        raise BootstrapError(
            "release {0} does not publish {1}, so the download cannot be verified. Refusing to "
            "install it.".format(release.get("tag_name", "?"), CHECKSUM_ASSET))

    try:
        with _open(url) as response:
            listing = response.read().decode("utf-8")
    except (urllib.error.URLError, OSError) as error:
        raise BootstrapError("could not read {0} ({1}).".format(CHECKSUM_ASSET, error))

    for line in listing.splitlines():
        # `sha256sum` writes "<hex>  <name>"; the second space is a "binary mode" marker in some
        # implementations, so split on whitespace rather than on a fixed offset.
        parts = line.split()
        if len(parts) >= 2 and os.path.basename(parts[-1].lstrip("*")) == asset_name:
            return parts[0].lower()

    raise BootstrapError(
        "{0} in release {1} has no line for {2}, so the download cannot be verified.".format(
            CHECKSUM_ASSET, release.get("tag_name", "?"), asset_name))


def _download(url, destination):
    """Fetch *url* into *destination*, continuing a part-finished file if one is there.

    A partial download is left in place on failure precisely so the next run can continue it; the
    caller deletes it when what arrived does not match the published checksum.
    """
    have = os.path.getsize(destination) if os.path.exists(destination) else 0
    headers = {"Range": "bytes={0}-".format(have)} if have else {}
    if have:
        _say("continuing an earlier download at {0} bytes".format(have))

    try:
        try:
            response = _open(url, headers)
        except urllib.error.HTTPError as error:
            # 416 means the range starts at or past the end of the file: the download finished, and
            # what stopped the previous run was the check or the unpacking after it. There is
            # nothing left to fetch, and the checksum the caller takes next decides whether what is
            # on disk is usable.
            if error.code == 416 and have:
                _say("the download was already complete")
                return
            raise

        with response:
            # A server that ignores Range answers 200 with the whole file. Start again rather than
            # append to it, which would produce a longer file that fails its checksum for ever.
            resuming = response.status == 206
            if have and not resuming:
                _say("the server would not continue the download; starting it again")
                have = 0
            total = response.headers.get("Content-Length")
            total = (int(total) + have) if total is not None else None

            with open(destination, "ab" if resuming else "wb") as sink:
                if not resuming:
                    sink.truncate(0)
                written = have
                announced = -1
                while True:
                    chunk = response.read(DOWNLOAD_CHUNK_BYTES)
                    if not chunk:
                        break
                    sink.write(chunk)
                    written += len(chunk)
                    if total:
                        percent = (written * 100) // total
                        if percent >= announced + 10:
                            announced = percent - (percent % 10)
                            _say("downloaded {0}% of {1}".format(announced, _human(total)))
    except (urllib.error.URLError, http.client.HTTPException, OSError) as error:
        # HTTPException is here for the stream that ends in a protocol error rather than silently.
        raise BootstrapError(
            "the download failed ({0}). What arrived is kept, and the next run continues it.".format(error))

    # A connection that drops mid-body is not an error the standard library reports. `read(amt)`
    # returns b"" and closes the connection when fewer bytes arrive than Content-Length promised -
    # http.client says so in a comment, and declines to raise IncompleteRead there "because it might
    # break compatibility". Without this check a cut-off download looks finished, fails its checksum,
    # and is deleted, so the next run starts from nothing instead of continuing what is already here.
    if total is not None and written < total:
        raise BootstrapError(
            "the download stopped after {0} of {1} bytes. What arrived is kept, and the next run "
            "continues it.".format(written, total))


def _human(size):
    """A byte count a person can read, so the progress line means something for any asset size."""
    for unit in ("bytes", "KiB", "MiB", "GiB"):
        if size < 1024 or unit == "GiB":
            return "{0:.0f} {1}".format(size, unit)
        size /= 1024.0


def _sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as source:
        for chunk in iter(lambda: source.read(DOWNLOAD_CHUNK_BYTES), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _safe_join(root, name):
    """*name* resolved inside *root*, or an error. Archive entries are not to be trusted."""
    target = os.path.realpath(os.path.join(root, name))
    if target != root and not target.startswith(root + os.sep):
        raise BootstrapError("the archive contains an entry that would be written outside the "
                             "installation directory ({0}). Refusing to unpack it.".format(name))
    return target


def _extract_zip(archive, destination):
    """Unpack *archive*, keeping the execute bit and symbolic links.

    ``ZipFile.extractall`` drops both: it writes symbolic links as ordinary files holding their
    target's path, and gives every file the default mode, which leaves the importer non-executable
    on macOS. The mode is in the high half of ``external_attr`` for archives written on a Unix host,
    which is what ``vpk pack`` produces for the macOS bundle.
    """
    root = os.path.realpath(destination)
    with zipfile.ZipFile(archive) as bundle:
        for entry in bundle.infolist():
            target = _safe_join(root, entry.filename)
            mode = entry.external_attr >> 16

            if entry.is_dir():
                os.makedirs(target, exist_ok=True)
                continue

            parent = os.path.dirname(target)
            if parent:
                os.makedirs(parent, exist_ok=True)

            if stat.S_ISLNK(mode):
                link = bundle.read(entry).decode("utf-8")
                if os.path.lexists(target):
                    os.remove(target)
                os.symlink(link, target)
                continue

            with bundle.open(entry) as source, open(target, "wb") as sink:
                shutil.copyfileobj(source, sink, DOWNLOAD_CHUNK_BYTES)
            if mode & 0o111:
                os.chmod(target, os.stat(target).st_mode | 0o111)


def _install(archive, kind, staging):
    """Put the contents of *archive* into the empty directory *staging*."""
    if kind == "appimage":
        # Nothing to unpack: the AppImage *is* the application, and it has to stay one file at a
        # stable path because that is the file Velopack replaces when it updates itself.
        target = os.path.join(staging, PACK_ID + ".AppImage")
        shutil.move(archive, target)
        os.chmod(target, os.stat(target).st_mode | 0o111)
        return
    _extract_zip(archive, staging)


def _replace_app_dir(staging):
    """Swap *staging* in as the installed application, keeping the old copy until it is."""
    app = _app_dir()
    previous = app + ".previous"

    if os.path.exists(previous):
        shutil.rmtree(previous, ignore_errors=True)
    if os.path.exists(app):
        os.replace(app, previous)
    os.replace(staging, app)
    if os.path.exists(previous):
        shutil.rmtree(previous, ignore_errors=True)


def _acquire_lock(path):
    """A lock nobody else holds, so two KiCad windows do not download the same 450 MB twice."""
    deadline = time.time() + LOCK_WAIT_SECONDS
    announced = False
    while True:
        try:
            handle = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
        except FileExistsError:
            pass
        else:
            try:
                os.write(handle, str(os.getpid()).encode("ascii"))
            finally:
                os.close(handle)
            return

        # Checked here, before any of the paths below that loop round again, so that every way
        # through this loop is bounded by the deadline rather than only the one that sleeps.
        if time.time() > deadline:
            raise BootstrapError("another copy of this plugin has been downloading the importer "
                                 "for too long. Start KiCad again to retry.")

        try:
            age = time.time() - os.path.getmtime(path)
        except OSError:
            # The lock was released between the two calls. Go round and take it.
            continue

        if age > LOCK_STALE_SECONDS:
            _say("an abandoned download lock is being removed")
            try:
                os.remove(path)
            except OSError:
                pass
            continue

        if not announced:
            _say("another KiCad window is already downloading the importer; waiting for it")
            announced = True
        time.sleep(2)


def bootstrap():
    """Download, verify and unpack the importer. Returns the path to its executable.

    Safe to run twice: an installation another process finished while this one waited is used as it
    stands, and a download this one had to abandon is continued rather than restarted.
    """
    target = target_asset()
    if target is None:
        raise BootstrapError(
            "there is no release of the importer for {0} {1}. It is built for 64-bit Windows, Linux "
            "and macOS (Intel and Apple silicon).".format(platform.system(), platform.machine()))
    asset_name, kind = target

    root = data_dir()
    downloads = os.path.join(root, "downloads")
    os.makedirs(downloads, exist_ok=True)

    lock = os.path.join(root, "bootstrap.lock")
    _acquire_lock(lock)
    try:
        installed = _find_executable(_app_dir(), kind)
        if installed is not None:
            return installed

        release = _latest_release()
        version = release.get("tag_name") or "?"
        url = _asset_url(release, asset_name)
        if url is None:
            raise BootstrapError(
                "release {0} has no asset named {1}, so there is nothing to install for this "
                "platform.".format(version, asset_name))

        expected = _expected_checksum(release, asset_name)

        archive = os.path.join(downloads, asset_name)
        _say("installing {0} {1} into {2}".format(asset_name, version, root))
        _download(url, archive)

        _say("checking SHA-256")
        actual = _sha256(archive)
        if actual != expected:
            os.remove(archive)
            raise BootstrapError(
                "{0} does not match the SHA-256 published in release {1} (expected {2}, got {3}). "
                "The file has been deleted; nothing was installed.".format(asset_name, version, expected, actual))

        staging = _app_dir() + ".incoming"
        shutil.rmtree(staging, ignore_errors=True)
        os.makedirs(staging)
        _install(archive, kind, staging)

        executable = _find_executable(staging, kind)
        if executable is None:
            shutil.rmtree(staging, ignore_errors=True)
            raise BootstrapError("{0} does not contain {1}. Nothing was installed.".format(asset_name, EXE_NAME))

        _replace_app_dir(staging)
        if os.path.exists(archive):
            os.remove(archive)

        with open(_marker_path(), "w", encoding="utf-8") as marker:
            json.dump({"version": version, "asset": asset_name, "sha256": expected}, marker, indent=2)

        # Looked up again rather than derived from the staging path, because the swap is what makes
        # the installation real. Never None in practice - it was found in the staging directory a
        # few lines up - but the caller starts whatever comes back, so it says so rather than
        # handing back nothing and failing later with a traceback.
        installed = _find_executable(_app_dir(), kind)
        if installed is None:
            raise BootstrapError("{0} was unpacked but {1} is not in {2}.".format(asset_name, EXE_NAME, _app_dir()))

        _say("installed {0}; it updates itself from now on".format(version))
        return installed
    finally:
        try:
            os.remove(lock)
        except OSError:
            pass


# ---------------------------------------------------------------------------------------------
# Starting it
# ---------------------------------------------------------------------------------------------


def launch_importer(wait=True):
    """Start the importer, fetching it first if this machine does not have it yet.

    Returns the child's exit code when *wait* is true, and 0 once it has been started otherwise.
    KiCad's IPC API runs this file as its own process and expects it to live as long as the action,
    so waiting is the right default. A caller running inside KiCad's own interpreter - a pcbnew
    ActionPlugin, say - should pass wait=False so it does not block the editor's UI thread.
    """
    exe_path = find_importer()
    if exe_path is None:
        try:
            exe_path = bootstrap()
        except BootstrapError as error:
            _say(str(error))
            return 1

    if not os.access(exe_path, os.X_OK):
        # Copying the published output around (or unzipping it) commonly drops the execute bit.
        try:
            os.chmod(exe_path, os.stat(exe_path).st_mode | 0o111)
        except OSError as error:
            _say("{0} is not executable and chmod failed: {1}".format(exe_path, error))
            return 1

    # env=os.environ is the whole point: it carries KiCad's API socket, token and project directory.
    # cwd is the executable's own directory so the app finds nlog.config and the CEF resources next
    # to it rather than relative to wherever KiCad happened to be started from. For the Linux
    # AppImage that directory holds the image itself and the app reads its own files from the mount
    # it is running out of, which it locates from AppContext.BaseDirectory.
    process = subprocess.Popen([exe_path], env=os.environ, cwd=os.path.dirname(exe_path))
    return process.wait() if wait else 0


if __name__ == "__main__":
    sys.exit(launch_importer())
