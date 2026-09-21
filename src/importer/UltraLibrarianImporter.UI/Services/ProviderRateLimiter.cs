using System;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace UltraLibrarianImporter.UI.Services;

/// <summary>
/// One token bucket per provider id (#55): a burst of <see cref="ProviderSearchOptions.BurstLimit"/>
/// requests, then one per <see cref="ProviderSearchOptions.ReplenishmentPeriod"/>. Requests over the
/// limit wait in a queue of <see cref="ProviderSearchOptions.QueueLimit"/>; a wait is cancelled with
/// the search that is waiting, so a superseded search gives its place up instead of spending a token.
/// </summary>
public sealed class ProviderRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public ProviderRateLimiter(ProviderSearchOptions options)
    {
        var bucket = new TokenBucketRateLimiterOptions
        {
            TokenLimit = options.BurstLimit,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = options.ReplenishmentPeriod,
            QueueLimit = options.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        };

        _limiter = PartitionedRateLimiter.Create<string, string>(
            providerId => RateLimitPartition.GetTokenBucketLimiter(providerId, _ => bucket),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Waits for permission to send one request to <paramref name="providerId"/>. The returned lease
    /// is not acquired when the queue is already full; the caller must check
    /// <see cref="RateLimitLease.IsAcquired"/>.
    /// </summary>
    public ValueTask<RateLimitLease> AcquireAsync(string providerId, CancellationToken cancellationToken) =>
        _limiter.AcquireAsync(providerId, 1, cancellationToken);

    public void Dispose() => _limiter.Dispose();
}
