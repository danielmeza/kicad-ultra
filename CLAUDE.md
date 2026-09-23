# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet restore KiCadUltra.sln
dotnet build   KiCadUltra.sln -c Release   # also the code-style gate; see below
dotnet build   KiCadUltra.sln -c Debug     # CI builds both (#89)
dotnet run --project src/importer/KiCadUltra   # the app starts without KiCad
```

**There are no .NET tests.** The README's `dotnet test` line is stale: the solution contains only
the Avalonia app and the `SampleConsole` harness, and `.github/workflows/ci.yml` deliberately has no
`dotnet test` step (see the comment at the end of that file). Add the CI step together with the
first test project rather than running `dotnet test` over a solution with nothing to run.

**The one test suite is `tests/`, and it is Python** (#128): integration tests for the plugin's
bootstrapper, driving `plugin/importer_launcher.py` against a local HTTP server that answers like
GitHub's releases API. Standard library only, no network, and each run gets its own temporary tree
through `KICAD_ULTRA_HOME`, so they touch nothing on the machine. CI runs them, and so should you:

```bash
python3 -m unittest discover -s tests -v
```

`SampleConsole` is a scratch harness against a running KiCad, not a test — its IPC token and socket
path are hardcoded in `Program.cs` and will not match another machine. Its one self-contained mode:

```bash
dotnet run --project src/importer/SampleConsole -- --test-parser
```

### Building against local library checkouts

```bash
scripts/use-local-libs.sh ../sexpressions ../kicad-sharp   # prints the exact build command
dotnet build KiCadUltra.sln -c Release \
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
dotnet format KiCadUltra.sln --severity warn                      # fix
dotnet format KiCadUltra.sln --severity warn --verify-no-changes  # check (CI adds --no-restore)
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
2. **`src/importer/KiCadUltra`** — Avalonia 11 desktop app on the generic host
   (`Host.CreateApplicationBuilder` + `Lemon.Hosting.AvaloniauiDesktop`), NLog, CommunityToolkit.Mvvm,
   and ReactiveUI for the search grid only. Started with `--mcp` it is instead an MCP server — see below.

**The KiCad file formats and the IPC client are not in this repo.** `SExpressions` (lossless
s-expression read/write) and `KiCadSharp` (IPC client + library formats) live in
`danielmeza/sexpressions` and `danielmeza/kicad-sharp` and arrive from nuget.org at the versions
`Directory.Build.props` pins. Changes to parsing or the IPC surface belong there, not here.

**The name, and the two identifiers that did not take it.** The solution, the project, the assembly,
the apphost, the namespaces and the application-data folder are all `KiCadUltra` since #132, and the
Velopack `packId` has been since #128. Two published identifiers are deliberately still spelled the
old way, and changing either orphans every installation that keys on it: the Plugin and Content
Manager package id `com.github.danielmeza.kicad-ultralibrarian-importer` and the action id
`kicad-ultralibrarian-importer.import`. `scripts/rename-identity.py` protects both by default; use
it for a rename of this kind rather than a `sed`, read its "review" list, and write the migrations
it points at (see **Configuration and secrets**).

### The launcher hand-off

`importer_launcher.py` starts the importer, and since #128 it also **fetches it on the first run**.
It looks in exactly two places, in this order:

1. `plugin/bin/KiCadUltra` (`.exe` on Windows) — the developer's override, which is
   what `dotnet publish -o plugin/bin` is for. Unchanged.
2. the per-user data directory: `%LOCALAPPDATA%\kicad-ultra`, `~/Library/Application Support/kicad-ultra`,
   or `$XDG_DATA_HOME/kicad-ultra`. Nothing is shipped there — the launcher downloads this
   platform's asset from the project's GitHub release, checks it against the SHA-256 the release
   publishes as `SHA256SUMS.txt`, and unpacks it.

**Never add a candidate that is neither of those two.** The rule this replaces said "never outside
the plugin directory", after an earlier version searched a development machine's absolute path that
an installed bundle would actually have reached. The data directory is deliberate and computed from
the OS, not from anyone's machine, and it is outside the plugin directory for a reason: PCM replaces
the plugin's own directory whenever the plugin is updated, which would discard the download and
every self-update since, and under a system-wide KiCad it is not writable by the user at all.

