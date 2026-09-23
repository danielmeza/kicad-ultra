# KiCad Ultra

A KiCad plugin that searches component sources and imports the schematic symbol, footprint and 3D
model for a part straight into the open project.

This is the README that ships inside the plugin package, so it describes the installed plugin. The
repository, its source and its development notes are at
[danielmeza/kicad-ultra](https://github.com/danielmeza/kicad-ultra).

## What is in this package, and what is not

The package holds the Python launcher, the icons and the metadata - a few kilobytes. The importer
itself is a desktop application of about 200 MB compressed, most of it the embedded browser the Web
Browser tab runs inside, which is far too large for a Plugin and Content Manager package.

So the first time the plugin is used it downloads the application for this platform from the
project's [GitHub releases](https://github.com/danielmeza/kicad-ultra/releases), checks it against a
SHA-256 published in the same release, and unpacks it into a per-user data directory:

| | |
|---|---|
| Windows | `%LOCALAPPDATA%\kicad-ultra` |
| macOS | `~/Library/Application Support/kicad-ultra` |
| Linux | `$XDG_DATA_HOME/kicad-ultra`, else `~/.local/share/kicad-ultra` |

It is written there rather than beside the plugin because the Plugin and Content Manager replaces
the plugin's own directory whenever the plugin is updated, and on a system-wide KiCad that directory
is not writable by the user at all.

Only the first run needs the network. Every later run starts the application straight away, and the
application keeps itself up to date from the same releases in the background: an update is
downloaded while you work and installed the next time the importer starts, so an import is never
interrupted.

The launcher writes what it is doing to stderr. In KiCad that goes to the log the IPC API plugin
system captures; started from a terminal it goes to the terminal.

## Installation

### Prerequisites

- KiCad 9.0 or later.
- 64-bit Windows, macOS (Intel or Apple silicon) or Linux.
- About 600 MB of free space in the data directory above, and a network connection for the first
  run.

Nothing has to be installed into KiCad's Python: `requirements.txt` is empty on purpose, and the
launcher uses only the standard library.

### Plugin and Content Manager

1. In KiCad, open **Plugin and Content Manager**.
2. Find **KiCad UltraLibrarian Importer** and click **Install**.
3. Restart KiCad.

### By hand

1. Download the plugin archive from the
   [releases page](https://github.com/danielmeza/kicad-ultra/releases) - the small
   `kicad-ultralibrarian-importer-<version>.zip`, not one of the platform packages.
2. Extract it into KiCad's plugin directory:
   - Windows: `%APPDATA%\kicad\9.0\plugins\`
   - macOS: `~/Library/Application Support/kicad/9.0/plugins/`
   - Linux: `~/.local/share/kicad/9.0/plugins/`
3. Restart KiCad.

## Usage

1. In KiCad, run **Import from UltraLibrarian** from the toolbar or the plugin menu. The first run
   downloads the application, which takes a few minutes.
2. **Part Explorer** searches the configured sources. Each result imports by the route it has: a
   part with an LCSC code through the `easyeda2kicad` command-line tool, if you have installed it,
   and a part with a manufacturer part number through **Find on Ultra Librarian**, which opens the
   part in the **Web Browser** tab.
3. Download the part in the Web Browser tab; the plugin picks up the download and offers to import
   it.
4. Choose what to import - symbol, footprint, 3D model - and the libraries are written and
   registered in KiCad's library tables.

KiCad does not reload its library tables by itself, so reopen the project or restart KiCad to see a
newly registered library.

`easyeda2kicad` is not shipped with this plugin and never will be: it is AGPL-3.0, and the plugin
only ever runs it as a separate program. Install it yourself if you want that import route.

## Configuration

**Settings** in the main window covers the download directory, the libraries to import into, which
of KiCad's library tables a new library is registered in, and API credentials. Credentials are kept
in the operating system's credential store - Windows Credential Manager, the macOS Keychain, or
libsecret on Linux - and never in a configuration file.

## Troubleshooting

**The plugin does not appear in KiCad.** Restart KiCad after installing, and check that KiCad's IPC
API is enabled in *Preferences → Plugins*.

**The first run fails to download.** The message on stderr names what went wrong. A part-finished
download is kept and continued on the next run, so starting the plugin again is the first thing to
try. A download whose SHA-256 does not match the one published in the release is deleted and
refused - it is never run.

**Nothing happens when the plugin starts, on Linux.** The application is an AppImage, which needs
FUSE to mount itself. If your system has no FUSE, set `APPIMAGE_EXTRACT_AND_RUN=1` in the
environment KiCad runs in; the application then unpacks itself on each start instead.

**Downloads are not picked up.** Check the download directory in Settings, and that the browser tab
actually finished the download.

**An import fails.** The application's log says why. It is in
`%APPDATA%\KiCadUltra\logs` on Windows, `~/Library/Application
Support/KiCadUltra/logs` on macOS and `$XDG_CONFIG_HOME/KiCadUltra/logs`
(usually `~/.config/...`) on Linux.

### Reporting problems

Please open an issue at
[danielmeza/kicad-ultra/issues](https://github.com/danielmeza/kicad-ultra/issues) with:

1. what happened and what you expected;
2. how to reproduce it;
3. the message, if there was one, and the relevant part of the log;
4. your KiCad version, your operating system, and the plugin version.

## License

MIT - see the LICENSE file beside this one.

## Acknowledgements

- [KiCad](https://www.kicad.org/), for the EDA suite this plugs into.
- [UltraLibrarian](https://www.ultralibrarian.com/), for the component library this started with.
- [Velopack](https://velopack.io/), which packages and updates the application.
