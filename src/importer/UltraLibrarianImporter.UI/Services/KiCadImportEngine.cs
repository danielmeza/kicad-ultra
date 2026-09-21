using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using KiCadSharp;
using KiCadSharp.Documents;

using Microsoft.Extensions.Logging;

using SExpressions;

using UltraLibrarianImporter.UI.Services.EasyEda2KiCad;
using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services;

public class KiCadImportEngine : IKiCadImportEngine
{
    /// <summary>How long to wait for KiCad to report its version before looking on disk instead.</summary>
    private static readonly TimeSpan KiCadQueryTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How many lines of easyeda2kicad's stderr go into an import's details.</summary>
    private const int MaxToolOutputLines = 60;

    private readonly KiCad _kicad;
    private readonly EasyEda2KiCadLocator _easyEda2KiCadLocator;
    private readonly EasyEda2KiCadConverter _easyEda2KiCadConverter;
    private readonly ILogger<KiCadImportEngine> _logger;

    // One import at a time, whichever path it comes from: two at once could write the same library,
    // or rewrite the same library table and lose one of the two new rows.
    private readonly SemaphoreSlim _importGate = new(1, 1);

    public KiCadImportEngine(
        KiCad kicad,
        EasyEda2KiCadLocator easyEda2KiCadLocator,
        EasyEda2KiCadConverter easyEda2KiCadConverter,
        ILogger<KiCadImportEngine> logger)
    {
        _kicad = kicad;
        _easyEda2KiCadLocator = easyEda2KiCadLocator;
        _easyEda2KiCadConverter = easyEda2KiCadConverter;
        _logger = logger;
    }

    public async Task<ImportResult> ImportAsync(
        IComponentProvider provider,
        string packageFilePath,
        ImportType importType,
        ImportOptions options)
    {
        await _importGate.WaitAsync();
        try
        {
            return await ImportPackageAsync(provider, packageFilePath, importType, options);
        }
        finally
        {
            _ = _importGate.Release();
        }
    }

    private async Task<ImportResult> ImportPackageAsync(
        IComponentProvider provider,
        string packageFilePath,
        ImportType importType,
        ImportOptions options)
    {
        var result = new ImportResult();
        var tempDir = Path.Combine(Path.GetTempPath(), $"{provider.DefaultLibraryName}_Import_{Guid.NewGuid()}");

        try
        {
            _logger.LogInformation("Importing {Provider} component from {FilePath}", provider.DisplayName, packageFilePath);

            ResolveProjectContext(options, provider.DefaultLibraryName, out var projectDirectory, out var projectName);

            // Before extracting, so a refused import leaves the downloaded package for the next attempt.
            if (SelectLibraryTable(options, projectDirectory, result) is not { } table)
            {
                return result;
            }

            _logger.LogDebug("Extracting package via provider {Provider} into {TempDir}", provider.DisplayName, tempDir);
            ProviderExtractionResult extraction = await provider.ExtractPackageAsync(packageFilePath, tempDir);

            var success = false;

            if (importType.HasFlag(ImportType.Symbol))
            {
                var symbolSuccess = await RunStepAsync("Symbol import", () => ImportSymbolsAsync(extraction, projectDirectory, projectName, options, table, provider, result), result);
                result.SymbolImportSuccess = symbolSuccess;
                success |= symbolSuccess;
                result.Details.Add($"Symbol import: {(symbolSuccess ? "Success" : "Failed")}");
            }

            if (importType.HasFlag(ImportType.Footprint))
            {
                var footprintSuccess = await RunStepAsync("Footprint import", () => ImportFootprintsAsync(extraction, projectDirectory, projectName, options, table, provider, result), result);
                result.FootprintImportSuccess = footprintSuccess;
                success |= footprintSuccess;
                result.Details.Add($"Footprint import: {(footprintSuccess ? "Success" : "Failed")}");
            }

            if (importType.HasFlag(ImportType.Model3D))
            {
                var modelSuccess = await RunStepAsync("3D Model import", () => Import3DModelsAsync(extraction, projectDirectory, provider, result), result);
                result.Model3DImportSuccess = modelSuccess;
                success |= modelSuccess;
                result.Details.Add($"3D Model import: {(modelSuccess ? "Success" : "Failed")}");
            }

            result.Success = success;

            if (options.CleanupAfterImport && File.Exists(packageFilePath))
            {
                try
                {
                    File.Delete(packageFilePath);
                    _logger.LogDebug("Cleaned up downloaded file: {FilePath}", packageFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete downloaded file: {FilePath}", packageFilePath);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing {Provider} component: {Message}", provider.DisplayName, ex.Message);
            result.Success = false;
            result.Details.Add($"Error: {ex.Message}");
            return result;
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up temp directory: {TempDir}", tempDir);
                }
            }
        }
    }

