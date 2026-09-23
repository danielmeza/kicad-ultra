"""Integration tests for the plugin's bootstrapper, `plugin/importer_launcher.py` (#128).

They drive the real launcher against a local HTTP server that answers like GitHub's releases API
and asset store, and cover the four things the bootstrap has to get right:

* a first run downloads the asset for this platform, checks its SHA-256 and starts it;
* a second run starts it without touching the network at all;
* an asset that does not match the SHA-256 the release publishes is refused, deleted, and nothing
  is installed;
* a download cut off part-way is kept and continued by the next run rather than started again.

The last one is not hypothetical. `http.client.HTTPResponse.read(amt)` returns `b""` instead of
raising when the connection drops before Content-Length is satisfied, so a truncated download once
looked finished here, failed its checksum, and was deleted - which made it restart from zero every
time. The test caught it; `_download` now compares what it wrote against what was promised.

Standard library only, like the launcher itself. Nothing here touches the real home directory, the
network, or KiCad: every run gets its own temporary tree through `KICAD_ULTRA_HOME`, and the server
listens on 127.0.0.1 on a port the operating system picks.

Run them with `python3 -m unittest discover -s tests -v`.
"""

import hashlib
import http.server
import importlib.util
import json
import os
import stat
import sys
import tempfile
import threading
import time
import unittest
import zipfile

REPOSITORY_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LAUNCHER = os.path.join(REPOSITORY_ROOT, "plugin", "importer_launcher.py")

# Something small that is still large enough to be cut in half by the server, and that runs.
FAKE_APPLICATION = b"#!/bin/sh\necho 'fake importer started'\n" + b"#" * 262144 + b"\n"


class _Handler(http.server.BaseHTTPRequestHandler):
    """GitHub's two endpoints the launcher uses, with Range support and an optional short answer."""

    protocol_version = "HTTP/1.1"
    root = None
    truncate_at = 0
    origin = ""
    requests = None

    def log_message(self, fmt, *args):
        pass

    def do_GET(self):
        type(self).requests.append((self.path, self.headers.get("Range")))
        if self.path.endswith("/releases/latest"):
            self._release()
        elif self.path.startswith("/assets/"):
            self._asset(os.path.basename(self.path))
        else:
            self.send_error(404)

    def _release(self):
        assets = [
            {"name": name, "browser_download_url": "%s/assets/%s" % (type(self).origin, name)}
            for name in sorted(os.listdir(type(self).root))
        ]
        body = json.dumps({"tag_name": "v9.9.9", "assets": assets}).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _asset(self, name):
        path = os.path.join(type(self).root, name)
        if not os.path.isfile(path):
            self.send_error(404)
            return

        size = os.path.getsize(path)
        start = 0
        requested = self.headers.get("Range")
        if requested and requested.startswith("bytes="):
            start = int(requested[len("bytes="):].split("-")[0])
            if start >= size:
                self.send_response(416)
                self.send_header("Content-Range", "bytes */%d" % size)
                self.send_header("Content-Length", "0")
                self.end_headers()
                return
            self.send_response(206)
            self.send_header("Content-Range", "bytes %d-%d/%d" % (start, size - 1, size))
        else:
            self.send_response(200)

        remaining = size - start
        self.send_header("Content-Type", "application/octet-stream")
        self.send_header("Content-Length", str(remaining))
        self.end_headers()

        # A truncated answer promises the whole body and then stops, which is what a connection that
        # drops part-way through a download looks like to the client.
        wanted = min(type(self).truncate_at or remaining, remaining)
        with open(path, "rb") as source:
            source.seek(start)
            self.wfile.write(source.read(wanted))
        if wanted < remaining:
            self.close_connection = True


