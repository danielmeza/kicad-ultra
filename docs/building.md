# Building and testing

[CLAUDE.md](../CLAUDE.md) at the repository root is the working guide: the build constraints, the
code-style gates, the architecture and every trap that has already cost someone a day. This page is
the short version.

## The gates

```sh
dotnet build UltraLibrarianImporter.sln -c Release   # also the code-style gate
dotnet build UltraLibrarianImporter.sln -c Debug     # CI builds both
dotnet format UltraLibrarianImporter.sln --severity warn --verify-no-changes
dotnet run --project src/importer/SampleConsole -- --test-parser
```

Warnings fail the build: `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` and `NuGetAuditMode=all`
are on for every project, so a nullable warning, an `.editorconfig` violation or a newly disclosed
CVE in a transitive package all stop it. Run the format check too — it catches two things the build
cannot, and the build catches one it cannot.

**The tests that exist are the plugin bootstrap's**, and CI runs them:

```sh
python3 -m unittest discover -s tests -v
```

They cover the launcher that downloads, verifies and unpacks the application: the checksum refusal, a
resumed download, the archive-entry containment check, and that `wait=False` never blocks KiCad's UI
thread. Standard library only, no network — each test runs against a local server in its own temp
tree.

**There is no .NET test project yet.** `--test-parser` is the one self-contained harness on that
side; `SampleConsole`'s other mode talks to a KiCad with a socket path hardcoded for another machine.
A CI step for it should arrive with the first test project.

## Running it during development

The app is a desktop application that talks to KiCad over its IPC API. To exercise it without
touching your own KiCad configuration, run it against a headless display with a temporary `HOME` and
XDG directories, and a `DBUS_SESSION_BUS_ADDRESS` pointed at a dead socket so the credential store
cannot reach your real keyring.

Two things bite:

- **`TMPDIR` must be short.** CEF puts its singleton socket under it and a Unix socket path must fit
  in 108 bytes; a long one aborts startup with exit 133 and a core dump.
- **`TMPDIR` is also where KiCad's socket is looked for.** Since KiCadSharp 0.4.0 the client resolves
  the default socket the way KiCad 10 does, so an app started with a different `TMPDIR` than KiCad's
  will not find it.

CLAUDE.md's "Running the app during development" has the full recipe.

## Against local checkouts of the libraries

The KiCad file formats and the IPC client live in
[SExpressions](https://github.com/danielmeza/sexpressions) and
[KiCadSharp](https://github.com/danielmeza/kicad-sharp), consumed from nuget.org. To build against
local checkouts of them:

```sh
scripts/use-local-libs.sh ../sexpressions ../kicad-sharp
dotnet build UltraLibrarianImporter.sln -c Release \
    -p:SExpressionsVersion=0.1.0-local.<stamp> -p:KiCadSharpVersion=0.1.0-local.<stamp>
```

The script packs each library into the git-ignored `local-packages/` feed at its own
`-local.<timestamp>` version, so a restore cannot quietly fall back to the published package. Drop
the properties to go back to nuget.org.

There is deliberately no switch to a `ProjectReference`: consuming the real `.nupkg` is what proves
the packages work, and the packages are what break.

## Packages and versions

Package versions are centrally managed in `Directory.Packages.props`, so a `PackageReference` in a
csproj carries an id and nothing else — adding a package means editing both files. `KiCadSharp` and
`SExpressions` take their versions from properties in `Directory.Build.props`, which is what makes
the command-line override above work.

Avalonia is held on the 11.3 line on purpose: the embedded browser is Avalonia 11–only, and moving to
12 is a port, tracked in [#67](https://github.com/danielmeza/kicad-ultra/issues/67).
