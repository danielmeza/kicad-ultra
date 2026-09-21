# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet restore UltraLibrarianImporter.sln
dotnet build   UltraLibrarianImporter.sln -c Release   # also the code-style gate; see below
dotnet build   UltraLibrarianImporter.sln -c Debug     # CI builds both (#89)
dotnet run --project src/importer/UltraLibrarianImporter.UI   # the app starts without KiCad
```

**There are no tests.** The README's `dotnet test` line is stale: the solution contains only the
Avalonia app and the `SampleConsole` harness, and `.github/workflows/ci.yml` deliberately has no
test step (see the comment at the end of that file). Add the CI step together with the first test
project rather than running `dotnet test` over a solution with nothing to run.

`SampleConsole` is a scratch harness against a running KiCad, not a test — its IPC token and socket
path are hardcoded in `Program.cs` and will not match another machine. Its one self-contained mode:

```bash
dotnet run --project src/importer/SampleConsole -- --test-parser
```

### Building against local library checkouts

```bash
scripts/use-local-libs.sh ../sexpressions ../kicad-sharp   # prints the exact build command
dotnet build UltraLibrarianImporter.sln -c Release \
    -p:SExpressionsVersion=0.1.0-local.<stamp> -p:KiCadSharpVersion=0.1.0-local.<stamp>
```

The script packs each library into the git-ignored `local-packages/` feed at a distinct
`-local.<timestamp>` version so restore cannot silently fall back to the published package. Omit the
properties to go back to nuget.org. There is deliberately no `ProjectReference` switch — consuming
the real `.nupkg` is what proves the packages work, and the packages are what break.

## Build constraints that bite

`Directory.Build.props` applies to every project: `net10.0`, nullable enabled,
`TreatWarningsAsErrors=true`, `NuGetAuditMode=all` and `EnforceCodeStyleInBuild=true`. A
nullable-analyzer warning, a code-style violation, or a newly disclosed CVE in a *transitive*
package fails the local build, not just CI. `global.json` pins SDK 10.0.400
(`rollForward: latestMajor`, prerelease allowed).

Package versions are centrally managed: `Directory.Packages.props` sets
`ManagePackageVersionsCentrally=true` and carries every `PackageVersion`, so a `PackageReference` in
a csproj is an id and nothing else. Add a package by adding both. `KiCadSharp` and `SExpressions`
resolve their `PackageVersion` from `$(KiCadSharpVersion)` / `$(SExpressionsVersion)` in
`Directory.Build.props`, which is what keeps `scripts/use-local-libs.sh`'s command-line override
working — verified: `-p:KiCadSharpVersion=0.1.0` still resolves 0.1.0.

`GenerateDocumentationFile=true` is set for one reason: IDE0005 (unnecessary usings) is silently
skipped during a build unless the compiler is also emitting a doc file
([roslyn#41640](https://github.com/dotnet/roslyn/issues/41640)). `CS1591` is in `NoWarn` because of
it — nothing here ships as a documented public API. The doc file is a means to the analyzer, not a
deliverable; do not start treating missing XML comments as a real signal.

**Avalonia is held on the 11.3 line on purpose.** 12.x is available and `dotnet list package
--outdated` will keep offering it, but the CEF browser the browser-based import flow runs inside —
`WebViewControl-Avalonia`, and `CefGlue.Avalonia` beneath it — is built against Avalonia 11, and so is
`Lemon.Hosting.AvaloniauiDesktop`. Take 11.3 patches; 12.x is a port, tracked in #67 (in progress on a
local build of an upstream CefGlue PR).

**ReactiveUI: `ReactiveUI.Avalonia` 11.4.13 (ReactiveUI 23.2.28), enabled with `.UseReactiveUI(_ => { })`
in `Program.BuildAvaloniaApp`.** CefGlue.Avalonia also pulls in the older `Avalonia.ReactiveUI` 11.0.9,
built against a ReactiveUI that still had `RxApp`; CefGlue only uses its `AvaloniaScheduler`, which
still binds. **Never import the `Avalonia.ReactiveUI` namespace** or call its parameterless
`UseReactiveUI()`. ReactiveUI 24 needs Avalonia 12 (#67).

**The rule for which MVVM library to use:** CommunityToolkit.Mvvm (`ObservableObject`,
`[ObservableProperty]`, `[RelayCommand]`) for ordinary bindings, which is almost everything. ReactiveUI
**only where high-throughput display or async streams need it** — the Part Explorer's result grid fed
from an `IAsyncEnumerable` is the case today. Do not convert view models to `ReactiveObject`
wholesale.

`Lemon.Hosting.AvaloniauiDesktop` 1.1.1 has two traps, both handled in `Program.ConfigureServices`, one
with a comment carrying each reason:
- **Do not remove the explicit `IHostLifetime` registration (#77).** 1.1.1 gave
  `AvaloniauiApplicationLifetime<App>` a constructor that takes `App`, and DI prefers it. That builds
  `App` before Avalonia's platform setup, binds the UI dispatcher to `NullDispatcherImpl`, and the app
  aborts in `Dispatcher.MainLoop` with `PlatformNotSupportedException` right after "Main window created"
  — exit code 134. The registration forces the lazy constructor.
- `AddAvaloniauiDesktopApplication` is deprecated in favour of `AddAppBuilder`, and CS0618 is
  suppressed rather than migrated: `AddAppBuilder` invokes its `Func<AppBuilder>` eagerly with no
  service provider, and `App`'s constructor needs the container.

### Code style is enforced by the build and by `dotnet format`

`.editorconfig` follows [dotnet/roslyn's](https://github.com/dotnet/roslyn/blob/main/.editorconfig)
structure and escalates most `IDE*` diagnostics to `error`. With `EnforceCodeStyleInBuild=true` most
of them are ordinary build diagnostics, so `dotnet build` catches most style violations, **but not
all of them.** CI therefore has two style gates, the Build step and a Format step
(`dotnet format --verify-no-changes`), and each catches something the other misses (#59). The tree
is clean against both; keep it that way, and run both before pushing.

CI also builds **Debug** (#89). Code under `#if DEBUG` compiles only there, so a Release build
reports a `using` that only such code needs as IDE0005, and deleting it breaks Debug. Put that using
inside its own `#if DEBUG` block, as `Views/AboutWindow.axaml.cs` and `Views/SettingsWindow.axaml.cs`
do for `AttachDevTools()`.

