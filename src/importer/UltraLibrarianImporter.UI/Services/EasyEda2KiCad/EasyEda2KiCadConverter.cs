using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace UltraLibrarianImporter.UI.Services.EasyEda2KiCad;

/// <summary>How an easyeda2kicad run ended.</summary>
public enum EasyEda2KiCadRunStatus
{
    /// <summary>It exited with code 0.</summary>
    Completed,

    /// <summary>It exited with another code, or could not be started.</summary>
    Failed,

    /// <summary>It was still running when the timeout expired, and was stopped.</summary>
    TimedOut,

    /// <summary>The user cancelled the import while it ran, and it was stopped.</summary>
    Cancelled,
}

/// <summary>
/// What one easyeda2kicad run did to the library it wrote into.
/// </summary>
/// <param name="Status">How the run ended. Only <see cref="EasyEda2KiCadRunStatus.Completed"/> is a success.</param>
/// <param name="Summary">One sentence on how it ended, for the import log.</param>
/// <param name="StandardError">What the tool printed to stderr, which is where it reports progress and errors.</param>
/// <param name="SymbolLibraryPath">The <c>.kicad_sym</c> it was told to write.</param>
/// <param name="SymbolLibraryWritten">The run created or rewrote <paramref name="SymbolLibraryPath"/>.</param>
/// <param name="FootprintLibraryPath">The <c>.pretty</c> directory it was told to write into.</param>
/// <param name="FootprintFiles">Footprint files the run created or rewrote.</param>
/// <param name="ModelFiles">3D model files the run created or rewrote.</param>
/// <param name="RollbackNotes">What was undone after a run that did not complete.</param>
public sealed record EasyEda2KiCadConversion(
    EasyEda2KiCadRunStatus Status,
    string Summary,
    string StandardError,
    string SymbolLibraryPath,
    bool SymbolLibraryWritten,
    string FootprintLibraryPath,
    IReadOnlyList<string> FootprintFiles,
    IReadOnlyList<string> ModelFiles,
    IReadOnlyList<string> RollbackNotes)
{
    public bool Succeeded => Status == EasyEda2KiCadRunStatus.Completed;
}

/// <summary>
/// Converts one LCSC part into a KiCad library by running the user-installed easyeda2kicad as a
/// separate process (#76), using only its documented command-line flags:
/// <c>--lcsc_id</c>, <c>--full</c>/<c>--symbol</c>/<c>--footprint</c>/<c>--3d</c>, <c>--output</c>,
/// <c>--overwrite</c> and <c>--project-relative</c>. What it produced is read back from the files
/// themselves, which are in KiCad's formats; its console messages are only passed on to the user.
/// </summary>
/// <remarks>
/// <para>
/// <c>--output &lt;dir&gt;/&lt;name&gt;</c> makes it write <c>&lt;name&gt;.kicad_sym</c>,
/// <c>&lt;name&gt;.pretty/</c> and <c>&lt;name&gt;.3dshapes/</c> in <c>&lt;dir&gt;</c>, which must already
/// exist. It writes them in place, straight into the library the user will register: its footprints
/// refer to their 3D models by path, and its symbols to their footprints by <c>&lt;name&gt;:</c>, so
/// the files cannot be produced somewhere else and moved. <c>--overwrite</c> replaces a part that is
/// already in the library instead of adding a second copy (the duplicate #69 describes on the other
/// path).
/// </para>
/// <para>
/// Nothing here reads, loads or re-saves those files: they reach KiCad exactly as the tool wrote
/// them, and never pass through KiCadSharp.
/// </para>
/// <para>
/// Because the run writes into the live library, a run that does not complete (non-zero exit,
/// timeout, cancellation) is rolled back as far as it can be: the symbol library, which holds every
/// other imported part too, is restored from a copy taken before the run, and footprint and 3D files
/// the run created are removed. Existing footprint or 3D files it rewrote cannot be restored, and are
/// named in <see cref="EasyEda2KiCadConversion.RollbackNotes"/>.
/// </para>
/// </remarks>
public sealed partial class EasyEda2KiCadConverter
{
    /// <summary>
    /// How long one conversion may take. It downloads the part's data and its STEP model, which runs to
    /// several megabytes for larger packages.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private readonly ILogger<EasyEda2KiCadConverter> _logger;

