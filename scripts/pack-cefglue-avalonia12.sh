#!/usr/bin/env bash
#
# Packs CefGlue.Avalonia with Avalonia 12 support into ./local-packages (#67).
#
#   scripts/pack-cefglue-avalonia12.sh [path-to-an-existing-CefGlue-checkout]
#
# TEMPORARY. No CefGlue.Avalonia built against Avalonia 12 is published yet: the port is the open
# upstream pull request https://github.com/OutSystems/CefGlue/pull/249. Until it is merged and
# released, this script builds CefGlue.Avalonia from that pull request at one pinned commit, and the
# build picks it up through $(CefGlueAvaloniaVersion) in Directory.Build.props. CI runs this script
# before restoring, so a clean checkout builds the same way.
#
# Only CefGlue.Avalonia is packed. CefGlue.Common - the CEF bindings, the browser subprocess and the
# native redistributables - is unchanged by the pull request (its source is identical to commit
# 98a0655, which the published CefGlue.Common 120.6099.211 was built from), so it keeps coming from
# nuget.org.
#
# Switching back when upstream publishes: set CefGlueAvaloniaVersion in Directory.Build.props to the
# published version (and CefGlue.Common in Directory.Packages.props to the matching one), delete this
# script and the "Pack CefGlue.Avalonia" step in .github/workflows/ci.yml.
set -euo pipefail

CEFGLUE_REPOSITORY="https://github.com/OutSystems/CefGlue.git"
# Head of https://github.com/OutSystems/CefGlue/pull/249 ("Feature support AvaloniaUI 12").
CEFGLUE_COMMIT="e204172cdf6ad8d048fe0a3a3c0436db8a7c767c"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FEED="$REPO_ROOT/local-packages"

# The version the build asks for, read from Directory.Build.props so the two cannot disagree. It
# names the pull request and the commit: no published package can ever have it, so restore cannot
# fall back to one, and a different commit is a different version rather than a stale cache entry.
VERSION="$(sed -n 's:.*<CefGlueAvaloniaVersion[^>]*>\(.*\)</CefGlueAvaloniaVersion>.*:\1:p' "$REPO_ROOT/Directory.Build.props")"
if [[ -z "$VERSION" ]]; then
    echo "error: no CefGlueAvaloniaVersion in $REPO_ROOT/Directory.Build.props." >&2
    exit 2
fi
if [[ "$VERSION" != *"${CEFGLUE_COMMIT:0:7}"* ]]; then
    echo "error: CefGlueAvaloniaVersion '$VERSION' does not name the pinned commit ${CEFGLUE_COMMIT:0:7}." >&2
    exit 2
fi

PACKAGE="$FEED/CefGlue.Avalonia.$VERSION.nupkg"
if [[ -f "$PACKAGE" ]]; then
    echo "$PACKAGE already exists; nothing to do."
    exit 0
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

if [[ $# -ge 1 ]]; then
    SOURCE="$(cd "$1" && pwd)"
else
    SOURCE="$WORK/CefGlue"
    echo "Fetching CefGlue $CEFGLUE_COMMIT ..."
    git init -q "$SOURCE"
    git -C "$SOURCE" remote add origin "$CEFGLUE_REPOSITORY"
    git -C "$SOURCE" fetch -q --depth 1 origin "$CEFGLUE_COMMIT"
    git -C "$SOURCE" checkout -q FETCH_HEAD
fi

ACTUAL="$(git -C "$SOURCE" rev-parse HEAD)"
if [[ "$ACTUAL" != "$CEFGLUE_COMMIT" ]]; then
    echo "error: $SOURCE is at $ACTUAL, not the pinned $CEFGLUE_COMMIT." >&2
    exit 2
fi

echo "Packing CefGlue.Avalonia $VERSION ..."
# `dotnet build`, not `dotnet pack`: CefGlue's projects set GeneratePackageOnBuild, and `dotnet pack`
# on them fails with NU5026 (it packs before the assembly exists). Packed into a scratch folder and
# only CefGlue.Avalonia copied to the feed: the build also packs CefGlue.Common, which must keep
# coming from nuget.org (see above).
#
# RestorePackagesPath overrides CefGlue's NuGet.config, which keeps a package folder inside the
# checkout. With the usual global folder, the CEF redistributables the build restores are the same
# files this repository's own restore needs, so they are downloaded once, not twice.
dotnet build "$SOURCE/CefGlue.Avalonia/CefGlue.Avalonia.csproj" \
    -c Release -p:Platform=x64 -p:Version="$VERSION" -p:PackageOutputPath="$WORK/out" \
    -p:RestorePackagesPath="${NUGET_PACKAGES:-$HOME/.nuget/packages}" \
    -nologo -clp:ErrorsOnly
mkdir -p "$FEED"
cp "$WORK/out/CefGlue.Avalonia.$VERSION.nupkg" "$FEED/"

echo "Packed $PACKAGE"
