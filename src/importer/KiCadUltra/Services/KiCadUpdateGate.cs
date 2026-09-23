using System;
using System.Globalization;

namespace KiCadUltra.Services;

/// <summary>
/// Whether a candidate release may be installed under the KiCad that is running (#138).
/// </summary>
/// <remarks>
/// <para>
/// A pure function of two inputs, deliberately: it is what <see cref="AppUpdateService"/> consults
/// before it downloads anything, and what <c>--kicad-compatibility</c> prints a table of, so the
/// rules a user is shown cannot drift from the rules that run.
/// </para>
/// <para>
/// <b>Three of the five answers permit the update, and each does so for its own reason.</b> The one
/// thing this gate must never become is a way for a plugin to stop updating itself for good:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>KiCad did not answer.</b> It is not running, the API server is off, or the importer was
/// started by hand - the last of which is how it is run during development, and is the normal case
/// for the diagnostic modes. "Which KiCad" has no answer yet in any of them, and the next start may
/// well be from a KiCad that is perfectly current. Refusing here would mean an importer started once
/// outside KiCad stops updating until somebody notices, which is a worse failure than the one this
/// gate exists to prevent - and the two checks either side of it still apply: the bootstrap checks
/// before the first download, and the application says so in its log and its About window when the
/// KiCad that launched it is out of range.
/// </description></item>
/// <item><description>
/// <b>The release declares nothing.</b> Every release cut before this feature is in that position,
/// and so is any later one whose asset failed to publish. An absent declaration is the state this
/// project was in until now, and its meaning is "unknown", not "incompatible".
/// </description></item>
/// <item><description>
/// <b>KiCad is newer than the release was tested against.</b> A maximum that is only a
/// "tested up to" is a statement about what was run, not about what works. Skipping on it would
/// strand every user on the day a new KiCad ships, which is exactly when a new release is most
/// likely to be the one that supports it.
/// </description></item>
/// </list>
/// <para>
/// The two that refuse are the ones where the release itself says it will not work: KiCad older
/// than its minimum, or newer than a maximum somebody declared on purpose.
/// </para>
/// </remarks>
internal static class KiCadUpdateGate
{
    /// <param name="releaseVersion">The candidate release, for the log line.</param>
    /// <param name="candidate">What that release declares, or <see langword="null"/> when it declares nothing.</param>
    /// <param name="runningKiCad">The KiCad that answered, or <see langword="null"/> when none did.</param>
    public static UpdateGateDecision Decide(string releaseVersion, KiCadCompatibilityManifest? candidate, Version? runningKiCad) =>
        candidate is null
            ? new UpdateGateDecision(true, UpdateGateOutcome.ReleaseDeclaresNothing, string.Create(
                CultureInfo.InvariantCulture,
                $"Release {releaseVersion} publishes no {KiCadCompatibilityManifest.FileName}, so there is nothing to check the running KiCad against. Downloading it."))
            : runningKiCad is null
                ? new UpdateGateDecision(true, UpdateGateOutcome.KiCadDidNotAnswer, string.Create(
                    CultureInfo.InvariantCulture,
                    $"KiCad did not say which version it is, so release {releaseVersion} ({candidate.Describe()}) cannot be checked against it. Downloading it rather than never updating again."))
                : Judge(releaseVersion, candidate, runningKiCad);

    private static UpdateGateDecision Judge(string releaseVersion, KiCadCompatibilityManifest candidate, Version runningKiCad) =>
        candidate.Judge(runningKiCad) switch
        {
            KiCadSupport.BelowMinimum => new UpdateGateDecision(false, UpdateGateOutcome.KiCadTooOld, string.Create(
                CultureInfo.InvariantCulture,
                $"Release {releaseVersion} needs KiCad {candidate.Minimum} or newer, and KiCad {runningKiCad} is running. Skipping it.")),

            KiCadSupport.AboveMaximum => new UpdateGateDecision(false, UpdateGateOutcome.KiCadTooNew, string.Create(
                CultureInfo.InvariantCulture,
                $"Release {releaseVersion} declares KiCad {candidate.Maximum} as its maximum, and KiCad {runningKiCad} is running. Skipping it.")),

            KiCadSupport.NewerThanTested => new UpdateGateDecision(true, UpdateGateOutcome.KiCadNewerThanTested, string.Create(
                CultureInfo.InvariantCulture,
                $"Release {releaseVersion} was tested up to KiCad {candidate.TestedUpTo}, and KiCad {runningKiCad} is running. Downloading it anyway; tested up to is not known broken.")),

            KiCadSupport.Supported or _ => new UpdateGateDecision(true, UpdateGateOutcome.Supported, string.Create(
                CultureInfo.InvariantCulture,
                $"Release {releaseVersion} supports {candidate.Describe()}, and KiCad {runningKiCad} is running. Downloading it.")),
        };
}

/// <summary>What <see cref="KiCadUpdateGate.Decide"/> concluded, and the sentence that says so.</summary>
/// <param name="Proceed">Whether the candidate release may be downloaded and staged.</param>
/// <param name="Outcome">Which of the five rules applied.</param>
/// <param name="Reason">One line naming both versions, for the log and for the console report.</param>
internal readonly record struct UpdateGateDecision(bool Proceed, UpdateGateOutcome Outcome, string Reason);

/// <summary>The five answers <see cref="KiCadUpdateGate.Decide"/> can give.</summary>
internal enum UpdateGateOutcome
{
    /// <summary>The running KiCad is inside the range the release declares.</summary>
    Supported,

    /// <summary>The release declares no range, so there is nothing to check.</summary>
    ReleaseDeclaresNothing,

    /// <summary>No KiCad answered, so there is nothing to check the release against.</summary>
    KiCadDidNotAnswer,

    /// <summary>KiCad is newer than the release was tested against, and no maximum is declared.</summary>
    KiCadNewerThanTested,

    /// <summary>KiCad is older than the release's minimum.</summary>
    KiCadTooOld,

    /// <summary>KiCad is newer than a maximum the release declares.</summary>
    KiCadTooNew,
}
