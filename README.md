<h1 align="center">
  <img src="docs/images/logo.png" alt="" width="96"><br>
  kicad-ultra
</h1>

<h4 align="center">Find a part and get its symbol, footprint and 3D model into your KiCad project, without leaving KiCad. Built with <a href="https://avaloniaui.net/">Avalonia</a>.</h4>

<p align="center">
  <a href="https://github.com/danielmeza/kicad-ultra/actions/workflows/ci.yml"><img src="https://github.com/danielmeza/kicad-ultra/actions/workflows/ci.yml/badge.svg" alt="CI status"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/danielmeza/kicad-ultra" alt="MIT license"></a>
  <img src="https://img.shields.io/badge/KiCad-10.0%20%C2%B7%2011%20nightly-314cb0" alt="Verified on KiCad 10.0.6 and 10.99">
  <img src="https://img.shields.io/badge/.NET-10-512bd4" alt=".NET 10">
</p>

<p align="center">
  <a href="#key-features">Key features</a> •
  <a href="#install">Install</a> •
  <a href="#quick-start">Quick start</a> •
  <a href="#where-the-parts-come-from">Where parts come from</a> •
  <a href="#how-it-works">How it works</a> •
  <a href="#troubleshooting">Troubleshooting</a>
</p>

<p align="center">
  <img src="docs/images/part-explorer.png" alt="The Part Explorer listing NE555 parts with stock, price and CAD availability, each row offering Import and Find on Ultra Librarian" width="100%">
</p>

Adding a part to a KiCad project usually means leaving KiCad: search a vendor site, download a zip,
unpack it somewhere, add two library-table rows by hand, and hope the 3D model still points at a path
that exists. kicad-ultra does it from inside KiCad. It searches several component sources at once,
imports the symbol, the footprint and the 3D model, and registers the library in KiCad's own tables,
so the part is there the next time you open the project.

## Key features

- **One search across sources.** EasyEDA / LCSC parts from JLCPCB's library, and Octopart through
  Nexar with your own token. Results stream in as each source answers, with stock, price breaks and
  the datasheet link.
