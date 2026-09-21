# kicad-ultra

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A KiCad plugin that pulls symbols, footprints and 3D models from
[UltraLibrarian](https://www.ultralibrarian.com/) straight into your project — no download folder,
no manual library juggling.

Browse UltraLibrarian from inside KiCad, pick a part, and it lands in your symbol and footprint
libraries with the 3D model attached.

## Install

Copy `plugin/` into your KiCad plugin directory:

| | |
|---|---|
| Linux | `~/.local/share/kicad/9.0/plugins/` |
| macOS | `~/Library/Application Support/kicad/9.0/plugins/` |
| Windows | `%APPDATA%\kicad\9.0\plugins\` |

```bash
pip install -r plugin/requirements.txt
```

Then enable the IPC API — **Preferences → Plugins → Enable IPC API** — and restart KiCad. The
importer appears as **Import from UltraLibrarian** in the schematic editor, the PCB editor and the
project manager.

Needs KiCad 9.0 or newer, Python 3.8+ and wxPython.

## Using it

1. Open **Import from UltraLibrarian**. It opens UltraLibrarian in a browser window.
2. Find your part and download it. The plugin picks the download up on its own.
3. Choose what you want — symbol, footprint, 3D model.
4. **Import.** It goes into your project's libraries.

## EasyEDA / LCSC parts (optional: easyeda2kicad)

Search results from **EasyEDA / LCSC** that carry an LCSC part number (`C2040`, …) have an **Import**
button. It converts the part with [easyeda2kicad](https://github.com/uPesy/easyeda2kicad.py) and adds the
symbol, footprint and 3D model to your KiCad libraries.

**easyeda2kicad is an optional third-party tool, licensed under AGPL-3.0. It is not part of this project**,
and this project does not bundle, vendor, install or modify it. You install it yourself; kicad-ultra only
runs it as a separate program, through its documented command-line flags, and reads back the KiCad library
files it writes. Without it, everything else works and the Import button stays disabled.

Install it (it needs Python 3.9 or newer):

| KiCad installed as | Command, in a terminal |
|---|---|
| a native package (Windows, macOS, Linux) | `pipx install easyeda2kicad` |
| the **Flatpak** (Linux) | `flatpak run --command=pip3 org.kicad.KiCad install --user easyeda2kicad` |

Under the Flatpak, KiCad — and this importer, which KiCad starts — run inside KiCad's sandbox, so a copy
installed with `pip` or `pipx` on the host is invisible to them. The command above installs it inside the
sandbox, where KiCad's own `pip3` puts user packages.

The importer looks for it in this order, and **Settings → Component Providers → easyeda2kicad** shows what
it found:

1. the path set there — `easyeda2kicad` itself, or a Python interpreter that has it installed;
2. `easyeda2kicad` on `PATH`;
3. `python -m easyeda2kicad` with KiCad's Python interpreter (`api.interpreter_path` in `kicad_common.json`).

The part is converted straight into the same library the other imports use (`<project>_EasyEDA` in the
project folder, or `EasyEDA` next to KiCad's global tables), then registered in a library table. By
default that is the project's table when there is a project and KiCad's global table when there is not;
**Settings → General → Register imported libraries in** can pin it to either one.
Re-importing a part replaces it rather than adding a second copy. When an import fails, times out or is
cancelled, nothing is registered, the symbol library is restored and the files the run created are removed;
the log names anything it could not undo.

easyeda2kicad downloads the part's data from EasyEDA itself; that traffic comes from the tool you
installed, not from kicad-ultra.

## How it works

Two pieces:

- **`plugin/`** — a small Python launcher. KiCad loads this; it starts the UI and talks to KiCad over
  the IPC API.
- **`src/importer/`** — an [Avalonia](https://avaloniaui.net/) application that does the browsing,
  downloading and importing.

The KiCad file handling underneath comes from two libraries that live in their own repositories:
[SExpressions](https://github.com/danielmeza/sexpressions) for lossless reads and writes, and
[KiCadSharp](https://github.com/danielmeza/kicad-sharp) for the IPC client and the library formats.
They're consumed from nuget.org like any other dependency.

## Building

```bash
dotnet build UltraLibrarianImporter.sln -c Release
dotnet test  UltraLibrarianImporter.sln -c Release
```

No submodules, nothing to pack first.

### Against local library checkouts

If you're changing SExpressions or KiCadSharp at the same time:

```bash
scripts/use-local-libs.sh ../sexpressions ../kicad-sharp
dotnet build UltraLibrarianImporter.sln -c Release \
    -p:SExpressionsVersion=0.1.0-local.<stamp> \
    -p:KiCadSharpVersion=0.1.0-local.<stamp>
```

The script packs each library at its own `-local.<timestamp>` version into `local-packages/`, which
is registered as a package source. The distinct version means restore can't quietly fall back to the
published package. Drop the properties to go back to the released ones.

There's deliberately no switch to `ProjectReference`: consuming the real `.nupkg` is what proves the
packages work, and the packages are what break.

## Releasing

This repository doesn't publish packages any more — the four it used to push moved to their own
repositories. What it should release instead is still open: see [docs/releasing.md](docs/releasing.md).

## Data sources and trademarks

UltraLibrarian, EasyEDA, LCSC, JLCPCB, Octopart, SnapEDA and SamacSys are trademarks of their
owners. They're named only to identify the services and data this plugin works with; this project
isn't affiliated with or endorsed by any of them.

Part search results labelled **EasyEDA / LCSC** come from
[jlcsearch](https://github.com/tscircuit/jlcsearch), an independent index of JLCPCB's parts list
run by tscircuit — not from JLCPCB or LCSC directly. The app says so beside each of those results.

easyeda2kicad is © its authors and licensed under AGPL-3.0; it is a separate program that you install, not
a part or a dependency of this MIT-licensed project.

## Contributing

Issues and pull requests welcome.

Name a supplier only to identify where data comes from, and credit the actual source wherever its
data is shown. JLCPCB's API terms forbid its trademark or logo in a partner's advertising and "JLC"
in its website URLs, and breaking them ends API access. So: no JLCPCB or LCSC logos in this
repository, no JLCPCB in the plugin's name, icon or Plugin and Content Manager listing, and no "JLC"
in any URL this project controls ([#58](https://github.com/danielmeza/kicad-ultra/issues/58)).

## License

MIT — see [LICENSE](LICENSE).
