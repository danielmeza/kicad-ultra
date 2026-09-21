namespace UltraLibrarianImporter.UI.Services.Interfaces;

/// <summary>
/// A per-user store for secrets such as provider API tokens, backed by the operating system's
/// credential store so that the values never have to be written to <c>config.json</c>.
/// </summary>
/// <remarks>
/// Implementations live in <c>Services/Secrets</c>, one per platform, and
/// <see cref="Secrets.PlatformSecretStore.Create"/> picks the one for the running OS. Every
/// failure to reach the store - no store on this system, a keyring that stayed locked, a native
/// library that is not installed - surfaces as a <see cref="Secrets.SecretStoreException"/>, so a
/// caller needs exactly one <c>catch</c> to fall back. Messages never contain a secret value.
/// </remarks>
public interface ISecretStore
{
    /// <summary>
    /// Where the secrets end up, phrased for a sentence shown to the user, e.g.
    /// "Windows Credential Manager".
    /// </summary>
    string DisplayName { get; }

    /// <summary>Returns the secret stored under <paramref name="key"/>, or null if there is none.</summary>
    /// <exception cref="Secrets.SecretStoreException">The store could not be read.</exception>
    string? Get(string key);

    /// <summary>Creates or replaces the secret stored under <paramref name="key"/>.</summary>
    /// <exception cref="Secrets.SecretStoreException">The store could not be written.</exception>
    void Set(string key, string value);

    /// <summary>Removes the secret stored under <paramref name="key"/>; a missing secret is not an error.</summary>
    /// <exception cref="Secrets.SecretStoreException">The store could not be written.</exception>
    void Delete(string key);
}
