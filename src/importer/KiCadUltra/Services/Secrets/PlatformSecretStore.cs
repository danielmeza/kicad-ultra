using System;
using System.Runtime.InteropServices;

using KiCadUltra.Services.Interfaces;

namespace KiCadUltra.Services.Secrets;

/// <summary>
/// Chooses the <see cref="ISecretStore"/> for the operating system the app is running on.
/// </summary>
public static class PlatformSecretStore
{
    /// <summary>
    /// The name every secret is filed under in the OS store - the Windows credential target
    /// prefix, the macOS Keychain service and the Secret Service <c>service</c> attribute. It
    /// matches the app-data folder that holds <c>config.json</c>, so the two are easy to find
    /// together.
    /// </summary>
    public const string ServiceName = "KiCadUltra";

    /// <summary>
    /// The service name used before #132. Everything an installation from before the rename stored
    /// is filed under it, which is why <see cref="SecretStoreMigration"/> exists: a store is asked
    /// for a secret by service name, so a rename hides every token the user already gave.
    /// </summary>
    public const string LegacyServiceName = "UltraLibrarianImporter";

    /// <summary>
    /// Returns the store for this OS. Nothing is loaded or contacted here: a store that turns out
    /// not to work reports it from its first operation, as a <see cref="SecretStoreException"/>.
    /// </summary>
    public static ISecretStore Create() => CreateFor(ServiceName);

    /// <summary>
    /// Returns the store for this OS under the service name used before #132, to be read from and
    /// never written to. <see cref="SecretStoreMigration"/> is its only caller.
    /// </summary>
    public static ISecretStore CreateLegacy() => CreateFor(LegacyServiceName);

    private static ISecretStore CreateFor(string serviceName) =>
        OperatingSystem.IsWindows() ? new WindowsCredentialStore(serviceName)
        : OperatingSystem.IsMacOS() ? new MacOSKeychainStore(serviceName)
        : OperatingSystem.IsLinux() ? new LibSecretStore(serviceName)
        : new UnavailableSecretStore($"no credential store is supported on {RuntimeInformation.OSDescription}");
}
