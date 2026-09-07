# kicad-ultra

A KiCad plugin that imports components from UltraLibrarian, in two parts: a Python launcher
(`plugin/`) and an Avalonia UI application (`src/importer/`).

## The libraries moved out

The .NET libraries and the `kicadsharp` tool that used to live here now have their own repositories
and their own packages. This repository consumes them from nuget.org like any other dependency.

| Package | Repository |
|---|---|
| [`SExpressions`](https://www.nuget.org/packages/SExpressions) (was `SExpressionSharp`) | [danielmeza/sexpressions](https://github.com/danielmeza/sexpressions) |
| [`KiCadSharp`](https://www.nuget.org/packages/KiCadSharp) | [danielmeza/kicad-sharp](https://github.com/danielmeza/kicad-sharp) |
| [`KiCadSharp.Protos`](https://www.nuget.org/packages/KiCadSharp.Protos) | [danielmeza/kicad-sharp](https://github.com/danielmeza/kicad-sharp) |
| [`KiCadSharp.Cli`](https://www.nuget.org/packages/KiCadSharp.Cli) (`kicadsharp`) | [danielmeza/kicad-sharp](https://github.com/danielmeza/kicad-sharp) |

Their history moved with them - `git log` in either repository goes back to the commits made here.
`SExpressionSharp` was renamed to `SExpressions` on the way out, before its first push to nuget.org,
because a package ID is permanent afterwards; `KiCadSharp` kept its name on purpose. Each repository's
README explains which and why.

The `submodules/kicad` submodule is gone with them. It existed only so `KiCadSharp.Protos` could
compile KiCad's `.proto` files, and cost a 1.4 GB checkout of the entire KiCad source tree per clone;
`kicad-sharp` vendors those 12 files (128 KB) pinned to a KiCad release tag instead.

## The CLI

Built and released from [danielmeza/kicad-sharp](https://github.com/danielmeza/kicad-sharp), and
useful alongside this repository:

```bash
dotnet tool install --global KiCadSharp.Cli
```

```
kicadsharp parse    <file>                 structural summary: forms, top-level heads, nodes, depth
kicadsharp fmt      <file> [--in-place]    reformat through a verified parse/write round trip
kicadsharp query    <file> <path> [--all]  read a value by a dotted or slashed path
kicadsharp validate <file>                 check the file parses; non-zero exit when it does not
```

`fmt` always re-parses its own output and compares the trees before it prints anything, so a
successful run is the proof that the round trip is lossless. It refuses `--in-place` on a file whose
content the round trip would drop — a file with comments, or a multi-form file the parser can only
read the first form of.

Paths are segments separated by `.` or `/`, and a segment may carry a zero-based occurrence index:

```bash
kicadsharp query board.kicad_pcb kicad_pcb.version
kicadsharp query sheet.kicad_sch 'kicad_sch.symbol[2].lib_id'
kicadsharp query sheet.kicad_sch kicad_sch/symbol/lib_id --all
```

## Building

No submodules, and nothing to pack:

```bash
dotnet build UltraLibrarianImporter.sln -c Release
dotnet test  UltraLibrarianImporter.sln -c Release
```

### Developing against local library sources

`SExpressions` and `KiCadSharp` arrive as `PackageReference`, not project references or submodules.
To build against unreleased local checkouts of them:

```bash
scripts/use-local-libs.sh ../sexpressions ../kicad-sharp
dotnet build UltraLibrarianImporter.sln -c Release \
    -p:SExpressionsVersion=0.1.0-local.<stamp> \
    -p:KiCadSharpVersion=0.1.0-local.<stamp>
```

The script packs each library at a distinct `-local.<timestamp>` version into `local-packages/`, a
git-ignored folder registered as a package source in `NuGet.config`. The distinct version is the
point: restore can never silently fall back to, or prefer, the published package. Omit the properties
to go back to the released ones.

There is deliberately no "swap to `ProjectReference`" switch. Consuming the real `.nupkg` is what
proves the packages work, and the packages are what actually break.

## Releasing

**Open decision.** This repository no longer publishes anything: the four packages it used to push to
nuget.org moved out, and each new repository publishes its own. `release.yml` has had its publish jobs
and its `v*` tag trigger removed rather than being left pointed at a solution that no longer exists.
What it should release instead - the importer as a downloadable app, the KiCad plugin bundle, or
nothing at all - is written up at the top of
[`.github/workflows/release.yml`](.github/workflows/release.yml) and is the owner's call.

The stale nuget.org Trusted Publishing policy for `danielmeza/kicad-ultra` should be deleted once that
is settled: it grants push rights for a workflow file that no longer pushes anything.

## The UltraLibrarian importer plugin

Two parts: a Python launcher (`plugin/`) and an Avalonia UI application (`src/importer/`). The NUKE
build under `build/` that used to package it has not compiled since Nuke 8 removed `Nuke.Common.IO.FileSystemTasks`, and it still
points at a `UltraLibrarianImporter/` directory that no longer exists; it is excluded from the
solutions and from CI until it is repaired.

## License

MIT — see [LICENSE](LICENSE).
