using System.Threading;
using System.Threading.Tasks;
using KiCadSharp;

namespace KiCadUltra.Services.Interfaces;

/// <summary>
/// What this build declares about the KiCad versions it supports, and how the KiCad that is running
/// compares with it (#138).
/// </summary>
public interface IKiCadCompatibility
{
    /// <summary>
    /// What this build ships, or <see langword="null"/> when the file beside the executable is
    /// missing or unreadable. Nothing refuses to run over it; "not declared" means "cannot judge".
    /// </summary>
    KiCadCompatibilityManifest? Shipped { get; }

    /// <summary>One line naming the range this build supports, for a log line or a window.</summary>
    string DescribeShipped();

    /// <summary>
    /// One line saying that <paramref name="kicad"/> is outside what this build supports, or
    /// <see langword="null"/> when it is inside it or nothing is declared.
    /// </summary>
    string? WarnAbout(KiCadVersion kicad);

    /// <summary>
    /// The running KiCad's version, or <see langword="null"/> when KiCad cannot be asked: nothing is
    /// listening, it answered with an error, or it did not answer in time.
    /// </summary>
    Task<KiCadVersion?> AskKiCadVersionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// What the release tagged <paramref name="tag"/> declares, or <see langword="null"/> when it
    /// declares nothing - which is the case for every release cut before this feature.
    /// </summary>
    Task<KiCadCompatibilityManifest?> GetReleaseManifestAsync(string tag, CancellationToken cancellationToken);

    /// <summary>
    /// Logs where <paramref name="kicad"/> falls against <see cref="Shipped"/>, which is the
    /// application's own answer to "was I started by a KiCad I support?".
    /// </summary>
    void ReportRunningKiCad(KiCadVersion? kicad);
}
