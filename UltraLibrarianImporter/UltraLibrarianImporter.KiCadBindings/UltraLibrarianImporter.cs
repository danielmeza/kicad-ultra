using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace UltraLibrarianImporter.KiCadBindings
{
    /// <summary>
    /// Handles importing UltraLibrarian components into KiCad
    /// </summary>
    public class UltraLibrarianImporter
    {
        private readonly KiCadIPCClient _kicadClient;
        private readonly ILogger _logger;
        private readonly ImportOptions _options;

        /// <summary>
        /// Creates a new instance of the UltraLibrarian Importer
        /// </summary>
        /// <param name="kicadClient">KiCad IPC client for communication with KiCad</param>
        /// <param name="logger">Logger for recording operations</param>
        /// <param name="options">Import options</param>
        public UltraLibrarianImporter(KiCadIPCClient kiCadClient, ILogger logger, ImportOptions options)
        {
            _kicadClient = kiCadClient;
            _logger = logger;
            _options = options;
        }

        /// <summary>
        /// Imports a UltraLibrarian component file into KiCad
        /// </summary>
        /// <param name="zipFilePath">Path to the downloaded UltraLibrarian ZIP file</param>
        /// <param name="importType">Type of components to import</param>
        /// <returns>Result of the import operation with detailed messages</returns>
        public async Task<ImportResult> ImportComponentAsync(string zipFilePath, ImportType importType)
        {
            var result = new ImportResult();
            string tempDir = Path.Combine(Path.GetTempPath(), $"UltraLibrarian_Import_{Guid.NewGuid()}");

            try
            {
                _logger.LogInformation($"Importing component from {zipFilePath}");
                Directory.CreateDirectory(tempDir);

                // Extract the downloaded ZIP file
                _logger.Log(LogLevel.Debug, $"Extracting to {tempDir}");
                ZipFile.ExtractToDirectory(zipFilePath, tempDir, true);

                // Process based on selected import type
                bool success = false;

                if (importType.HasFlag(ImportType.Symbol))
                {
                    var symbolSuccess = await ImportSymbolsAsync(tempDir);
                    result.SymbolImportSuccess = symbolSuccess;
                    success |= symbolSuccess;
                    result.Details.Add($"Symbol import: {(symbolSuccess ? "Success" : "Failed")}");
                }

                if (importType.HasFlag(ImportType.Footprint))
                {
                    var footprintSuccess = await ImportFootprintsAsync(tempDir);
                    result.FootprintImportSuccess = footprintSuccess;
                    success |= footprintSuccess;
                    result.Details.Add($"Footprint import: {(footprintSuccess ? "Success" : "Failed")}");
                }

                if (importType.HasFlag(ImportType.Model3D))
                {
                    var modelSuccess = await Import3DModelsAsync(tempDir);
                    result.Model3DImportSuccess = modelSuccess;
                    success |= modelSuccess;
                    result.Details.Add($"3D Model import: {(modelSuccess ? "Success" : "Failed")}");
                }

                result.Success = success;

                // Clean up the downloaded file if configured to do so
                if (_options.CleanupAfterImport && File.Exists(zipFilePath))
                {
                    File.Delete(zipFilePath);
                    _logger.Log(LogLevel.Debug, $"Cleaned up downloaded file: {zipFilePath}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error importing component: {ex.Message}");
                result.Success = false;
                result.Details.Add($"Error: {ex.Message}");
                return result;
            }
            finally
            {
                // Clean up temp directory
                if (Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, true);
                        _logger.Log(LogLevel.Debug, $"Cleaned up temp directory: {tempDir}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Log(LogLevel.Warning, $"Failed to clean up temp directory: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Imports schematic symbols from extracted UltraLibrarian files
        /// </summary>
        /// <param name="extractDir">Directory containing extracted UltraLibrarian files</param>
        /// <returns>True if successful, false otherwise</returns>
        private async Task<bool> ImportSymbolsAsync(string extractDir)
        {
            throw new NotImplementedException();
            try
            {
                _logger.LogInformation("Importing schematic symbols");

                // Find symbol files (.lib or .kicad_sym)
                var symbolFiles = Directory.GetFiles(extractDir, "*.lib", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(extractDir, "*.kicad_sym", SearchOption.AllDirectories))
                    .ToList();

                if (symbolFiles.Count == 0)
                {
                    _logger.Log(LogLevel.Warning, "No symbol files found");
                    return false;
                }

                //bool success = false;
                //foreach (var symbolFile in symbolFiles)
                //{
                //    string libName = Path.GetFileNameWithoutExtension(symbolFile);
                //    _logger.LogDebug($"Importing symbol library: {libName} from {symbolFile}");

                //    var result = await _kicadClient.Send(
                //        symbolFile,
                //        libName,
                //        _options.AddToGlobalLibrary);

                //    if (result)
                //    {
                //        _logger.LogInformation($"Successfully imported symbol library: {libName}");
                //        success = true;
                //    }
                //    else
                //    {
                //        _logger.Log(LogLevel.Warning, $"Failed to import symbol library: {libName}");
                //    }
                //}

                //return success;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error importing symbols: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Imports footprints from extracted UltraLibrarian files
        /// </summary>
        /// <param name="extractDir">Directory containing extracted UltraLibrarian files</param>
        /// <returns>True if successful, false otherwise</returns>
        private async Task<bool> ImportFootprintsAsync(string extractDir)
        {
            throw new NotImplementedException();
            //try
            //{
            //    _logger.LogInformation("Importing footprints");

            //    // Look for .pretty directories first
            //    var prettyDirs = Directory.GetDirectories(extractDir, "*.pretty", SearchOption.AllDirectories);
            //    bool success = false;

            //    // If .pretty directories exist, import those
            //    foreach (var prettyDir in prettyDirs)
            //    {
            //        string libName = Path.GetFileNameWithoutExtension(prettyDir);
            //        _logger.Log(LogLevel.Debug, $"Importing footprint library: {libName} from {prettyDir}");

            //        var result = await _kicadClient.ImportFootprintLibraryAsync(
            //            prettyDir,
            //            libName,
            //            _options.AddToGlobalLibrary);

            //        if (result)
            //        {
            //            _logger.LogInformation($"Successfully imported footprint library: {libName}");
            //            success = true;
            //        }
            //        else
            //        {
            //            _logger.Log(LogLevel.Warning, $"Failed to import footprint library: {libName}");
            //        }
            //    }

            //    // If no .pretty directories, look for individual .kicad_mod files
            //    if (prettyDirs.Length == 0)
            //    {
            //        var footprintFiles = Directory.GetFiles(extractDir, "*.kicad_mod", SearchOption.AllDirectories);

            //        if (footprintFiles.Length > 0)
            //        {
            //            // Create a temporary .pretty directory and copy files there
            //            string tempPrettyDir = Path.Combine(Path.GetTempPath(), "UltraLibrarian.pretty");
            //            Directory.CreateDirectory(tempPrettyDir);

            //            foreach (var footprintFile in footprintFiles)
            //            {
            //                File.Copy(
            //                    footprintFile,
            //                    Path.Combine(tempPrettyDir, Path.GetFileName(footprintFile)),
            //                    true);
            //            }

            //            // Import the temporary .pretty directory
            //            var result = await _kicadClient.ImportFootprintLibraryAsync(
            //                tempPrettyDir,
            //                "UltraLibrarian",
            //                _options.AddToGlobalLibrary);

            //            if (result)
            //            {
            //                _logger.LogInformation("Successfully imported footprint files");
            //                success = true;
            //            }
            //            else
            //            {
            //                _logger.Log(LogLevel.Warning, "Failed to import footprint files");
            //            }

            //            // Clean up the temporary directory
            //            try
            //            {
            //                Directory.Delete(tempPrettyDir, true);
            //            }
            //            catch (Exception ex)
            //            {
            //                _logger.Log(LogLevel.Warning, $"Failed to clean up temporary .pretty directory: {ex.Message}");
            //            }
            //        }
            //        else
            //        {
            //            _logger.Log(LogLevel.Warning, "No footprint files found");
            //        }
            //    }

            //    return success;
            //}
            //catch (Exception ex)
            //{
            //    _logger.Log(LogLevel.Error, $"Error importing footprints: {ex.Message}");
            //    return false;
            //}
        }

        /// <summary>
        /// Imports 3D models from extracted UltraLibrarian files
        /// </summary>
        /// <param name="extractDir">Directory containing extracted UltraLibrarian files</param>
        /// <returns>True if successful, false otherwise</returns>
        private async Task<bool> Import3DModelsAsync(string extractDir)
        {
            try
            {
                throw new NotImplementedException();
                //_logger.LogInformation("Importing 3D models");

                //// Find 3D model files (STEP, STP, VRML, WRL)
                //var modelFiles = Directory.GetFiles(extractDir, "*.step", SearchOption.AllDirectories)
                //    .Concat(Directory.GetFiles(extractDir, "*.stp", SearchOption.AllDirectories))
                //    .Concat(Directory.GetFiles(extractDir, "*.wrl", SearchOption.AllDirectories))
                //    .Concat(Directory.GetFiles(extractDir, "*.vrml", SearchOption.AllDirectories))
                //    .ToList();

                //if (modelFiles.Count == 0)
                //{
                //    _logger.Log(LogLevel.Warning, "No 3D model files found");
                //    return false;
                //}

                //// Get KiCad 3D models directory
                //string? kicad3DDir = await _kicadClient.GetKiCad3DModelsDirAsync();
                //if (string.IsNullOrEmpty(kicad3DDir))
                //{
                //    _logger.Log(LogLevel.Error, "KiCad 3D models directory not found");
                //    return false;
                //}

                //// Create UltraLibrarian subdirectory in 3D models folder
                //string ultralibrarian3DDir = Path.Combine(kicad3DDir, "UltraLibrarian");
                //Directory.CreateDirectory(ultralibrarian3DDir);

                //// Copy 3D model files
                //bool success = false;
                //foreach (var modelFile in modelFiles)
                //{
                //    string destPath = Path.Combine(ultralibrarian3DDir, Path.GetFileName(modelFile));
                //    File.Copy(modelFile, destPath, true);
                //    _logger.Log(LogLevel.Debug, $"Copied 3D model: {Path.GetFileName(modelFile)}");
                //    success = true;
                //}

                //if (success)
                //{
                //    _logger.LogInformation($"3D models imported to {ultralibrarian3DDir}");
                //}

                //return success;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error importing 3D models: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Options for importing UltraLibrarian components
    /// </summary>
    public class ImportOptions
    {
        /// <summary>
        /// Whether to add components to the global library
        /// </summary>
        public bool AddToGlobalLibrary { get; set; } = true;

        /// <summary>
        /// Whether to clean up downloaded files after import
        /// </summary>
        public bool CleanupAfterImport { get; set; } = true;
    }

    /// <summary>
    /// Types of components to import
    /// </summary>
    [Flags]
    public enum ImportType
    {
        Symbol = 1,
        Footprint = 2,
        Model3D = 4,
        All = Symbol | Footprint | Model3D
    }

    /// <summary>
    /// Result of an import operation
    /// </summary>
    public class ImportResult
    {
        /// <summary>
        /// Whether the import was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Whether the symbol import was successful
        /// </summary>
        public bool SymbolImportSuccess { get; set; }

        /// <summary>
        /// Whether the footprint import was successful
        /// </summary>
        public bool FootprintImportSuccess { get; set; }

        /// <summary>
        /// Whether the 3D model import was successful
        /// </summary>
        public bool Model3DImportSuccess { get; set; }

        /// <summary>
        /// Detailed messages about the import process
        /// </summary>
        public List<string> Details { get; } = new List<string>();
    }
}