```bash
dotnet format UltraLibrarianImporter.sln --severity warn                      # fix
dotnet format UltraLibrarianImporter.sln --severity warn --verify-no-changes  # check (CI adds --no-restore)
```

Two classes of violation pass the build and are caught only by the format check:

- **`CHARSET`.** `.editorconfig` requires `utf-8-bom` for `.cs` files. A file saved without the BOM
  compiles fine.
- **IDE0001 (name can be simplified).** It is set to `error` but is not reported during a build: a
  fully qualified `System.Threading.Tasks.Task` return type with `using System.Threading.Tasks;` in
  scope builds clean, and only `dotnet format` flags it.

Both turned up while merging #44, with the build green throughout. A green build does not mean the
format check passes.

Use `--severity warn`, never `--severity info`: the naming conventions are deliberately left at
`suggestion` (as in Roslyn), and `info` would let the formatter rename members.

Three things `dotnet format` will not do for you:

- **IDE0005 is invisible to it**, the reverse of the case above. The formatter does not emit a
  documentation file, so unnecessary usings only show up in a real build. `dotnet format` passing
  does not mean the build passes.
- **IDE0060 has no fixer.** Unused parameters must be removed or used by hand.
- **Do not accept its IDE0058 fix for builder chains.** It resolves "expression value is never used"
  by prefixing `_ =`, and on DI or logging registrations that stacks a discard on every line —
  rejected in review. Chain the calls instead, as an expression-bodied member
  (`private static void ConfigureServices(IServiceCollection services) => services.AddX().AddY();`):
  the value is returned rather than dropped, so IDE0058 does not apply. A single `_ =` on a call
  that genuinely cannot chain, like `Directory.CreateDirectory`, is fine.

Two deliberate deviations from Roslyn, both noted in the file itself: `var` is not preferred
everywhere (`csharp_style_var_elsewhere = false`), and severities are error rather than suggestion.
IDE0007 and IDE0008 are both at error, which looks contradictory and is not — together they mean
each declaration has exactly one accepted spelling, `var` where the right-hand side already names
the type and an explicit type everywhere else.

## Architecture

Two processes, and the interesting one is the .NET side.

