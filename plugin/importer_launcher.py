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

**Nothing is downloaded until the release says it supports this KiCad (#138).** Every release
publishes a ``kicad-compatibility.json`` naming the KiCad versions that build was built for, and
this file reads it before it spends 200 MB. Which KiCad this is comes from this file's own location:
the Plugin and Content Manager unpacks a package into ``<documents>/kicad/<version>/3rdparty/plugins/``,
and because that path is per KiCad version, the directory holding this file names the only KiCad
that will ever load it. A release that declares nothing, and a KiCad that cannot be worked out, are
both installed rather than refused - see ``_check_kicad_compatibility``.

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
import threading
import time
import urllib.error
import urllib.request
import zipfile

# The published apphost. Windows is the only platform that gets an extension; the name follows the
# assembly's, which is what `dotnet publish` writes.
#
# It has to name the same file as `--mainExe` in .github/workflows/release.yml, which is what stamps
# it into the package. Both were `UltraLibrarianImporter.UI` until the rename in #132, and both
# changed in it. Nothing was released under the old name, so no installed copy is looking for it.
EXE_NAME = "KiCadUltra.exe" if os.name == "nt" else "KiCadUltra"

# The Velopack package id, which is what every asset name is built from. Keep it in step with
# .github/workflows/release.yml, which passes it to `vpk pack --packId`.
#
# This id is permanent in a way the rest of the naming is not: Velopack matches an installed copy to
# a feed by it, so changing it later would cut every installed copy off from updates. It was chosen
# in #128, before the assembly and the namespaces caught up with it in #132, for exactly that
# reason - the project is no longer only an UltraLibrarian importer, EasyEDA / LCSC and Octopart are
# providers of their own, and the id had to say so from the first release rather than after one.
PACK_ID = "KiCadUltra"

# Where the releases live. GitHub *Packages* answers 401 to an anonymous request, so it cannot serve
# plugin users; GitHub *Releases* answers 200.
#
# Both are overridable so the bootstrap can be exercised against a local server without touching the
# network or this repository, but the override has to be https unless it is loopback. The checksum
# in the release is only worth something while the release is the project's: an override that could
# name any plain-http host would let whatever set it serve both the asset and a matching
# SHA256SUMS.txt, and what is downloaded is then executed with KiCad's API token in its environment.
REPOSITORY = os.environ.get("KICAD_ULTRA_REPOSITORY", "danielmeza/kicad-ultra")

# The release asset that carries a SHA-256 for every other asset, in `sha256sum` format. A release
# without it is refused rather than trusted: an unverified 450 MB download is not something to run.
CHECKSUM_ASSET = "SHA256SUMS.txt"

# The release asset that names the KiCad versions that build supports (#138). One file, written from
# kicad-compatibility.json at the root of the repository, which is also what the Plugin and Content
# Manager metadata's kicad_version comes from and what ships beside the application itself.
#
# Unlike the checksum above, a release without it is *not* refused. Every release cut before #138
# has none, and "this release does not say" has to mean "install it" or the plugin could never
# install anything published before the feature existed.
COMPATIBILITY_ASSET = "kicad-compatibility.json"

# The only manifest_version this file knows how to read. A newer one is treated as no manifest,
# which permits the install; guessing at a format is worse than not checking.
KNOWN_MANIFEST_VERSION = 1

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


def _api_base_url():
    """The releases API to ask, honouring KICAD_ULTRA_API only where it is safe to."""
    configured = os.environ.get("KICAD_ULTRA_API", "https://api.github.com").rstrip("/")
    host = configured.split("//", 1)[-1].split("/", 1)[0].split(":", 1)[0]
    if configured.startswith("https://") or host in ("127.0.0.1", "::1", "localhost"):
        return configured
    raise BootstrapError(
        "KICAD_ULTRA_API is set to {0}, which is neither https nor loopback. Refusing to fetch the "
        "importer over it.".format(configured))


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


def target_asset(os_name=None, platform_id=None, machine=None):
    """``(asset name, kind)`` for this platform, or ``None`` if no release is built for it.

    The names are Velopack's own, from ``DefaultName`` in its packaging code: on Linux the portable
    artifact is ``{packId}.AppImage`` for the default ``linux`` channel, and everywhere else it is
    ``{packId}-{channel}-Portable.zip``. release.yml packs exactly these four channels.

    The three arguments describe the machine, and default to this one. They exist so the tests can
    ask what a Windows or a macOS run would fetch without patching ``os.name`` process-wide, which
    is the only way a Linux runner can check those branches at all.
    """
    os_name = os.name if os_name is None else os_name
    platform_id = sys.platform if platform_id is None else platform_id
    machine = (platform.machine() if machine is None else machine).lower()
    # An allow-list, not a deny-list. Asking "is it ARM?" and treating everything else as x86-64
    # hands an i686, armv7l, riscv64 or s390x Linux user a 200 MB download and an exec format error
    # after it, which is a worse answer than saying there is no build for them.
    is_x64 = machine in ("x86_64", "amd64", "x64")
    is_arm64 = machine in ("arm64", "aarch64")

    if os_name == "nt":
        # Windows on ARM runs x64 binaries under emulation, so it gets the x64 build rather than
        # nothing. There is no win-arm64 channel to offer it instead.
        return (PACK_ID + "-win-Portable.zip", "zip") if is_x64 or is_arm64 else None
    if platform_id == "darwin":
        if is_arm64:
            return (PACK_ID + "-osx-arm64-Portable.zip", "zip")
        return (PACK_ID + "-osx-x64-Portable.zip", "zip") if is_x64 else None
    if platform_id.startswith("linux"):
        return (PACK_ID + ".AppImage", "appimage") if is_x64 else None
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
    url = "{0}/repos/{1}/releases/latest".format(_api_base_url(), REPOSITORY)
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


# ---------------------------------------------------------------------------------------------
# Which KiCad this is, and whether the release supports it
# ---------------------------------------------------------------------------------------------


def _version_tuple(text, missing):
    """``(major, minor, patch)`` from ``"10"``, ``"10.0"`` or ``"10.0.6"``.

    *missing* fills in every component the string does not carry: 0 for a lower bound and 999 for an
    upper one, which is how KiCad's own Plugin and Content Manager compares these numbers
    (``PLUGIN_CONTENT_MANAGER::PreparePackage`` in ``kicad/pcm/pcm.cpp``). It is what makes
    "tested up to 10.99" cover a KiCad that calls itself 10.99.0 instead of treating it as newer.

    Raises ``ValueError`` for anything that is not a version, which is how the path search below
    tells a version directory from any other directory name.
    """
    parts = text.strip().split(".")
    if not 1 <= len(parts) <= 3 or not all(part.isdigit() for part in parts):
        raise ValueError("{0!r} is not a major[.minor[.patch]] version".format(text))
    numbers = [int(part) for part in parts]
    return tuple(numbers + [missing] * (3 - len(numbers)))


def kicad_version_from_install_path(path=None):
    """The KiCad version whose plugin directory holds *path*, or ``None``.

    **This is the reliable source, and it is the one asked first.** The Plugin and Content Manager
    unpacks a package into ``<documents>/kicad/<major.minor>/3rdparty/plugins/<identifier>/``:
    ``PATHS::GetDefault3rdPartyPath()`` is ``getUserDocumentPath()`` - itself
    ``<documents>/kicad/<major.minor>`` - with ``3rdparty`` appended. That path is per KiCad
    version, so the directory this file sits in does not merely name the KiCad that installed the
    plugin; it names the only KiCad that will ever load it. It is also there before anything has
    talked to KiCad, which is what lets the check happen before the download.

    The search is for the ``3rdparty`` component rather than for a fixed depth, because
    ``KICAD_DOCUMENTS_HOME`` can move everything above it and a package may nest its own files.
    """
    parts = os.path.abspath(path or __file__).split(os.sep)
    for index in range(len(parts) - 1, 0, -1):
        if parts[index] != "3rdparty":
            continue
        try:
            _version_tuple(parts[index - 1], 0)
        except ValueError:
            continue
        return parts[index - 1]
    return None


def kicad_version_from_environment():
    """The KiCad version KiCad's own environment names, or ``None``.

    The fallback, not the first answer. KiCad promises an API plugin exactly two variables -
    ``KICAD_API_SOCKET`` and ``KICAD_API_TOKEN`` (``API_PLUGIN_MANAGER`` sets those and nothing
    else) - and neither carries a version. What does carry one is the versioned path variables KiCad
    sets into its own environment and a child inherits: ``KICAD10_3RD_PARTY`` and its siblings,
    whose value ends in ``kicad/<major.minor>/3rdparty``. They are user-editable under
    Preferences -> Paths and they are inherited rather than set for the plugin, which is why they
    are consulted only when the install path says nothing - a working tree, mostly.
    """
    for name, value in os.environ.items():
        if name.startswith("KICAD") and name.endswith("_3RD_PARTY") and value:
            found = kicad_version_from_install_path(value)
            if found is not None:
                return found
    return None


def detect_kicad_version():
    """``(version, where it came from)``. *version* is ``None`` when it cannot be worked out."""
    override = os.environ.get("KICAD_ULTRA_KICAD_VERSION")
    if override:
        return override, "KICAD_ULTRA_KICAD_VERSION"

    found = kicad_version_from_install_path()
    if found is not None:
        return found, "this plugin's install path"

    found = kicad_version_from_environment()
    if found is not None:
        return found, "KiCad's path variables in the environment"

    return None, "nowhere"


def _declared_compatibility(release):
    """What *release* says about KiCad as ``(minimum, tested_up_to, maximum)``, or ``None``.

    ``None`` for a release that publishes nothing, for one whose manifest this file is too old to
    read, and for one whose manifest could not be fetched or made sense of. Every one of those is
    "not declared", and the caller installs rather than refuses.
    """
    url = _asset_url(release, COMPATIBILITY_ASSET)
    if url is None:
        return None

    try:
        with _open(url) as response:
            declared = json.loads(response.read().decode("utf-8"))
        manifest_version = declared.get("manifest_version", KNOWN_MANIFEST_VERSION)
        if manifest_version > KNOWN_MANIFEST_VERSION:
            _say("release {0} declares {1} version {2}, which this plugin cannot read".format(
                release.get("tag_name", "?"), COMPATIBILITY_ASSET, manifest_version))
            return None

        kicad = declared["kicad"]
        minimum, tested = kicad["minimum"], kicad["tested_up_to"]
        maximum = kicad.get("maximum")
        # Parsed here so a malformed file is one message rather than a traceback further down.
        _version_tuple(minimum, 0)
        _version_tuple(tested, 999)
        if maximum is not None:
            _version_tuple(maximum, 999)
        return minimum, tested, maximum
    except (urllib.error.URLError, OSError, ValueError, KeyError, TypeError, AttributeError) as error:
        _say("{0} in release {1} could not be read ({2}); installing without the check".format(
            COMPATIBILITY_ASSET, release.get("tag_name", "?"), error))
        return None


def _check_kicad_compatibility(release):
    """Refuse a release that says it will not work with the KiCad this plugin belongs to.

    Before the download, which is the point: a refusal after 200 MB has arrived has cost the user
    everything the check was meant to save.

    **Two of the three ways this can end without an answer let the install through, deliberately.**
    A release that declares nothing is every release cut before #138, and refusing those would leave
    a plugin that installs nothing at all. A KiCad that cannot be worked out is a working tree, a
    package unpacked by hand, or a KiCad whose paths have been moved - none of which is evidence
    that the release is wrong for it, and all of which are cases where the application's own check
    still runs once it has started. Only what the release itself declares can refuse: KiCad older
    than its minimum, or newer than a maximum somebody set on purpose.
    """
    tag = release.get("tag_name", "?")
    declared = _declared_compatibility(release)
    if declared is None:
        _say("release {0} declares no KiCad versions, so there is nothing to check it against".format(tag))
        return

    minimum, tested, maximum = declared
    version, source = detect_kicad_version()
    if version is None:
        _say("which KiCad this plugin belongs to could not be worked out, so release {0} "
             "(KiCad {1} and up, tested to {2}) is installed unchecked".format(tag, minimum, tested))
        return

    try:
        running = _version_tuple(version, 0)
    except ValueError:
        _say("{0} does not look like a KiCad version, so release {1} is installed unchecked".format(version, tag))
        return

    if running < _version_tuple(minimum, 0):
        raise BootstrapError(
            "release {0} of the importer needs KiCad {1} or newer, and this is KiCad {2} (from {3}). "
            "Nothing was downloaded. Update KiCad, or install a plugin release that supports "
            "it.".format(tag, minimum, version, source))

    if maximum is not None and running > _version_tuple(maximum, 999):
        raise BootstrapError(
            "release {0} of the importer supports KiCad up to {1}, and this is KiCad {2} (from {3}). "
            "Nothing was downloaded.".format(tag, maximum, version, source))

    if running > _version_tuple(tested, 999):
        _say("release {0} was tested up to KiCad {1} and this is KiCad {2}; installing it anyway, "
             "because tested up to is not known broken".format(tag, tested, version))
        return

    _say("release {0} supports KiCad {1} and up, and this is KiCad {2}".format(tag, minimum, version))


def _download(url, destination, heartbeat=None):
    """Fetch *url* into *destination*, continuing a part-finished file if one is there.

    A partial download is left in place on failure precisely so the next run can continue it; the
    caller deletes it when what arrived does not match the published checksum.

    *heartbeat* is the lock file this download holds, if any. Its modification time is pushed
    forward as bytes arrive: the lock is judged abandoned by its age, and a 200 MB asset on a poor
    connection can easily take longer than LOCK_STALE_SECONDS, at which point another KiCad window
    would take the lock and both would write the same file.
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
                    if heartbeat:
                        try:
                            os.utime(heartbeat, None)
                        except OSError:
                            pass
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
            age = 0

        if age > LOCK_STALE_SECONDS:
            if not announced:
                _say("an abandoned download lock is being removed")
            try:
                os.remove(path)
            except OSError:
                # Read-only directory, or Windows with the file still open elsewhere. Falling
                # through to the sleep is the point: without it this spins at full speed for the
                # whole deadline, printing on every pass.
                pass
        elif not announced:
            _say("another KiCad window is already downloading the importer; waiting for it")

        announced = True
        # Every path through the loop sleeps, including the two that used to `continue` past it.
        time.sleep(2)


def _release_lock(path):
    """Remove the lock, but only while it is still ours.

    A download slow enough to pass LOCK_STALE_SECONDS lets another process treat this lock as
    abandoned and replace it with its own. Removing it blindly at the end would then let a third
    process in while the second is still downloading.
    """
    try:
        with open(path, encoding="ascii") as handle:
            owner = handle.read().strip()
    except OSError:
        return
    if owner == str(os.getpid()):
        try:
            os.remove(path)
        except OSError:
            pass


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

        # Before the asset is looked up, the checksum is fetched or a byte is downloaded (#138):
        # "this release does not support your KiCad" is the most useful thing that can be said
        # about it, and saying it after 200 MB has arrived saves nobody anything.
        _check_kicad_compatibility(release)

        url = _asset_url(release, asset_name)
        if url is None:
            raise BootstrapError(
                "release {0} has no asset named {1}, so there is nothing to install for this "
                "platform.".format(version, asset_name))

        expected = _expected_checksum(release, asset_name)

        # The part-finished download is named after the checksum it is expected to have, not after
        # the asset. Asset names carry no version - KiCadUltra.AppImage is the same string in every
        # release - so a run interrupted before a new release, resuming after it, would otherwise
        # append the new asset's bytes onto the old one's, fail the checksum and throw the whole
        # 200 MB away. A different release simply has a different name here, and anything left from
        # one is cleared out rather than kept for ever.
        archive = os.path.join(downloads, expected)
        for stale in os.listdir(downloads):
            if stale != expected:
                _say("discarding a part-finished download of a different release")
                try:
                    os.remove(os.path.join(downloads, stale))
                except OSError:
                    pass

        _say("installing {0} {1} into {2}".format(asset_name, version, root))
        _download(url, archive, heartbeat=lock)

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
        _release_lock(lock)


# ---------------------------------------------------------------------------------------------
# Starting it
# ---------------------------------------------------------------------------------------------


def _start(exe_path, wait):
    """Start the importer at *exe_path*, and wait for it if asked to."""
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


def _bootstrap_then_start(wait):
    """Fetch the importer and start it, turning every failure into a line the user can read."""
    try:
        return _start(bootstrap(), wait)
    except BootstrapError as error:
        _say(str(error))
        return 1
    except OSError as error:
        # The filesystem half of the bootstrap - makedirs, move, chmod, replace, the writes in
        # _extract_zip - raises plain OSError for a full disk, a read-only data directory or a
        # rename across filesystems. Every other failure in this file is phrased for the user, and a
        # traceback on KiCad's stderr is not; these get the same treatment.
        _say("the importer could not be installed in {0}: {1}".format(data_dir(), error))
        return 1


def launch_importer(wait=True):
    """Start the importer, fetching it first if this machine does not have it yet.

    Returns the child's exit code when *wait* is true, and 0 once it has been started otherwise.
    KiCad's IPC API runs this file as its own process and expects it to live as long as the action,
    so waiting is the right default. A caller running inside KiCad's own interpreter - a pcbnew
    ActionPlugin, say - should pass wait=False so it does not block the editor's UI thread.

    **wait=False never blocks, even on the first run.** `__init__.py`'s ActionPlugin calls this from
    KiCad's UI thread, and the first run now downloads about 200 MB: doing that inline would freeze
    the editor for minutes with no repaint, and for up to LOCK_WAIT_SECONDS if another window were
    already downloading. So when the importer still has to be fetched and the caller cannot wait,
    the fetch goes to a thread of its own and this returns at once. The thread is not a daemon: an
    interpreter that is shutting down should not abandon a half-written installation directory.
    """
    exe_path = find_importer()
    if exe_path is not None:
        return _start(exe_path, wait)

    if wait:
        return _bootstrap_then_start(wait=True)

    _say("the importer has to be downloaded first; this runs in the background and KiCad stays usable")
    threading.Thread(target=_bootstrap_then_start, args=(False,), name="kicad-ultra-bootstrap").start()
    return 0


if __name__ == "__main__":
    sys.exit(launch_importer())
