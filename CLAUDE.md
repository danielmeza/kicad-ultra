# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet restore UltraLibrarianImporter.sln
dotnet build   UltraLibrarianImporter.sln -c Release   # also the code-style gate; see below
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
--outdated` will keep offering it, but `WebViewControl-Avalonia` (the CEF browser the entire import
flow runs inside) and `Lemon.Hosting.AvaloniauiDesktop` are both built against Avalonia 11. Take
11.3 patches; a 12.x bump is a port, not an upgrade.

`Lemon.Hosting.AvaloniauiDesktop` 1.1.1 deprecates `AddAvaloniauiDesktopApplication` in favour of
`AddAppBuilder`, and `Program.ConfigureServices` suppresses CS0618 rather than migrating.
`AddAppBuilder` invokes its `Func<AppBuilder>` eagerly, with no service provider in scope, and
`App`'s constructor requires the container. Migrating means moving `App`'s startup work out of its
constructor — a startup change, not a package bump. The pragma carries the same note.

### Code style is enforced by the build and by `dotnet format`

`.editorconfig` follows [dotnet/roslyn's](https://github.com/dotnet/roslyn/blob/main/.editorconfig)
structure and escalates most `IDE*` diagnostics to `error`. With `EnforceCodeStyleInBuild=true` most
of them are ordinary build diagnostics, so `dotnet build` catches most style violations, **but not
all of them.** CI therefore has two style gates, the Build step and a Format step
(`dotnet format --verify-no-changes`), and each catches something the other misses (#59). The tree
is clean against both; keep it that way, and run both before pushing.

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
   `pcbnew.ActionPlugin` whose `Run()` calls `launch_importer()`. Both only start the .NET app.
2. **`src/importer/UltraLibrarianImporter.UI`** — Avalonia 11 desktop app on the generic host
   (`Host.CreateApplicationBuilder` + `Lemon.Hosting.AvaloniauiDesktop`), NLog, CommunityToolkit.Mvvm.

**The KiCad file formats and the IPC client are not in this repo.** `SExpressions` (lossless
s-expression read/write) and `KiCadSharp` (IPC client + library formats) live in
`danielmeza/sexpressions` and `danielmeza/kicad-sharp` and arrive from nuget.org at the versions
`Directory.Build.props` pins. Changes to parsing or the IPC surface belong there, not here.

### The launcher hand-off

`importer_launcher.py` looks for `plugin/bin/UltraLibrarianImporter.UI.exe` (the real apphost name)
and `plugin/bin/UltralibrarianImporter.exe`, nothing else — never add candidates outside the plugin
directory; the #34 review removed three that an installed bundle would actually have reached. Still
broken:

- **`.exe` is hardcoded**, so Linux and macOS cannot launch anything. #36 rewrites it per platform.
- **`release.yml` never stages `plugin/bin/`**, so an installed bundle has nothing to start.
- When the launcher starts waiting on the child (as #36 does), `__init__.py`'s `ActionPlugin.Run()`
  must call it with `wait=False` — it runs on KiCad's UI thread and would freeze the editor.

What the launcher gets right is passing the environment through: KiCad exports the IPC socket
path, the API token and the project directory into it, and `KiCadSharp` reads them from there
(`KiCadEnvironment.GetApiToken()` / `GetDefaultSocketPath()` / `GetProjectDirectory()`). Launched any
other way, the app starts fine and every KiCad operation fails.

### Providers and the import engine

Since #34 the importer is multi-provider:

- **`IComponentProvider`** (`Services/Interfaces`) is one source. `ComponentProviderRegistry`
  collects every registered provider from DI; `PartAggregatorService` fans a search out to those
  with `SupportsDirectApi == true`.
- **`KiCadImportEngine`** does all the KiCad file work: extract the archive, then symbols, footprints
  and 3D models, each wrapped by `RunStepAsync` so one failing step reports its own flag in
  `ImportResult` instead of aborting the rest. `Services/UltraLibrarianImporter.cs` is now only a
  thin facade over it.
- **Live providers:** `EasyEdaProvider`, which searches the third-party `jlcsearch.tscircuit.com`
  index (#52), and `OctopartProvider` (Nexar GraphQL, needs the user's token). `SnapEdaProvider` and
  `ComponentSearchEngineProvider` are deliberately `SupportsDirectApi => false` because they used to
  fabricate results (#56, #57). `UltraLibrarianProvider` is browser-only.
- **Never fabricate** — the rule the #34 review converged on. A CAD-availability flag is `true` only
  when the provider said so, and a provider that cannot answer is absent from the results rather
  than present with invented values. The same review is why providers use narrow `catch`es that log
  (`OperationCanceledException` rethrown) and an honest `kicad-ultra/1.0` User-Agent.

### Import flow

`MainWindow` has two tabs.

- **Part Explorer** searches through the aggregator but **cannot import**: `PackageDownloadUrl` is
  never read, and activating a result opens a browser (#47).
- **Web Browser** hosts CEF on the selected provider's site. `InternalDownloadHandler`
  (`Views/MainWindow.axaml.cs`) intercepts the download, reduces the server-supplied name with
  `Path.GetFileName`, and refuses anything that would resolve outside the download directory —
  keep that containment check. The archive then goes to `MainViewModel`, which calls
  `KiCadImportEngine.ImportAsync` with options read from `ConfigService` at import time.

Writing the libraries is real, through KiCadSharp. **Registering them is not:** `KiCadImportEngine`
calls `RunAction($"eeschema.SymLibTable.AddLibrary:{path}:{name}")` and the `pcbnew` equivalent, but
`RunAction` runs an action by name and does not interpret that colon suffix, so nothing is
registered (#46). Treat "the part shows up in KiCad's library list" as unimplemented.

### DI wiring — one trap

`Program.ConfigureLogging` and `Program.ConfigureServices` build the container;
`AddUltraLibrarianKiCadServices()` (`UltraLibrarianKiCadExtensions.cs`) registers KiCadSharp under the
**keyed** client name `com.ultralibrarian.kicad.importer` (re-exposed unkeyed), plus the providers,
registry, aggregator and import engine. `Services/ServiceConfigurator.cs` is a parallel, older
registration path that **nothing calls** — registrations added there have no effect.

- **Logging works now.** `ConfigureLogging` does `ClearProviders().AddConsole().AddNLog()`, so
  `ILogger<T>` output reaches the file targets `nlog.config` declares under
  `%APPDATA%/UltraLibrarianImporter/logs/`. Before #34 nothing bridged `ILogger` into NLog.
- **`KiCadClientSettings` is bound from configuration only in the dead path.** `SettingsViewModel`
  edits `IOptionsMonitor<KiCadClientSettings>.CurrentValue` in place and nothing persists
  `PipeName`/`Token`, so KiCad connection settings entered in the Settings window are lost on restart.

### Configuration

`ConfigService` serializes to `%APPDATA%/UltraLibrarianImporter/config.json`.

- **Settings do not survive a restart (#45).** `Load()` calls `Deserialize<ConfigService>`, whose only
  constructor takes an `ILogger`; System.Text.Json binds constructors by parameter name, throws, and
  the `catch` swallows it. This includes the provider API tokens. The fix is a `ConfigData` DTO, in
  both #36 and #44 — do not add a third copy.
- **API tokens are stored in cleartext** in that file, although the Settings dialog masks them (#54).
- **Three app-data folders:** `UltraLibrarianImporter` (config and logs), `UltralibrarianKicad`
  (the CEF cache), and `KiCadComponentDownloads` (the fallback download directory when
  `DownloadDirectory` is empty; it defaults to `~/Documents/UltraLibrarianDownloads`).

### Tracked work

Open work from the #34 review and the provider research is filed as #45–#58 — settings persistence,
library-table registration, importing from the explorer, real CAD availability, streaming results,
the official JLCPCB API, the AGPL question for EasyEDA conversion, credential storage and rate
limiting among them. Check there before starting on anything provider-related.

## Release state

Nothing publishes to nuget.org any more. `release.yml` builds a Plugin and Content Manager bundle on
`v*` tags, but it stages only the Python files and icons — an installed bundle has no `plugin/bin/`,
so the launcher has nothing to start. Shipping the Avalonia app was consciously deferred (it has no
smoke test and would need Windows code signing); the reasoning is in `docs/releasing.md` and the
header comment of `.github/workflows/release.yml`. Read both before touching packaging.
