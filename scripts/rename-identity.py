#!/usr/bin/env python3
"""Rename the project's identity: assemblies, namespaces, projects, files and the text that names
them.

    scripts/rename-identity.py UltraLibrarianImporter.UI KiCadUltra \\
        --pair UltraLibrarianImporter=KiCadUltra

It is a dry run unless you pass --apply, and it only ever touches files git tracks, so `git diff`
and `git checkout .` undo everything it did.

What it does, in order:

1. renames tracked files and directories whose *name* contains an old string, with `git mv`
   (deepest first, so a rename inside a renamed directory cannot miss);
2. rewrites the old strings inside tracked text files;
3. reports what it deliberately did not touch, and what is left over afterwards.

Two kinds of occurrence must NOT be swept up, which is why this tool has --protect and a report
rather than being one `sed`:

- **Published identifiers.** A Plugin and Content Manager package id, an action id, a URL someone
  has bookmarked. Changing those orphans what is already installed. They are protected by default;
  see PROTECTED_BY_DEFAULT.
- **Strings that name something on the user's disk**: a settings directory, a credential-store
  service name. Renaming those in code is easy and silently loses the user's settings or tokens
  unless code migrates them. The tool renames them (they are ordinary occurrences) but lists every
  one it changed under "review", so the migration cannot be forgotten.
"""
import argparse
import pathlib
import re
import subprocess
import sys

# Occurrences matching these are never rewritten. Anything published under a name that outlives a
# rename belongs here.
PROTECTED_BY_DEFAULT = [
    r"com\.github\.danielmeza\.kicad-ultralibrarian-importer",  # PCM package id: installed copies key on it
    r"kicad-ultralibrarian-importer\.import",                    # the plugin action id KiCad stores
]

# Files whose contents are never rewritten, whatever they contain.
SKIP_DIRS = {".git", "bin", "obj", "local-packages", "node_modules"}
SKIP_SUFFIXES = {".png", ".jpg", ".jpeg", ".gif", ".ico", ".svg", ".zip", ".nupkg", ".dll", ".so",
                 ".dylib", ".exe", ".step", ".wrl", ".pdf"}

# An occurrence inside a quoted string is usually a name the running program uses: a directory, a
# service name, a window title. Those are the ones a rename can break silently.
QUOTED = re.compile(r"""["']([^"'\n]*)["']""")


def tracked_files(root: pathlib.Path) -> list[pathlib.Path]:
    out = subprocess.run(["git", "-C", str(root), "ls-files", "-z"],
                         capture_output=True, check=True).stdout
    paths = [root / p.decode() for p in out.split(b"\0") if p]
    return [p for p in paths if not (SKIP_DIRS & set(p.parts)) and p.suffix.lower() not in SKIP_SUFFIXES]


def protected_spans(line: str, protectors: list[re.Pattern[str]]) -> list[tuple[int, int]]:
    """Where in this line an occurrence must be left alone."""
    return [m.span() for p in protectors for m in p.finditer(line)]


def rewrite_line(line: str, pairs: list[tuple[str, str]],
                 protectors: list[re.Pattern[str]]) -> tuple[str, int, list[str]]:
    """The line with every unprotected occurrence replaced, in ONE left-to-right pass.

    One pass matters: replacing in place while holding offsets from the text before the replacement
    corrupts any line with two occurrences, and a solution file's project line has three.
    """
    spans = protected_spans(line, protectors)
    replacements = {old: new for old, new in pairs}
    # Longest alternative first, so UltraLibrarianImporter.UI wins over UltraLibrarianImporter.
    pattern = re.compile("|".join(re.escape(old) for old, _ in pairs))
    changed = 0
    kept: list[str] = []

    def replace(match: re.Match[str]) -> str:
        nonlocal changed
        if any(start <= match.start() < end for start, end in spans):
            kept.append(match.group(0))
            return match.group(0)
        changed += 1
        return replacements[match.group(0)]

    return pattern.sub(replace, line), changed, kept


