"""Entry point KiCad runs for the "Import from UltraLibrarian" action.

KiCad loads this file through the IPC API plugin system and runs it as its own process. All this
script does is start the .NET importer that lives in bin/ next to it and wait for it.

The environment matters more than anything else here. KiCad exports the API socket path, the API
token and the open project's directory into this process's environment, and KiCadSharp on the .NET
side reads them from there - there is no other channel. Passing os.environ through to the child is
therefore what makes the importer able to talk to KiCad at all.
"""

import os
import subprocess
import sys

# The published apphost. Windows is the only platform that gets an extension; the assembly name is
# UltraLibrarianImporter.UI, which is what `dotnet publish` writes.
EXE_NAME = "UltraLibrarianImporter.UI.exe" if os.name == "nt" else "UltraLibrarianImporter.UI"


def find_importer():
    """Return the path to the importer executable, or None if it has not been published."""
    bin_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "bin")
    exe_path = os.path.join(bin_dir, EXE_NAME)
    return exe_path if os.path.isfile(exe_path) else None


def launch_importer(wait=True):
    """Start the importer.

    Returns the child's exit code when *wait* is true, and 0 once it has been started otherwise.
    KiCad's IPC API runs this file as its own process and expects it to live as long as the action,
    so waiting is the right default. A caller running inside KiCad's own interpreter - a pcbnew
    ActionPlugin, say - should pass wait=False so it does not block the editor's UI thread.
    """
    exe_path = find_importer()
    if exe_path is None:
        sys.stderr.write(
            "UltraLibrarian importer: no executable in {0}.\n"
            "The plugin bundle ships only the Python side; the .NET application has to be published "
            "into that directory first:\n"
            "    dotnet publish src/importer/UltraLibrarianImporter.UI/UltraLibrarianImporter.UI.csproj \\\n"
            "        -c Release -r <rid> --self-contained true -o plugin/bin\n".format(
                os.path.join(os.path.dirname(os.path.abspath(__file__)), "bin")
            )
        )
        return 1

    if not os.access(exe_path, os.X_OK):
        # Copying the published output around (or unzipping it) commonly drops the execute bit.
        try:
            os.chmod(exe_path, os.stat(exe_path).st_mode | 0o111)
        except OSError as error:
            sys.stderr.write(
                "UltraLibrarian importer: {0} is not executable and chmod failed: {1}\n".format(
                    exe_path, error
                )
            )
            return 1

    # env=os.environ is the whole point: it carries KiCad's API socket, token and project directory.
    # cwd is set to the executable's directory so the app finds nlog.config and the CEF resources
    # next to it rather than relative to wherever KiCad happened to be started from.
    process = subprocess.Popen([exe_path], env=os.environ, cwd=os.path.dirname(exe_path))
    return process.wait() if wait else 0


if __name__ == "__main__":
    sys.exit(launch_importer())