- **Two ways to import.** A result carrying an LCSC code converts through
  [easyeda2kicad](https://github.com/uPesy/easyeda2kicad.py). Any result with a manufacturer part
  number gets **Find on Ultra Librarian**, which opens that search in the built-in browser tab, where
  the download you make is intercepted and imported.
- **The library ends up registered.** Symbols, footprints and 3D models are written into one library
  and added to the project's `sym-lib-table` and `fp-lib-table` with `${KIPRJMOD}`-relative paths, or
  to KiCad's global tables when there is no project. Rows that were already there are left byte for
  byte as they were.
- **It says what it actually knows.** CAD availability is yes, no or **unknown** — never a guess. Each
  row names the source its data came from. A provider that fails is reported as failed, never as "no
  results".
- **Re-importing replaces.** Importing a part again replaces its symbol, footprint and model instead of
  leaving duplicates behind, and says what it replaced.
- **Nothing is half-written.** A failed, timed-out or cancelled import rolls back: the symbol library
  is restored, files the run created are removed, and nothing is registered.
- **An MCP server, if you want one.** Started with `--mcp` it serves three read-only tools over stdio
  (`search_components`, `list_providers`, `get_component_details`) so an AI client can look parts up.
  It never imports, and never uses JLCPCB's official API.

## Install

> [!NOTE]
> A Plugin and Content Manager package that fetches the application for you is being built in
> [#128](https://github.com/danielmeza/kicad-ultra/issues/128). Until it lands, install by hand as
> below. The application is a 450 MB self-contained build — 340 MB of that is the embedded browser —
> which is why it does not live inside the plugin package.

**1. Build and stage the application.**

```sh
dotnet publish src/importer/UltraLibrarianImporter.UI/UltraLibrarianImporter.UI.csproj \
    -c Release -r linux-x64 --self-contained true -o plugin/bin
```

Use `win-x64`, `osx-x64` or `osx-arm64` for the other platforms.

**2. Put `plugin/` where KiCad looks for plugins**, as a copy or a symlink:

| KiCad installed as | Directory |
|---|---|
| native (Linux) | `~/.local/share/kicad/10.0/3rdparty/plugins/` |
| Flatpak (Linux) | `~/.var/app/org.kicad.KiCad/data/kicad/10.0/3rdparty/plugins/` |
| macOS | `~/Library/Application Support/kicad/10.0/3rdparty/plugins/` |
| Windows | `%APPDATA%\kicad\10.0\3rdparty\plugins\` |

**3. Turn the API on.** **Preferences → Plugins → Enable IPC API**, then restart KiCad. The action
appears as **Import from UltraLibrarian** in the schematic and PCB editors, the footprint and symbol
editors, and the project manager.

**Optional, for LCSC parts: easyeda2kicad.** It is a third-party tool under **AGPL-3.0**, it is *not*
part of this project, and this project never bundles, vendors, installs or modifies it — it only runs
it as a separate program through its documented flags. Without it everything else still works, and the
Import button stays disabled with a tip saying why.

| KiCad installed as | Command |
|---|---|
| native | `pipx install easyeda2kicad` |
| Flatpak | `flatpak run --command=pip3 org.kicad.KiCad install --user easyeda2kicad` |

Under the Flatpak, KiCad and this importer run inside KiCad's sandbox, so a copy installed on the host
is invisible to them; the second command installs it where the sandbox can see it.

## Quick start

1. Open **Import from UltraLibrarian** in KiCad, with your project open.
2. Type a part number or a keyword in the **Part Explorer** and press **Search All Providers**.
3. On a row that has an LCSC code, press **Import**. On any other row, press **Find on Ultra
   Librarian**, sign in there and download the KiCad model — the download is intercepted and imported.
4. Reopen the project, or restart KiCad, so it re-reads the library tables. Then place the part.

**Settings → General → Register imported libraries in** decides which table gets the new library:
**Automatic** (the project's when a project is open, KiCad's global one when not), **Project**, or
**Global**.

## Where the parts come from

The app names the source beside every result, because these are other people's services and one of
them is not a published API.

| Source | What it gives | What you need |
|---|---|---|
| **EasyEDA / LCSC** | JLCPCB's parts library: LCSC code, manufacturer, stock, price breaks, datasheet | nothing |
| **Octopart** | Multi-distributor stock and pricing, datasheets, real CAD availability | your own [Nexar](https://nexar.com/) token |
| **Ultra Librarian** | Symbols, footprints and 3D models, through the browser tab | a free account, in the browser |
| **SnapEDA, SamacSys** | browser only | neither publishes an API a desktop app may use ([#56](https://github.com/danielmeza/kicad-ultra/issues/56), [#57](https://github.com/danielmeza/kicad-ultra/issues/57)) |

EasyEDA / LCSC searches go one of two ways:

- **JLCPCB's official Components API**, if you enter your own credentials in **Settings → Component
  Providers**. JLCPCB reviews applications for API access, per service type, so credentials whose
  **Parts** permission is not approved are refused; the app then says so once and keeps searching the
  other way for the rest of the session. See
  [its guide](https://jlcpcb.com/help/article/jlcpcb-online-api-available-now). That API looks parts
  up by LCSC number only — it has no keyword search.
- **An internal endpoint of JLCPCB's website**, otherwise. It is not a published API and can change or
  stop working at any time, which the Part Explorer says while it is in use. One page of 25 results per
  search, an honest `kicad-ultra/1.0` User-Agent, a slower rate limit of its own, and when JLCPCB turns
  searches down the app backs off for up to two minutes and tells you, instead of dropping the results
  silently.

The MCP server never uses the official API, even with credentials stored: JLCPCB's API terms forbid
passing that data to third parties, and an MCP server hands every result to the connected AI client.

## How it works

Two processes, and KiCad starts the first one.

- **`plugin/`** — a small Python launcher that KiCad's IPC API plugin system runs. It starts the
  application and passes KiCad's environment through: the API socket, the token and the open project's
  directory travel that way, and there is no other channel.
- **`src/importer/UltraLibrarianImporter.UI`** — an [Avalonia](https://avaloniaui.net/) application on
  .NET 10: the Part Explorer, the embedded browser (CEF), the import engine, and the MCP server.

The KiCad file formats and the IPC client are not in this repository. They are two libraries of their
own, consumed from nuget.org: [SExpressions](https://github.com/danielmeza/sexpressions) for lossless
s-expression reading and writing, and [KiCadSharp](https://github.com/danielmeza/kicad-sharp) for the
IPC client and the library formats. Changes to parsing or to the IPC surface belong there.

**Verified on 2026-09-22** against real KiCad instances, 10.0.6 and 10.99 (the KiCad 11 line): connect,
search, import an LCSC part, register it in the project's tables, and `kicad-cli` loads the resulting
symbol and footprint on both.

## Building

```sh
dotnet build UltraLibrarianImporter.sln -c Release   # also the code-style gate
dotnet build UltraLibrarianImporter.sln -c Debug
dotnet format UltraLibrarianImporter.sln --severity warn --verify-no-changes
```

There are no tests yet; `dotnet run --project src/importer/SampleConsole -- --test-parser` is the one
self-contained harness. Warnings fail the build, and `.editorconfig` is enforced by it. See
[CLAUDE.md](CLAUDE.md), the working guide this repository is developed against.

<details>
<summary>Building against local checkouts of SExpressions or KiCadSharp</summary>

```sh
scripts/use-local-libs.sh ../sexpressions ../kicad-sharp
dotnet build UltraLibrarianImporter.sln -c Release \
    -p:SExpressionsVersion=0.1.0-local.<stamp> -p:KiCadSharpVersion=0.1.0-local.<stamp>
```

The script packs each library into the git-ignored `local-packages/` feed at its own
`-local.<timestamp>` version, so restore cannot quietly fall back to the published package. Drop the
properties to go back to nuget.org. There is deliberately no `ProjectReference` switch: consuming the
real `.nupkg` is what proves the packages work, and the packages are what break.

</details>

## Troubleshooting

<details>
<summary>The Import button is disabled</summary>

Hover it: the tip says why. Usually easyeda2kicad is not installed, or the row carries no LCSC code —
in which case use **Find on Ultra Librarian** instead. **Settings → Component Providers →
easyeda2kicad** shows what was found and where it looked: the path set there, then `PATH`, then
KiCad's own Python interpreter.

</details>

<details>
<summary>The imported part does not show up in KiCad</summary>

KiCad reads the library tables when a project opens and does not re-read them on its own. Reopen the
project, or restart KiCad. The import log names the table it wrote to.

</details>

<details>
<summary>The About window says KiCad is busy, or is not connected</summary>

KiCad answers the API only while its main loop is free: a modal dialog — the stale lock-file prompt,
for instance — makes every request wait. Close the dialog. "Not connected" means nothing is listening:
check **Preferences → Plugins → Enable IPC API**, and that KiCad is running.

Started by hand rather than by KiCad, the app looks for KiCad's socket under its own `TMPDIR`, falling
back to a Flatpak KiCad's socket. A `TMPDIR` different from KiCad's means it will not find it.

</details>

<details>
<summary>Searches suddenly return nothing from EasyEDA / LCSC</summary>

JLCPCB rate-limits its website endpoint. The app notices, backs off for up to two minutes and says so
in the Part Explorer; cached results are still served meanwhile. With JLCPCB API credentials whose
**Parts** permission was never approved, the official route is refused and the app falls back to the
same endpoint, also saying so.

</details>

<details>
<summary>Where are the logs?</summary>

`<app data>/UltraLibrarianImporter/logs/`: `~/.config/UltraLibrarianImporter/logs/` on Linux,
`%APPDATA%\UltraLibrarianImporter\logs\` on Windows, `~/Library/Application Support/…` on macOS. Under
the Flatpak it is inside KiCad's sandbox:
`~/.var/app/org.kicad.KiCad/config/UltraLibrarianImporter/logs/`. Tokens are never written there.

</details>

## Limitations

- **The KiCad GUI itself has not been driven end to end.** The import path is verified with
  `kicad-cli` and against a running KiCad over IPC, not by clicking through the editors.
- **Windows and macOS build but are unverified**; the runtime checks so far are Linux.
- **Symbols, footprints and 3D models only.** No simulation models, and datasheets are not attached to
  imported parts yet ([#73](https://github.com/danielmeza/kicad-ultra/issues/73)).
- **Registration edits the table files.** KiCad 11 adds IPC commands for it
  ([#72](https://github.com/danielmeza/kicad-ultra/issues/72)); KiCad master declares them but does not
  answer them yet.
- Everything still open is listed under "Tracked work" in [CLAUDE.md](CLAUDE.md), with what each item
  waits on.

## Contributing

Issues and pull requests are welcome. [CLAUDE.md](CLAUDE.md) is worth reading first: it is the guide
this repository is actually developed against, including the build gates and the traps that have
already cost someone a day.

Two rules that are not style preferences:

- **Never fabricate data.** A result, a price or a CAD-availability flag appears only because a source
  said so. A provider that cannot answer is left out, and a failure is reported as a failure.
- **Name a supplier only to identify where data comes from.** JLCPCB's API terms forbid its trademark
  or logo in a partner's advertising and "JLC" in a partner's URLs, and breaking them ends API access:
  no JLCPCB or LCSC logos here, none in the plugin's name, icon or Plugin and Content Manager listing,
  and no "JLC" in any URL this project controls
  ([#58](https://github.com/danielmeza/kicad-ultra/issues/58)).

## Credits and license

- Built with [.NET 10](https://dotnet.microsoft.com/), [Avalonia](https://avaloniaui.net/),
  [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet),
  [ReactiveUI](https://www.reactiveui.net/) (the result grid),
  [CefGlue](https://github.com/OutSystems/CefGlue) and
  [WebViewControl-Avalonia](https://github.com/OutSystems/WebView) (the browser tab),
  [NLog](https://nlog-project.org/), and this project's own
  [SExpressions](https://github.com/danielmeza/sexpressions) and
  [KiCadSharp](https://github.com/danielmeza/kicad-sharp).
- [easyeda2kicad](https://github.com/uPesy/easyeda2kicad.py) is © its authors under AGPL-3.0. It is a
  separate program you install, not a part or a dependency of this project.
- UltraLibrarian, EasyEDA, LCSC, JLCPCB, Octopart, Nexar, SnapEDA and SamacSys are trademarks of their
  owners, named here only to identify the services this plugin works with. This project is not
  affiliated with or endorsed by any of them.
- Released under the [MIT License](LICENSE).