def plan_renames(root: pathlib.Path, pairs: list[tuple[str, str]]) -> list[tuple[pathlib.Path, pathlib.Path]]:
    """Paths to rename, deepest first so a parent's rename never invalidates a child's."""
    planned: list[tuple[pathlib.Path, pathlib.Path]] = []
    seen: set[pathlib.Path] = set()
    for path in tracked_files(root):
        for part_index, part in enumerate(path.relative_to(root).parts):
            renamed = part
            for old, new in pairs:
                renamed = renamed.replace(old, new)
            if renamed == part:
                continue
            source = root / pathlib.Path(*path.relative_to(root).parts[:part_index + 1])
            if source in seen:
                continue
            seen.add(source)
            planned.append((source, source.with_name(renamed)))
    planned.sort(key=lambda pair: len(pair[0].parts), reverse=True)
    return planned


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("old", help="the string to replace, for example UltraLibrarianImporter.UI")
    parser.add_argument("new", help="what to replace it with, for example KiCadUltra")
    parser.add_argument("--pair", action="append", default=[], metavar="OLD=NEW",
                        help="another replacement, applied in the same pass (repeatable)")
    parser.add_argument("--protect", action="append", default=[], metavar="REGEX",
                        help="never rewrite an occurrence this matches (repeatable)")
    parser.add_argument("--apply", action="store_true", help="make the changes (default: dry run)")
    parser.add_argument("--root", default=".", help="repository root (default: the current directory)")
    args = parser.parse_args()

    root = pathlib.Path(args.root).resolve()
    pairs = [(args.old, args.new)]
    for extra in args.pair:
        if "=" not in extra:
            parser.error(f"--pair needs OLD=NEW, got {extra!r}")
        old, new = extra.split("=", 1)
        pairs.append((old, new))
    # Longest first: UltraLibrarianImporter.UI must be replaced before UltraLibrarianImporter.
    pairs.sort(key=lambda pair: len(pair[0]), reverse=True)

    protectors = [re.compile(p) for p in PROTECTED_BY_DEFAULT + args.protect]

    if args.apply:
        dirty = subprocess.run(["git", "-C", str(root), "status", "--porcelain"],
                               capture_output=True, text=True, check=True).stdout.strip()
        if dirty:
            print("refusing to --apply with uncommitted changes; commit or stash first:", file=sys.stderr)
            print(dirty, file=sys.stderr)
            return 2

    renames = plan_renames(root, pairs)
    edits: dict[pathlib.Path, int] = {}
    review: list[str] = []
    protected: list[str] = []

    for path in tracked_files(root):
        try:
            text = path.read_text(encoding="utf-8")
        except (UnicodeDecodeError, FileNotFoundError):
            continue
        if not any(old in text for old, _ in pairs):
            continue

        changed = 0
        lines = text.splitlines(keepends=True)
        for number, line in enumerate(lines, start=1):
            rewritten, count, kept = rewrite_line(line, pairs, protectors)
            changed += count
            if kept:
                protected.append(f"{path.relative_to(root)}:{number}: {line.strip()[:110]}")
            if count and any(old in quoted.group(1) for quoted in QUOTED.finditer(line)
                             for old, _ in pairs):
                review.append(f"{path.relative_to(root)}:{number}: {line.strip()[:110]}")
            lines[number - 1] = rewritten
        if changed:
            edits[path] = changed
            if args.apply:
                path.write_text("".join(lines), encoding="utf-8")

    print(f"{'APPLIED' if args.apply else 'DRY RUN'}: {' , '.join(f'{o} -> {n}' for o, n in pairs)}\n")

    print(f"== {len(renames)} path(s) to rename")
    for source, target in renames:
        print(f"   {source.relative_to(root)}  ->  {target.name}")
        if args.apply:
            subprocess.run(["git", "-C", str(root), "mv", str(source), str(target)], check=True)

    print(f"\n== {sum(edits.values())} occurrence(s) in {len(edits)} file(s)")
    for path, count in sorted(edits.items(), key=lambda item: -item[1])[:25]:
        print(f"   {count:4d}  {path.relative_to(root)}")
    if len(edits) > 25:
        print(f"   … and {len(edits) - 25} more file(s)")

    if protected:
        print(f"\n== {len(protected)} occurrence(s) left alone (protected)")
        for line in protected[:20]:
            print(f"   {line}")

    if review:
        print(f"\n== {len(review)} occurrence(s) inside quoted strings — REVIEW THESE")
        print("   A name in a string is often a directory, a service or a title the running program")
        print("   uses. Renaming it without migrating loses the user's settings or credentials.")
        for line in dict.fromkeys(review):
            print(f"   {line}")

    if args.apply:
        left = subprocess.run(["git", "-C", str(root), "grep", "-n", "-I", "--", pairs[0][0]],
                              capture_output=True, text=True).stdout.strip()
        print("\n== leftovers after the rename")
        print(left if left else f"   none: no tracked file mentions {pairs[0][0]} any more")
        print("""
Next, in this order:

  dotnet format <solution> --severity warn            # NOT --verify-no-changes yet
  dotnet build  <solution> -c Release
  dotnet build  <solution> -c Debug
  dotnet format <solution> --severity warn --verify-no-changes

The formatter has to run first and be allowed to write: a namespace rename changes where each
`using` sorts, so every file that imports the renamed namespace fails IMPORTS ordering until it is
re-sorted. Measured on this repository: 77 files, and the build is clean afterwards.

Then deal with the "review" list above. A directory or service name that moved needs code that
migrates what is already on the user's disk, or their settings and stored credentials are orphaned.""")
    else:
        print("\nNothing was changed. Re-run with --apply to make it so.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
