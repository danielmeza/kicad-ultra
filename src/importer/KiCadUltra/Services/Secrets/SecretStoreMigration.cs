using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using KiCadUltra.Services.Interfaces;

using Microsoft.Extensions.Logging;

namespace KiCadUltra.Services.Secrets;

/// <summary>
/// Copies the stored credentials from the service name this application used before #132
/// (<see cref="PlatformSecretStore.LegacyServiceName"/>) to the one it uses now
/// (<see cref="PlatformSecretStore.ServiceName"/>).
/// </summary>
/// <remarks>
/// <para>
/// The service name is what Windows Credential Manager, the macOS Keychain and the Secret Service
/// file a secret under. Renaming it without copying does not lose the tokens - they stay in the
/// keyring - but the application can no longer see them, and the user is asked for a token they
/// already gave. So the rename copies.
/// </para>
/// <para>
/// <b>The original is never deleted</b>, whether the copy worked or not. A keyring entry costs
/// nothing to leave behind, and a delete is the one step that cannot be taken back if the write it
/// followed turns out to be wrong. Each copy is read back and compared before it counts, so a store
/// that accepted a write it did not keep is reported rather than believed.
/// </para>
/// <para>
/// <b>No value is ever logged</b>, and none is returned. The log lines name keys
/// (<c>octopart-api-token</c>) and the two service names, which is what a support question needs.
/// </para>
/// <para>
/// A stamp file in the application-data folder ends it: once a pass has completed, the old service
/// is never read again. That is not only about the cost of six lookups per start. Without it, a
/// token the user clears in Settings - a delete against the new service - would be copied back from
/// the old one at the next start, and the setting would not stick.
/// </para>
/// </remarks>
public static class SecretStoreMigration
{
    /// <summary>
    /// The stamp file, in the application-data folder beside <c>config.json</c>. Its contents are
    /// for a human reading the folder; only its existence is read.
    /// </summary>
    public const string StampFileName = "credential-store-migrated.txt";

    /// <summary>
    /// Copies every key in <paramref name="keys"/> that <paramref name="current"/> does not already
    /// hold and <paramref name="legacy"/> does, and stamps
    /// <paramref name="applicationDataFolder"/> once a pass has completed.
    /// </summary>
    /// <param name="current">The store this version reads and writes.</param>
    /// <param name="legacy">The same platform store under the service name used before #132.</param>
    /// <param name="keys">Every key this application files a secret under.</param>
    /// <param name="applicationDataFolder">Where the stamp file lives; created if it does not exist.</param>
    /// <param name="logger">Where the outcome is reported. No secret value reaches it.</param>
    /// <returns>What happened, for the caller and for a harness that drives this with fake stores.</returns>
    public static SecretStoreMigrationResult Run(
        ISecretStore current,
        ISecretStore legacy,
        IReadOnlyList<string> keys,
        string applicationDataFolder,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(legacy);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(logger);

        var stampPath = Path.Combine(applicationDataFolder, StampFileName);
        if (File.Exists(stampPath))
        {
            return new SecretStoreMigrationResult(0, 0, 0, true, null);
        }

        var copied = 0;
        var alreadyPresent = 0;
        var notFound = 0;
        foreach (var key in keys)
        {
            try
            {
                if (!string.IsNullOrEmpty(current.Get(key)))
                {
                    // What this version wrote wins. Nothing is read from the old service for this
                    // key, and nothing is written.
                    alreadyPresent++;
                    continue;
                }

                var value = legacy.Get(key);
                if (string.IsNullOrEmpty(value))
                {
                    notFound++;
                    continue;
                }

                current.Set(key, value);
                if (!string.Equals(current.Get(key), value, StringComparison.Ordinal))
                {
                    // The store took the write and gave something else back. Leaving the original
                    // where it is means the user can still be reached by a later attempt.
                    logger.LogWarning(
                        "{SecretKey} was copied from the {LegacyService} credentials to {Service}, but reading it back did not return what was written; the original is left in place",
                        key, PlatformSecretStore.LegacyServiceName, PlatformSecretStore.ServiceName);
                    return new SecretStoreMigrationResult(copied, alreadyPresent, notFound, false, "a copied secret did not read back");
                }

                copied++;
                logger.LogInformation(
                    "Copied {SecretKey} from the {LegacyService} credentials to {Service} (#132); the original is left in place",
                    key, PlatformSecretStore.LegacyServiceName, PlatformSecretStore.ServiceName);
            }
            catch (SecretStoreException ex)
            {
                // Asked once. A keyring that is locked, or absent, answers the same for every key,
                // and asking six times is six unlock prompts. ConfigService reports the same
                // condition to the user; this is only why nothing moved.
                logger.LogInformation(
                    "The credential store could not be used ({Reason}), so the credentials stored under {LegacyService} were not copied; this is tried again at the next start",
                    ex.Message, PlatformSecretStore.LegacyServiceName);
                return new SecretStoreMigrationResult(copied, alreadyPresent, notFound, false, ex.Message);
            }
        }

        WriteStamp(stampPath, copied, logger);
        return new SecretStoreMigrationResult(copied, alreadyPresent, notFound, true, null);
    }

    private static void WriteStamp(string stampPath, int copied, ILogger logger)
    {
        try
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(stampPath)!);
            File.WriteAllText(stampPath, string.Create(CultureInfo.InvariantCulture,
                $"""
                 The credentials stored under the service name "{PlatformSecretStore.LegacyServiceName}" were
                 copied to "{PlatformSecretStore.ServiceName}" on {DateTimeOffset.UtcNow:u} ({copied} copied).
                 The originals were left where they were. Delete this file to run that copy again.

                 """));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without the stamp the copy runs again at the next start. It is idempotent, so the only
            // cost is the lookups - and a token cleared in Settings coming back once more.
            logger.LogWarning(ex, "{StampFile} could not be written; the credential-store migration will run again at the next start", stampPath);
        }
    }
}

/// <summary>
/// What one <see cref="SecretStoreMigration.Run"/> did. Counts and a reason, never a value.
/// </summary>
/// <param name="Copied">Secrets copied from the old service name to the new one.</param>
/// <param name="AlreadyPresent">Keys the new service name already held, which were left alone.</param>
/// <param name="NotFound">Keys neither service name held.</param>
/// <param name="Completed">
/// True when every key was dealt with, which is when the stamp file is written and the old service
/// name is never read again.
/// </param>
/// <param name="Failure">Why the pass stopped, or null when it did not.</param>
public sealed record SecretStoreMigrationResult(int Copied, int AlreadyPresent, int NotFound, bool Completed, string? Failure);
