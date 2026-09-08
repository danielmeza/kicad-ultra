# Releasing, and an open decision

**Open decision.** This repository no longer publishes anything: the four packages it used to push to
nuget.org moved out, and each new repository publishes its own. `release.yml` has had its publish jobs
and its `v*` tag trigger removed rather than being left pointed at a solution that no longer exists.
What it should release instead - the importer as a downloadable app, the KiCad plugin bundle, or
nothing at all - is written up at the top of
[`.github/workflows/release.yml`](.github/workflows/release.yml) and is the owner's call.

The stale nuget.org Trusted Publishing policy for `danielmeza/kicad-ultra` should be deleted once that
is settled: it grants push rights for a workflow file that no longer pushes anything.

## History: where the libraries went

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
