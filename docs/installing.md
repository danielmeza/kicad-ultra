# Installing

## How it fits together

Two processes, and KiCad starts the first one.

- **`plugin/`** is a small Python launcher that KiCad's IPC API plugin system runs. It starts the
  application and passes KiCad's environment through — the API socket, the token and the open
  project's directory travel that way, and there is no other channel. It uses the standard library
  only.
- **The application** is an [Avalonia](https://avaloniaui.net/) desktop app on .NET 10 holding the
  Part Explorer, the embedded browser, the import engine and the MCP server. It is a 450 MB build,
  340 MB of it the embedded browser, so it does not travel inside the plugin package.

The KiCad file formats and the IPC client are not in this repository. They come from nuget.org as
[SExpressions](https://github.com/danielmeza/sexpressions) and
[KiCadSharp](https://github.com/danielmeza/kicad-sharp).

## From the Plugin and Content Manager

Install **KiCad UltraLibrarian Importer** there, then enable the API (below) and restart KiCad.

The first run downloads the application for this platform from the project's
[releases](https://github.com/danielmeza/kicad-ultra/releases), checks it against a SHA-256 published
in the same release, and unpacks it into a per-user data directory:

| | |
|---|---|
| Windows | `%LOCALAPPDATA%\kicad-ultra` |
| macOS | `~/Library/Application Support/kicad-ultra` |
| Linux | `$XDG_DATA_HOME/kicad-ultra`, else `~/.local/share/kicad-ultra` |

Not beside the plugin, because the Plugin and Content Manager replaces the plugin's own directory on
every plugin update, and on a system-wide KiCad it is not user-writable.

Only that first run needs the network. The application then keeps itself up to date from the same
releases: an update downloads while you work and installs the next time the importer starts. About
600 MB of free space, and on Linux FUSE to run the AppImage — `APPIMAGE_EXTRACT_AND_RUN=1` if the
system has none. Deleting that directory makes the next run fetch it again.

> [!NOTE]
> **No release is published yet**, so there is nothing for the first run to download. Until the first
> tag, build the application yourself as below. The launcher prefers `plugin/bin` when it exists, so
> this is also how development works.

## Building it yourself

### 1. Build and stage the application

```sh
dotnet publish src/importer/KiCadUltra/KiCadUltra.csproj \
    -c Release -r linux-x64 --self-contained true -o plugin/bin
```

Use `win-x64`, `osx-x64` or `osx-arm64` for the other platforms. It needs the
[.NET 10 SDK](https://dotnet.microsoft.com/); nothing else has to be installed first.

### 2. Put `plugin/` where KiCad looks

Copy it, or symlink it, into KiCad's third-party plugin directory:

| KiCad installed as | Directory |
|---|---|
| native (Linux) | `~/.local/share/kicad/10.0/3rdparty/plugins/` |
| Flatpak (Linux) | `~/.var/app/org.kicad.KiCad/data/kicad/10.0/3rdparty/plugins/` |
| macOS | `~/Library/Application Support/kicad/10.0/3rdparty/plugins/` |
| Windows | `%APPDATA%\kicad\10.0\3rdparty\plugins\` |

`plugin/requirements.txt` is deliberately empty: the Python side uses the standard library only, so
nothing is installed into KiCad's interpreter.

## Turn KiCad's API on

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
| Settings | `<app data>/KiCadUltra/config.json` |
| Logs | `<app data>/KiCadUltra/logs/` |
| Browser cache | `<app data>/KiCadUltra/browser/` |
| Downloads | `~/Documents/UltraLibrarianDownloads`, or wherever **Settings → General** points |
| API tokens and credentials | the operating system's credential store, never a file |

`<app data>` is `~/.config` on Linux, `%APPDATA%` on Windows and `~/Library/Application Support` on
macOS. Under the Flatpak everything sits inside KiCad's sandbox, under
`~/.var/app/org.kicad.KiCad/config/`.

> **Coming from a build older than the rename?** It kept the same things in
> `<app data>/UltraLibrarianImporter/` and `<app data>/UltralibrarianKicad/`, and filed its API
> tokens in the credential store under the service name `UltraLibrarianImporter`. The first start
> moves both folders into `<app data>/KiCadUltra/` and copies the stored credentials to the service
> name `KiCadUltra`; the log says what it did. The credential-store originals are left where they
> are, so nothing is lost if you go back to an older build. A download directory you set yourself is
> never touched.

Imported libraries go into the project folder when a project is open, next to KiCad's global tables
otherwise, and every import shares one library per provider: `<project>_EasyEDA.kicad_sym` and its
`.pretty` and `.3dshapes` folders beside it, or `EasyEDA` when there is no project.
**Settings → General → Register imported libraries in** decides which table gets the new library:
**Automatic**, **Project** or **Global**.

## Removing it

Two things are installed — the plugin, and the application the plugin fetches — and they come off
separately.

1. **The plugin.** KiCad's **Plugin and Content Manager → Manage**, select it, **Uninstall**. A
   `plugin/` you symlinked in by hand goes away with the symlink.
2. **The application.** If you installed it yourself from `KiCadUltra-win-Setup.exe`, it is in
   Windows' **Apps & features** like any other program; the macOS `.app` and the Linux `.AppImage`
   are deleted. The copy the plugin downloads for itself is not installed in that sense — delete the
   per-user data directory it unpacked into:

   | | |
   |---|---|
   | Windows | `%LOCALAPPDATA%\kicad-ultra` |
   | macOS | `~/Library/Application Support/kicad-ultra` |
   | Linux | `$XDG_DATA_HOME/kicad-ultra`, usually `~/.local/share/kicad-ultra` |

3. **Settings, logs and credentials**, if you want those gone too: delete `<app data>/KiCadUltra/`
   and remove the `KiCadUltra` entries from the operating system's credential store — Credential
   Manager on Windows, Keychain Access on macOS, a keyring manager such as Seahorse on Linux.

Imported libraries are ordinary KiCad libraries and are left alone by all of this. Remove them the
way you would any other: drop the row in **Preferences → Manage Symbol Libraries** and **Manage
Footprint Libraries**, then delete the `.kicad_sym`, `.pretty` and `.3dshapes` files it pointed at.