    public async Task<ImportResult> ImportLcscPartAsync(
        IComponentProvider provider,
        string lcscPartNumber,
        ImportType importType,
        ImportOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = new ImportResult();

        if (!EasyEda2KiCadConverter.IsLcscPartNumber(lcscPartNumber))
        {
            result.Details.Add($"'{lcscPartNumber}' is not an LCSC part number (C followed by digits), so easyeda2kicad was not run.");
            return result;
        }

        if ((importType & ImportType.All) == 0)
        {
            result.Details.Add("Nothing to import: choose a symbol, a footprint or a 3D model.");
            return result;
        }

        try
        {
            await _importGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Cancelled(result, "The import was cancelled before it started. Nothing was changed.");
        }

        try
        {
            return await ImportLcscPartCoreAsync(provider, lcscPartNumber, importType, options, result, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Only possible while looking for the tool: a cancelled conversion is rolled back and
            // reported by the converter itself, as a result rather than an exception.
            return Cancelled(result, "The import was cancelled before easyeda2kicad ran. Nothing was changed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing LCSC part {Part}: {Message}", lcscPartNumber, ex.Message);
            result.Success = false;
            result.Details.Add($"Error: {ex.Message}");
            return result;
        }
        finally
        {
            _ = _importGate.Release();
        }
    }

    /// <summary>
    /// The easyeda2kicad path (#76). The tool writes the library itself, straight into the directory
    /// the other path would use, and this only registers it. The files are deliberately not passed
    /// through <see cref="ImportSymbolsAsync"/> and <see cref="ImportFootprintsAsync"/>: those re-save
    /// symbols with KiCadSharp's writer, whose output KiCad 10 rejects (#68), and rename every symbol
    /// and footprint with the provider prefix, which would break the links between the tool's
    /// symbols, footprints and 3D models.
    /// </summary>
    private async Task<ImportResult> ImportLcscPartCoreAsync(
        IComponentProvider provider,
        string lcscPartNumber,
        ImportType importType,
        ImportOptions options,
        ImportResult result,
        CancellationToken cancellationToken)
    {
        EasyEda2KiCadDetection detection = await _easyEda2KiCadLocator.LocateAsync(options.EasyEda2KiCadPath, cancellationToken);
        if (detection.Command is null)
        {
            // No fallback: without the tool there is nothing honest to import.
            EasyEda2KiCadInstallHelp help = EasyEda2KiCadLocator.InstallHelp;
            result.Details.Add("easyeda2kicad was not found, so EasyEDA / LCSC parts cannot be imported. It is an optional third-party tool (AGPL-3.0) that you install yourself; it is not part of this application.");
            result.Details.Add($"Install it with: {help.Command}");
            result.Details.Add(help.Note);
            result.Details.AddRange(detection.Attempts.Select(attempt => $"Looked for it: {attempt}"));
            return result;
        }

        ResolveProjectContext(options, provider.DefaultLibraryName, out var projectDirectory, out var projectName);
        if (SelectLibraryTable(options, projectDirectory, result) is not { } table)
        {
            return result;
        }

        var libraryName = LibraryBaseName(options, provider, projectDirectory, projectName);
        if (libraryName is "." or ".." || libraryName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            result.Details.Add($"The library name '{libraryName}' cannot be used as a file name. Set a different library name in Settings.");
            return result;
        }

        result.Details.Add($"Converting {lcscPartNumber} with {detection.Command.Description}.");

        var symbolSettingsDirectory = await ResolveKiCadSettingsDirectoryIfNeededAsync(projectDirectory, table, LibraryTableKind.Symbol);
        var outputBase = Path.Combine(ChooseLibraryDirectory(projectDirectory, symbolSettingsDirectory, "kicad_easyeda2kicad"), libraryName);

        // ${KIPRJMOD}-relative 3D model paths only for a library registered in the project's own table,
        // which refers to the library relative to the project as well. A library in the global table is
        // used from other projects too, where ${KIPRJMOD} is somewhere else, so it keeps absolute paths,
        // as its table row does (RegisterLibrary), even when the library itself is in a project.
        var projectRelative = table == LibraryTableScope.Project;

        EasyEda2KiCadConversion conversion = await _easyEda2KiCadConverter.ConvertAsync(
            detection.Command,
            lcscPartNumber,
            importType,
            outputBase,
            projectRelative ? projectDirectory : null,
            cancellationToken);

        result.Details.AddRange(ToolOutput(conversion.StandardError));
        result.Details.Add(conversion.Summary);

        if (!conversion.Succeeded)
        {
            result.Cancelled = conversion.Status == EasyEda2KiCadRunStatus.Cancelled;
            result.Details.AddRange(conversion.RollbackNotes);
            result.Details.Add("Nothing was registered in KiCad's library tables.");
            return result;
        }

        // Each asset is judged by the files the run actually wrote, not by the exit code alone: a part
        // EasyEDA has no 3D model for converts without one.
        var success = false;

        if (importType.HasFlag(ImportType.Symbol))
        {
            var symbolSuccess = await RunStepAsync(
                "Symbol import",
                () => Task.FromResult(conversion.SymbolLibraryWritten
                    ? RegisterLibrary(LibraryTableKind.Symbol, conversion.SymbolLibraryPath, libraryName, projectDirectory, symbolSettingsDirectory, table, result)
                    : NotWritten("symbol", result)),
                result);
            result.SymbolImportSuccess = symbolSuccess;
            success |= symbolSuccess;
            result.Details.Add($"Symbol import: {(symbolSuccess ? $"Success ({conversion.SymbolLibraryPath})" : "Failed")}");
        }

        if (importType.HasFlag(ImportType.Footprint))
        {
            var footprintSuccess = await RunStepAsync(
                "Footprint import",
                async () => conversion.FootprintFiles.Count > 0
                    ? RegisterLibrary(
                        LibraryTableKind.Footprint,
                        conversion.FootprintLibraryPath,
                        libraryName,
                        projectDirectory,
                        await ResolveKiCadSettingsDirectoryIfNeededAsync(projectDirectory, table, LibraryTableKind.Footprint),
                        table,
                        result)
                    : NotWritten("footprint", result),
                result);
            result.FootprintImportSuccess = footprintSuccess;
            success |= footprintSuccess;
            result.Details.Add($"Footprint import: {(footprintSuccess ? $"Success ({string.Join(", ", conversion.FootprintFiles.Select(Path.GetFileName))})" : "Failed")}");
        }

        if (importType.HasFlag(ImportType.Model3D))
        {
            var modelSuccess = conversion.ModelFiles.Count > 0 || NotWritten("3D model", result);
            result.Model3DImportSuccess = modelSuccess;
            success |= modelSuccess;
            result.Details.Add($"3D Model import: {(modelSuccess ? $"Success ({string.Join(", ", conversion.ModelFiles.Select(Path.GetFileName))})" : "Failed")}");
        }

        result.Success = success;
        return result;
    }

    private static bool NotWritten(string asset, ImportResult result)
    {
        result.Details.Add($"easyeda2kicad finished without writing a {asset} for this part; its messages above may say why.");
        return false;
    }

    private static ImportResult Cancelled(ImportResult result, string message)
    {
        result.Cancelled = true;
        result.Details.Add(message);
        return result;
    }

    /// <summary>easyeda2kicad's stderr, where it reports progress and errors, as lines for the import log.</summary>
    private static IEnumerable<string> ToolOutput(string standardError)
    {
        List<string> lines = standardError
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        foreach (var line in lines.Take(MaxToolOutputLines))
        {
            yield return $"easyeda2kicad: {line}";
        }

        if (lines.Count > MaxToolOutputLines)
        {
            yield return $"easyeda2kicad: ({lines.Count - MaxToolOutputLines} more line(s) not shown)";
        }
    }

    private async Task<bool> RunStepAsync(string step, Func<Task<bool>> body, ImportResult result)
    {
        try
        {
            return await body();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Step} failed", step);
            result.Details.Add($"{step} failed: {ex.Message}");
            return false;
        }
    }

