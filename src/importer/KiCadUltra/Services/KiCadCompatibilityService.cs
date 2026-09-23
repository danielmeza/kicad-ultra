using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KiCadSharp;
using KiCadUltra.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace KiCadUltra.Services;

/// <summary>
/// Reads the compatibility declarations (#138): the one this build ships, and the one a candidate
/// release publishes.
/// </summary>
/// <remarks>
/// <para>
/// The judging itself is in <see cref="KiCadCompatibilityManifest"/> and
/// <see cref="KiCadUpdateGate"/>, which are pure. What is here is everything that touches the
/// outside world - the file beside the executable, the running KiCad, and one small asset fetched
/// over HTTP - so that the rules can be exercised without any of it.
/// </para>
/// <para>
/// A singleton. The shipped manifest is read once, and so is the line that says it could not be.
/// </para>
/// </remarks>
internal sealed class KiCadCompatibilityService : IKiCadCompatibility
{
    /// <summary>
    /// As long as the import engine and the About window give KiCad to answer, and for the same
    /// reason: the question is asked with it as a token, not waited on with it.
    /// </summary>
    private static readonly TimeSpan KiCadQueryTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// One client for the handful of small requests this makes over a session. The timeout is its
    /// own rather than the default 100 seconds: a release's manifest is a few hundred bytes, and
    /// nothing downstream is waiting on it.
    /// </summary>
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "kicad-ultra/1.0" } },
    };

    private readonly ILogger<KiCadCompatibilityService> _logger;
    private readonly KiCad _kicad;
    private readonly HttpClient _httpClient;

    public KiCadCompatibilityService(ILogger<KiCadCompatibilityService> logger, KiCad kicad)
        : this(logger, kicad, SharedHttpClient)
    {
    }

    // Takes the HttpClient so that a harness can answer the release's request itself, as
    // OctopartProvider does for Nexar.
    internal KiCadCompatibilityService(ILogger<KiCadCompatibilityService> logger, KiCad kicad, HttpClient httpClient)
    {
        _logger = logger;
        _kicad = kicad;
        _httpClient = httpClient;

        Shipped = KiCadCompatibilityManifest.TryLoadShipped(out var error);
        if (Shipped is null)
        {
            _logger.LogWarning(
                "This build does not say which KiCad versions it supports, so nothing will be checked against it. {Error}",
                error);
        }
    }

    public KiCadCompatibilityManifest? Shipped { get; }

    public string DescribeShipped() =>
        Shipped?.Describe() ?? "The KiCad versions this build supports are not declared.";

    public string? WarnAbout(KiCadVersion kicad) => Shipped is not { } manifest
        ? null
        : manifest.Judge(kicad) switch
        {
            KiCadSupport.BelowMinimum => string.Create(
                CultureInfo.InvariantCulture,
                $"KiCad {kicad} is older than the KiCad {manifest.Minimum} this build needs. Imports may fail."),

            KiCadSupport.AboveMaximum => string.Create(
                CultureInfo.InvariantCulture,
                $"KiCad {kicad} is newer than the KiCad {manifest.Maximum} this build supports. Imports may fail."),

            KiCadSupport.NewerThanTested => string.Create(
                CultureInfo.InvariantCulture,
                $"KiCad {kicad} is newer than the KiCad {manifest.TestedUpTo} this build was tested against. It is not known to be a problem."),

            KiCadSupport.Supported or _ => null,
        };

    public void ReportRunningKiCad(KiCadVersion? kicad)
    {
        if (kicad is null)
        {
            _logger.LogInformation(
                "No KiCad answered, so nothing was checked against what this build supports ({Supported}).",
                DescribeShipped());
            return;
        }

        if (WarnAbout(kicad) is { } warning)
        {
            _logger.LogWarning("{Warning} This build supports {Supported}.", warning, DescribeShipped());
            return;
        }

        _logger.LogInformation("KiCad {Version} is running, and this build supports {Supported}.", kicad, DescribeShipped());
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The same shape as <c>KiCadImportEngine.AskKiCadVersionAsync</c> and the About window's query:
    /// the deadline is a token passed into the call rather than a wait around it, because since
    /// KiCadSharp 0.4.0 the token ends the dial or the wait itself, where a wait would only stop
    /// watching and leave the call running against a KiCad that never answers.
    /// </remarks>
    public async Task<KiCadVersion?> AskKiCadVersionAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(KiCadQueryTimeout);

        try
        {
            return await _kicad.GetVersion(deadline.Token).ConfigureAwait(false);
        }
        catch (KiCadIpcException ex)
        {
            // Every IPC failure is one of these since KiCadSharp 0.4.0: KiCadConnectionException when
            // there is no socket to dial, nothing answers at it, or its native nng library cannot be
            // loaded, and ApiException, with KiCad's status, when KiCad answers with an error.
            _logger.LogInformation(ex, "KiCad could not be asked for its version.");
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The deadline, not the caller. The caller's cancellation is the application closing and
            // is left to propagate.
            _logger.LogInformation("KiCad did not report its version within {Timeout}.", KiCadQueryTimeout);
            return null;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The address is built from the tag rather than looked up through the releases API, because the
    /// release workflow builds the same address for the Plugin and Content Manager entry it
    /// publishes, and because an extra unauthenticated API call is one more thing to spend against
    /// GitHub's 60-per-hour limit. Anything other than a 200 - a release from before this feature,
    /// an asset that failed to publish, a network that is down - is <see langword="null"/>, which
    /// <see cref="KiCadUpdateGate"/> reads as "declares nothing" and permits.
    /// </remarks>
    public async Task<KiCadCompatibilityManifest?> GetReleaseManifestAsync(string tag, CancellationToken cancellationToken)
    {
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{AppUpdateService.ReleasesRepositoryUrl}/releases/download/{tag}/{KiCadCompatibilityManifest.FileName}");

        try
        {
            using HttpResponseMessage response = await _httpClient
                .GetAsync(url, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Release {Tag} publishes no {File}.", tag, KiCadCompatibilityManifest.FileName);
                return null;
            }

            _ = response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return KiCadCompatibilityManifest.Parse(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or FormatException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // HttpClient reports its own timeout as a TaskCanceledException with nothing cancelled,
            // which is the third case here. The caller's cancellation is the application closing and
            // is left to propagate.
            _logger.LogWarning(ex, "Could not read {File} for release {Tag}.", KiCadCompatibilityManifest.FileName, tag);
            return null;
        }
    }
}
