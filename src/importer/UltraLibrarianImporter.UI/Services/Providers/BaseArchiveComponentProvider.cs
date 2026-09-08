using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services.Providers
{
    /// <summary>
    /// Common base class for component providers that distribute assets as zip archives.
    /// </summary>
    public abstract class BaseArchiveComponentProvider : IComponentProvider
    {
        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public abstract string SearchUrl { get; }
        public abstract string DefaultPrefix { get; }
        public abstract string DefaultLibraryName { get; }

        public virtual bool CanHandleDownload(string filePath)
        {
            return filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        }

        public virtual Task<ProviderExtractionResult> ExtractPackageAsync(string packageFilePath, string destinationDir)
        {
            if (!File.Exists(packageFilePath))
            {
                throw new FileNotFoundException("Package file not found.", packageFilePath);
            }

            Directory.CreateDirectory(destinationDir);
            ZipFile.ExtractToDirectory(packageFilePath, destinationDir, overwriteFiles: true);

            var symbolFiles = Directory.GetFiles(destinationDir, "*.kicad_sym", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(destinationDir, "*.lib", SearchOption.AllDirectories))
                .ToList();

            var prettyDirs = Directory.GetDirectories(destinationDir, "*.pretty", SearchOption.AllDirectories)
                .ToList();

            var prettyDirSet = new HashSet<string>(prettyDirs, StringComparer.OrdinalIgnoreCase);
            var looseFootprints = Directory.GetFiles(destinationDir, "*.kicad_mod", SearchOption.AllDirectories)
                .Where(f =>
                {
                    string? dir = Path.GetDirectoryName(f);
                    return dir == null || !prettyDirSet.Any(p => dir.StartsWith(p, StringComparison.OrdinalIgnoreCase));
                })
                .ToList();

            var model3DFiles = Directory.GetFiles(destinationDir, "*.step", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(destinationDir, "*.stp", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(destinationDir, "*.wrl", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(destinationDir, "*.vrml", SearchOption.AllDirectories))
                .ToList();

            var result = new ProviderExtractionResult(
                symbolFiles,
                prettyDirs,
                looseFootprints,
                model3DFiles
            );

            return Task.FromResult(result);
        }

        public virtual bool SupportsDirectApi => false;

        public virtual Task<IReadOnlyList<PartSearchResult>> SearchPartsAsync(string query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PartSearchResult>>(Array.Empty<PartSearchResult>());
        }
    }
}
