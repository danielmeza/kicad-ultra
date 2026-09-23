using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KiCadUltra.Services;

/// <summary>
/// The per-user folder this application keeps its settings, logs and browser cache in, and the
/// one-time move of what the names before #132 left behind.
/// </summary>
/// <remarks>
/// <para>
/// Every path under <see cref="Environment.SpecialFolder.ApplicationData"/> that belongs to this
/// application is named here, so that a rename is one edit rather than a search. The folder is
/// resolved through <see cref="SpecialFolders.GetPath"/>, never
/// <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> (#70, #93).
/// </para>
/// <para>
/// Until #132 the application wrote to two folders under two names: <c>UltraLibrarianImporter</c>
/// for <c>config.json</c> and the logs, and <c>UltralibrarianKicad</c> for CEF's cache. Both fold
/// into <see cref="Current"/>. <see cref="Migrate"/> moves what is already on disk, because the
/// alternative is that a user who has been running this for a year silently loses their settings.
/// </para>
/// </remarks>
public static class AppDataFolder
{
    /// <summary>The folder name under <see cref="Environment.SpecialFolder.ApplicationData"/>.</summary>
    public const string Name = "KiCadUltra";

    /// <summary>Where <c>config.json</c> and the logs were before #132.</summary>
    public const string LegacySettingsName = "UltraLibrarianImporter";

    /// <summary>Where CEF's cache was before #132.</summary>
    public const string LegacyBrowserName = "UltralibrarianKicad";

    private const string BrowserFolderName = "browser";
    private const string LogsFolderName = "logs";

    /// <summary><c>&lt;app data&gt;/KiCadUltra</c>. It may not exist yet; nothing here creates it.</summary>
    /// <exception cref="InvalidOperationException">There is no absolute application-data path.</exception>
    public static string Current =>
        Path.Combine(SpecialFolders.GetPath(Environment.SpecialFolder.ApplicationData), Name);

    /// <summary>The folder <c>nlog.config</c> writes the log files into.</summary>
    public static string Logs => Path.Combine(Current, LogsFolderName);

    /// <summary>The root of CEF's cache, profile and resources.</summary>
    public static string BrowserCache => Path.Combine(Current, BrowserFolderName);

    /// <summary>
    /// Moves what the pre-#132 folders hold into <see cref="Current"/>, and reports what it did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call this before anything computes a path under the application-data folder - in particular
    /// before the log directory is set, because that is what decides where this run's own log lines
    /// go. It is therefore too early to log, which is why the notes are returned rather than
    /// written; the caller logs them once a configuration is assigned.
    /// </para>
    /// <para>
    /// Nothing here throws. A folder that could not be moved is reported and left exactly as it was,
    /// and the application starts with empty settings rather than not at all. It is safe to run
    /// twice: each step is a rename that only happens while the destination does not exist, so a
    /// second run finds nothing to do. Where both the old and the new folder exist - an older build
    /// started in between, or a half-finished move - the new one wins and the old one is left
    /// untouched, with a note saying so.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<AppDataFolderNote> Migrate()
    {
        var notes = new List<AppDataFolderNote>();
        string current;
        string appData;
        try
        {
            appData = SpecialFolders.GetPath(Environment.SpecialFolder.ApplicationData);
            current = Path.Combine(appData, Name);
        }
        catch (InvalidOperationException ex)
        {
            // The same exception ends the application a moment later, in SetLogDirectory or
            // ConfigService. Reporting it rather than throwing keeps this step out of that story.
            notes.Add(new AppDataFolderNote("The application-data folder could not be resolved, so nothing was migrated", ex));
            return notes;
        }

        MigrateSettings(Path.Combine(appData, LegacySettingsName), current, notes);
        MigrateBrowserCache(Path.Combine(appData, LegacyBrowserName), current, notes);
        return notes;
    }

    // config.json, the logs and anything else the old folder holds, in one rename.
    private static void MigrateSettings(string legacy, string current, List<AppDataFolderNote> notes)
    {
        if (!Directory.Exists(legacy))
        {
            return;
        }

        if (Directory.Exists(current))
        {
            notes.Add(new AppDataFolderNote(
                $"Both {current} and {legacy} exist; {current} is used and {legacy} is left untouched (#132)"));
            return;
        }

        try
        {
            Directory.Move(legacy, current);
            notes.Add(new AppDataFolderNote($"Moved the settings and logs from {legacy} to {current} (#132)"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notes.Add(new AppDataFolderNote(
                $"{legacy} could not be moved to {current}; it is left as it is, and this run starts with whatever {current} holds", ex));
        }
    }

    // The cache moves rather than being abandoned or deleted (#132). Abandoning it leaves a folder
    // of a few hundred megabytes on the user's disk for ever, and deleting it throws away the
    // session cookies CEF is told to persist, which is what keeps a user signed in to the sites the
    // Web Browser tab imports from. A move costs a rename, and in the worst case - CEF refusing a
    // profile that has moved - it is still a cache, and it is rebuilt.
    private static void MigrateBrowserCache(string legacyRoot, string current, List<AppDataFolderNote> notes)
    {
        if (!Directory.Exists(legacyRoot))
        {
            return;
        }

        var legacyBrowser = Path.Combine(legacyRoot, BrowserFolderName);
        var browser = Path.Combine(current, BrowserFolderName);
        var moved = false;
        if (Directory.Exists(legacyBrowser))
        {
            if (Directory.Exists(browser))
            {
                notes.Add(new AppDataFolderNote(
                    $"Both {browser} and {legacyBrowser} exist; {browser} is used and {legacyBrowser} is left untouched (#132)"));
                return;
            }

            try
            {
                _ = Directory.CreateDirectory(current);
                Directory.Move(legacyBrowser, browser);
                moved = true;
                notes.Add(new AppDataFolderNote($"Moved the browser cache from {legacyBrowser} to {browser} (#132)"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                notes.Add(new AppDataFolderNote(
                    $"{legacyBrowser} could not be moved to {browser}; it is left as it is, and the browser starts with an empty cache", ex));
                return;
            }
        }

        // Only what this application put there is moved. Anything else - an extracted archive from a
        // build old enough to have unpacked into this folder - stays where its owner left it.
        try
        {
            if (!Directory.EnumerateFileSystemEntries(legacyRoot).Any())
            {
                Directory.Delete(legacyRoot);
                notes.Add(new AppDataFolderNote($"Removed {legacyRoot}, which is now empty (#132)"));
            }
            else if (moved)
            {
                notes.Add(new AppDataFolderNote($"{legacyRoot} still holds files this application did not write, and is left alone"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notes.Add(new AppDataFolderNote($"{legacyRoot} could not be removed", ex));
        }
    }
}

/// <summary>
/// One thing <see cref="AppDataFolder.Migrate"/> did, or could not do. It never carries a secret:
/// the folders it names hold settings, logs and a browser cache, and the credentials are elsewhere.
/// </summary>
/// <param name="Message">What happened, phrased for the log.</param>
/// <param name="Failure">The exception that stopped it, or null when nothing went wrong.</param>
public sealed record AppDataFolderNote(string Message, Exception? Failure = null);