class _ReleaseServer:
    def __init__(self, root, truncate_at=0):
        self._server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), _Handler)
        self.origin = "http://127.0.0.1:%d" % self._server.server_port
        self.requests = []
        _Handler.root = root
        _Handler.truncate_at = truncate_at
        _Handler.origin = self.origin
        _Handler.requests = self.requests
        self._thread = threading.Thread(target=self._server.serve_forever, daemon=True)
        self._thread.start()

    def stop(self):
        # Idempotent: a test that stops the server itself is also registered for cleanup.
        if self._server is None:
            return
        self._server.shutdown()
        self._server.server_close()
        self._thread.join(timeout=10)
        self._server = None


class BootstrapTests(unittest.TestCase):
    """The bootstrap itself, which on Linux fetches the AppImage."""

    def setUp(self):
        self._workspace = tempfile.TemporaryDirectory(prefix="kicad-ultra-test-")
        self.addCleanup(self._workspace.cleanup)
        self.home = os.path.join(self._workspace.name, "home")
        self.release = os.path.join(self._workspace.name, "release")
        os.makedirs(self.release)
        self._saved_environment = dict(os.environ)
        self.addCleanup(self._restore_environment)

    def _restore_environment(self):
        os.environ.clear()
        os.environ.update(self._saved_environment)

    # -- helpers ------------------------------------------------------------------------------

    def _publish(self, payload=FAKE_APPLICATION, corrupt=False, checksums=True):
        """Fill the release directory the way release.yml fills a GitHub release."""
        asset = "KiCadUltra.AppImage"
        published = payload if not corrupt else payload.replace(b"fake", b"FAKE", 1)
        with open(os.path.join(self.release, asset), "wb") as handle:
            handle.write(published)
        if checksums:
            with open(os.path.join(self.release, "SHA256SUMS.txt"), "w", encoding="utf-8") as handle:
                handle.write("%s  %s\n" % (hashlib.sha256(payload).hexdigest(), asset))
        return asset

    def _launcher(self, origin):
        """The launcher module, loaded afresh against *origin*.

        Reloaded rather than patched, because the repository and the API base are read from the
        environment when the module is imported, and that is the path the tests should exercise.
        """
        os.environ["KICAD_ULTRA_HOME"] = os.path.join(self.home, "kicad-ultra")
        os.environ["KICAD_ULTRA_API"] = origin
        os.environ["KICAD_ULTRA_REPOSITORY"] = "danielmeza/kicad-ultra"
        specification = importlib.util.spec_from_file_location("importer_launcher_under_test", LAUNCHER)
        module = importlib.util.module_from_spec(specification)
        specification.loader.exec_module(module)
        return module

    def _serve(self, truncate_at=0):
        server = _ReleaseServer(self.release, truncate_at)
        self.addCleanup(server.stop)
        return server

    # -- the four scenarios -------------------------------------------------------------------

    def test_a_first_run_downloads_verifies_and_starts_the_application(self):
        self._publish()
        server = self._serve()
        launcher = self._launcher(server.origin)

        self.assertIsNone(launcher.find_importer(), "nothing should be installed yet")
        self.assertEqual(0, launcher.launch_importer(), "the fake application exits 0")

        installed = launcher.find_importer()
        self.assertIsNotNone(installed)
        self.assertTrue(os.access(installed, os.X_OK), "the AppImage has to be executable")
        with open(os.path.join(launcher.data_dir(), "install.json"), encoding="utf-8") as handle:
            self.assertEqual("v9.9.9", json.load(handle)["version"])
        self.assertEqual([], os.listdir(os.path.join(launcher.data_dir(), "downloads")),
                         "the archive is removed once it is installed")

    def test_a_second_run_starts_without_the_network(self):
        self._publish()
        server = self._serve()
        launcher = self._launcher(server.origin)
        self.assertEqual(0, launcher.launch_importer())

        # Nothing to ask: the server is gone, and the launcher must not need it.
        server.stop()
        asked = len(server.requests)
        self.assertEqual(0, launcher.launch_importer())
        self.assertEqual(asked, len(server.requests), "the second run must not reach the network")

    def test_an_asset_that_fails_its_checksum_is_refused(self):
        self._publish(corrupt=True)
        server = self._serve()
        launcher = self._launcher(server.origin)

        self.assertEqual(1, launcher.launch_importer(), "a refused download is a failed launch")
        self.assertIsNone(launcher.find_importer(), "nothing may be installed")
        self.assertEqual([], os.listdir(os.path.join(launcher.data_dir(), "downloads")),
                         "the file that failed its checksum is deleted, not left to be resumed")

    def test_a_download_that_is_cut_off_is_continued_by_the_next_run(self):
        asset = self._publish()
        cut = 65536
        server = self._serve(truncate_at=cut)
        launcher = self._launcher(server.origin)

        self.assertEqual(1, launcher.launch_importer(), "an incomplete download is not a launch")
        partial = os.path.join(launcher.data_dir(), "downloads", asset)
        self.assertTrue(os.path.exists(partial), "what arrived has to be kept")
        self.assertEqual(cut, os.path.getsize(partial))

        # The same release, served whole this time.
        server.stop()
        resumed = self._serve()
        launcher = self._launcher(resumed.origin)
        self.assertEqual(0, launcher.launch_importer())
        self.assertIsNotNone(launcher.find_importer())
        self.assertFalse(os.path.exists(partial), "the archive is removed once it is installed")
        self.assertTrue(
            any(ranged for path, ranged in resumed.requests if path.endswith(asset)),
            "the second attempt has to ask for the rest, not the whole file")

    # -- the lock two KiCad windows compete for -----------------------------------------------

    def test_a_lock_left_behind_by_a_killed_run_does_not_block_the_install(self):
        self._publish()
        server = self._serve()
        launcher = self._launcher(server.origin)

        lock = os.path.join(launcher.data_dir(), "bootstrap.lock")
        os.makedirs(launcher.data_dir(), exist_ok=True)
        with open(lock, "w", encoding="utf-8") as handle:
            handle.write("99999")
        stale = time.time() - launcher.LOCK_STALE_SECONDS - 60
        os.utime(lock, (stale, stale))

        self.assertEqual(0, launcher.launch_importer())
        self.assertIsNotNone(launcher.find_importer())
        self.assertFalse(os.path.exists(lock), "the lock is released again afterwards")

    def test_a_lock_another_run_still_holds_is_waited_for_and_then_given_up_on(self):
        self._publish()
        server = self._serve()
        launcher = self._launcher(server.origin)
        # A second or two, rather than the quarter of an hour a real run would wait.
        launcher.LOCK_WAIT_SECONDS = 1

        lock = os.path.join(launcher.data_dir(), "bootstrap.lock")
        os.makedirs(launcher.data_dir(), exist_ok=True)
        with open(lock, "w", encoding="utf-8") as handle:
            handle.write("99999")

        self.assertEqual(1, launcher.launch_importer(), "it gives up rather than hanging")
        self.assertIsNone(launcher.find_importer(), "and installs nothing behind the other run")
        self.assertTrue(os.path.exists(lock), "the other run's lock is left alone")

    # -- refusals -----------------------------------------------------------------------------

    def test_a_release_without_published_checksums_is_refused(self):
        self._publish(checksums=False)
        server = self._serve()
        launcher = self._launcher(server.origin)

        self.assertEqual(1, launcher.launch_importer())
        self.assertIsNone(launcher.find_importer())

    def test_a_release_without_this_platform_s_asset_is_refused(self):
        self._publish()
        os.rename(os.path.join(self.release, "KiCadUltra.AppImage"),
                  os.path.join(self.release, "SomethingElse.AppImage"))
        server = self._serve()
        launcher = self._launcher(server.origin)

        self.assertEqual(1, launcher.launch_importer())
        self.assertIsNone(launcher.find_importer())


