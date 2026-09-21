using System.Threading;
using System.Threading.Tasks;

namespace UltraLibrarianImporter.UI.Services.Interfaces;

/// <summary>
/// Core engine responsible for importing extracted component assets into KiCad libraries and project structures.
/// </summary>
public interface IKiCadImportEngine
{
    Task<ImportResult> ImportAsync(
        IComponentProvider provider,
        string packageFilePath,
        ImportType importType,
        ImportOptions options);

    /// <summary>
    /// Imports an LCSC part by running the user-installed easyeda2kicad, which converts it straight into
    /// the target library, and then registers that library as <see cref="ImportAsync"/> does (#76).
    /// </summary>
    /// <param name="provider">Names the default library. The Part Explorer passes the EasyEDA provider, whichever
    /// search found the part (#47).</param>
    /// <param name="lcscPartNumber">The part's LCSC code, <c>C</c> followed by digits.</param>
    /// <param name="importType">Which of symbol, footprint and 3D model to import.</param>
    /// <param name="options">Where to import to, as for <see cref="ImportAsync"/>.</param>
    /// <param name="cancellationToken">Stops the conversion; the library is then rolled back.</param>
    /// <returns>Per-asset success, from the files the tool actually wrote, with its stderr in
    /// <see cref="ImportResult.Details"/>. Not installed, a non-zero exit, a timeout or a
    /// cancellation is a failure.</returns>
    Task<ImportResult> ImportLcscPartAsync(
        IComponentProvider provider,
        string lcscPartNumber,
        ImportType importType,
        ImportOptions options,
        CancellationToken cancellationToken = default);
}
