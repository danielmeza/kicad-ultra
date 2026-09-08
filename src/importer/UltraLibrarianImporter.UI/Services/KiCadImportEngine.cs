using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using KiCadSharp;
using KiCadSharp.Documents;

using Microsoft.Extensions.Logging;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services
{
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
            string tempDir = Path.Combine(Path.GetTempPath(), $"{provider.DefaultLibraryName}_Import_{Guid.NewGuid()}");

            try
            {
                _logger.LogInformation("Importing {Provider} component from {FilePath}", provider.DisplayName, packageFilePath);

                ResolveProjectContext(options, provider.DefaultLibraryName, out string projectPath, out string projectName);

                _logger.LogDebug("Extracting package via provider {Provider} into {TempDir}", provider.DisplayName, tempDir);
                var extraction = await provider.ExtractPackageAsync(packageFilePath, tempDir);

                bool success = false;

                if (importType.HasFlag(ImportType.Symbol))
                {
                    bool symbolSuccess = await ImportSymbolsAsync(extraction, projectPath, projectName, options, provider);
                    result.SymbolImportSuccess = symbolSuccess;
                    success |= symbolSuccess;
                    result.Details.Add($"Symbol import: {(symbolSuccess ? "Success" : "Failed")}");
                }

                if (importType.HasFlag(ImportType.Footprint))
                {
                    bool footprintSuccess = await ImportFootprintsAsync(extraction, projectPath, projectName, options, provider);
                    result.FootprintImportSuccess = footprintSuccess;
                    success |= footprintSuccess;
                    result.Details.Add($"Footprint import: {(footprintSuccess ? "Success" : "Failed")}");
                }

                if (importType.HasFlag(ImportType.Model3D))
                {
                    bool modelSuccess = await Import3DModelsAsync(extraction, projectPath, provider);
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

        private void ResolveProjectContext(ImportOptions options, string defaultName, out string projectPath, out string projectName)
        {
            try
            {
                if (options.UseProjectPath)
                {
                    projectPath = KiCadEnvironment.GetProjectDirectory()
                        ?? throw new InvalidOperationException("Project path not in environment variables. Ensure this was launched from KiCad.");

                    var projects = Directory.GetFiles(projectPath, $"*{KiCadFileExtensions.Project}");
                    projectName = projects.FirstOrDefault() ?? throw new InvalidOperationException($"No project file found in {projectPath}");
                    projectName = Path.GetFileNameWithoutExtension(projectName);
                    _logger.LogInformation("Using KiCad project: {Name} at {Path}", projectName, projectPath);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(options.TargetPath))
                {
                    projectPath = options.TargetPath;
                    projectName = Path.GetFileNameWithoutExtension(projectPath);
                    if (string.IsNullOrEmpty(projectName))
                    {
                        projectName = defaultName;
                    }
                    _logger.LogInformation("Using custom target path: {Path}", projectPath);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not resolve project path from environment; falling back to target path or defaults.");
                if (!string.IsNullOrWhiteSpace(options.TargetPath) && !options.UseProjectPath)
                {
                    projectPath = options.TargetPath;
                    projectName = Path.GetFileNameWithoutExtension(projectPath);
                    if (string.IsNullOrEmpty(projectName))
                    {
                        projectName = defaultName;
                    }
                    return;
                }
            }

            projectPath = string.Empty;
            projectName = defaultName;
        }

        private async Task<bool> ImportSymbolsAsync(
            ProviderExtractionResult extraction,
            string projectPath,
            string projectName,
            ImportOptions options,
            IComponentProvider provider)
        {
            if (extraction.SymbolFiles.Count == 0)
            {
                _logger.LogWarning("No symbol files found in package.");
                return false;
            }

            string libraryBaseName = !string.IsNullOrEmpty(options.LibraryName)
                ? options.LibraryName
                : (string.IsNullOrEmpty(projectPath) ? provider.DefaultLibraryName : $"{Path.GetFileNameWithoutExtension(projectName)}_{provider.DefaultLibraryName}");

            string symbolLibPath = !string.IsNullOrEmpty(projectPath)
                ? Path.Combine(DirectoryOf(projectPath), $"{libraryBaseName}.kicad_sym")
                : Path.Combine(DirectoryOf(await GetSymbolLibraryPath(projectPath)), $"{libraryBaseName}.kicad_sym");

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

            bool success = false;
            foreach (var symbolFile in extraction.SymbolFiles)
            {
                try
                {
                    var sourceLibrary = KiCadSymbolLibrary.Load(symbolFile);
                    int addedCount = 0;

                    foreach (var symbol in sourceLibrary.Symbols)
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

            if (success)
            {
                try
                {
                    symbolLibrary.Save(symbolLibPath);
                    if (options.AddToGlobalLibrary)
                    {
                        await AddSymbolLibraryToTableAsync(symbolLibPath, libraryBaseName);
                    }
                    _logger.LogInformation("Successfully saved symbol library to {Path}", symbolLibPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save symbol library to {Path}: {Message}", symbolLibPath, ex.Message);
                    success = false;
                }
            }

            return success;
        }

        private async Task<bool> ImportFootprintsAsync(
            ProviderExtractionResult extraction,
            string projectPath,
            string projectName,
            ImportOptions options,
            IComponentProvider provider)
        {
            string libraryBaseName = !string.IsNullOrEmpty(options.LibraryName)
                ? options.LibraryName
                : (string.IsNullOrEmpty(projectPath) ? provider.DefaultLibraryName : $"{Path.GetFileNameWithoutExtension(projectName)}_{provider.DefaultLibraryName}");

            string footprintLibPath = !string.IsNullOrEmpty(projectPath)
                ? Path.Combine(DirectoryOf(projectPath), $"{libraryBaseName}.pretty")
                : Path.Combine(DirectoryOf(await GetFootprintLibraryPath(projectPath)), $"{libraryBaseName}.pretty");

            Directory.CreateDirectory(footprintLibPath);

            bool success = false;

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
                var sourceLib = KiCadFootprintLibrary.Load(sourceFilePath);
                if (sourceLib.Footprints.Count == 0)
                {
                    return false;
                }

                bool anySaved = false;
                foreach (var footprint in sourceLib.Footprints)
                {
                    string newFootprintId = $"{prefix}{footprint.Id}";
                    var newFootprint = CloneFootprint(footprint, newFootprintId);

                    string destPath = Path.Combine(targetPrettyDir, $"{newFootprintId}.kicad_mod");
                    KiCadFootprintLibrary.SaveFootprint(newFootprint, destPath);
                    _logger.LogDebug("Saved footprint to {DestPath}", destPath);
                    anySaved = true;
                }

                return anySaved;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing footprint file {File}", Path.GetFileName(sourceFilePath));
                return false;
            }
        }

        private static KiCadFootprint CloneFootprint(KiCadFootprint source, string newId)
        {
            var target = new KiCadFootprint(newId);

            foreach (var pad in source.Pads) target.Pads.Add(pad);
            foreach (var text in source.TextItems) target.TextItems.Add(text);
            foreach (var line in source.Lines) target.Lines.Add(line);
            foreach (var circle in source.Circles) target.Circles.Add(circle);
            foreach (var arc in source.Arcs) target.Arcs.Add(arc);
            foreach (var poly in source.Polygons) target.Polygons.Add(poly);
            foreach (var model in source.Models) target.Models.Add(model);

            return target;
        }

        private async Task<bool> Import3DModelsAsync(
            ProviderExtractionResult extraction,
            string projectPath,
            IComponentProvider provider)
        {
            if (extraction.Model3DFiles.Count == 0)
            {
                _logger.LogWarning("No 3D model files found in package.");
                return false;
            }

            string modelDir = !string.IsNullOrEmpty(projectPath)
                ? Path.Combine(DirectoryOf(projectPath), "3d_models", provider.DefaultLibraryName)
                : Path.Combine(await Get3DModelPath(projectPath), provider.DefaultLibraryName);

            Directory.CreateDirectory(modelDir);

            bool success = false;
            foreach (var modelFile in extraction.Model3DFiles)
            {
                try
                {
                    string destPath = Path.Combine(modelDir, Path.GetFileName(modelFile));
                    File.Copy(modelFile, destPath, overwrite: true);
                    _logger.LogDebug("Copied 3D model to {DestPath}", destPath);
                    success = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to copy 3D model {File}", Path.GetFileName(modelFile));
                }
            }

            if (success)
            {
                try
                {
                    await _kicad.RefreshPaths();
                    _logger.LogDebug("Refreshed KiCad paths after 3D model import.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh KiCad paths after 3D model import.");
                }
            }

            return success;
        }

        private async Task AddSymbolLibraryToTableAsync(string libraryPath, string libraryName)
        {
            var result = await _kicad.RunAction("common.Control.addLibrary");
            if (result.Status == Kiapi.Common.Commands.RunActionStatus.RasOk)
            {
                _logger.LogInformation("Symbol library registered in table: {Name}", libraryName);
            }
        }

        private async Task AddFootprintLibraryToTableAsync(string libraryPath, string libraryName)
        {
            await _kicad.RunAction($"pcbnew.FpLibTable.AddLibrary:{libraryPath}:{libraryName}");
            _logger.LogInformation("Footprint library registered in table: {Name}", libraryName);
        }

        private static string DirectoryOf(string path) =>
            Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException($"'{path}' has no parent directory. Expected a path inside a project folder.");

        private Task<string> GetSymbolLibraryPath(string projectPath)
        {
            if (!string.IsNullOrEmpty(projectPath))
            {
                string projectDir = DirectoryOf(projectPath);
                string symLibTable = Path.Combine(projectDir, "sym-lib-table");
                if (File.Exists(symLibTable))
                {
                    return Task.FromResult(symLibTable);
                }
            }

            string kicadConfigDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            kicadConfigDir = Path.Combine(kicadConfigDir, "kicad", "9.0");
            if (Directory.Exists(kicadConfigDir))
            {
                string symLibTable = Path.Combine(kicadConfigDir, "sym-lib-table");
                if (File.Exists(symLibTable))
                {
                    return Task.FromResult(symLibTable);
                }
            }

            return Task.FromResult(!string.IsNullOrEmpty(projectPath)
                ? Path.Combine(DirectoryOf(projectPath), "symbols")
                : Path.Combine(Path.GetTempPath(), "kicad_symbols"));
        }

        private Task<string> GetFootprintLibraryPath(string projectPath)
        {
            if (!string.IsNullOrEmpty(projectPath))
            {
                string projectDir = DirectoryOf(projectPath);
                string fpLibTable = Path.Combine(projectDir, "fp-lib-table");
                if (File.Exists(fpLibTable))
                {
                    return Task.FromResult(fpLibTable);
                }
            }

            string kicadConfigDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            kicadConfigDir = Path.Combine(kicadConfigDir, "kicad", "7.0");
            if (Directory.Exists(kicadConfigDir))
            {
                string fpLibTable = Path.Combine(kicadConfigDir, "fp-lib-table");
                if (File.Exists(fpLibTable))
                {
                    return Task.FromResult(fpLibTable);
                }
            }

            return Task.FromResult(!string.IsNullOrEmpty(projectPath)
                ? Path.Combine(DirectoryOf(projectPath), "footprints")
                : Path.Combine(Path.GetTempPath(), "kicad_footprints"));
        }

        private Task<string> Get3DModelPath(string projectPath)
        {
            string? kicadEnv = Environment.GetEnvironmentVariable("KICAD7_3DMODEL_DIR");
            if (!string.IsNullOrEmpty(kicadEnv) && Directory.Exists(kicadEnv))
            {
                return Task.FromResult(kicadEnv);
            }

            if (!string.IsNullOrEmpty(projectPath))
            {
                string projectDir = DirectoryOf(projectPath);
                string modelDir = Path.Combine(projectDir, "3d_models");
                Directory.CreateDirectory(modelDir);
                return Task.FromResult(modelDir);
            }

            if (OperatingSystem.IsWindows())
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string modelDir = Path.Combine(programFiles, "KiCad", "7.0", "share", "kicad", "3dmodels");
                if (Directory.Exists(modelDir))
                {
                    return Task.FromResult(modelDir);
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                string modelDir = "/Applications/KiCad/KiCad.app/Contents/SharedSupport/3dmodels";
                if (Directory.Exists(modelDir))
                {
                    return Task.FromResult(modelDir);
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                string modelDir = "/usr/share/kicad/3dmodels";
                if (Directory.Exists(modelDir))
                {
                    return Task.FromResult(modelDir);
                }
            }

            return Task.FromResult(!string.IsNullOrEmpty(projectPath)
                ? Path.Combine(DirectoryOf(projectPath), "3d_models")
                : Path.Combine(Path.GetTempPath(), "kicad_3dmodels"));
        }
    }
}
