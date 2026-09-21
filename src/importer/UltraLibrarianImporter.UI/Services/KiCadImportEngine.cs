using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using KiCadSharp;
using KiCadSharp.Documents;

using Microsoft.Extensions.Logging;

using SExpressions;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services;

public class KiCadImportEngine : IKiCadImportEngine
{
    private readonly KiCad _kicad;
    private readonly ILogger<KiCadImportEngine> _logger;

    public KiCadImportEngine(KiCad kicad, ILogger<KiCadImportEngine> logger)
    {
        _kicad = kicad;
        _logger = logger;
    }

    public async Task<ImportResult> ImportAsync(
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

            _logger.LogDebug("Extracting package via provider {Provider} into {TempDir}", provider.DisplayName, tempDir);
            ProviderExtractionResult extraction = await provider.ExtractPackageAsync(packageFilePath, tempDir);

            var success = false;

            if (importType.HasFlag(ImportType.Symbol))
            {
                var symbolSuccess = await RunStepAsync("Symbol import", () => ImportSymbolsAsync(extraction, projectDirectory, projectName, options, provider), result);
                result.SymbolImportSuccess = symbolSuccess;
                success |= symbolSuccess;
                result.Details.Add($"Symbol import: {(symbolSuccess ? "Success" : "Failed")}");
            }

            if (importType.HasFlag(ImportType.Footprint))
            {
                var footprintSuccess = await RunStepAsync("Footprint import", () => ImportFootprintsAsync(extraction, projectDirectory, projectName, options, provider), result);
                result.FootprintImportSuccess = footprintSuccess;
                success |= footprintSuccess;
                result.Details.Add($"Footprint import: {(footprintSuccess ? "Success" : "Failed")}");
            }

            if (importType.HasFlag(ImportType.Model3D))
            {
                var modelSuccess = await RunStepAsync("3D Model import", () => Import3DModelsAsync(extraction, projectDirectory, provider), result);
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
        IComponentProvider provider)
    {
        if (extraction.SymbolFiles.Count == 0)
        {
            _logger.LogWarning("No symbol files found in package.");
            return false;
        }

        var libraryBaseName = !string.IsNullOrEmpty(options.LibraryName)
            ? options.LibraryName
            : (string.IsNullOrEmpty(projectDirectory) ? provider.DefaultLibraryName : $"{Path.GetFileNameWithoutExtension(projectName)}_{provider.DefaultLibraryName}");

        var symbolLibPath = !string.IsNullOrEmpty(projectDirectory)
            ? Path.Combine(projectDirectory, $"{libraryBaseName}.kicad_sym")
            : Path.Combine(await GetSymbolLibraryPath(projectDirectory), $"{libraryBaseName}.kicad_sym");

        KiCadSymbolLibrary symbolLibrary;
        if (File.Exists(symbolLibPath))
        {
            try
            {
                symbolLibrary = KiCadSymbolLibrary.Load(symbolLibPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load existing symbol library at {Path}. Creating a new one.", symbolLibPath);
                symbolLibrary = new KiCadSymbolLibrary($"KiCad Importer {DateTime.Now:yyyy-MM-dd}");
            }
        }
        else
        {
            symbolLibrary = new KiCadSymbolLibrary($"KiCad Importer {DateTime.Now:yyyy-MM-dd}");
        }

        var success = false;
        foreach (var symbolFile in extraction.SymbolFiles)
        {
            try
            {
                var sourceLibrary = KiCadSymbolLibrary.Load(symbolFile);
                var addedCount = 0;

                foreach (KiCadSymbol symbol in sourceLibrary.Symbols)
                {
                    symbol.Id = $"{provider.DefaultPrefix}{symbol.Id}";
                    symbolLibrary.AddSymbol(symbol);
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
            if (options.AddToGlobalLibrary)
            {
                await AddSymbolLibraryToTableAsync(symbolLibPath, libraryBaseName);
            }
            _logger.LogInformation("Successfully saved symbol library to {Path}", symbolLibPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save symbol library to {Path}: {Message}", symbolLibPath, ex.Message);
            return false;
        }
    }

    private async Task<bool> ImportFootprintsAsync(
        ProviderExtractionResult extraction,
        string projectDirectory,
        string projectName,
        ImportOptions options,
        IComponentProvider provider)
    {
        var libraryBaseName = !string.IsNullOrEmpty(options.LibraryName)
            ? options.LibraryName
            : (string.IsNullOrEmpty(projectDirectory) ? provider.DefaultLibraryName : $"{Path.GetFileNameWithoutExtension(projectName)}_{provider.DefaultLibraryName}");

        var footprintLibPath = !string.IsNullOrEmpty(projectDirectory)
            ? Path.Combine(projectDirectory, $"{libraryBaseName}.pretty")
            : Path.Combine(await GetFootprintLibraryPath(projectDirectory), $"{libraryBaseName}.pretty");

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
                    success |= SaveRenamedFootprint(file, footprintLibPath, provider.DefaultPrefix);
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
            success |= SaveRenamedFootprint(file, footprintLibPath, provider.DefaultPrefix);
        }

        if (success && options.AddToGlobalLibrary)
        {
            await AddFootprintLibraryToTableAsync(footprintLibPath, libraryBaseName);
        }

        return success;
    }

    private bool SaveRenamedFootprint(string sourceFilePath, string targetPrettyDir, string prefix)
    {
        try
        {
            var expr = SExpression.Load(sourceFilePath);
            var currentId = expr.GetValue(0) ?? Path.GetFileNameWithoutExtension(sourceFilePath);
            var newFootprintId = $"{prefix}{currentId}";
            _ = expr.SetValue(0, newFootprintId);

            var newFootprint = new KiCadFootprint(expr);
            var destPath = Path.Combine(targetPrettyDir, $"{newFootprintId}.kicad_mod");
            KiCadFootprintLibrary.SaveFootprint(newFootprint, destPath);
            _logger.LogDebug("Saved footprint to {DestPath}", destPath);
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
        IComponentProvider provider)
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
                var destPath = Path.Combine(modelDir, Path.GetFileName(modelFile));
                File.Copy(modelFile, destPath, overwrite: true);
                _logger.LogDebug("Copied 3D model to {DestPath}", destPath);
                success = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to copy 3D model {File}", Path.GetFileName(modelFile));
            }
        }

        return success;
    }

    private async Task AddSymbolLibraryToTableAsync(string libraryPath, string libraryName)
    {
        _ = await _kicad.RunAction($"eeschema.SymLibTable.AddLibrary:{libraryPath}:{libraryName}");
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Symbol library registered in table: {Name}", libraryName);
        }
    }

    private async Task AddFootprintLibraryToTableAsync(string libraryPath, string libraryName)
    {
        _ = await _kicad.RunAction($"pcbnew.FpLibTable.AddLibrary:{libraryPath}:{libraryName}");
        _logger.LogInformation("Footprint library registered in table: {Name}", libraryName);
    }

    private Task<string> GetSymbolLibraryPath(string projectDirectory)
    {
        if (!string.IsNullOrEmpty(projectDirectory))
        {
            var symLibTable = Path.Combine(projectDirectory, "sym-lib-table");
            if (File.Exists(symLibTable))
            {
                return Task.FromResult(symLibTable);
            }
        }

        var kicadConfigDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        kicadConfigDir = Path.Combine(kicadConfigDir, "kicad", "9.0");
        if (Directory.Exists(kicadConfigDir))
        {
            var symLibTable = Path.Combine(kicadConfigDir, "sym-lib-table");
            if (File.Exists(symLibTable))
            {
                return Task.FromResult(symLibTable);
            }
        }

        return Task.FromResult(!string.IsNullOrEmpty(projectDirectory)
            ? Path.Combine(projectDirectory, "symbols")
            : Path.Combine(Path.GetTempPath(), "kicad_symbols"));
    }

    private Task<string> GetFootprintLibraryPath(string projectDirectory)
    {
        if (!string.IsNullOrEmpty(projectDirectory))
        {
            var fpLibTable = Path.Combine(projectDirectory, "fp-lib-table");
            if (File.Exists(fpLibTable))
            {
                return Task.FromResult(fpLibTable);
            }
        }

        var kicadConfigDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        kicadConfigDir = Path.Combine(kicadConfigDir, "kicad", "7.0");
        if (Directory.Exists(kicadConfigDir))
        {
            var fpLibTable = Path.Combine(kicadConfigDir, "fp-lib-table");
            if (File.Exists(fpLibTable))
            {
                return Task.FromResult(fpLibTable);
            }
        }

        return Task.FromResult(!string.IsNullOrEmpty(projectDirectory)
            ? Path.Combine(projectDirectory, "footprints")
            : Path.Combine(Path.GetTempPath(), "kicad_footprints"));
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
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
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