It waits on the child by default (the IPC API runs it as its own process); `__init__.py`'s
`ActionPlugin.Run()` runs on KiCad's UI thread and so calls `launch_importer(wait=False)`.
**`wait=False` must never block**, which now means more than not waiting on the child: when the
importer still has to be downloaded, `launch_importer` puts the whole bootstrap on a thread of its
own and returns at once, because doing ~200 MB inline on KiCad's UI thread freezes the editor.

It passes the environment through: KiCad exports the IPC socket path, the API token and the project
directory, and `KiCadSharp` reads them from there (`KiCadEnvironment.GetApiToken()` /
`GetDefaultSocketPath()` / `GetProjectDirectory()`). Launched any other way, there is no project
directory, because it comes only from that environment. The connection itself still works since
KiCadSharp 0.3.1 (#99): with no token in the environment, it dials the socket KiCad opens by default,
with an empty token. Since 0.4.0 that path is worked out as KiCad 10 does (kicad-sharp#113):
- `<temp>/kicad/api.sock`, where `<temp>` is the first of `TMPDIR`, `TMP` and `TEMP` that names an
  existing directory, else `/tmp` (on Windows the system temp path; on macOS always `/tmp`);
- on Linux, when nothing is there but a Flathub KiCad's socket exists, that one:
  `~/.var/app/org.kicad.KiCad/cache/tmp/kicad/api.sock` (kicad-sharp#126).

The importer's own `TMPDIR` picks `<temp>`, so one started by hand under another `TMPDIR` than KiCad's
misses the socket. An importer started by hand reaches a running KiCad whose API server is on, and
otherwise falls back to reading the disk.
`plugin/requirements.txt` is deliberately empty; its comment explains why.

**`release.yml` does not stage `plugin/bin/`, and that is now the design rather than a gap** (#128).
A self-contained build is 453 MB, 341 MB of it CEF; the PCM package carries the Python side and the
launcher fetches the rest. Do not add it back.

**`HarfBuzzPreload.Apply()` must stay the first thing on `Main`'s GUI path that touches Avalonia or
CEF (#78)** — which since #128 means after NLog's setup and after `VelopackApp.Build().Run()`, and
before everything else.

CEF loads GTK with `RTLD_GLOBAL`, which brings in the system `libharfbuzz.so.0`. `libHarfBuzzSharp.so` calls its own
`hb_*` functions through lazily bound slots, so those calls land in the system copy and the app dies
with SIGSEGV (exit 139) right after the main window opens. The preload `dlopen`s HarfBuzzSharp with
`RTLD_NOW` before anything else touches it, binding every slot to itself. It deliberately stays
`RTLD_LOCAL`; the file's remarks explain why `RTLD_GLOBAL` or the old `LD_PRELOAD` workaround is wrong.
It only looks in the app's own native directories, never the working directory. No `LD_PRELOAD` is
needed any more.

**Velopack sits above it, and that is not a violation of #78** (#128). `VelopackApp.Build().Run()`
may not return — it runs the `--veloapp-*` hooks and exits, and hands the process to the updater
when a package is staged — so anything above it is work a restart throws away. It loads no graphics
stack at all (verified by decompiling it: it reads the package manifest, may spawn the updater, and
returns), so the preload still happens before the first line of Avalonia or CEF code, which is what
#78 actually requires. Only NLog's setup is above Velopack, because both of those are in-memory and
put Velopack's own account of the start — including "restarting to apply update" — into the
application's log instead of a file in the temporary directory.

### Providers, search and the import engine

- **`IComponentProvider`** (`Services/Interfaces`) is one source; `ComponentProviderRegistry` collects
  them from DI. **Live:** `EasyEdaProvider` and `OctopartProvider` (Nexar, needs the user's token).
  `SnapEdaProvider` and `ComponentSearchEngineProvider` are `SupportsDirectApi => false` until real
  integrations exist (#56, #57). `UltraLibrarianProvider` is browser-only.
- **`EasyEdaProvider` searches JLCPCB's parts library** (`Services/Providers/Jlcpcb/`), with one of two
  sources per search, and every result's `Attribution` names the source used:
  - **The official Components API** (#51) for an LCSC-number query, when the user has entered all three
    credentials (App ID, Access Key, Secret Key). Every call is a signed POST to `open.jlcpcb.com`, using
    the `JOP` HMAC-SHA256 scheme from JLCPCB's public docs. The signing reproduces their worked example.
    The API has **no keyword search**: its other interfaces are bulk feeds, and bulk feeds are never
    used. The endpoint path and response fields come from open-source clients, because JLCPCB documents
    them only inside its console. They are **untested against the live API** (no credentials here).
  - **Otherwise, JLCPCB's website endpoint** `selectSmtComponentList` (#52). It is unofficial and can
    break without notice. The Part Explorer shows a notice while it is in use, and the MCP output lists
    it as a data source. It asks for one page of 25 results, never more.
  - **It has turned quick searches down with 403** (#110), and publishes no limit. The provider has
    its own bucket in `ProviderSearchOptions.ProviderRateLimits`: one request at once, then one every
    3 s, below what was measured to work. A 403 or 429 from the endpoint throws
    `ProviderRateLimitedException`, "JLCPCB is rate-limiting; try again shortly". The aggregator
    reports it in that search (`ProviderRateLimited`) and backs off: no request for 30 s, doubled per
    further refusal up to 2 min, or longer if Retry-After says so (still capped at 2 min). Meanwhile
    each search reports it again without a request, and cached answers are still served. The Part
    Explorer's JLCPCB notice and status line and the MCP "Not answered" line say so, and an MCP call
    that found nothing while a provider was left out is `isError`. Other failures are still only logged.
  - A failed official lookup throws. It never falls back to the website endpoint — **except when JLCPCB
    refuses this application the API altogether** (#126), which is not a failed lookup but a standing
    answer about the account, and must not cost the user the search. JLCPCB grants API access per
    service and the Components API needs the **Parts** permission approved, so an account can hold
    working credentials that it refuses.
    - **Only the two statuses JLCPCB documents as the platform turning the caller away count**
      (https://api.jlcpcb.com/docs/start, "Error Information"): 401 "Unauthorized request. Usually due
      to signature verification failure" → `CredentialsRejected`, and 403 "Forbidden. The request is
      not allowed" → `NotApproved`. A credential holding a character no HTTP header can carry is
      `CredentialsRejected` too, before any request. **Everything else stays strict and throws**: 400,
      500, any other status, and *every* business `code` in a 200 body. JLCPCB documents the
      `{code, message}` envelope and one example code (1001, "Insufficient prepaid balance") and
      publishes no code meaning "this application is not approved for this interface", so none is
      guessed at.
    - `JlcpcbApiAccessDeniedException` carries which one. `EasyEdaProvider` records it in
      `JlcpcbOfficialApiAccess` (one per process, in DI), logs it once, and answers that same search
      from the website endpoint. Later LCSC-number searches go straight there.
    - The memory is keyed to a **hash** of the three credentials, never the values, so editing any of
      them in Settings clears it, as #109 does for the Nexar token. Nothing logs or shows a credential.
    - The Part Explorer's JLCPCB notice says which refusal it was, and Settings says the application
      needs the Parts permission approved.
  - **`--mcp` never uses the official API**, even with credentials stored. JLCPCB's API terms (III.6(9))
    forbid passing API data to third parties, and the MCP server hands every result to the AI client.
    Each container passes a `JlcpcbSourcePolicy` to `AddUltraLibrarianKiCadServices` (a required
    argument): the GUI passes `OfficialApiWhenConfigured`, the MCP container `WebsiteEndpointOnly`.
    `McpServer` refuses to be built with any other, and its data-source line says so.
  - The tscircuit index is gone.
  - Neither source reports CAD availability, so the CAD flags stay `Unknown`.
- **CAD availability is tri-state** (#48, #107): `CadAvailability` is `Unknown` (the default),
  `Available` or `NotAvailable`, and `HasSymbol`, `HasFootprint` and `Has3DModel` use it. "Unknown" and
  "none" are different answers, so never collapse them back to `bool`.
  - Octopart reads Nexar's public `cad { hasKicad has3dModel }`. Do not use `cadModels`: the schema
    marks it **Internal**, and master's misspelled `cadModels { has3DModel }` made Nexar reject every
    Octopart query.
  - `hasKicad` answers for both symbol and footprint.
  - A null `cad` means "none" only in a response without `errors`, because a field outside the token's
    plan can null it too (#109).
  - After an easyeda2kicad import, the row marks the assets that import produced as `Available`.
- **Never fabricate.** A result or a CAD-availability flag appears only if the provider said so. A
  provider that cannot answer is absent — it does not appear with invented values.
- **Failures are not "no results".** Providers use narrow catches that log, and then **rethrow**
  (`EnsureSuccessStatusCode`; a missing token throws `ProviderNotConfiguredException`). The aggregator
  logs the failure and leaves that provider out, and failures are never cached. Honest
  `kicad-ultra/1.0` User-Agent.
- **`PartAggregatorService.StreamAllProvidersAsync`** yields each provider's outcome as it finishes, its
  parts or `ProviderRateLimited`, each on its own `Task.Run`, behind `ProviderResponseCache` (5-minute
  TTL) and `ProviderRateLimiter` (a token bucket per provider, plus the back-off after a refusal).
  `SearchAllProvidersAsync` is the materialising overload the MCP server uses.
- **`MainViewModel` search (ReactiveUI).** `SearchPartsCommand` only emits the normalised query and
  completes at once. If it executed the whole search it would stay disabled for its duration, and a
  new query could not supersede the running one. Its output runs through `Switch()`: a new query drops
  the old search's subscription, which cancels its provider calls. The list is cleared inside the same
  ordered stream as the results, which is what makes stale rows impossible. Keep it that way.
- **`KiCadImportEngine`** does the file work: extract, then symbols, footprints and 3D models, each
  wrapped by `RunStepAsync` so a failing step reports its own `ImportResult` flag.
  `ImportResult.Outcome` judges those flags against `RequestedSteps`: succeeded, partially succeeded
  (the status line names the failed steps), failed, or cancelled (#102). `Success` only means that at
  least one requested step worked, so a partial import has it too; report `Outcome`, not `Success`.

### Import flow

`MainWindow` has two tabs.
- **Part Explorer.** Every row has an import route, or a disabled button whose tip says why (#47):
  - **A result with an LCSC code** imports through the user-installed `easyeda2kicad` CLI (#76),
    always into the EasyEDA library. The code is `PartSearchResult.LcscPartNumber`: JLCPCB's own code
    for EasyEDA / LCSC results, or LCSC's offer SKU, read from the query's `allSellers`, for Octopart.
  - **A result with a manufacturer part number** gets **Find on Ultra Librarian**. It selects the
    UltraLibrarian provider and opens `app.ultralibrarian.com/search?queryText=<MPN>` in the Web
    Browser tab, whose download interception then imports what the user downloads.
  - A provider never fills the part number with the search text. When the source has none, it is
    empty, and Find on Ultra Librarian is disabled.
  - `PackageDownloadUrl` is gone: no provider had a direct CAD archive. Nexar's
    `cad.downloadUrlKicad` exists but is unverified.
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
    - The output **never** goes through `ImportSymbolsAsync` / `ImportFootprintsAsync`: their prefix
      renaming would break the links.
    - A non-zero exit, a timeout or a cancel rolls back: the symbol library is restored from a copy,
      created files are removed, and nothing is registered.
    - Both import paths share one `_importGate` in the engine.

    #88 checked that kicad-cli 10.0.6 loads the output through the registered tables and resolves
    the 3D models. It has not been checked in the KiCad GUI.
- **Web Browser** hosts CEF. `InternalDownloadHandler` (`Views/MainWindow.axaml.cs`) reduces the
  server-supplied file name with `Path.GetFileName` and refuses anything that would resolve outside
  the download directory — keep that containment check. Three rules around it:
  - **Refusing means never calling `Continue`** (#97, #106). `Continue("")` is not a refusal: it
    downloads to CEF's temp directory. A download whose callback is merely released stays pending, so
    the handler also cancels a refused id on its next `OnDownloadUpdated`.
  - **Only downloads the handler accepted can become an import,** and only at the exact path it
    accepted.
  - **Never read Avalonia state on CEF's thread** (#108, #111). The download folder comes from
    `IConfigService`, passed in from `App`. Reading `ViewModel`/`DataContext` there throws, and it
    silently sent every download to the fallback folder.

**Registration in KiCad's library tables is real** (#66), by editing the table files; KiCad 10's IPC API
has no library-table calls (they arrive in 11 — #72). `KiCadLibraryTable` parses with SExpressions but
splices the new row into the original text, so existing bytes are untouched. It refuses tables outside
KiCad's strict grammar, is idempotent, and writes through a temporary file. The project table is
created if missing, `${KIPRJMOD}`-relative; the global table is **never created**.
`KiCadSettingsDirectory` resolves KiCad's config directory for the running version. KiCad does not
reload tables on its own, so the user must reopen the project or restart KiCad.

The engine asks the running KiCad for its version only when it needs that directory: for the global
table, or with no project. It calls `GetVersion` directly with a 5 s deadline as a token:
- KiCadSharp 0.4.0 dials on a thread of its own and waits for the reply without blocking the caller
  (kicad-sharp#127), so the call needs no `Task.Run`.
- The token ends the dial or the wait itself. A `WaitAsync` around the call would only stop
  watching it, and leave it running.
- A `KiCadIpcException` (kicad-sharp#46), or the deadline, falls back to the newest settings
  directory on disk. Nothing else is caught there.

**Which table is the `RegistrationScope` setting** (#71): **Automatic** (the default) picks the project
table when the import goes into a KiCad project, and the global table only when it does not. **Project**
always picks the project table, and with no project the import fails before any library is written.
**Global** always picks the global table, by absolute path. "A project" means that the directory the
libraries are written into holds a `.kicad_pro`. `KiCadImportEngine.SelectLibraryTable` resolves the scope
once per import, for both paths; `--project-relative` follows the resolved table. `config.json` stores
the scope by name. A pre-#71 `AddToGlobalLibrary` is migrated on load and the file is rewritten
without it: `true`, the old default rather than a choice, becomes Automatic, and `false` becomes Project.

**The Ultra Librarian `.zip` path writes symbols through KiCadSharp.** `ImportSymbolsAsync` gives every
symbol the provider prefix by setting `KiCadSymbol.Id`. KiCad 10 refuses the whole library over one
name left behind, and since KiCadSharp 0.4.0 the setter renames everything that carries the name
(kicad-sharp#48, #68):
- the symbol's sub-units, `NAME_1_1`;
- every `(extends …)` that names it and is **still in the same library**.

Two rules follow, and #68's check depends on both:
- **Rename every symbol before moving any.** A derived symbol can come before its parent in the file;
  KiCad's own libraries are in name order. Renamed and moved one at a time, such a symbol leaves before
  its parent is renamed and keeps the old name, and KiCad refuses the library.
- **`AddSymbol` moves the node** out of its source library. The move loop is a plain `foreach` over the
  live `Symbols` view, which since 0.4.0 walks what was there when it started (kicad-sharp#53). Before
  0.4.0 such a walk skipped every other symbol, 18 of 35.

Checked with kicad-cli 10.0.6: 271 imported symbols, 141 of them derived, load and plot. An ERC through
the project's table finds no `lib_symbol_issues` or `lib_symbol_mismatch`.

A new library takes the version of the first library its symbols come from (kicad-sharp#57), and
keeps it for every later import. KiCad reads some content by that version: a lone `~` is empty text
before `20250318`. KiCadSharp does not convert symbols between versions.

The rest of this path:
- **Re-importing replaces** symbols, footprints and 3D models by name and says so (#69).
- **A `.kicad_sym` that exists but fails to load is never overwritten** (#94). The symbol step fails
  with the reason. A new library is started only when the file does not exist.

The easyeda2kicad path never re-saves through KiCadSharp.

### DI wiring — one trap

`Program.ConfigureLogging` and `Program.ConfigureServices` build the GUI container;
`AddUltraLibrarianKiCadServices(JlcpcbSourcePolicy)` (`UltraLibrarianKiCadExtensions.cs`) registers KiCadSharp under the
**keyed** client name `com.ultralibrarian.kicad.importer` (re-exposed unkeyed), the providers, registry,
aggregator, cache, rate limiter, import engine and MCP server. `ISecretStore` is registered in **both**
the GUI and `--mcp` containers. `Services/ServiceConfigurator.cs` is an older registration path that
**nothing calls**. Registrations added there have no effect.

**Logging goes through NLog alone.** `ConfigureLogging` is `ClearProviders().AddNLog()`.
`AddConsole()` is gone because it printed every line twice next to `nlog.config`'s console target
(#98). The log folder is computed in code, never by `nlog.config` itself:
- `Program.SetLogDirectory` sets NLog's global diagnostics context `logDirectory` from
  `SpecialFolders.GetPath(ApplicationData)`, and `nlog.config` reads it as `${gdc:item=logDirectory}`.
- NLog's own `${specialfolder}` rendered `""` on a fresh Linux account for the whole session.
- The internal log (`nlog-internal.log`, errors only) is set there too.
- The `Microsoft.*` and `System.Net.Http.*` `final` rules sit first in `<rules>`, because `final` only
  stops the rules below it.

**No window builds its own `LoggerFactory`** (#121, #123). A window takes `ILoggerFactory` from the
container and creates what it needs from it, as `AboutWindow` and `SettingsWindow` do, with
`NullLoggerFactory.Instance` for the XAML designer. A `LoggerFactory.Create(b => b.AddConsole())`
inside a window sends that window's lines to stdout in a different format and never reaches the log
files, which is exactly what #98 removed from the app.

`KiCadClientSettings` is bound only in the dead path, so KiCad connection settings entered in
Settings are lost on restart.

### The MCP server (`--mcp`)

`Program.RunMcpHostAsync` builds its own container and serves MCP over **stdio** (no network port)
with three read-only tools: `search_components`, `list_providers` and `get_component_details`. **stdout
is the protocol channel**: nothing in MCP mode may write to it.

`Program.ConfigureMcpLogging` builds MCP mode's logging (#80, #95):
- It keeps **only the `FileTarget`s** from `nlog.config`. This is an allowlist, so a console target
  added or renamed later cannot reach stdout.
- It turns `autoReload` off. A reload would re-read the stdout target.
- It adds a stderr target at Info.
- If the log folder or `nlog.config` is unusable, it logs to stderr alone.
- Never let NLog load `nlog.config` by itself in MCP mode, for example through a `GetCurrentClassLogger()`
  before a configuration is assigned.

Verify any change by piping
`initialize`, `notifications/initialized` and a `tools/call`, and checking that every stdout line is
valid JSON-RPC.

### Configuration and secrets

`ConfigService` persists non-secret settings through a single private `ConfigData` DTO to
`<app data>/KiCadUltra/config.json`. Keep it the only persistence path.

**`AppDataFolder` is the only place that names the application-data folder** (#132): `Current`
(`<app data>/KiCadUltra`), `Logs` and `BrowserCache`. `ConfigService`, `Program.SetLogDirectory` and
`App.OnFrameworkInitializationCompleted` take their paths from it rather than spelling the folder
out, which is what makes the next rename one edit. Do not add a fourth spelling.

**API tokens live in the OS credential store** (#54), and so do the three JLCPCB API credentials
(#51): `ISecretStore`, with `PlatformSecretStore` choosing Windows Credential Manager, the macOS
Keychain, or libsecret on Linux (under Flatpak, libsecret goes through the Secret portal). A token
found in an old `config.json` is migrated into the store, and the file is rewritten without it — only
after the store write succeeds. With no working store, tokens are session-only and never written to
the file. **Never log a token value.**

**Renaming a user-visible name means writing a migration** (#132). Three names on the user's disk
changed with the assembly, and each needed code, because a rename on its own silently loses what is
already there:
- `<app data>/UltraLibrarianImporter/` (settings and logs) and `<app data>/UltralibrarianKicad/`
  (CEF's cache) both fold into `<app data>/KiCadUltra/`. `AppDataFolder.Migrate()` moves them, and
  it is **the first thing `Main` does, in both modes** — before `SetLogDirectory`, because that is
  what decides where this run's own log lines go. It is therefore too early to log, so it returns
  its notes and `Program.LogAppDataMigration` writes them once a configuration is assigned. It never
  throws: a folder that could not be moved is reported and left alone. Each step is a rename that
  only runs while the destination does not exist, so a second run finds nothing to do; where both
  exist the new one wins and the old one is left untouched. The browser cache **moves** rather than
  being abandoned — it carries the session cookies CEF is told to persist — and the old folder is
  removed only if the move left it empty.
- The credential-store service name, `UltraLibrarianImporter` → `KiCadUltra`. A store is asked for a
  secret *by service name*, so the rename hides every token the user gave.
  `SecretStoreMigration.Run` copies each key in `ConfigService.SecretKeys` from
  `PlatformSecretStore.CreateLegacy()` to `Create()`, reads each copy back before it counts it, and
  **never deletes the original, whether the copy worked or not**. It runs from
  `Program.CreateSecretStore`, which both containers register for `ISecretStore`. A stamp file,
  `credential-store-migrated.txt` in the app-data folder, ends it: without it a token the user
  cleared in Settings would be copied back from the old service at the next start. A store failure
  (a locked keyring) stops the pass, writes no stamp, and is tried again next start. No value is
  logged or returned.
- The default download directory, `~/Documents/UltraLibrarianDownloads`, deliberately did **not**
  change: it holds the user's files, a configured one in `config.json` has to keep working, and
  nothing in the code depends on its name.

**Never call `Environment.GetFolderPath` directly; use `SpecialFolders.GetPath`** (#70, #93). On Unix,
`GetFolderPath` returns `""` for a folder that does not exist yet, such as `~/.config` on a fresh account,
and `Path.Combine("", …)` then lands in the working directory. The helper uses `DoNotVerify`,
creates nothing, and throws when there is no absolute path (a relative `HOME`). A relative
`DownloadDirectory` already saved in `config.json` is dropped on load, and the file is rewritten.

### Running the app during development — without touching the user's machine

Runtime checks matter here: two real defects (#77, #78) passed every build, format and unit-level
check. To run it:
- a headless display — Xvfb, not the user's `DISPLAY`;
- a temp `HOME` and `XDG_CONFIG_HOME` / `XDG_DATA_HOME` / `XDG_CACHE_HOME`. Since #93 they do not
  need to exist, and leaving them missing is a useful check;
- `DBUS_SESSION_BUS_ADDRESS` pointed at a dead socket, so the libsecret store cannot read or write the
  user's real keyring;
- a **short** `TMPDIR` of your own. CEF puts its `SingletonSocket` under it, and a Unix socket path must
  fit in 108 bytes. A long `TMPDIR`, such as one deep inside a scratch directory, aborts the app at
  startup (exit 133, dumped core) on every run. Point it at a short symlink instead of `/tmp`, so CEF's
  temp files don't land in the real `/tmp` either.

A process that exits on its own under `timeout` is **not** a clean run. Exit 133, 134 or 139, or
"dumped core", means it crashed — SIGTERM from `timeout` does not dump core.

### Tracked work

Open, and each of these waits on something outside this repo:
- **Real credentials or plans:**
  - #48: Octopart's `cad` has not been seen in a live, authenticated response;
  - #51: the official JLCPCB API has not been called with real credentials;
  - #109: Nexar plan-restricted fields.
- **No usable API:** #56 (SnapEDA) and #57 (SamacSys). Research is posted on each; both need access
  granted by the vendor.
- **Upstream code:**
  - #67 / #75: Avalonia 12, waiting on OutSystems/CefGlue#249;
  - #72 and #73: KiCad 11's IPC library commands are declared on KiCad master, but nothing handles
    them. KiCadSharp 0.4.0 wraps them (kicad-sharp#47), gated on `KiCadVersion.SupportsLibraryCommands`,
    and KiCad master answers them `AS_UNHANDLED`. Its `KiCad.ImportLibrary` sends one of them, so do
    not use it for registration yet.

The upstream issues this repo reported are all closed and released: kicad-sharp#46 and kicad-sharp#47
in KiCadSharp 0.4.0, and danielmeza/sexpressions#24 in SExpressions 0.2.0. `KiCadLibraryTable` still
splices rows into the text, and under 0.2.0 it writes the same bytes as under 0.1.3.

**Careful with issue numbers in PR descriptions and commit messages.** GitHub closes an issue when a
PR merges if its text contains `close`, `fix` or `resolve` (in any tense) followed by `#N`, anywhere in
a sentence and whatever comes before it. "This does not close #68" closed #68, and "Close #51 after
someone runs it" closed #51; both had to be reopened. Write `Fixes #N` only when the PR really
finishes the issue, and otherwise use `Part of #N`, or put the number before the verb: "#68 stays open".

## Release state

Nothing publishes to nuget.org any more. `release.yml` builds a Plugin and Content Manager bundle on
`v*` tags, but it stages only the Python files and icons — an installed bundle has no `plugin/bin/`,
so the launcher has nothing to start. Shipping the Avalonia app was consciously deferred (it has no
smoke test and would need Windows code signing); the reasoning is in `docs/releasing.md` and the
header comment of `.github/workflows/release.yml`. Read both before touching packaging.
