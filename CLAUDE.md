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

### Code style is enforced by the build

`.editorconfig` follows [dotnet/roslyn's](https://github.com/dotnet/roslyn/blob/main/.editorconfig)
structure and escalates most `IDE*` diagnostics to `error`. With `EnforceCodeStyleInBuild=true` they
are ordinary build diagnostics, so **`dotnet build` is the style gate** — there is no separate lint
step to run or to add to CI. The tree is clean against it; keep it that way.

```bash
dotnet format UltraLibrarianImporter.sln --severity warn                      # fix
dotnet format UltraLibrarianImporter.sln --severity warn --verify-no-changes  # check
```

Use `--severity warn`, never `--severity info`: the naming conventions are deliberately left at
`suggestion` (as in Roslyn), and `info` would let the formatter rename members.

Two things `dotnet format` will not do for you:

- **IDE0005 is invisible to it.** The formatter does not emit a documentation file, so unnecessary
  usings only show up in a real build. `dotnet format` passing does not mean the build passes.
- **IDE0060 has no fixer.** Unused parameters must be removed or used by hand.

Two deliberate deviations from Roslyn, both noted in the file itself: `var` is not preferred
everywhere (`csharp_style_var_elsewhere = false`), and severities are error rather than suggestion.
IDE0007 and IDE0008 are both at error, which looks contradictory and is not — together they mean
each declaration has exactly one accepted spelling, `var` where the right-hand side already names
the type and an explicit type everywhere else.

## Architecture

Two processes, and the interesting one is the .NET side.

1. **`plugin/`** — Python, loaded by KiCad through the IPC API plugin system. `plugin.json`
   registers a single action scoped to schematic, pcb, footprint, symbol and project_manager.
   `importer_launcher.py` does nothing but `subprocess.Popen` a .NET executable it expects at
   `plugin/bin/UltralibrarianImporter.exe`.
2. **`src/importer/UltraLibrarianImporter.UI`** — Avalonia 11 desktop app on the generic host
   (`Host.CreateApplicationBuilder` + `Lemon.Hosting.AvaloniauiDesktop`), NLog, CommunityToolkit.Mvvm.

**The KiCad file formats and the IPC client are not in this repo.** `SExpressions` (lossless
s-expression read/write) and `KiCadSharp` (IPC client + library formats) live in
`danielmeza/sexpressions` and `danielmeza/kicad-sharp` and arrive from nuget.org at the versions
`Directory.Build.props` pins. Changes to parsing or the IPC surface belong there, not here.

### The launcher hand-off is currently broken in three ways

`importer_launcher.py` is 20 lines and every one of them matters, because it is the only link
between the two processes:

- The path it starts is `bin/UltralibrarianImporter.exe`. The app builds as
  `UltraLibrarianImporter.UI.exe` (note the `.UI`, and the `L` casing differs too), so the name does
  not match the assembly even if the binary were staged there.
- `.exe` is hardcoded, so the launcher cannot work on Linux or macOS at all.
- `release.yml` never stages `plugin/bin/`, so an installed bundle has nothing to start.

What the launcher *does* get right is `env=os.environ`: KiCad exports the IPC socket path, the API
token and the project directory into the child's environment, and that is how the .NET app finds
KiCad. `AddKiCad(name)` in `UltraLibrarianKiCadExtensions` is called with no settings callback, so
the connection comes entirely from `KiCadEnvironment.GetApiToken()` / `GetDefaultSocketPath()`, and
`UltraLibrarianImporter.GetProjectPath()` reads `KiCadEnvironment.GetProjectDirectory()`. Launched
any other way, the app starts fine and every KiCad operation fails.

### Import flow

`MainWindow` hosts a CEF `WebView` pointed at `app.ultralibrarian.com`. The user downloads a part
inside that embedded browser; `InternalDownloadHandler` (`Views/MainWindow.axaml.cs`) intercepts the
CEF download and hands the `.zip` path to `MainViewModel`, which calls
`UltraLibrarianImporter.ImportComponentAsync(zip, ImportType)`. That service
(`Services/UltraLibrarianImporter.cs` — ~1/3 of all the C# here) extracts the archive and runs
symbol, footprint and 3D-model import independently, each returning its own success flag in
`ImportResult`.

Writing the libraries is real — `KiCadSymbolLibrary.Save`, `KiCadFootprintLibrary.SaveFootprint`
and a file copy for the models, all through KiCadSharp. **Registering them in the library tables is
not.** `AddSymbolLibraryToTable` calls `RunAction("common.Control.addLibrary")` with no arguments at
all, and `AddFootprintLibraryToTable` calls `RunAction("pcbnew.FpLibTable.AddLibrary:{path}:{name}")`
with a colon-delimited argument string that is not how KiCad action identifiers work. Both are
placeholders, and their own comments say so. `GetSymbolLibraryPath()` / `GetFootprintLibraryPath()`
locate an existing `sym-lib-table` / `fp-lib-table` but are only used to *derive a directory* for
the new library when no project is open; nothing ever writes to those files. Treat "the part shows
up in KiCad's library list" as unimplemented, not as a regression.

One more thing to know before editing that file: `_projectPath` holds a **directory**
(`GetProjectDirectory()`, then `Directory.GetFiles(_projectPath, "*.kicad_pro")`), but every use
site passes it through `DirectoryOf(...)` as if it were a file, which yields the project folder's
*parent*. Preserve or fix that deliberately; do not half-change it.

### DI wiring — one trap, with consequences

`Program.ConfigureServices` registers the view models and `IConfigService`, then calls
`AddUltraLibrarianKiCadServices()` (`UltraLibrarianKiCadExtensions.cs`), which registers KiCadSharp
under the **keyed** client name `com.ultralibrarian.kicad.importer` and re-exposes it unkeyed.
`Services/ServiceConfigurator.cs` is a parallel, older registration path that **nothing calls** —
registrations added there have no effect.

That dead path is why two things are not wired up as they look:

- **NLog is configured but not connected.** `Program.Main` calls `LoadConfigurationFromFile("nlog.config")`,
  which sets up NLog's own `LogManager`, but the only `AddNLog()` bridge lives in the dead
  `ServiceConfigurator`. Every injected `ILogger<T>` therefore goes to the host's default providers
  (console/debug), and the file targets `nlog.config` declares under
  `%APPDATA%/UltraLibrarianImporter/logs/` stay empty. Console output is the log.
- **`KiCadClientSettings` is bound from configuration only in the dead path.** `SettingsViewModel`
  edits `IOptionsMonitor<KiCadClientSettings>.CurrentValue` in place and `ConfigService.Save()` does
  not carry `PipeName`/`Token`, so KiCad connection settings entered in the Settings window are lost
  on restart.

### Configuration and where files actually land

`ConfigService` serializes itself to `%APPDATA%/UltraLibrarianImporter/config.json` and supplies
`ImportOptions` per import. Three traps around it:

- **Two different app-data folders.** Config and the (unused) NLog targets use
  `UltraLibrarianImporter`; the CEF cache and the actual downloads use `UltralibrarianKicad`.
- **`DownloadDirectory` is not where downloads go.** It defaults to
  `~/Documents/UltraLibrarianDownloads` and `EnsureDownloadDirectoryExists()` creates it, but
  `InternalDownloadHandler.OnBeforeDownload` writes the `.zip` to
  `%APPDATA%/UltralibrarianKicad/<filename>` unconditionally. Changing the setting moves nothing.
- **`ImportOptions` is snapshotted at startup.** `AddUltraLibrarianKiCadServices` calls
  `configService.GetImportOptions()` inside the importer's factory, and although both the importer
  and `MainViewModel` are transient, `MainViewModel` is resolved exactly once in
  `App.OnFrameworkInitializationCompleted`. Saving the Settings window updates `ConfigService` and
  `config.json` but not the live importer, so import options only take effect on the next launch.

## Release state

Nothing publishes to nuget.org any more. `release.yml` builds a Plugin and Content Manager bundle on
`v*` tags, but it stages only the Python files and icons — an installed bundle has no `plugin/bin/`,
so the launcher has nothing to start. Shipping the Avalonia app was consciously deferred (it has no
smoke test and would need Windows code signing); the reasoning is in `docs/releasing.md` and the
header comment of `.github/workflows/release.yml`. Read both before touching packaging.