1. **`plugin/`** — Python. KiCad loads it two ways at once: `plugin.json` registers an IPC API action
   whose entrypoint is `importer_launcher.py`, and `__init__.py` also registers a legacy
   `pcbnew.ActionPlugin`. Both only start the .NET app.
2. **`src/importer/UltraLibrarianImporter.UI`** — Avalonia 11 desktop app on the generic host
   (`Host.CreateApplicationBuilder` + `Lemon.Hosting.AvaloniauiDesktop`), NLog, CommunityToolkit.Mvvm,
   and ReactiveUI for the search grid only. Started with `--mcp` it is instead an MCP server — see below.

**The KiCad file formats and the IPC client are not in this repo.** `SExpressions` (lossless
s-expression read/write) and `KiCadSharp` (IPC client + library formats) live in
`danielmeza/sexpressions` and `danielmeza/kicad-sharp` and arrive from nuget.org at the versions
`Directory.Build.props` pins. Changes to parsing or the IPC surface belong there, not here.

### The launcher hand-off

`importer_launcher.py` starts exactly one thing: `plugin/bin/UltraLibrarianImporter.UI` (`.exe` on
Windows). **Never add candidates outside the plugin directory** — an earlier version searched a
development machine's absolute path, which an installed bundle would actually have reached. It waits
on the child by default (the IPC API runs it as its own process); `__init__.py`'s
`ActionPlugin.Run()` runs on KiCad's UI thread and so calls `launch_importer(wait=False)`.

It passes the environment through: KiCad exports the IPC socket path, the API token and the project
directory, and `KiCadSharp` reads them from there (`KiCadEnvironment.GetApiToken()` /
`GetDefaultSocketPath()` / `GetProjectDirectory()`). Launched any other way, the app starts fine and
every KiCad operation fails. `plugin/requirements.txt` is deliberately empty; its comment explains why.

Still missing: **`release.yml` never stages `plugin/bin/`**, so an installed bundle has nothing to start.

**`HarfBuzzPreload.Apply()` must stay the first thing on `Main`'s GUI path (#78).** CEF loads GTK
with `RTLD_GLOBAL`, which brings in the system `libharfbuzz.so.0`. `libHarfBuzzSharp.so` calls its own
`hb_*` functions through lazily bound slots, so those calls land in the system copy and the app dies
with SIGSEGV (exit 139) right after the main window opens. The preload `dlopen`s HarfBuzzSharp with
`RTLD_NOW` before anything else touches it, binding every slot to itself. It deliberately stays
`RTLD_LOCAL`; the file's remarks explain why `RTLD_GLOBAL` or the old `LD_PRELOAD` workaround is wrong.
It only looks in the app's own native directories, never the working directory. No `LD_PRELOAD` is
needed any more.

### Providers, search and the import engine

