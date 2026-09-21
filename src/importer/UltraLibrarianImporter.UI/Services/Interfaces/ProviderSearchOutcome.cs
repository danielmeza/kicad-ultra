using System;
using System.Collections.Generic;

namespace UltraLibrarianImporter.UI.Services.Interfaces;

/// <summary>
/// What one provider contributed to a search, as
/// <see cref="IPartAggregatorService.StreamAllProvidersAsync"/> yields it: its parts, or why it has
/// none (#110).
/// </summary>
public abstract record ProviderSearchOutcome;

/// <summary>A provider's answer. Only an answer with at least one part is yielded.</summary>
public sealed record ProviderResults(IReadOnlyList<PartSearchResult> Parts) : ProviderSearchOutcome;

/// <summary>
/// A provider left out of a search because its service is turning requests down as too frequent
/// (#110): it refused this search's request, or refused one moments ago and is not asked again before
/// <paramref name="RetryAt"/>. A failure, never "no results": its parts are missing, not empty.
/// </summary>
/// <param name="ProviderId">The provider left out.</param>
/// <param name="ProviderName">Its display name.</param>
/// <param name="Reason">What to tell the user, from the provider's <see cref="ProviderRateLimitedException"/>.</param>
/// <param name="RetryAt">When the provider will next be asked.</param>
public sealed record ProviderRateLimited(string ProviderId, string ProviderName, string Reason, DateTimeOffset RetryAt)
    : ProviderSearchOutcome;

/// <summary>
/// A whole search, for callers that need it at once: every part found, and every provider left out
/// because it was rate-limiting.
/// </summary>
public sealed record AggregatedSearchResult(IReadOnlyList<PartSearchResult> Parts, IReadOnlyList<ProviderRateLimited> RateLimited);