    public EasyEda2KiCadConverter(ILogger<EasyEda2KiCadConverter> logger)
    {
        _logger = logger;
    }

    /// <summary>How long a run may take before it is stopped.</summary>
    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>
    /// True for an LCSC part number: <c>C</c> followed by ASCII digits and nothing else. Checked
    /// before any value reaches the command line.
    /// </summary>
    public static bool IsLcscPartNumber(string? value) => value is not null && LcscPartNumberPattern().IsMatch(value);

    // [0-9] rather than \d, which also matches non-ASCII digits, and \z rather than $, which also
    // matches before a trailing newline.
    [GeneratedRegex(@"^C[0-9]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex LcscPartNumberPattern();

    /// <summary>
    /// Runs easyeda2kicad for <paramref name="lcscPartNumber"/> into the library at
    /// <paramref name="outputBase"/>, and reports what it wrote.
    /// </summary>
    /// <param name="command">How to start easyeda2kicad, from <see cref="EasyEda2KiCadLocator"/>.</param>
    /// <param name="lcscPartNumber">The part; must pass <see cref="IsLcscPartNumber"/>.</param>
    /// <param name="importType">Which of symbol, footprint and 3D model to convert.</param>
    /// <param name="outputBase">The library's directory and name, without extension. The directory must exist.</param>
    /// <param name="projectDirectory">When set, 3D model paths are stored relative to this project
    /// (<c>${KIPRJMOD}/…</c>, easyeda2kicad's <c>--project-relative</c>). It must contain
    /// <paramref name="outputBase"/>. <see langword="null"/> for absolute paths.</param>
    /// <param name="cancellationToken">Stops the run; what it wrote is then rolled back.</param>
    public async Task<EasyEda2KiCadConversion> ConvertAsync(
        EasyEda2KiCadCommand command,
        string lcscPartNumber,
        ImportType importType,
        string outputBase,
        string? projectDirectory,
        CancellationToken cancellationToken)
    {
        if (!IsLcscPartNumber(lcscPartNumber))
        {
            throw new ArgumentException($"'{lcscPartNumber}' is not an LCSC part number.", nameof(lcscPartNumber));
        }

        // easyeda2kicad's working directory. It writes nothing there with the flags used here, and
        // this keeps it that way should that change. With --project-relative it has to be the project
        // instead: the tool stores the 3D path relative to its working directory, and fails when the
        // library is not below it.
        var scratchDirectory = Path.Combine(Path.GetTempPath(), $"easyeda2kicad-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(scratchDirectory);

        try
        {
            var snapshot = LibrarySnapshot.Take(outputBase, scratchDirectory);
            List<string> arguments = BuildArguments(command, lcscPartNumber, importType, outputBase, projectDirectory is not null);

            _logger.LogInformation("Running {Command} for {Part} into {Output}", command.DisplayCommand, lcscPartNumber, outputBase);

            ProcessOutcome outcome;
            try
            {
                outcome = await ExternalProcess.RunAsync(
                    command.FileName,
                    arguments,
                    projectDirectory ?? scratchDirectory,
                    EasyEda2KiCadLocator.ChildEnvironment(),
                    Timeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Unfinished(snapshot, EasyEda2KiCadRunStatus.Cancelled, "The import was cancelled while easyeda2kicad was running, and easyeda2kicad was stopped.", string.Empty);
            }
            catch (Win32Exception ex)
            {
                // The locator started it moments ago, so it was removed or changed in between. Nothing ran.
                _logger.LogWarning(ex, "{Command} could not be started", command.DisplayCommand);
                return Unfinished(snapshot, EasyEda2KiCadRunStatus.Failed, $"{command.DisplayCommand} could not be started: {ex.Message}", string.Empty);
            }

            if (outcome.TimedOut)
            {
                return Unfinished(snapshot, EasyEda2KiCadRunStatus.TimedOut, $"easyeda2kicad did not finish within {Timeout.TotalSeconds:0} seconds and was stopped.", outcome.StandardError);
            }

            if (outcome.ExitCode != 0)
            {
                // A failure, whatever it wrote before it failed: never "success with nothing".
                return Unfinished(snapshot, EasyEda2KiCadRunStatus.Failed, $"easyeda2kicad failed with exit code {outcome.ExitCode}.", outcome.StandardError);
            }

            var conversion = new EasyEda2KiCadConversion(
                EasyEda2KiCadRunStatus.Completed,
                $"easyeda2kicad converted {lcscPartNumber}.",
                outcome.StandardError,
                snapshot.SymbolLibraryPath,
                snapshot.SymbolLibraryWritten(),
                snapshot.FootprintLibraryPath,
                snapshot.WrittenFootprints(),
                snapshot.WrittenModels(),
                []);

            _logger.LogInformation(
                "easyeda2kicad converted {Part}: symbol library written {Symbol}, {Footprints} footprint file(s), {Models} 3D model file(s)",
                lcscPartNumber, conversion.SymbolLibraryWritten, conversion.FootprintFiles.Count, conversion.ModelFiles.Count);
            return conversion;
        }
        finally
        {
            try
            {
                Directory.Delete(scratchDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not remove {Directory}", scratchDirectory);
            }
        }
    }

    private EasyEda2KiCadConversion Unfinished(LibrarySnapshot snapshot, EasyEda2KiCadRunStatus status, string summary, string standardError)
    {
        IReadOnlyList<string> notes = snapshot.RollBack();
        _logger.LogWarning("{Summary} Rolled back: {Notes}", summary, string.Join(" ", notes));
        return new EasyEda2KiCadConversion(
            status, summary, standardError, snapshot.SymbolLibraryPath, false, snapshot.FootprintLibraryPath, [], [], notes);
    }

    private static List<string> BuildArguments(EasyEda2KiCadCommand command, string lcscPartNumber, ImportType importType, string outputBase, bool projectRelative)
    {
        List<string> arguments = [.. command.LeadingArguments, "--lcsc_id", lcscPartNumber];

        if ((importType & ImportType.All) == ImportType.All)
        {
            arguments.Add("--full");
        }
        else
        {
            if (importType.HasFlag(ImportType.Symbol))
            {
                arguments.Add("--symbol");
            }

            if (importType.HasFlag(ImportType.Footprint))
            {
                arguments.Add("--footprint");
            }

            if (importType.HasFlag(ImportType.Model3D))
            {
                arguments.Add("--3d");
            }
        }

        arguments.AddRange(["--output", outputBase, "--overwrite"]);

        if (projectRelative)
        {
            arguments.Add("--project-relative");
        }

        return arguments;
    }

    /// <summary>
    /// The state of one easyeda2kicad library before a run: enough to tell afterwards which files the
    /// run wrote, and to undo what it can if the run does not complete.
    /// </summary>
    private sealed class LibrarySnapshot
    {
        private readonly FileStamp? _symbolLibrary;
        private readonly string? _symbolLibraryBackup;
        private readonly DirectorySnapshot _footprints;
        private readonly DirectorySnapshot _models;

        private LibrarySnapshot(string outputBase, string backupDirectory)
        {
            SymbolLibraryPath = outputBase + ".kicad_sym";
            FootprintLibraryPath = outputBase + ".pretty";

            _symbolLibrary = FileStamp.Of(SymbolLibraryPath);
            if (_symbolLibrary is not null)
            {
                _symbolLibraryBackup = Path.Combine(backupDirectory, "before.kicad_sym");
                File.Copy(SymbolLibraryPath, _symbolLibraryBackup);
            }

            _footprints = new DirectorySnapshot(FootprintLibraryPath);
            _models = new DirectorySnapshot(outputBase + ".3dshapes");
        }

        public string SymbolLibraryPath { get; }

        public string FootprintLibraryPath { get; }

        public static LibrarySnapshot Take(string outputBase, string backupDirectory) => new(outputBase, backupDirectory);

        public bool SymbolLibraryWritten() => FileStamp.Of(SymbolLibraryPath) is { } now && now != _symbolLibrary;

        public IReadOnlyList<string> WrittenFootprints() => _footprints.Written();

        public IReadOnlyList<string> WrittenModels() => _models.Written();

        /// <summary>Undoes what can be undone, and says what it did and what it could not.</summary>
        public IReadOnlyList<string> RollBack()
        {
            var notes = new List<string>();

            if (SymbolLibraryWritten())
            {
                try
                {
                    if (_symbolLibraryBackup is not null)
                    {
                        File.Copy(_symbolLibraryBackup, SymbolLibraryPath, overwrite: true);
                        notes.Add($"Restored {SymbolLibraryPath} to what it was before the import.");
                    }
                    else
                    {
                        File.Delete(SymbolLibraryPath);
                        notes.Add($"Removed {SymbolLibraryPath}, which the unfinished import had created.");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    notes.Add($"Could not restore {SymbolLibraryPath}: {ex.Message}");
                }
            }

            _footprints.RollBack(notes);
            _models.RollBack(notes);
            return notes;
        }
    }

    /// <summary>The files of one output directory before a run.</summary>
    private sealed class DirectorySnapshot
    {
        private readonly string _directory;
        private readonly bool _existed;
        private readonly Dictionary<string, FileStamp> _files;

        public DirectorySnapshot(string directory)
        {
            _directory = directory;
            _existed = Directory.Exists(directory);
            _files = _existed
                ? Directory.EnumerateFiles(directory).ToDictionary(file => file, file => FileStamp.Of(file) ?? default)
                : [];
        }

        /// <summary>Files that are new, or whose size or modification time changed.</summary>
        public IReadOnlyList<string> Written() =>
            Directory.Exists(_directory)
                ? Directory.EnumerateFiles(_directory)
                    .Where(file => !_files.TryGetValue(file, out FileStamp before) || FileStamp.Of(file) != before)
                    .Order(StringComparer.Ordinal)
                    .ToList()
                : [];

        /// <summary>Removes the files the run created, and the directory if the run created that too.</summary>
        public void RollBack(List<string> notes)
        {
            var created = new List<string>();
            var rewritten = new List<string>();
            foreach (var file in Written())
            {
                if (!_files.ContainsKey(file))
                {
                    try
                    {
                        File.Delete(file);
                        created.Add(Path.GetFileName(file));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        notes.Add($"Could not remove {file}: {ex.Message}");
                    }
                }
                else
                {
                    rewritten.Add(Path.GetFileName(file));
                }
            }

            if (created.Count > 0)
            {
                notes.Add($"Removed {created.Count} file(s) the unfinished import had created in {_directory}: {string.Join(", ", created)}.");
            }

            if (rewritten.Count > 0)
            {
                notes.Add($"Not restored: the unfinished import rewrote {rewritten.Count} existing file(s) in {_directory}: {string.Join(", ", rewritten)}.");
            }

            try
            {
                if (!_existed && Directory.Exists(_directory) && !Directory.EnumerateFileSystemEntries(_directory).Any())
                {
                    Directory.Delete(_directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                notes.Add($"Could not remove the empty directory {_directory}: {ex.Message}");
            }
        }
    }

    /// <summary>A file's size and last write time: what changes when a file is rewritten.</summary>
    private readonly record struct FileStamp(long Length, DateTime LastWriteTimeUtc)
    {
        public static FileStamp? Of(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.Length, info.LastWriteTimeUtc) : null;
        }
    }
}
