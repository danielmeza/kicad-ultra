using System.Threading.Tasks;

namespace UltraLibrarianImporter.UI.Services.Interfaces
{
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
    }
}
