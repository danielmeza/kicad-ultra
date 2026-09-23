using System;
using System.Net;

namespace KiCadUltra.Services.Interfaces;

/// <summary>
/// Thrown by <see cref="IComponentProvider.SearchPartsAsync"/> when the provider's service turned the
/// request down as too frequent (#110). It is a failure, never "no results", so it is not cached. The
/// aggregator stops asking that provider for a while, and reports it in every search it is left out of.
/// </summary>
public sealed class ProviderRateLimitedException : Exception
{
    /// <param name="message">What to tell the user, such as "JLCPCB is rate-limiting; try again shortly".
    /// Shown as it is, so it names the service and never carries a credential.</param>
    /// <param name="statusCode">The HTTP status the service answered with, for the log.</param>
    /// <param name="retryAfter">How long the service asked to be left alone, when it said.</param>
    public ProviderRateLimitedException(string message, HttpStatusCode statusCode, TimeSpan? retryAfter)
        : base(message)
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public HttpStatusCode StatusCode { get; }

    public TimeSpan? RetryAfter { get; }
}