    private void ResolveProjectContext(ImportOptions options, string defaultName, out string projectDirectory, out string projectName)
    {
        ResolveConfiguredContext(options, defaultName, out projectDirectory, out projectName);

        // A custom TargetPath may not exist yet. The footprint and 3D-model steps create their own
        // directories, but the symbol step saves straight into this one, so create it here (#50).
        // If it cannot be created, fall back to the default location, as the importer did before #34.
        if (string.IsNullOrEmpty(projectDirectory) || Directory.Exists(projectDirectory))
        {
            return;
        }

        try
        {
            _ = Directory.CreateDirectory(projectDirectory);
            _logger.LogInformation("Created target directory {Path}", projectDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not create target directory {Path}; importing into the default location instead.", projectDirectory);
            projectDirectory = string.Empty;
            projectName = defaultName;
        }
    }

    private void ResolveConfiguredContext(ImportOptions options, string defaultName, out string projectDirectory, out string projectName)
    {
        try
        {
            if (options.UseProjectPath)
            {
                var dir = KiCadEnvironment.GetProjectDirectory()
                    ?? throw new InvalidOperationException("Project path not in environment variables. Ensure this was launched from KiCad.");

                projectDirectory = File.Exists(dir) ? (Path.GetDirectoryName(dir) ?? dir) : dir;

                var projects = Directory.GetFiles(projectDirectory, $"*{KiCadFileExtensions.Project}");
                projectName = projects.FirstOrDefault() ?? throw new InvalidOperationException($"No project file found in {projectDirectory}");
                projectName = Path.GetFileNameWithoutExtension(projectName);
                _logger.LogInformation("Using KiCad project: {Name} at {Path}", projectName, projectDirectory);
                return;
            }

            if (!string.IsNullOrWhiteSpace(options.TargetPath))
            {
                var target = options.TargetPath;
                projectDirectory = File.Exists(target) ? (Path.GetDirectoryName(target) ?? target) : target;
                projectName = Path.GetFileNameWithoutExtension(target);
                if (string.IsNullOrEmpty(projectName))
                {
                    projectName = defaultName;
                }
                _logger.LogInformation("Using custom target path: {Path}", projectDirectory);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve project path from environment; falling back to target path or defaults.");
            if (!string.IsNullOrWhiteSpace(options.TargetPath) && !options.UseProjectPath)
            {
                var target = options.TargetPath;
                projectDirectory = File.Exists(target) ? (Path.GetDirectoryName(target) ?? target) : target;
                projectName = Path.GetFileNameWithoutExtension(target);
                if (string.IsNullOrEmpty(projectName))
                {
                    projectName = defaultName;
                }
                return;
            }
        }

        projectDirectory = string.Empty;
        projectName = defaultName;
    }

    private async Task<bool> ImportSymbolsAsync(
        ProviderExtractionResult extraction,
        string projectDirectory,
        string projectName,
        ImportOptions options,
        LibraryTableScope table,
        IComponentProvider provider,
        ImportResult result)
    {
        if (extraction.SymbolFiles.Count == 0)
        {
            _logger.LogWarning("No symbol files found in package.");
            return false;
        }

        var libraryBaseName = LibraryBaseName(options, provider, projectDirectory, projectName);

        var kicadSettingsDirectory = await ResolveKiCadSettingsDirectoryIfNeededAsync(projectDirectory, table, LibraryTableKind.Symbol);
        var symbolLibPath = Path.Combine(ChooseLibraryDirectory(projectDirectory, kicadSettingsDirectory, "kicad_symbols"), $"{libraryBaseName}.kicad_sym");

        KiCadSymbolLibrary symbolLibrary;
        if (File.Exists(symbolLibPath))
        {
            try
            {
                symbolLibrary = KiCadSymbolLibrary.Load(symbolLibPath);
            }
            catch (Exception ex)
            {
                // Never start a new library in place of one that exists (#94): saving it would replace
                // the file and lose every symbol in it. A load can fail for reasons that say nothing
                // about the file's worth, such as a newer format, a hand edit or a gap in KiCadSharp's
                // parser, so the file is left exactly as it is and nothing is registered.
                _logger.LogError(ex, "Could not load the existing symbol library {Path}; it was left unchanged", symbolLibPath);
                result.Details.Add($"The symbol library {symbolLibPath} already exists but could not be loaded, so it was left unchanged and nothing was imported into it: {ex.Message}");
                result.Details.Add("Repair or move that file, or set a different library name in Settings, and import again.");
                return false;
            }
        }
        else
        {
            symbolLibrary = new KiCadSymbolLibrary($"KiCad Importer {DateTime.Now:yyyy-MM-dd}");
        }

        var success = false;
        var written = new List<string>();
        foreach (var symbolFile in extraction.SymbolFiles)
        {
            try
            {
                var sourceLibrary = KiCadSymbolLibrary.Load(symbolFile);
                var addedCount = 0;

                foreach (KiCadSymbol symbol in sourceLibrary.Symbols)
                {
                    symbol.Id = $"{provider.DefaultPrefix}{symbol.Id}";
                    var replaced = RemoveSymbolsNamed(symbolLibrary, symbol.Id);
                    symbolLibrary.AddSymbol(symbol);
                    written.Add(replaced switch
                    {
                        0 => $"Symbol {symbol.Id} added to {symbolLibPath}.",
                        1 => $"Symbol {symbol.Id} replaced the one already in {symbolLibPath}.",
                        _ => $"Symbol {symbol.Id} replaced the {replaced} copies already in {symbolLibPath}.",
                    });
                    addedCount++;
                }

                _logger.LogInformation("Added {Count} symbols from {File}", addedCount, Path.GetFileName(symbolFile));
                success = addedCount > 0 || success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing symbol file {File}", Path.GetFileName(symbolFile));
            }
        }

        if (!success)
        {
            return false;
        }

        try
        {
            symbolLibrary.Save(symbolLibPath);
            _logger.LogInformation("Successfully saved symbol library to {Path}", symbolLibPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save symbol library to {Path}: {Message}", symbolLibPath, ex.Message);
            result.Details.Add($"The symbol library {symbolLibPath} could not be saved: {ex.Message}");
            return false;
        }

        // Only now that the library is on disk: before the save, nothing was added or replaced.
        result.Details.AddRange(written);
        return RegisterLibrary(LibraryTableKind.Symbol, symbolLibPath, libraryBaseName, projectDirectory, kicadSettingsDirectory, table, result);
    }

    /// <summary>
    /// Removes every symbol the library has under <paramref name="id"/>, so that the one added next
    /// replaces it, and returns how many were removed. Re-importing a part therefore replaces its symbol
    /// rather than appending a second one of the same name (#69), as easyeda2kicad's
    /// <c>--overwrite</c> does on the LCSC path. Removing every copy, not just the first, also repairs a
    /// library that an import before this fix already left with duplicates.
    /// </summary>
    private static int RemoveSymbolsNamed(KiCadSymbolLibrary library, string id)
    {
        var previous = library.Symbols.Where(existing => existing.Id == id).ToList();
        foreach (KiCadSymbol existing in previous)
        {
            _ = library.Symbols.Remove(existing);
        }

        return previous.Count;
    }

    private async Task<bool> ImportFootprintsAsync(
        ProviderExtractionResult extraction,
        string projectDirectory,
        string projectName,
        ImportOptions options,
        LibraryTableScope table,
        IComponentProvider provider,
        ImportResult result)
    {
        var libraryBaseName = LibraryBaseName(options, provider, projectDirectory, projectName);

        var kicadSettingsDirectory = await ResolveKiCadSettingsDirectoryIfNeededAsync(projectDirectory, table, LibraryTableKind.Footprint);
        var footprintLibPath = Path.Combine(ChooseLibraryDirectory(projectDirectory, kicadSettingsDirectory, "kicad_footprints"), $"{libraryBaseName}.pretty");

        _ = Directory.CreateDirectory(footprintLibPath);

        var success = false;

        // 1. Process footprints inside .pretty directories
        foreach (var prettyDir in extraction.PrettyDirectories)
        {
            try
            {
                var files = Directory.GetFiles(prettyDir, "*.kicad_mod");
                foreach (var file in files)
                {
                    success |= SaveRenamedFootprint(file, footprintLibPath, provider.DefaultPrefix, result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing footprint library directory {Dir}", Path.GetFileName(prettyDir));
            }
        }

        // 2. Process loose .kicad_mod files
        foreach (var file in extraction.FootprintFiles)
        {
            success |= SaveRenamedFootprint(file, footprintLibPath, provider.DefaultPrefix, result);
        }

        return success
            && RegisterLibrary(LibraryTableKind.Footprint, footprintLibPath, libraryBaseName, projectDirectory, kicadSettingsDirectory, table, result);
    }

    /// <summary>
    /// The library's name, which is also its nickname in the library tables: the one set in Settings,
    /// else <c>&lt;project&gt;_&lt;provider library&gt;</c> with a project, else the provider's library.
    /// </summary>
    private static string LibraryBaseName(ImportOptions options, IComponentProvider provider, string projectDirectory, string projectName) =>
        !string.IsNullOrEmpty(options.LibraryName)
            ? options.LibraryName
            : (string.IsNullOrEmpty(projectDirectory) ? provider.DefaultLibraryName : $"{Path.GetFileNameWithoutExtension(projectName)}_{provider.DefaultLibraryName}");

    private bool SaveRenamedFootprint(string sourceFilePath, string targetPrettyDir, string prefix, ImportResult result)
    {
        try
        {
            var expr = SExpression.Load(sourceFilePath);
            var currentId = expr.GetValue(0) ?? Path.GetFileNameWithoutExtension(sourceFilePath);
            var newFootprintId = $"{prefix}{currentId}";
            _ = expr.SetValue(0, newFootprintId);

            var newFootprint = new KiCadFootprint(expr);
            var destPath = Path.Combine(targetPrettyDir, $"{newFootprintId}.kicad_mod");

            // A .pretty library is one file per footprint, named after it, so a re-imported footprint
            // replaces the old one rather than adding a second (#69). Say which happened.
            var replaced = File.Exists(destPath);
            KiCadFootprintLibrary.SaveFootprint(newFootprint, destPath);
            _logger.LogDebug("Saved footprint to {DestPath}", destPath);
            result.Details.Add(replaced
                ? $"Footprint {newFootprintId} replaced the one already in {targetPrettyDir}."
                : $"Footprint {newFootprintId} added to {targetPrettyDir}.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing footprint file {File}", Path.GetFileName(sourceFilePath));
            return false;
        }
    }

    private async Task<bool> Import3DModelsAsync(
        ProviderExtractionResult extraction,
        string projectDirectory,
        IComponentProvider provider,
        ImportResult result)
    {
        if (extraction.Model3DFiles.Count == 0)
        {
            _logger.LogWarning("No 3D model files found in package.");
            return false;
        }

        var modelDir = !string.IsNullOrEmpty(projectDirectory)
            ? Path.Combine(projectDirectory, "3d_models", provider.DefaultLibraryName)
            : Path.Combine(await Get3DModelPath(projectDirectory), provider.DefaultLibraryName);

        _ = Directory.CreateDirectory(modelDir);

        var success = false;
        foreach (var modelFile in extraction.Model3DFiles)
        {
            try
            {
                var modelName = Path.GetFileName(modelFile);
                var destPath = Path.Combine(modelDir, modelName);

                // Same file name, same model: a re-import overwrites it, as footprints do (#69).
                var replaced = File.Exists(destPath);
                File.Copy(modelFile, destPath, overwrite: true);
                _logger.LogDebug("Copied 3D model to {DestPath}", destPath);
                result.Details.Add(replaced
                    ? $"3D model {modelName} replaced the one already in {modelDir}."
                    : $"3D model {modelName} added to {modelDir}.");
                success = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to copy 3D model {File}", Path.GetFileName(modelFile));
            }
        }

        return success;
    }

    /// <summary>
    /// Adds the library to <paramref name="table"/>, as <see cref="SelectLibraryTable"/> chose it: KiCad's
    /// global table, or the table of the project the library was imported into. Returns the step's
    /// result: <see langword="false"/> when the library is not registered, with the reason in
    /// <see cref="ImportResult.Details"/>.
    /// </summary>
    /// <remarks>
    /// KiCad 10 has no IPC call to reload a library table, so a running KiCad keeps the table it has in
    /// memory: it loads the global tables at start-up and a project's tables when the project is
    /// opened. Until then, changing anything in that KiCad's Manage Symbol/Footprint Libraries dialog
    /// saves its in-memory copy over the file and drops the new row.
    /// </remarks>
    private bool RegisterLibrary(
        LibraryTableKind kind,
        string libraryPath,
        string nickname,
        string projectDirectory,
        string? kicadSettingsDirectory,
        LibraryTableScope table,
        ImportResult result)
    {
        var label = kind == LibraryTableKind.Symbol ? "Symbol" : "Footprint";
        var tableName = KiCadLibraryTable.FileName(kind);

        string tablePath;
        string? kiprjmod;
        string reload;
        if (table == LibraryTableScope.Global)
        {
            if (kicadSettingsDirectory is null)
            {
                _logger.LogWarning("{Label} library {Path} not registered: KiCad's settings directory was not found", label, libraryPath);
                result.Details.Add($"{label} library not registered: KiCad's settings directory, which holds the global {tableName}, was not found. Start KiCad once, or add {libraryPath} to its library table by hand.");
                return false;
            }

            tablePath = Path.Combine(kicadSettingsDirectory, tableName);
            kiprjmod = null;
            reload = "restart KiCad to load it";
        }
        else
        {
            // SelectLibraryTable chooses the project's table only when projectDirectory is a KiCad project.
            tablePath = Path.Combine(projectDirectory, tableName);
            kiprjmod = projectDirectory;
            reload = "reopen the project to load it";
        }

        var entry = new LibraryTableEntry(
            nickname,
            KiCadLibraryTable.ToUri(libraryPath, kiprjmod),
            libraryPath,
            "Imported by KiCad UltraLibrarian Importer");

        // Only a project table may be created: see KiCadLibraryTable.Register on the global one.
        LibraryTableUpdate update = KiCadLibraryTable.Register(tablePath, kind, entry, createIfMissing: table == LibraryTableScope.Project, kiprjmod);
        switch (update.Status)
        {
            case LibraryTableUpdateStatus.Added:
                _logger.LogInformation("{Label} library {Name} registered in {Table}", label, nickname, update.TablePath);
                result.Details.Add($"{label} library registered: {update.Message} KiCad does not reload library tables on its own; {reload}.");
                return true;

            case LibraryTableUpdateStatus.AlreadyRegistered:
                _logger.LogInformation("{Label} library {Name} already registered in {Table}", label, nickname, update.TablePath);
                result.Details.Add($"{label} library already registered: {update.Message}");
                return true;

            case LibraryTableUpdateStatus.NameConflict:
                _logger.LogWarning("{Label} library {Name} not registered: {Message}", label, nickname, update.Message);
                result.Details.Add($"{label} library not registered: {update.Message} Set a different library name in Settings.");
                return false;

            case LibraryTableUpdateStatus.Failed:
                _logger.LogError(update.Error, "{Label} library {Name} not registered: {Message}", label, nickname, update.Message);
                result.Details.Add($"{label} library not registered: {update.Message}");
                return false;

            default:
                throw new InvalidOperationException($"Unhandled library table status {update.Status}.");
        }
    }

    /// <summary>
    /// The library table this import registers in, from <see cref="ImportOptions.RegistrationScope"/> and
    /// whether <paramref name="projectDirectory"/> is a KiCad project (#71). <see langword="null"/>, with
    /// the reason in <see cref="ImportResult.Details"/>, when the scope is
    /// <see cref="LibraryRegistrationScope.Project"/> and there is no project: the import then stops
    /// before any library is written.
    /// </summary>
    /// <remarks>
    /// The project is the one the libraries are written into: the project KiCad started the importer
    /// from, or, with "Use active KiCad project directory" off, a target path that holds a
    /// <c>.kicad_pro</c>.
    /// </remarks>
    private LibraryTableScope? SelectLibraryTable(ImportOptions options, string projectDirectory, ImportResult result)
    {
        LibraryTableScope? table = options.RegistrationScope.SelectTable(IsKiCadProject(projectDirectory));
        if (table is { } selected)
        {
            _logger.LogInformation("Registering in the {Table} library tables (registration scope {Scope})", selected, options.RegistrationScope);
            return selected;
        }

        var where = string.IsNullOrEmpty(projectDirectory)
            ? "there is no KiCad project to import into"
            : $"{projectDirectory} is not a KiCad project folder (it has no {KiCadFileExtensions.Project} file)";
        _logger.LogWarning("Import refused: libraries are registered only in the project's library tables, and {Where}", where);
        result.Details.Add($"Nothing was imported: Settings registers libraries only in the project's library tables, and {where}. Import with a KiCad project open, or choose Automatic or the global library tables in Settings.");
        return null;
    }

    private static bool IsKiCadProject(string directory) =>
        !string.IsNullOrEmpty(directory)
        && Directory.Exists(directory)
        && Directory.EnumerateFiles(directory, $"*{KiCadFileExtensions.Project}").Any();

    /// <summary>
    /// Chooses the directory the library files are written to, and creates it (#63). With a project
    /// directory that is where they go. Without one they go next to KiCad's global library tables, in
    /// its settings directory for the running version, which is where the importer put them before #34
    /// (<c>DirectoryOf(libraryTable)</c>). Only when that directory is unknown do they fall back to the
    /// temporary directory, and a library there is never registered globally, because
    /// <see cref="RegisterLibrary"/> needs the same settings directory to find the global table.
    /// </summary>
    private static string ChooseLibraryDirectory(string projectDirectory, string? kicadSettingsDirectory, string temporaryFolderName)
    {
        var directory = !string.IsNullOrEmpty(projectDirectory)
            ? projectDirectory
            : kicadSettingsDirectory is not null && Directory.Exists(kicadSettingsDirectory)
                ? kicadSettingsDirectory
                : Path.Combine(Path.GetTempPath(), temporaryFolderName);

        _ = Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// KiCad's settings directory for the running version, when the step needs it: to find the global
    /// table, or to hold the library when there is no project directory. <see langword="null"/> when it
    /// is not needed or cannot be found. A library registered in the project's table always has a
    /// project directory to go into, so it never needs it.
    /// </summary>
    private async Task<string?> ResolveKiCadSettingsDirectoryIfNeededAsync(string projectDirectory, LibraryTableScope table, LibraryTableKind kind)
    {
        if (table == LibraryTableScope.Project && !string.IsNullOrEmpty(projectDirectory))
        {
            return null;
        }

        try
        {
            // KiCadSharp 0.1.1 blocks the calling thread on the socket read, and the caller is the UI
            // thread when the import comes from MainViewModel. Run it on the pool and stop waiting
            // after a few seconds rather than hang the import on a KiCad that never answers.
            KiCadVersion version = await Task.Run(() => _kicad.GetVersion().AsTask()).WaitAsync(KiCadQueryTimeout);
            var directory = KiCadSettingsDirectory.ForVersion(version.Major, version.Minor);
            _logger.LogDebug("KiCad {Version} is running; its settings directory is {Directory}", version, directory);
            return directory;
        }
        catch (Exception ex)
        {
            // Deliberately broad. KiCadSharp 0.1.1 surfaces a failed request as unrelated types: its
            // public KiCadConnectionException, its internal ApiException (KiCad answered with an
            // error), nng.NngException straight from the dial when nothing listens on the socket, and
            // ArgumentNullException when there is no API token because KiCad did not launch the
            // importer. With the timeout above, every one of them means the same thing here: KiCad
            // cannot be asked, so fall back to looking at the disk.
            _logger.LogWarning(ex, "Could not ask KiCad for its version; looking for its settings directory on disk instead");
        }

        var newest = KiCadSettingsDirectory.FindNewestContaining(KiCadLibraryTable.FileName(kind));
        if (newest is null)
        {
            _logger.LogWarning("No KiCad settings directory with a {Table} was found under {Root}", KiCadLibraryTable.FileName(kind), KiCadSettingsDirectory.GetRoot());
        }
        else
        {
            _logger.LogWarning("Using the newest KiCad settings directory that has a {Table}: {Directory}", KiCadLibraryTable.FileName(kind), newest);
        }

        return newest;
    }

    private Task<string> Get3DModelPath(string projectDirectory)
    {
        var kicadEnv = Environment.GetEnvironmentVariable("KICAD7_3DMODEL_DIR");
        if (!string.IsNullOrEmpty(kicadEnv) && Directory.Exists(kicadEnv))
        {
            return Task.FromResult(kicadEnv);
        }

        if (!string.IsNullOrEmpty(projectDirectory))
        {
            var modelDir = Path.Combine(projectDirectory, "3d_models");
            _ = Directory.CreateDirectory(modelDir);
            return Task.FromResult(modelDir);
        }

        if (OperatingSystem.IsWindows())
        {
            var programFiles = SpecialFolders.GetPath(Environment.SpecialFolder.ProgramFiles);
            var modelDir = Path.Combine(programFiles, "KiCad", "7.0", "share", "kicad", "3dmodels");
            if (Directory.Exists(modelDir))
            {
                return Task.FromResult(modelDir);
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            var modelDir = "/Applications/KiCad/KiCad.app/Contents/SharedSupport/3dmodels";
            if (Directory.Exists(modelDir))
            {
                return Task.FromResult(modelDir);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            var modelDir = "/usr/share/kicad/3dmodels";
            if (Directory.Exists(modelDir))
            {
                return Task.FromResult(modelDir);
            }
        }

        return Task.FromResult(!string.IsNullOrEmpty(projectDirectory)
            ? Path.Combine(projectDirectory, "3d_models")
            : Path.Combine(Path.GetTempPath(), "kicad_3dmodels"));
    }
}
