using System;

namespace KiCadUltra.Services.Secrets;

/// <summary>
/// The OS credential store could not be used. The message says why, in terms a user can act on,
/// and never contains a secret value.
/// </summary>
public sealed class SecretStoreException : Exception
{
    public SecretStoreException(string message)
        : base(message)
    {
    }

    public SecretStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
