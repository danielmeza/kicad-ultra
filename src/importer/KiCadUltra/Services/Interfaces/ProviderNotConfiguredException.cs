using System;

namespace KiCadUltra.Services.Interfaces;

/// <summary>
/// Thrown by <see cref="IComponentProvider.SearchPartsAsync"/> when the provider cannot search at
/// all until the user configures it (for example, no API token). It is not an answer, so it is not
/// cached: once the token is entered, the very next search asks the provider. The aggregator logs it
/// at debug level only, because for most users it is the normal state of an optional provider.
/// </summary>
public sealed class ProviderNotConfiguredException : Exception
{
    public ProviderNotConfiguredException(string message)
        : base(message)
    {
    }
}
