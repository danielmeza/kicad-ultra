using KiCadUltra.Services.Interfaces;

namespace KiCadUltra.Services.Secrets;

/// <summary>
/// The store used where no OS credential store is supported. Every operation fails with the
/// reason given, which sends <see cref="ConfigService"/> down its session-only path.
/// </summary>
public sealed class UnavailableSecretStore : ISecretStore
{
    private readonly string _reason;

    public UnavailableSecretStore(string reason)
    {
        _reason = reason;
    }

    public string DisplayName => "no credential store";

    public string? Get(string key) => throw new SecretStoreException(_reason);

    public void Set(string key, string value) => throw new SecretStoreException(_reason);

    public void Delete(string key) => throw new SecretStoreException(_reason);
}
