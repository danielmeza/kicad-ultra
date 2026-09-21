using System;
using System.Runtime.InteropServices;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services.Secrets;

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
    public const string ServiceName = "UltraLibrarianImporter";

    /// <summary>
    /// Returns the store for this OS. Nothing is loaded or contacted here: a store that turns out
    /// not to work reports it from its first operation, as a <see cref="SecretStoreException"/>.
    /// </summary>
    public static ISecretStore Create() =>
        OperatingSystem.IsWindows() ? new WindowsCredentialStore(ServiceName)
        : OperatingSystem.IsMacOS() ? new MacOSKeychainStore(ServiceName)
        : OperatingSystem.IsLinux() ? new LibSecretStore(ServiceName)
        : new UnavailableSecretStore($"no credential store is supported on {RuntimeInformation.OSDescription}");
}
