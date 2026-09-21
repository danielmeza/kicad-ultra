using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace UltraLibrarianImporter.UI.Services;

/// <summary>
/// One token bucket per provider id (#55), shaped by <see cref="ProviderSearchOptions.GetRateLimit"/>:
/// a burst of requests, then one per replenishment period. Requests over the limit wait in a bounded
/// queue; a wait is cancelled with the search that is waiting, so a superseded search gives its place
/// up instead of spending a token.
/// </summary>
/// <remarks>
/// It also backs off from a provider whose service turned a request down as too frequent (#110):
/// <see cref="BackOff"/> records the refusal, and <see cref="GetBackoff"/> says not to ask again until
/// it has passed. Each refusal before the provider answers again doubles the wait, from
/// <see cref="ProviderSearchOptions.RateLimitBackoff"/> up to
/// <see cref="ProviderSearchOptions.MaxRateLimitBackoff"/>. This is a per-process state, like the
/// buckets: the GUI and a <c>--mcp</c> server each keep their own.
/// </remarks>
public sealed class ProviderRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;
    private readonly ProviderSearchOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, ProviderBackoff> _backoffs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public ProviderRateLimiter(ProviderSearchOptions options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
        _limiter = PartitionedRateLimiter.Create<string, string>(
            providerId => RateLimitPartition.GetTokenBucketLimiter(providerId, CreateBucket),
            StringComparer.OrdinalIgnoreCase);
    }

    private TokenBucketRateLimiterOptions CreateBucket(string providerId)
    {
        ProviderRateLimit limit = _options.GetRateLimit(providerId);
        return new TokenBucketRateLimiterOptions
        {
            TokenLimit = limit.BurstLimit,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = limit.ReplenishmentPeriod,
            QueueLimit = limit.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        };
    }

    /// <summary>
    /// Waits for permission to send one request to <paramref name="providerId"/>. The returned lease
    /// is not acquired when the queue is already full; the caller must check
    /// <see cref="RateLimitLease.IsAcquired"/>.
    /// </summary>
    public ValueTask<RateLimitLease> AcquireAsync(string providerId, CancellationToken cancellationToken) =>
        _limiter.AcquireAsync(providerId, 1, cancellationToken);

    /// <summary>
    /// The refusal <paramref name="providerId"/> is still being left alone after, or <c>null</c> when
    /// it may be asked.
    /// </summary>
    public ProviderBackoff? GetBackoff(string providerId)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            return _backoffs.TryGetValue(providerId, out ProviderBackoff? backoff) && backoff.RetryAt > now ? backoff : null;
        }
    }

    /// <summary>
    /// Records that <paramref name="providerId"/>'s service turned a request down as too frequent, and
    /// returns the back-off: how long it is left alone, and what to say meanwhile.
    /// </summary>
    /// <param name="providerId">The provider refused.</param>
    /// <param name="reason">What to tell the user while it is left alone.</param>
    /// <param name="retryAfter">How long the service asked to be left alone, when it said.</param>
    public ProviderBackoff BackOff(string providerId, string reason, TimeSpan? retryAfter)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            _ = _backoffs.TryGetValue(providerId, out ProviderBackoff? previous);
            if (previous is not null && previous.RetryAt > now)
            {
                // Another request that was already on its way when the first refusal came in. The
                // service has not been asked again since, so this is the same refusal, not a new one.
                return previous;
            }

            var refusals = (previous?.Refusals ?? 0) + 1;
            TimeSpan wait = _options.RateLimitBackoff * Math.Pow(2, Math.Min(refusals - 1, 16));
            if (retryAfter > wait)
            {
                wait = retryAfter.Value;
            }

            if (wait > _options.MaxRateLimitBackoff)
            {
                wait = _options.MaxRateLimitBackoff;
            }

            var backoff = new ProviderBackoff(reason, now + wait, refusals);
            _backoffs[providerId] = backoff;
            return backoff;
        }
    }

    /// <summary>
    /// Records that <paramref name="providerId"/> answered, so that a later refusal starts from the
    /// shortest back-off again.
    /// </summary>
    public void Answered(string providerId)
    {
        lock (_gate)
        {
            _ = _backoffs.Remove(providerId);
        }
    }

    public void Dispose() => _limiter.Dispose();
}

/// <summary>
/// A provider left alone after its service turned a request down as too frequent (#110).
/// </summary>
/// <param name="Reason">What to tell the user meanwhile.</param>
/// <param name="RetryAt">When the provider may be asked again.</param>
/// <param name="Refusals">Refusals since it last answered, this one included.</param>
public sealed record ProviderBackoff(string Reason, DateTimeOffset RetryAt, int Refusals);
