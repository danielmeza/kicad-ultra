using System;
using System.Collections.Generic;

using KiCadUltra.Services.Providers;

namespace KiCadUltra.Services;

/// <summary>
/// Limits applied to every direct-API provider search (#55). The values are shared, but each
/// provider gets its own cache entries and its own token bucket, so a burst against one API never
/// delays or evicts another.
/// </summary>
/// <remarks>
/// No provider publishes a per-second limit today. JLCPCB's API terms forbid exceeding "specified"
/// call frequency limits, but its public documentation specifies none (#51), its website endpoint
/// documents nothing (#52), and Nexar's quota is monthly, which the cache protects far better than
/// any spacing could. The defaults are therefore modest rather than derived from a contract. A
/// provider whose source is known to need less gets its own bucket in
/// <see cref="ProviderRateLimits"/> instead of lowering these for everyone.
/// </remarks>
public sealed class ProviderSearchOptions
{
    /// <summary>
    /// How long a provider's answer to a query is reused. Stock and price move, so this is short.
    /// </summary>
    public TimeSpan CacheTimeToLive { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Upper bound on cached answers across all providers; the oldest is dropped beyond it.
    /// </summary>
    public int CacheCapacity { get; init; } = 256;

    /// <summary>
    /// Requests a provider may receive back to back before the limiter starts spacing them out.
    /// </summary>
    public int BurstLimit { get; init; } = 3;

    /// <summary>
    /// Once the burst is spent, one more request is allowed per period.
    /// </summary>
    public TimeSpan ReplenishmentPeriod { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Requests allowed to wait for a permit. A request beyond this is refused, and that provider is
    /// left out of that one search rather than answering late.
    /// </summary>
    public int QueueLimit { get; init; } = 4;

    /// <summary>
    /// Buckets for the providers whose source needs a slower one than the defaults above, by provider
    /// id.
    /// </summary>
    public IReadOnlyDictionary<string, ProviderRateLimit> ProviderRateLimits { get; init; } =
        new Dictionary<string, ProviderRateLimit>(StringComparer.OrdinalIgnoreCase)
        {
            // JLCPCB's website endpoint (#52, #110) documents no limit, and it sits behind Akamai. It once
            // answered 403 to a request sent 0.5 s after the previous one (#107), yet when measured, six
            // requests in 10 s, three of them within 1.4 s, were all answered. Its threshold is therefore
            // not a simple rate per second, and finding it would take the kind of volume this unofficial
            // endpoint must never be sent. So this bucket stays below what was measured to work: one
            // request at once, then one every 3 s, which allows at most two within any second and five
            // within any 10 s. It also covers the provider's other JLCPCB route, the official API, which
            // publishes no limit either (#51). A refusal that still happens is reported and backed off
            // from (RateLimitBackoff).
            [EasyEdaProvider.ProviderId] = new(BurstLimit: 1, ReplenishmentPeriod: TimeSpan.FromSeconds(3), QueueLimit: 4),
        };

    /// <summary>
    /// How long a provider is left alone after its service turns a request down as too frequent
    /// (#110). Each further refusal before it answers again doubles it, up to
    /// <see cref="MaxRateLimitBackoff"/>. A longer Retry-After from the service is honoured up to the
    /// same bound.
    /// </summary>
    public TimeSpan RateLimitBackoff { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The longest a provider is left alone after a refusal, so that a service which keeps refusing is
    /// still asked again every few minutes rather than never.
    /// </summary>
    public TimeSpan MaxRateLimitBackoff { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>The bucket for <paramref name="providerId"/>: its own, or the defaults.</summary>
    public ProviderRateLimit GetRateLimit(string providerId) =>
        ProviderRateLimits.TryGetValue(providerId, out ProviderRateLimit? limit)
            ? limit
            : new ProviderRateLimit(BurstLimit, ReplenishmentPeriod, QueueLimit);
}

/// <summary>
/// One provider's token bucket: <paramref name="BurstLimit"/> requests back to back, then one per
/// <paramref name="ReplenishmentPeriod"/>, with up to <paramref name="QueueLimit"/> waiting for a
/// permit.
/// </summary>
public sealed record ProviderRateLimit(int BurstLimit, TimeSpan ReplenishmentPeriod, int QueueLimit);
