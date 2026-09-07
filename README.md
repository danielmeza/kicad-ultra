# kicad-ultra

.NET libraries and tooling for KiCad, plus a KiCad plugin that imports components from
UltraLibrarian.

| Package | What it is |
|---|---|
| [`SExpressionSharp`](https://www.nuget.org/packages/SExpressionSharp) | A small, dependency-free s-expression parser and writer. Nothing KiCad-specific. |
| [`KiCadSharp.Protos`](https://www.nuget.org/packages/KiCadSharp.Protos) | The generated `Kiapi.*` protobuf message types for KiCad's IPC API. |
| [`KiCadSharp`](https://www.nuget.org/packages/KiCadSharp) | The KiCad layer: talks to a running KiCad over its nng IPC API, and reads the on-disk s-expression formats. |
| [`KiCadSharp.Cli`](https://www.nuget.org/packages/KiCadSharp.Cli) | A `dotnet tool` (`kicadsharp`) for inspecting, reformatting, querying and validating KiCad files. |

## The CLI

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

The KiCad `.proto` definitions come from the `submodules/kicad` git submodule, so clone with
submodules (a shallow, blobless checkout is enough):

```bash
git clone --recurse-submodules https://github.com/danielmeza/kicad-ultra
# or, in an existing clone:
git submodule update --init --depth 1 --filter=blob:none submodules/kicad
```

Then:

```bash
dotnet build KiCadSharp.slnx -c Release
dotnet test  KiCadSharp.slnx -c Release
dotnet pack  KiCadSharp.slnx -c Release -o artifacts/packages
```

`KiCadSharp.slnx` is the shipping product — the libraries, the CLI and the tests. It is what CI
builds. `UltraLibrarianImporter.sln` additionally carries the Avalonia importer app; that app still
references an `UltraLibrarianImporter.KiCadBindings` project that no longer exists in the tree and
does not compile until it is ported to `KiCadSharp`.

## Releasing

Packages are published from GitHub Actions on a version tag, using NuGet Trusted Publishing (OIDC) —
there is no API key stored in the repository.

```bash
git tag v0.1.0 && git push origin v0.1.0
```

The workflow derives the package version from the tag (`v0.1.0` → `0.1.0`), packs, publishes to
nuget.org and creates a GitHub Release. It needs one repository secret, `NUGET_USER` (the nuget.org
*profile name*, not an email), and a `release` environment on the repository.

## The UltraLibrarian importer plugin

A KiCad plugin that imports components from UltraLibrarian, in two parts: a Python launcher
(`plugin/`) and an Avalonia UI application (`src/importer/`). The NUKE build under `build/` that used
to package it has not compiled since Nuke 8 removed `Nuke.Common.IO.FileSystemTasks`, and it still
points at a `UltraLibrarianImporter/` directory that no longer exists; it is excluded from the
solutions and from CI until it is repaired.

## License

MIT — see [LICENSE](LICENSE).