# The bootstrap fetches an AppImage on Linux and a portable .zip on Windows and macOS, and the fake
# application above is a shell script. CI runs Linux, which is the platform the plugin's own
# packaging is exercised on; the archive handling below is tested everywhere.
BootstrapTests = unittest.skipUnless(
    sys.platform.startswith("linux"), "the bootstrap's AppImage path is Linux-only")(BootstrapTests)


class ArchiveTests(unittest.TestCase):
    """`_extract_zip`, which the Windows and macOS bootstrap paths unpack with."""

    def setUp(self):
        self._workspace = tempfile.TemporaryDirectory(prefix="kicad-ultra-test-")
        self.addCleanup(self._workspace.cleanup)
        specification = importlib.util.spec_from_file_location("importer_launcher_archive", LAUNCHER)
        self.launcher = importlib.util.module_from_spec(specification)
        specification.loader.exec_module(self.launcher)

    def _archive(self, build):
        path = os.path.join(self._workspace.name, "bundle.zip")
        with zipfile.ZipFile(path, "w") as bundle:
            build(bundle)
        return path

    def test_the_execute_bit_survives(self):
        def build(bundle):
            entry = zipfile.ZipInfo("current/app")
            entry.external_attr = (stat.S_IFREG | 0o755) << 16
            bundle.writestr(entry, "#!/bin/sh\n")
            plain = zipfile.ZipInfo("current/data.txt")
            plain.external_attr = (stat.S_IFREG | 0o644) << 16
            bundle.writestr(plain, "data")

        target = os.path.join(self._workspace.name, "out")
        self.launcher._extract_zip(self._archive(build), target)

        self.assertTrue(os.access(os.path.join(target, "current", "app"), os.X_OK))
        self.assertFalse(os.access(os.path.join(target, "current", "data.txt"), os.X_OK))

    @unittest.skipIf(os.name == "nt", "symbolic links in a zip need no special handling on Windows")
    def test_a_symbolic_link_is_written_as_one(self):
        def build(bundle):
            bundle.writestr("real.txt", "content")
            link = zipfile.ZipInfo("link.txt")
            link.external_attr = (stat.S_IFLNK | 0o777) << 16
            bundle.writestr(link, "real.txt")

        target = os.path.join(self._workspace.name, "out")
        self.launcher._extract_zip(self._archive(build), target)

        self.assertTrue(os.path.islink(os.path.join(target, "link.txt")))
        self.assertEqual("real.txt", os.readlink(os.path.join(target, "link.txt")))

    def test_an_entry_that_escapes_the_target_is_refused(self):
        def build(bundle):
            bundle.writestr("../escaped.txt", "no")

        target = os.path.join(self._workspace.name, "out")
        os.makedirs(target)
        with self.assertRaises(self.launcher.BootstrapError):
            self.launcher._extract_zip(self._archive(build), target)
        self.assertFalse(os.path.exists(os.path.join(self._workspace.name, "escaped.txt")))


class AssetNameTests(unittest.TestCase):
    """The asset names have to be the ones `vpk pack` writes, or nothing installs anywhere."""

    def setUp(self):
        specification = importlib.util.spec_from_file_location("importer_launcher_names", LAUNCHER)
        self.launcher = importlib.util.module_from_spec(specification)
        specification.loader.exec_module(self.launcher)

    def test_this_platform_has_an_asset(self):
        target = self.launcher.target_asset()
        self.assertIsNotNone(target, "every platform CI runs on is released for")
        name, kind = target
        self.assertTrue(name.startswith(self.launcher.PACK_ID))
        self.assertIn(kind, ("appimage", "zip"))

    def test_the_names_follow_velopack_s_own_scheme(self):
        # Velopack's DefaultName: "{packId}.AppImage" for the default linux channel, and
        # "{packId}-{channel}-Portable.zip" everywhere else. release.yml packs these four channels.
        expected = {
            "KiCadUltra.AppImage",
            "KiCadUltra-win-Portable.zip",
            "KiCadUltra-osx-x64-Portable.zip",
            "KiCadUltra-osx-arm64-Portable.zip",
        }
        self.assertIn(self.launcher.target_asset()[0], expected)


if __name__ == "__main__":
    unittest.main()
