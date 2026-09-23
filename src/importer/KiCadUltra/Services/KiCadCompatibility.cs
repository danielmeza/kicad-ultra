using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using KiCadSharp;

namespace KiCadUltra.Services;

/// <summary>
/// The KiCad versions one build of this application declares support for (#138).
/// </summary>
/// <remarks>
/// <para>
/// There is one declaration, <c>kicad-compatibility.json</c> at the root of the repository, and
/// everything else reads it: <c>release.yml</c> turns it into the Plugin and Content Manager's
/// <c>kicad_version</c> / <c>kicad_version_max</c> and publishes it as a release asset,
/// <c>plugin/importer_launcher.py</c> reads the published copy before spending a ~200 MB download,
/// and this class reads the copy that ships beside the executable and the copy a candidate release
/// publishes. Three hand-maintained copies of the same numbers would drift; one file cannot.
/// </para>
/// <para>
/// A missing patch number is read the way KiCad's own Plugin and Content Manager reads it
/// (<c>PLUGIN_CONTENT_MANAGER::PreparePackage</c>): as 0 in the minimum and as 999 in the two upper
/// bounds. That is what makes <c>tested_up_to: "10.99"</c> cover a KiCad that reports itself as
/// 10.99.0 rather than treating it as newer than tested.
/// </para>
/// </remarks>
public sealed class KiCadCompatibilityManifest
{
    /// <summary>The file's name, in the repository, beside the executable and in a release.</summary>
    public const string FileName = "kicad-compatibility.json";

    /// <summary>
    /// The only <c>manifest_version</c> this build understands. A file that declares a newer one is
    /// refused rather than guessed at, and a refused manifest is treated as no manifest - which
    /// permits the update rather than blocking it, for the reason given on
    /// <see cref="KiCadUpdateGate"/>.
    /// </summary>
    public const int KnownManifestVersion = 1;

    private KiCadCompatibilityManifest(Version minimum, Version testedUpTo, Version? maximum)
    {
        Minimum = minimum;
        TestedUpTo = testedUpTo;
        Maximum = maximum;
    }

    /// <summary>The oldest KiCad this build is declared to work with. Older is a refusal.</summary>
    public Version Minimum { get; }

    /// <summary>The newest KiCad this build was exercised against. Newer is a warning.</summary>
    public Version TestedUpTo { get; }

    /// <summary>
    /// The newest KiCad this build may be used with at all, or <see langword="null"/> when no version
    /// is known to break it. A maximum is a refusal; <see cref="TestedUpTo"/> is not.
    /// </summary>
    public Version? Maximum { get; }