- **`IComponentProvider`** (`Services/Interfaces`) is one source; `ComponentProviderRegistry` collects
  them from DI. **Live:** `EasyEdaProvider` (searches the third-party `jlcsearch.tscircuit.com` index —
  every result carries an `Attribution` saying so, #52), and `OctopartProvider` (Nexar, needs the user's
  token). `SnapEdaProvider` and `ComponentSearchEngineProvider` are `SupportsDirectApi => false` until
  real integrations exist (#56, #57). `UltraLibrarianProvider` is browser-only.
- **Never fabricate.** A result or a CAD-availability flag appears only if the provider said so. A
  provider that cannot answer is absent — it does not appear with invented values.
- **Failures are not "no results".** Providers use narrow catches that log, and then **rethrow**
  (`EnsureSuccessStatusCode`; a missing token throws `ProviderNotConfiguredException`). The aggregator
  logs the failure and leaves that provider out, and failures are never cached. Honest
  `kicad-ultra/1.0` User-Agent.
- **`PartAggregatorService.StreamAllProvidersAsync`** yields results as each provider finishes, each on
  its own `Task.Run`, behind `ProviderResponseCache` (5-minute TTL) and `ProviderRateLimiter` (a token
  bucket per provider). `SearchAllProvidersAsync` is the materialising overload the MCP server uses.
- **`MainViewModel` search (ReactiveUI).** `SearchPartsCommand` only emits the normalised query and
  completes at once. If it executed the whole search it would stay disabled for its duration, and a
  new query could not supersede the running one. Its output runs through `Switch()`: a new query drops
  the old search's subscription, which cancels its provider calls. The list is cleared inside the same
  ordered stream as the results, which is what makes stale rows impossible. Keep it that way.
- **`KiCadImportEngine`** does the file work: extract, then symbols, footprints and 3D models, each
  wrapped by `RunStepAsync` so a failing step reports its own `ImportResult` flag.

### Import flow

`MainWindow` has two tabs.
- **Part Explorer** imports **EasyEDA / LCSC** results that carry an LCSC code
  (`PartSearchResult.LcscPartNumber`) through the user-installed `easyeda2kicad` CLI (#76). Other
  providers still cannot import: `PackageDownloadUrl` is never read (#47).
  - **easyeda2kicad is AGPL-3.0, so it runs only as a separate process.** Start it through
    `Services/EasyEda2KiCad/ExternalProcess`, which uses `ArgumentList` and never a command string.
    Never `import` it, and never distribute it: not in `plugin/requirements.txt`, not in
    `plugin/bin/`, not in the PCM bundle. Never copy its code, and use only its documented flags plus
    KiCad's file formats. The LCSC id is validated (`^C[0-9]+\z`) before it reaches the command line.
  - `EasyEda2KiCadLocator` looks in three places, in order:
    1. the Settings path (the tool itself, or a Python interpreter run with `-m`);
    2. `PATH`;
    3. `<api.interpreter_path> -m easyeda2kicad`.

    A candidate counts only if `-h` exits 0 and its help lists `--lcsc_id`. Inside KiCad's Flatpak
    the locator also sets `PYTHONUSERBASE=$XDG_DATA_HOME/python` and puts that `bin` on `PATH`, as
    the Flathub manifest's `pip3` wrapper does. Without them, the documented `pip3 install --user` is
    invisible.
  - `KiCadImportEngine.ImportLcscPartAsync` runs the tool **straight into the final library**
    (`--output <dir>/<name> --overwrite`), not a temp dir. Its footprints point at 3D models by path,
    and its symbols at `<name>:<footprint>`.
    - `--project-relative` is passed only for project-table libraries. It resolves against the working
      directory, so that run's cwd is the project.
    - The output **never** goes through `ImportSymbolsAsync` / `ImportFootprintsAsync`: a KiCadSharp
      re-save hits #68, and prefix renaming breaks the links.
    - A non-zero exit, a timeout or a cancel rolls back: the symbol library is restored from a copy,
      created files are removed, and nothing is registered.
    - Both import paths share one `_importGate` in the engine.

    #88 checked that kicad-cli 10.0.6 loads the output through the registered tables and resolves
    the 3D models. It has not been checked in the KiCad GUI.
- **Web Browser** hosts CEF. `InternalDownloadHandler` (`Views/MainWindow.axaml.cs`) reduces the
  server-supplied file name with `Path.GetFileName` and refuses anything that would resolve outside
  the download directory — keep that containment check.

**Registration in KiCad's library tables is real** (#66), by editing the table files; KiCad 10's IPC API
has no library-table calls (they arrive in 11 — #72). `KiCadLibraryTable` parses with SExpressions but
splices the new row into the original text, so existing bytes are untouched. It refuses tables outside
KiCad's strict grammar, is idempotent, and writes through a temporary file. The project table is
created if missing, `${KIPRJMOD}`-relative; the global table is **never created**.
`KiCadSettingsDirectory` resolves KiCad's config directory for the running version. KiCad does not
reload tables on its own, so the user must reopen the project or restart KiCad.

**Which table is the `RegistrationScope` setting** (#71): **Automatic** (the default) picks the project
table when the import goes into a KiCad project, and the global table only when it does not. **Project**
always picks the project table, and with no project the import fails before any library is written.
**Global** always picks the global table, by absolute path. "A project" means that the directory the
libraries are written into holds a `.kicad_pro`. `KiCadImportEngine.SelectLibraryTable` resolves the scope
once per import, for both paths; `--project-relative` follows the resolved table. `config.json` stores
the scope by name. A pre-#71 `AddToGlobalLibrary` is migrated on load and the file is rewritten
without it: `true`, the old default rather than a choice, becomes Automatic, and `false` becomes Project.

**But symbols from the Ultra Librarian `.zip` path still do not load in KiCad 10** (#68): the
`.kicad_sym` that KiCadSharp 0.1.1 writes is invalid for it (danielmeza/kicad-sharp#45). Footprints
load. Re-importing appends a duplicate symbol (#69). Neither affects the easyeda2kicad path, which
never re-saves through KiCadSharp.

### DI wiring — one trap

`Program.ConfigureLogging` and `Program.ConfigureServices` build the GUI container;
`AddUltraLibrarianKiCadServices()` (`UltraLibrarianKiCadExtensions.cs`) registers KiCadSharp under the
**keyed** client name `com.ultralibrarian.kicad.importer` (re-exposed unkeyed), the providers, registry,
aggregator, cache, rate limiter, import engine and MCP server. `ISecretStore` is registered in **both**
the GUI and `--mcp` containers. `Services/ServiceConfigurator.cs` is an older registration path that
**nothing calls**. Registrations added there have no effect.

`ConfigureLogging` bridges `ILogger` into NLog (`AddNLog()`), so `nlog.config`'s file targets under
`<app data>/UltraLibrarianImporter/logs/` receive output. `KiCadClientSettings` is bound only in the dead
path, so KiCad connection settings entered in Settings are lost on restart.

### The MCP server (`--mcp`)

`Program.RunMcpHostAsync` builds its own container and serves MCP over **stdio** (no network port)
with three read-only tools: `search_components`, `list_providers` and `get_component_details`. **stdout
is the protocol channel**: nothing in MCP mode may write to it. Today it stays clean only because MCP
mode logs **nothing at all** (#80). `nlog.config`'s `ColoredConsole` target writes Info to stdout, so
making MCP logging work means routing it to a file or stderr first. Verify any change by piping
`initialize`, `notifications/initialized` and a `tools/call`, and checking that every stdout line is
valid JSON-RPC.

### Configuration and secrets

`ConfigService` persists non-secret settings through a single private `ConfigData` DTO to
`<app data>/UltraLibrarianImporter/config.json`. Keep it the only persistence path.

**API tokens live in the OS credential store** (#54): `ISecretStore`, with `PlatformSecretStore` choosing
Windows Credential Manager, the macOS Keychain, or libsecret on Linux (under Flatpak, libsecret goes
through the Secret portal). A token found in an old `config.json` is migrated into the store, and the
file is rewritten without it — only after the store write succeeds. With no working store, tokens are
session-only and never written to the file. **Never log a token value.**

Caveat (#70): on Unix, `Environment.GetFolderPath` returns `""` for a folder that does not exist yet,
and five call sites would then resolve relative to the working directory.

### Running the app during development — without touching the user's machine

Runtime checks matter here: two real defects (#77, #78) passed every build, format and unit-level
check. To run it:
- a headless display — Xvfb, not the user's `DISPLAY`;
- a temp `HOME` and `XDG_CONFIG_HOME` / `XDG_DATA_HOME` / `XDG_CACHE_HOME`, plus a `Documents` folder in
  it (see #70);
- `DBUS_SESSION_BUS_ADDRESS` pointed at a dead socket, so the libsecret store cannot read or write the
  user's real keyring.

A process that exits on its own under `timeout` is **not** a clean run. Exit 134 or 139, or
"dumped core", means it crashed — SIGTERM from `timeout` does not dump core.

### Tracked work

Open: importing from the explorer for providers other than EasyEDA/LCSC (#47), CAD availability (#48),
providers (#51, #52, #56, #57), Avalonia 12 (#67, draft #75), symbols KiCad cannot load (#68),
duplicate symbols (#69), special-folder paths (#70), KiCad 11 IPC
(#72 tables, #73 datasheets), and MCP-mode logging (#80). Upstream: danielmeza/kicad-sharp#45 and #46, danielmeza/sexpressions#24.

## Release state

Nothing publishes to nuget.org any more. `release.yml` builds a Plugin and Content Manager bundle on
`v*` tags, but it stages only the Python files and icons — an installed bundle has no `plugin/bin/`,
so the launcher has nothing to start. Shipping the Avalonia app was consciously deferred (it has no
smoke test and would need Windows code signing); the reasoning is in `docs/releasing.md` and the
header comment of `.github/workflows/release.yml`. Read both before touching packaging.
