# Installing

## How it fits together

Two processes, and KiCad starts the first one.

- **`plugin/`** is a small Python launcher that KiCad's IPC API plugin system runs. It starts the
  application and passes KiCad's environment through — the API socket, the token and the open
  project's directory travel that way, and there is no other channel. It uses the standard library
  only.
- **`plugin/bin/`** is the application: an [Avalonia](https://avaloniaui.net/) desktop app on .NET 10
  holding the Part Explorer, the embedded browser, the import engine and the MCP server.

The KiCad file formats and the IPC client are not in this repository. They come from nuget.org as
[SExpressions](https://github.com/danielmeza/sexpressions) and
[KiCadSharp](https://github.com/danielmeza/kicad-sharp).

Until [#128](https://github.com/danielmeza/kicad-ultra/issues/128) lands, both halves are installed by
hand.

The application is a **450 MB** self-contained build — 340 MB of it the embedded browser — which is
why it is not inside the plugin package.

## 1. Build and stage the application

```sh
dotnet publish src/importer/UltraLibrarianImporter.UI/UltraLibrarianImporter.UI.csproj \
    -c Release -r linux-x64 --self-contained true -o plugin/bin
```

Use `win-x64`, `osx-x64` or `osx-arm64` for the other platforms. It needs the
[.NET 10 SDK](https://dotnet.microsoft.com/); nothing else has to be installed first.

## 2. Put `plugin/` where KiCad looks

Copy it, or symlink it, into KiCad's third-party plugin directory:

| KiCad installed as | Directory |
|---|---|
| native (Linux) | `~/.local/share/kicad/10.0/3rdparty/plugins/` |
| Flatpak (Linux) | `~/.var/app/org.kicad.KiCad/data/kicad/10.0/3rdparty/plugins/` |
| macOS | `~/Library/Application Support/kicad/10.0/3rdparty/plugins/` |
| Windows | `%APPDATA%\kicad\10.0\3rdparty\plugins\` |

`plugin/requirements.txt` is deliberately empty: the Python side uses the standard library only, so
nothing is installed into KiCad's interpreter.

## 3. Turn KiCad's API on

**Preferences → Plugins → Enable IPC API**, then restart KiCad. The action appears as **Import from
UltraLibrarian** in the schematic and PCB editors, the footprint and symbol editors, and the project
manager.

KiCad passes the API socket, the token and the open project's directory to the plugin through its
environment, and that is the only channel there is. Started any other way the app still connects —
it dials KiCad's default socket — but it has no project to import into.

## easyeda2kicad, for LCSC parts

Optional. It converts a part from its LCSC number into KiCad files, which is how every **Import**
button on an EasyEDA / LCSC or LCSC-sold result works. Without it the rest still works and those
buttons stay disabled with a tip saying why.

**It is a third-party tool under AGPL-3.0 and is not part of this project.** kicad-ultra never
bundles, vendors, installs or modifies it: you install it, and the app runs it as a separate program
through its documented command-line flags.

It needs Python 3.9 or newer.

| KiCad installed as | Command |
|---|---|
| native | `pipx install easyeda2kicad` |
| Flatpak | `flatpak run --command=pip3 org.kicad.KiCad install --user easyeda2kicad` |

Under the Flatpak, KiCad and this importer both run inside KiCad's sandbox, so a copy installed on
the host is invisible to them. The second command installs it where the sandbox can see it, in the
same place KiCad's own `pip3` puts user packages.

The app looks for it in this order, and **Settings → Component Providers → easyeda2kicad** shows what
it found:

1. the path set there — the tool itself, or a Python interpreter that has it installed;
2. `easyeda2kicad` on `PATH`;
3. `python -m easyeda2kicad` with KiCad's own interpreter (`api.interpreter_path` in
   `kicad_common.json`).

## Where it keeps things

| What | Where |
|---|---|
| Settings | `<app data>/UltraLibrarianImporter/config.json` |
| Logs | `<app data>/UltraLibrarianImporter/logs/` |
| Browser cache and downloads | `<app data>/UltralibrarianKicad/` |
| API tokens and credentials | the operating system's credential store, never a file |

`<app data>` is `~/.config` on Linux, `%APPDATA%` on Windows and `~/Library/Application Support` on
macOS. Under the Flatpak everything sits inside KiCad's sandbox, under
`~/.var/app/org.kicad.KiCad/config/`.

Imported libraries go into the project folder when a project is open, next to KiCad's global tables
otherwise, and every import shares one library per provider: `<project>_EasyEDA.kicad_sym` and its
`.pretty` and `.3dshapes` folders beside it, or `EasyEDA` when there is no project.
**Settings → General → Register imported libraries in** decides which table gets the new library:
**Automatic**, **Project** or **Global**.