    /// <summary>
    /// Reads a manifest, or throws <see cref="FormatException"/> saying what is wrong with it.
    /// </summary>
    /// <remarks>
    /// Hand-parsed from a <see cref="JsonDocument"/> rather than deserialised into a DTO: the file
    /// carries prose fields for the human who opens it in a release's assets, the shape has to be
    /// validated anyway (three version strings and their ordering), and a bad field has to produce a
    /// sentence rather than a <see cref="JsonException"/> about a property name.
    /// </remarks>
    public static KiCadCompatibilityManifest Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"{FileName} is not valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"{FileName} does not hold a JSON object.");
            }

            if (root.TryGetProperty("manifest_version", out JsonElement manifestVersion))
            {
                if (manifestVersion.ValueKind != JsonValueKind.Number || !manifestVersion.TryGetInt32(out var declared))
                {
                    throw new FormatException($"{FileName} has a manifest_version that is not a number.");
                }

                if (declared > KnownManifestVersion)
                {
                    throw new FormatException(
                        $"{FileName} declares manifest_version {declared}, and this build understands {KnownManifestVersion}.");
                }
            }

            if (!root.TryGetProperty("kicad", out JsonElement kicad) || kicad.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"{FileName} has no \"kicad\" object.");
            }

            Version minimum = ReadVersion(kicad, "minimum", required: true)!;
            Version testedUpTo = ReadVersion(kicad, "tested_up_to", required: true)!;
            Version? maximum = ReadVersion(kicad, "maximum", required: false);
            EnsureOrdered(minimum, testedUpTo, maximum);

            return new KiCadCompatibilityManifest(minimum, testedUpTo, maximum);
        }
    }

    /// <summary>Rejects a declaration whose upper bounds are below its lower one.</summary>
    private static void EnsureOrdered(Version minimum, Version testedUpTo, Version? maximum)
    {
        if (Bounded(testedUpTo, 999) < Bounded(minimum, 0))
        {
            throw new FormatException($"{FileName} declares tested_up_to {testedUpTo}, which is older than minimum {minimum}.");
        }

        if (maximum is not null && Bounded(maximum, 999) < Bounded(minimum, 0))
        {
            throw new FormatException($"{FileName} declares maximum {maximum}, which is older than minimum {minimum}.");
        }
    }

    /// <summary>
    /// The manifest that shipped with this build, or <see langword="null"/> when there is none to
    /// read. <paramref name="error"/> then says why, for the caller to log.
    /// </summary>
    /// <remarks>
    /// <c>KICAD_ULTRA_COMPATIBILITY_MANIFEST</c> names a file to read instead. It exists so that the
    /// out-of-range paths can be exercised against a real KiCad, which is otherwise impossible: no
    /// KiCad that exists today falls outside what this build declares. It is the same kind of
    /// override as the launcher's <c>KICAD_ULTRA_HOME</c> and <c>KICAD_ULTRA_API</c>.
    /// </remarks>
    public static KiCadCompatibilityManifest? TryLoadShipped(out string? error)
    {
        var overridden = Environment.GetEnvironmentVariable("KICAD_ULTRA_COMPATIBILITY_MANIFEST");
        var path = string.IsNullOrEmpty(overridden)
            ? Path.Combine(AppContext.BaseDirectory, FileName)
            : overridden;

        try
        {
            KiCadCompatibilityManifest manifest = Parse(File.ReadAllText(path));
            error = null;
            return manifest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException or NotSupportedException)
        {
            // Not a failure worth stopping for anywhere: an application that cannot read its own
            // manifest still imports parts, and every caller treats "not declared" as "cannot judge"
            // rather than as a refusal.
            error = $"{path} could not be read: {ex.Message}";
            return null;
        }
    }

    /// <summary>Where the running KiCad falls against this declaration.</summary>
    public KiCadSupport Judge(KiCadVersion kicad) => Judge(ToVersion(kicad));

    /// <summary>A KiCad version as a <see cref="Version"/>, which is what the comparisons use.</summary>
    public static Version ToVersion(KiCadVersion kicad) => new((int)kicad.Major, (int)kicad.Minor, (int)kicad.Patch);

    /// <summary>Where <paramref name="kicad"/> falls against this declaration.</summary>
    public KiCadSupport Judge(Version kicad)
    {
        Version running = Bounded(kicad, 0);

        return running < Bounded(Minimum, 0) ? KiCadSupport.BelowMinimum
            : Maximum is { } maximum && running > Bounded(maximum, 999) ? KiCadSupport.AboveMaximum
            : running > Bounded(TestedUpTo, 999) ? KiCadSupport.NewerThanTested
            : KiCadSupport.Supported;
    }

    /// <summary>One line naming the range, for a log, the About window or a console report.</summary>
    public string Describe() => Maximum is { } maximum
        ? string.Create(CultureInfo.InvariantCulture, $"KiCad {Minimum} to {maximum}, tested up to {TestedUpTo}")
        : string.Create(CultureInfo.InvariantCulture, $"KiCad {Minimum} and later, tested up to {TestedUpTo}");

    private static Version? ReadVersion(JsonElement kicad, string name, bool required)
    {
        if (!kicad.TryGetProperty(name, out JsonElement element) || element.ValueKind == JsonValueKind.Null)
        {
            return required
                ? throw new FormatException($"{FileName} has no kicad.{name}.")
                : null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"{FileName} has a kicad.{name} that is not a string.");
        }

        var text = element.GetString();

        // major.minor at least, which is what Version.TryParse already insists on: "10" alone would
        // have to mean 10.0 as a minimum and 10.999 as an upper bound, and a declaration that says
        // one thing in two places is not worth accepting.
        return Version.TryParse(text, out Version? version)
            ? version
            : throw new FormatException($"{FileName} has a kicad.{name} of \"{text}\", which is not a major.minor[.patch] version.");
    }

    /// <summary>
    /// <paramref name="version"/> with every component it does not carry filled in with
    /// <paramref name="missing"/>, so that two versions of different lengths compare the way KiCad
    /// compares them: 0 for a lower bound, 999 for an upper one.
    /// </summary>
    private static Version Bounded(Version version, int missing) => new(
        version.Major,
        version.Minor,
        version.Build < 0 ? missing : version.Build,
        version.Revision < 0 ? missing : version.Revision);
}

/// <summary>Where a KiCad version falls against a <see cref="KiCadCompatibilityManifest"/>.</summary>
public enum KiCadSupport
{
    /// <summary>Inside the declared range.</summary>
    Supported,

    /// <summary>Older than the declared minimum. This build is not meant to run under it.</summary>
    BelowMinimum,

    /// <summary>
    /// Newer than the newest KiCad this build was exercised against, and no maximum is declared.
    /// Worth saying, never worth refusing: "tested up to" is not "known broken".
    /// </summary>
    NewerThanTested,

    /// <summary>Newer than a declared maximum, which is a version this build is known not to work with.</summary>
    AboveMaximum,
}
