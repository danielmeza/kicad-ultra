# Troubleshooting

## The Import button is disabled

Hover it: the tip says which of these it is.

- **easyeda2kicad is not installed.** See [Installing](installing.md#easyeda2kicad-for-lcsc-parts).
  **Settings → Component Providers → easyeda2kicad** shows what was found and where it looked.
- **The result has no LCSC code.** Only EasyEDA / LCSC results, and Octopart results that LCSC sells,
  can be converted. Use **Find on Ultra Librarian** on the same row instead.
- **The provider is still being looked for.** The check runs once at startup; **Check again** re-runs
  it after you install something.

## The imported part does not show up in KiCad

KiCad reads the library tables when a project opens and does not re-read them on its own. Reopen the
project, or restart KiCad. The import log names the table it wrote to, and the library appears there
under the project's name.

## The About window says something other than Connected

| It says | What it means |
|---|---|
| **Not connected to KiCad** | Nothing is listening on the socket. Check **Preferences → Plugins → Enable IPC API**, and that KiCad is running. |
| **KiCad is busy** | A modal dialog owns KiCad's main loop — the stale lock-file prompt is the usual one. Answer it. |
| **KiCad did not answer within 5 s** | Something is listening but not replying. It is usually the same thing: a dialog, or a KiCad still starting. |
| **KiCad answered with an error** | The request reached KiCad and it refused it. The log carries what it said. |

Started by hand rather than by KiCad, the app looks for KiCad's socket under **its own `TMPDIR`**,
falling back to a Flatpak KiCad's socket. A `TMPDIR` different from KiCad's means it will not find it.

## Searches return nothing from EasyEDA / LCSC

JLCPCB rate-limits its website endpoint. The app notices, says "JLCPCB is rate-limiting; try again
shortly" in the Part Explorer, backs off for up to two minutes and keeps serving cached results
meanwhile.

With JLCPCB API credentials whose **Parts** permission was never approved, the official route is
refused; the app reports that once and falls back to the same endpoint for the rest of the session.
See [Where the parts come from](data-sources.md#easyeda--lcsc-two-routes).

## An import failed halfway

Nothing is left behind: the symbol library is restored from a copy, files the run created are
removed, and no library-table row is written. The import log lists anything it could not undo.

A part that imports but reports **partially succeeded** means one asset failed and the others landed
— the status line names which.

## Where are the logs?

`<app data>/UltraLibrarianImporter/logs/`:

| Platform | Path |
|---|---|
| Linux | `~/.config/UltraLibrarianImporter/logs/` |
| Linux, KiCad as a Flatpak | `~/.var/app/org.kicad.KiCad/config/UltraLibrarianImporter/logs/` |
| Windows | `%APPDATA%\UltraLibrarianImporter\logs\` |
| macOS | `~/Library/Application Support/UltraLibrarianImporter/logs/` |

API tokens and credentials are never written there, or to `config.json`: they live in the operating
system's credential store.

When reporting a bug, the log lines around the failure and the KiCad version from the About window
are what make it reproducible.
