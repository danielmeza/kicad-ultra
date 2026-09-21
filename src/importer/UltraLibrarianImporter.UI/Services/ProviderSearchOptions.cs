using System;

namespace UltraLibrarianImporter.UI.Services;

/// <summary>
/// Limits applied to every direct-API provider search (#55). The values are shared, but each
/// provider gets its own cache entries and its own token bucket, so a burst against one API never
/// delays or evicts another.
/// </summary>
/// <remarks>
/// No provider publishes a per-second limit today. JLCPCB's API terms forbid exceeding "specified"
/// call frequency limits, but its public documentation specifies none (#51), its website endpoint
/// documents nothing (#52), and Nexar's quota is monthly, which the cache protects far better than
/// any spacing could. The defaults are therefore modest rather than derived from a contract. When a
/// provider publishes a limit, give it its own bucket instead of lowering these for everyone.
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
}
