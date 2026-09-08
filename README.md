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

## Contributing

Issues and pull requests welcome.

## License

MIT — see [LICENSE](LICENSE).
