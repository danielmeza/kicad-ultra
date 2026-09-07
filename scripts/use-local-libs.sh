#!/usr/bin/env bash
#
# Builds this repository against local checkouts of the split-out libraries instead of the packages
# published on nuget.org.
#
#   scripts/use-local-libs.sh [path-to-sexpressions] [path-to-kicad-sharp]
#     defaults: ../sexpressions  ../kicad-sharp
#
# It packs each library with a distinct local version into ./local-packages (a git-ignored folder
# already registered as a package source in NuGet.config) and prints the build command that selects
# them. There is deliberately no "swap back to ProjectReference" switch: consuming the real .nupkg is
# what proves the packages work together, and the packages are what actually break.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SEXPR_REPO="$(cd "${1:-$REPO_ROOT/../sexpressions}" 2>/dev/null && pwd || true)"
KICAD_REPO="$(cd "${2:-$REPO_ROOT/../kicad-sharp}" 2>/dev/null && pwd || true)"

if [[ -z "$SEXPR_REPO" || ! -f "$SEXPR_REPO/src/SExpressions/SExpressions.csproj" ]]; then
    echo "error: no SExpressions checkout at '${1:-$REPO_ROOT/../sexpressions}'." >&2
    exit 2
fi
if [[ -z "$KICAD_REPO" || ! -f "$KICAD_REPO/src/KiCadSharp/KiCadSharp.csproj" ]]; then
    echo "error: no KiCadSharp checkout at '${2:-$REPO_ROOT/../kicad-sharp}'." >&2
    exit 2
fi

# A distinct version, so restore can never silently fall back to (or prefer) a published package.
LOCAL_VERSION="0.1.0-local.$(date -u +%Y%m%d%H%M%S)"
FEED="$REPO_ROOT/local-packages"
mkdir -p "$FEED"

echo "Packing SExpressions $LOCAL_VERSION from $SEXPR_REPO ..."
dotnet pack "$SEXPR_REPO/src/SExpressions/SExpressions.csproj" \
    -c Release -p:Version="$LOCAL_VERSION" -o "$FEED" >/dev/null

echo "Packing KiCadSharp $LOCAL_VERSION from $KICAD_REPO ..."
# KiCadSharp itself depends on SExpressions, so it is packed against the same local version. Two
# things are needed for that to resolve:
#   - both version properties, because a stable package may not declare a prerelease dependency
#     (NU5104), so while SExpressions is a local prerelease these packages must be prereleases too;
#   - RestoreAdditionalProjectSources, because the kicad-sharp checkout has its own NuGet.config
#     pointing at its own local-packages folder, which is not the feed we just filled.
dotnet pack "$KICAD_REPO/KiCadSharp.slnx" \
    -c Release -p:Version="$LOCAL_VERSION" -p:SExpressionsVersion="$LOCAL_VERSION" \
    -p:RestoreAdditionalProjectSources="$FEED" -o "$FEED" >/dev/null

cat <<MSG

Packed into $FEED

Build against them with:

    dotnet build UltraLibrarianImporter.sln -c Release \\
        -p:SExpressionsVersion=$LOCAL_VERSION \\
        -p:KiCadSharpVersion=$LOCAL_VERSION

The same properties work for restore, test, publish and run. Omit them to go back to the published
packages. Nothing is committed: local-packages/ is git-ignored and the default versions in
Directory.Build.props are unchanged.
MSG
