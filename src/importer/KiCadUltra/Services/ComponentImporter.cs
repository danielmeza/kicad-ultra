using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using KiCadUltra.Services.Interfaces;
using KiCadUltra.Services.Providers;

namespace KiCadUltra.Services;

/// <summary>
/// Options for importing component libraries.
/// </summary>
public class ImportOptions
{
    /// <summary>Which of KiCad's library tables imported libraries are registered in (#71).</summary>
    public LibraryRegistrationScope RegistrationScope { get; set; } = LibraryRegistrationScope.Automatic;

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
/// How an import ended, as <see cref="ImportResult.Outcome"/> judges it (#102).
/// </summary>
public enum ImportOutcome
{
    /// <summary>Every requested step worked.</summary>
    Succeeded,

    /// <summary>At least one requested step worked, and the <see cref="ImportResult.FailedSteps"/> did not.</summary>
    PartiallySucceeded,

    /// <summary>No requested step worked.</summary>
    Failed,

    /// <summary>The user cancelled the import before it finished.</summary>
    Cancelled,
}

/// <summary>
/// Result of an import operation.
/// </summary>
public class ImportResult
{
    /// <summary>
    /// At least one requested step worked. Also true for a partial import: <see cref="Outcome"/>
    /// tells a partial import from a full one (#102).
    /// </summary>
    public bool Success { get; set; }
    public bool SymbolImportSuccess { get; set; }
    public bool FootprintImportSuccess { get; set; }
    public bool Model3DImportSuccess { get; set; }

    /// <summary>
    /// The steps the import was asked for, which <see cref="Outcome"/> is judged against. Required
    /// because a result without it would report every import as failed.
    /// </summary>
    public required ImportType RequestedSteps { get; init; }

    /// <summary>The user cancelled the import before it finished. <see cref="Success"/> is then false.</summary>
    public bool Cancelled { get; set; }

    public List<string> Details { get; } = [];

    /// <summary>The requested steps whose per-step flag is false.</summary>
    public ImportType FailedSteps => ImportType.All & RequestedSteps & ~SucceededSteps;

    /// <summary>From the per-step flags and <see cref="RequestedSteps"/>, unless the import was cancelled.</summary>
    public ImportOutcome Outcome =>
        Cancelled ? ImportOutcome.Cancelled
        : (RequestedSteps & SucceededSteps) == 0 ? ImportOutcome.Failed
        : FailedSteps == 0 ? ImportOutcome.Succeeded
        : ImportOutcome.PartiallySucceeded;

    private ImportType SucceededSteps =>
        (SymbolImportSuccess ? ImportType.Symbol : 0)
        | (FootprintImportSuccess ? ImportType.Footprint : 0)
        | (Model3DImportSuccess ? ImportType.Model3D : 0);
}

/// <summary>
/// Imports one downloaded component archive with a fixed provider and a fixed set of options.
/// Delegates to <see cref="IKiCadImportEngine"/>, defaulting to <see cref="UltraLibrarianProvider"/>.
/// </summary>
/// <remarks>
/// Named after what it does rather than after the product (#132): the file carried the old assembly
/// name, <c>UltraLibrarianImporter</c>, which the rename would have turned into the product's new
/// name and said no more about the class than the old one did.
/// </remarks>
public class ComponentImporter
{
    private readonly IKiCadImportEngine _engine;
    private readonly IComponentProvider _provider;
    private readonly ImportOptions _options;

    public ComponentImporter(IKiCadImportEngine engine, ImportOptions options, IComponentProvider? provider = null)
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
