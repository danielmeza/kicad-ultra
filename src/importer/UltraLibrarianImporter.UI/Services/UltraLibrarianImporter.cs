using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

using KiCadSharp;
using KiCadSharp.Documents;

using Microsoft.Extensions.Logging;

namespace UltraLibrarianImporter.UI.Services
{

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

        /// <summary>
        /// Target path for importing libraries (if empty, uses project path)
        /// </summary>
        public string TargetPath { get; set; } = string.Empty;

        /// <summary>
        /// Whether to use the project path as the target path
        /// </summary>
        public bool UseProjectPath { get; set; } = true;

        /// <summary>
        /// Whether to automatically import the library when downloaded or require manual import
        /// </summary>
        public bool AutoImportWhenDownloaded { get; set; } = true;
        
        /// <summary>
        /// Custom name to use for the library (if empty, default naming will be used)
        /// </summary>
        public string LibraryName { get; set; } = string.Empty;
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

    /// <summary>
    /// Handles importing UltraLibrarian components into KiCad by directly creating/modifying libraries
    /// </summary>
    public class UltraLibrarianImporter
    {
        private readonly KiCad _kicad;
        private readonly ILogger _logger;
        private readonly ImportOptions _options;
        private string _projectPath;
        private string _projectName;

        /// <summary>
        /// Creates a new instance of the UltraLibrarian Importer
        /// </summary>
        /// <param name="kicad">KiCad client for communication with KiCad</param>
        /// <param name="logger">Logger for recording operations</param>
        /// <param name="options">Import options</param>
        public UltraLibrarianImporter(KiCad kicad, ILogger logger, ImportOptions options)
        {
            _kicad = kicad;
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

                // Get project path from KiCad
                GetProjectPath();

                Directory.CreateDirectory(tempDir);

                // Extract the downloaded ZIP file
                _logger.LogDebug($"Extracting to {tempDir}");
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
                    _logger.LogDebug($"Cleaned up downloaded file: {zipFilePath}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error importing component: {ex.Message}");
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
                        _logger.LogDebug($"Cleaned up temp directory: {tempDir}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to clean up temp directory: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Gets the current project path from KiCad
        /// </summary>
        private void GetProjectPath()
        {
            try
            {
                // If we should use project path and there's an open project
                if (_options.UseProjectPath)
                {
                    _projectPath = KiCadEnvironment.GetProjectDirectory() ?? throw new InvalidOperationException("Project path not in the environment variables, probably this was not launched from KiCad");
                    var projects = Directory.GetFiles(_projectPath, $"*{KiCadFileExtensions.Project}");
                    _projectName = projects.FirstOrDefault() ?? throw new InvalidOperationException("Not project found in: " + _projectPath);
                    _projectName = Path.GetFileNameWithoutExtension(_projectName);
                    _logger.LogInformation("Using project: {Name} at {Path}", _projectName, _projectPath);
                }
                else if (!string.IsNullOrWhiteSpace(_options.TargetPath))
                {
                    // Use the custom target path
                    _projectPath = _options.TargetPath;
                    _projectName = Path.GetFileNameWithoutExtension(_projectPath);
                    if (string.IsNullOrEmpty(_projectName))
                    {
                        _projectName = "UltraLibrarian";
                    }

                    _logger.LogInformation($"Using custom target path: {_projectPath}");
                }
                else
                {
                    // Fall back to default
                    _projectPath = string.Empty;
                    _projectName = "UltraLibrarian";
                    _logger.LogInformation("Using default library path");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not get project path, using custom target path or default location");

                if (!string.IsNullOrWhiteSpace(_options.TargetPath) && !_options.UseProjectPath)
                {
                    _projectPath = _options.TargetPath;
                    _projectName = Path.GetFileNameWithoutExtension(_projectPath);
                    if (string.IsNullOrEmpty(_projectName))
                    {
                        _projectName = "UltraLibrarian";
                    }

                    _logger.LogInformation($"Using custom target path: {_projectPath}");
                }
                else
                {
                    _projectPath = string.Empty;
                    _projectName = "UltraLibrarian";
                }
            }

            // Make sure the target directory exists
            if (!string.IsNullOrEmpty(_projectPath))
            {
                string targetDir = Path.GetDirectoryName(_projectPath);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    try
                    {
                        Directory.CreateDirectory(targetDir);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Could not create target directory: {targetDir}");
                        _projectPath = string.Empty;
                        _projectName = "UltraLibrarian";
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
            try
            {
                _logger.LogInformation("Importing schematic symbols");

                // Find symbol files (.lib or .kicad_sym)
                var symbolFiles = Directory.GetFiles(extractDir, "*.lib", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(extractDir, "*.kicad_sym", SearchOption.AllDirectories))
                    .ToList();

                if (symbolFiles.Count == 0)
                {
                    _logger.LogWarning("No symbol files found");
                    return false;
                }

                bool success = false;

                // Determine the target library path
                string libraryBaseName = !string.IsNullOrEmpty(_options.LibraryName) 
                    ? _options.LibraryName 
                    : "UltraLibrarian"; // Default name if custom name not provided
                
                if (string.IsNullOrEmpty(_options.LibraryName) && !string.IsNullOrEmpty(_projectPath))
                {
                    // Use project-specific symbol library if no custom name and project exists
                    libraryBaseName = Path.GetFileNameWithoutExtension(_projectName) + "_UltraLibrarian";
                }
                string symbolLibPath = string.Empty;

                // Determine the symbol library path
                if (!string.IsNullOrEmpty(_projectPath))
                {
                    // Create/use a project-specific library in the project directory
                    symbolLibPath = Path.Combine(Path.GetDirectoryName(_projectPath), $"{libraryBaseName}.kicad_sym");
                }
                else
                {
                    // If no project is open, use global library path
                    var libraryTable = await GetSymbolLibraryPath();
                    symbolLibPath = Path.Combine(Path.GetDirectoryName(libraryTable), $"{libraryBaseName}.kicad_sym");
                }

                // Create a new library or load existing
                KiCadSymbolLibrary symbolLibrary;
                if (File.Exists(symbolLibPath))
                {
                    try
                    {
                        _logger.LogDebug($"Loading existing symbol library from {symbolLibPath}");
                        symbolLibrary = KiCadSymbolLibrary.Load(symbolLibPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to load existing symbol library. Creating a new one.");
                        symbolLibrary = new KiCadSymbolLibrary($"UltraLibrarian Importer {DateTime.Now:yyyy-MM-dd}");
                    }
                }
                else
                {
                    _logger.LogDebug($"Creating new symbol library at {symbolLibPath}");
                    symbolLibrary = new KiCadSymbolLibrary($"UltraLibrarian Importer {DateTime.Now:yyyy-MM-dd}");
                }

                // Process each symbol file
                foreach (var symbolFile in symbolFiles)
                {
                    try
                    {
                        string fileName = Path.GetFileName(symbolFile);
                        _logger.LogDebug($"Processing symbol file: {fileName}");

                        // Load the source library
                        KiCadSymbolLibrary sourceLibrary = KiCadSymbolLibrary.Load(symbolFile);

                        // Add all symbols to our target library
                        int symbolsAdded = 0;
                        foreach (var symbol in sourceLibrary.Symbols)
                        {
                            // Add a prefix to avoid name collisions
                            string symbolId = $"UL_{symbol.Id}";
                            symbol.Id = symbolId;
                            //// Check if symbol already exists
                            //if (symbolLibrary.GetSymbol(symbolId) != null)
                            //{
                            //    _logger.LogDebug($"Symbol {symbolId} already exists, skipping");
                            //    continue;
                            //}

                            //// Create a new symbol with a unique ID
                            //var newSymbol = new KiCadSymbol(symbolId);

                            //// Copy properties from original symbol
                            //foreach (var prop in symbol.Properties)
                            //{
                            //    newSymbol.AddProperty(prop.Key, prop.Value);
                            //}

                            //// Copy pins from original symbol
                            //foreach (var pin in symbol.Pins)
                            //{
                            //    newSymbol.AddPin(pin);
                            //}

                            //// Copy graphical items from original symbol
                            //foreach (var item in symbol.GraphicalItems)
                            //{
                            //    newSymbol.AddGraphicalItem(item);
                            //}

                            //// Set other symbol properties
                            //newSymbol.HidePinNames = symbol.HidePinNames;
                            //newSymbol.HidePinNumbers = symbol.HidePinNumbers;
                            //newSymbol.InBom = symbol.InBom;
                            //newSymbol.OnBoard = symbol.OnBoard;

                            // Add the symbol to our library
                            symbolLibrary.AddSymbol(symbol);
                            symbolsAdded++;
                        }

                        _logger.LogInformation($"Added {symbolsAdded} symbols from {fileName}");
                        success = symbolsAdded > 0 || success;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing symbol file {Path.GetFileName(symbolFile)}");
                    }
                }

                if (success)
                {
                    // Save the modified library
                    try
                    {
                        _logger.LogDebug($"Saving symbol library to {symbolLibPath}");
                        symbolLibrary.Save(symbolLibPath);

                        // Update the library table if needed
                        if (_options.AddToGlobalLibrary)
                        {
                            await AddSymbolLibraryToTable(symbolLibPath, libraryBaseName);
                        }

                        _logger.LogInformation($"Successfully saved symbol library to {symbolLibPath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to save symbol library: {ex.Message}");
                        success = false;
                    }
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error importing symbols: {ex.Message}");
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
            try
            {
                _logger.LogInformation("Importing footprints");

                // Determine target footprint library path
                string libraryBaseName = !string.IsNullOrEmpty(_options.LibraryName) 
                    ? _options.LibraryName 
                    : "UltraLibrarian"; // Default name if custom name not provided
                
                if (string.IsNullOrEmpty(_options.LibraryName) && !string.IsNullOrEmpty(_projectPath))
                {
                    // Use project-specific footprint library if no custom name and project exists
                    libraryBaseName = Path.GetFileNameWithoutExtension(_projectName) + "_UltraLibrarian";
                }

                string footprintLibPath = string.Empty;

                // Determine the footprint library path
                if (!string.IsNullOrEmpty(_projectPath))
                {
                    // Create/use a project-specific library in the project directory
                    footprintLibPath = Path.Combine(Path.GetDirectoryName(_projectPath), $"{libraryBaseName}.pretty");
                }
                else
                {
                    // If no project is open, use global library path
                    var libraryTable = await GetFootprintLibraryPath();
                    footprintLibPath = Path.Combine(Path.GetDirectoryName(libraryTable), $"{libraryBaseName}.pretty");
                }

                // Create the .pretty directory if it doesn't exist
                Directory.CreateDirectory(footprintLibPath);

                // Look for .pretty directories first (KiCad footprint libraries)
                var prettyDirs = Directory.GetDirectories(extractDir, "*.pretty", SearchOption.AllDirectories);
                bool success = false;

                // Process all .pretty directories
                foreach (var prettyDir in prettyDirs)
                {
                    try
                    {
                        string dirName = Path.GetFileName(prettyDir);
                        _logger.LogDebug($"Processing footprint library: {dirName}");

                        // Get all .kicad_mod files in the directory
                        var footprintFiles = Directory.GetFiles(prettyDir, "*.kicad_mod");

                        // Process each footprint file
                        foreach (var footprintFile in footprintFiles)
                        {
                            try
                            {
                                string fileName = Path.GetFileName(footprintFile);
                                _logger.LogDebug($"Processing footprint: {fileName}");

                                // Load the footprint
                                KiCadFootprintLibrary sourceLib = KiCadFootprintLibrary.Load(footprintFile);

                                if (sourceLib.Footprints.Count <= 0)
                                {
                                    continue;
                                }
                                foreach (var footprint in sourceLib.Footprints)
                                {

                                    // Rename footprint to avoid conflicts
                                    string newFootprintId = $"UL_{footprint.Id}";
                                    var newFootprint = new KiCadFootprint(newFootprintId);

                                    // Copy properties from original footprint
                                    // Pads
                                    foreach (var pad in footprint.Pads)
                                    {
                                        newFootprint.Pads.Add(pad);
                                    }

                                    // Text items
                                    foreach (var text in footprint.TextItems)
                                    {
                                        newFootprint.TextItems.Add(text);
                                    }

                                    // Lines
                                    foreach (var line in footprint.Lines)
                                    {
                                        newFootprint.Lines.Add(line);
                                    }

                                    // Circles
                                    foreach (var circle in footprint.Circles)
                                    {
                                        newFootprint.Circles.Add(circle);
                                    }

                                    // Arcs
                                    foreach (var arc in footprint.Arcs)
                                    {
                                        newFootprint.Arcs.Add(arc);
                                    }

                                    // Polygons
                                    foreach (var poly in footprint.Polygons)
                                    {
                                        newFootprint.Polygons.Add(poly);
                                    }

                                    // 3D Models
                                    foreach (var model in footprint.Models)
                                    {
                                        newFootprint.Models.Add(model);
                                    }

                                    // Save the footprint to the target library
                                    string destPath = Path.Combine(footprintLibPath, $"{newFootprintId}.kicad_mod");
                                    KiCadFootprintLibrary.SaveFootprint(newFootprint, destPath);
                                    _logger.LogDebug($"Saved footprint to {destPath}");
                                    success = true;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, $"Error processing footprint file {Path.GetFileName(footprintFile)}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing footprint library {Path.GetFileName(prettyDir)}");
                    }
                }

                // Process individual .kicad_mod files outside .pretty directories
                var individualFootprintFiles = Directory.GetFiles(extractDir, "*.kicad_mod", SearchOption.AllDirectories)
                    .Where(f => !f.Contains(".pretty")).ToList();

                foreach (var footprintFile in individualFootprintFiles)
                {
                    try
                    {
                        string fileName = Path.GetFileName(footprintFile);
                        _logger.LogDebug($"Processing individual footprint: {fileName}");

                        // Load the footprint
                        KiCadFootprintLibrary sourceLib = KiCadFootprintLibrary.Load(footprintFile);

                        if (sourceLib.Footprints.Count > 0)
                        {
                            var footprint = sourceLib.Footprints[0];

                            // Rename footprint to avoid conflicts
                            string newFootprintId = $"UL_{footprint.Id}";
                            var newFootprint = new KiCadFootprint(newFootprintId);

                            // Copy properties from original footprint (same code as above)
                            // Pads
                            foreach (var pad in footprint.Pads)
                            {
                                newFootprint.Pads.Add(pad);
                            }

                            // Text items
                            foreach (var text in footprint.TextItems)
                            {
                                newFootprint.TextItems.Add(text);
                            }

                            // Lines
                            foreach (var line in footprint.Lines)
                            {
                                newFootprint.Lines.Add(line);
                            }

                            // Circles
                            foreach (var circle in footprint.Circles)
                            {
                                newFootprint.Circles.Add(circle);
                            }

                            // Arcs
                            foreach (var arc in footprint.Arcs)
                            {
                                newFootprint.Arcs.Add(arc);
                            }

                            // Polygons
                            foreach (var poly in footprint.Polygons)
                            {
                                newFootprint.Polygons.Add(poly);
                            }

                            // 3D Models
                            foreach (var model in footprint.Models)
                            {
                                newFootprint.Models.Add(model);
                            }

                            // Save the footprint to the target library
                            string destPath = Path.Combine(footprintLibPath, $"{newFootprintId}.kicad_mod");
                            KiCadFootprintLibrary.SaveFootprint(newFootprint, destPath);
                            _logger.LogDebug($"Saved footprint to {destPath}");
                            success = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Error processing individual footprint file {Path.GetFileName(footprintFile)}");
                    }
                }

                // Update the library table if needed
                if (success && _options.AddToGlobalLibrary)
                {
                    await AddFootprintLibraryToTable(footprintLibPath, libraryBaseName);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error importing footprints: {ex.Message}");
                return false;
            }
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
                _logger.LogInformation("Importing 3D models");

                // Find 3D model files (STEP, STP, VRML, WRL)
                var modelFiles = Directory.GetFiles(extractDir, "*.step", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(extractDir, "*.stp", SearchOption.AllDirectories))
                    .Concat(Directory.GetFiles(extractDir, "*.wrl", SearchOption.AllDirectories))
                    .Concat(Directory.GetFiles(extractDir, "*.vrml", SearchOption.AllDirectories))
                    .ToList();

                if (modelFiles.Count == 0)
                {
                    _logger.LogWarning("No 3D model files found");
                    return false;
                }

                // Determine the target 3D model directory
                string modelDir;

                if (!string.IsNullOrEmpty(_projectPath))
                {
                    // Create a project-specific 3D models folder
                    modelDir = Path.Combine(
                        Path.GetDirectoryName(_projectPath),
                        "3d_models",
                        "UltraLibrarian");
                }
                else
                {
                    // Use the KiCad 3D models directory
                    modelDir = await Get3DModelPath();
                    modelDir = Path.Combine(modelDir, "UltraLibrarian");
                }

                // Create the directory if it doesn't exist
                Directory.CreateDirectory(modelDir);

                // Copy 3D model files
                bool success = false;
                foreach (var modelFile in modelFiles)
                {
                    try
                    {
                        string destPath = Path.Combine(modelDir, Path.GetFileName(modelFile));
                        File.Copy(modelFile, destPath, true);
                        _logger.LogDebug($"Copied 3D model: {Path.GetFileName(modelFile)}");
                        success = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to copy 3D model {Path.GetFileName(modelFile)}");
                    }
                }

                if (success)
                {
                    _logger.LogInformation($"3D models imported to {modelDir}");

                    // Refresh the 3D model path
                    try
                    {
                        await _kicad.RefreshPaths();
                        _logger.LogDebug("Refreshed KiCad paths after 3D model import");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to refresh paths after 3D model import");
                    }
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error importing 3D models: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Adds a symbol library to the KiCad symbol library table
        /// </summary>
        private async Task AddSymbolLibraryToTable(string libraryPath, string libraryName)
        {
            // Run a KiCad tool action to register the library
            // Note: This would normally use a specific KiCad API call
            var result = await _kicad.RunAction($"common.Control.addLibrary");

            if (result.Status == Kiapi.Common.Commands.RunActionStatus.RasOk)
            {
                _logger.LogInformation($"Symbol library added to library table: {libraryName}");
            }

        }

        /// <summary>
        /// Adds a footprint library to the KiCad footprint library table
        /// </summary>
        private async Task AddFootprintLibraryToTable(string libraryPath, string libraryName)
        {
            // Run a KiCad tool action to register the library
            // Note: This would normally use a specific KiCad API call
            await _kicad.RunAction($"pcbnew.FpLibTable.AddLibrary:{libraryPath}:{libraryName}");
            _logger.LogInformation($"Footprint library added to library table: {libraryName}");
        }


        /// <summary>
        /// Gets the path to the KiCad symbol library table
        /// </summary>
        private async Task<string> GetSymbolLibraryPath()
        {
            // First try to get project-specific paths
            if (!string.IsNullOrEmpty(_projectPath))
            {
                string projectDir = Path.GetDirectoryName(_projectPath);
                string symLibTable = Path.Combine(projectDir, "sym-lib-table");
                if (File.Exists(symLibTable))
                {
                    return symLibTable;
                }
            }

            // Fall back to user's KiCad configuration directory
            string kicadConfigDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            kicadConfigDir = Path.Combine(kicadConfigDir, "kicad", "9.0");
            if (Directory.Exists(kicadConfigDir))
            {
                string symLibTable = Path.Combine(kicadConfigDir, "sym-lib-table");
                if (File.Exists(symLibTable))
                {
                    return symLibTable;
                }
            }

            // If all else fails, use the project directory or temp directory
            return !string.IsNullOrEmpty(_projectPath)
                ? Path.Combine(Path.GetDirectoryName(_projectPath), "symbols")
                : Path.Combine(Path.GetTempPath(), "kicad_symbols");
        }

        /// <summary>
        /// Gets the path to the KiCad footprint library table
        /// </summary>
        private async Task<string> GetFootprintLibraryPath()
        {
            // First try to get project-specific paths
            if (!string.IsNullOrEmpty(_projectPath))
            {
                string projectDir = Path.GetDirectoryName(_projectPath);
                string fpLibTable = Path.Combine(projectDir, "fp-lib-table");
                if (File.Exists(fpLibTable))
                {
                    return fpLibTable;
                }
            }

            // Fall back to user's KiCad configuration directory
            string kicadConfigDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            kicadConfigDir = Path.Combine(kicadConfigDir, "kicad", "7.0");
            if (Directory.Exists(kicadConfigDir))
            {
                string fpLibTable = Path.Combine(kicadConfigDir, "fp-lib-table");
                if (File.Exists(fpLibTable))
                {
                    return fpLibTable;
                }
            }

            // If all else fails, use the project directory or temp directory
            return !string.IsNullOrEmpty(_projectPath)
                ? Path.Combine(Path.GetDirectoryName(_projectPath), "footprints")
                : Path.Combine(Path.GetTempPath(), "kicad_footprints");
        }

        /// <summary>
        /// Gets the path to the KiCad 3D models directory
        /// </summary>
        private async Task<string> Get3DModelPath()
        {
            // First try KiCad's environment variables
            string kicadEnv = Environment.GetEnvironmentVariable("KICAD7_3DMODEL_DIR");
            if (!string.IsNullOrEmpty(kicadEnv) && Directory.Exists(kicadEnv))
            {
                return kicadEnv;
            }

            // Try to use a project-specific path
            if (!string.IsNullOrEmpty(_projectPath))
            {
                string projectDir = Path.GetDirectoryName(_projectPath);
                string modelDir = Path.Combine(projectDir, "3d_models");
                Directory.CreateDirectory(modelDir);
                return modelDir;
            }

            // Fall back to common KiCad paths based on platform
            if (OperatingSystem.IsWindows())
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string modelDir = Path.Combine(programFiles, "KiCad", "7.0", "share", "kicad", "3dmodels");
                if (Directory.Exists(modelDir))
                {
                    return modelDir;
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                string modelDir = "/Applications/KiCad/KiCad.app/Contents/SharedSupport/3dmodels";
                if (Directory.Exists(modelDir))
                {
                    return modelDir;
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                string modelDir = "/usr/share/kicad/3dmodels";
                if (Directory.Exists(modelDir))
                {
                    return modelDir;
                }
            }

            // If all else fails, use the project directory or temp directory
            return !string.IsNullOrEmpty(_projectPath)
                ? Path.Combine(Path.GetDirectoryName(_projectPath), "3d_models")
                : Path.Combine(Path.GetTempPath(), "kicad_3dmodels");
        }
    }
}