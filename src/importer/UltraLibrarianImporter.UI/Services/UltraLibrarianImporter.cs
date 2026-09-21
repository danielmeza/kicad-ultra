using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.Services.Providers;

namespace UltraLibrarianImporter.UI.Services;

/// <summary>
/// Options for importing component libraries.
/// </summary>
public class ImportOptions
{
    public bool AddToGlobalLibrary { get; set; } = true;
    public bool CleanupAfterImport { get; set; } = true;
    public string TargetPath { get; set; } = string.Empty;
    public bool UseProjectPath { get; set; } = true;
    public bool AutoImportWhenDownloaded { get; set; } = true;
    public string LibraryName { get; set; } = string.Empty;

    /// <summary>
    /// The easyeda2kicad executable, or a Python interpreter to run it with <c>-m</c>, chosen in Settings.
    /// Empty to look for it on <c>PATH</c> and then in KiCad's Python (#76).
    /// </summary>
    public string EasyEda2KiCadPath { get; set; } = string.Empty;
}

/// <summary>
/// Types of components to import.
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
/// Result of an import operation.
/// </summary>
public class ImportResult
{
    public bool Success { get; set; }
    public bool SymbolImportSuccess { get; set; }
    public bool FootprintImportSuccess { get; set; }
    public bool Model3DImportSuccess { get; set; }

    /// <summary>The user cancelled the import before it finished. <see cref="Success"/> is then false.</summary>
    public bool Cancelled { get; set; }

    public List<string> Details { get; } = [];
}

/// <summary>
/// Legacy importer entrypoint for UltraLibrarian. Delegates to <see cref="IKiCadImportEngine"/>
/// and <see cref="UltraLibrarianProvider"/> for backward compatibility.
/// </summary>
public class UltraLibrarianImporter
{
    private readonly IKiCadImportEngine _engine;
    private readonly IComponentProvider _provider;
    private readonly ImportOptions _options;

    public UltraLibrarianImporter(IKiCadImportEngine engine, ImportOptions options, IComponentProvider? provider = null)
    {
        _engine = engine;
        _options = options;
        _provider = provider ?? new UltraLibrarianProvider();
    }

    public Task<ImportResult> ImportComponentAsync(string zipFilePath, ImportType importType)
    {
        return _engine.ImportAsync(_provider, zipFilePath, importType, _options);
    }
}
