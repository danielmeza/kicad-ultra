using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services;

/// <summary>
/// Reuses a provider's answer to a query for <see cref="ProviderSearchOptions.CacheTimeToLive"/>.
/// Keyed by (provider id, normalised query), compared case-insensitively.
/// </summary>
/// <remarks>
/// Only answers go in: <see cref="PartAggregatorService"/> stores a provider's list after
/// <see cref="IComponentProvider.SearchPartsAsync"/> returned it, and never after it threw. An empty
/// list is cached because it is the provider saying "no matches"; a failure is not, so the next
/// search asks again.
/// </remarks>
public sealed class ProviderResponseCache
{
    private readonly Dictionary<CacheKey, Entry> _entries = [];
    private readonly Lock _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeToLive;
    private readonly int _capacity;

    public ProviderResponseCache(ProviderSearchOptions options, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.CacheCapacity, 1);
        _timeProvider = timeProvider;
        _timeToLive = options.CacheTimeToLive;
        _capacity = options.CacheCapacity;
    }

    public bool TryGet(string providerId, string normalizedQuery, [NotNullWhen(true)] out IReadOnlyList<PartSearchResult>? results)
    {
        var key = new CacheKey(providerId, normalizedQuery);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out Entry entry))
            {
                if (entry.ExpiresAt > now)
                {
                    results = entry.Results;
                    return true;
                }

                _ = _entries.Remove(key);
            }
        }

        results = null;
        return false;
    }

    public void Set(string providerId, string normalizedQuery, IReadOnlyList<PartSearchResult> results)
    {
        var key = new CacheKey(providerId, normalizedQuery);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        // A private copy, so a provider that reuses its list cannot change what is served later.
        var entry = new Entry(results.ToArray(), now, now + _timeToLive);
        lock (_gate)
        {
            if (!_entries.ContainsKey(key) && _entries.Count >= _capacity)
            {
                MakeRoom(now);
            }

            _entries[key] = entry;
        }
    }

    /// <summary>
    /// Forgets every answer. Settings call it on Save, because an answer can depend on them: with
    /// JLCPCB API credentials entered, the EasyEDA / LCSC provider answers an LCSC number from the
    /// official API instead of the unofficial endpoint (#51), and an answer cached before the change
    /// would otherwise keep being served, under the old source's label, until it expired.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    // Called under _gate. Drops everything expired; if that frees nothing, drops the oldest entry.
    private void MakeRoom(DateTimeOffset now)
    {
        var expired = _entries.Where(e => e.Value.ExpiresAt <= now).Select(e => e.Key).ToList();
        foreach (CacheKey key in expired)
        {
            _ = _entries.Remove(key);
        }

        if (_entries.Count >= _capacity)
        {
            CacheKey oldest = _entries.MinBy(e => e.Value.StoredAt).Key;
            _ = _entries.Remove(oldest);
        }
    }

    private readonly record struct CacheKey
    {
        public CacheKey(string providerId, string normalizedQuery)
        {
            ProviderId = providerId.ToUpperInvariant();
            Query = normalizedQuery.ToUpperInvariant();
        }

        public string ProviderId { get; }
        public string Query { get; }
    }

    private readonly record struct Entry(IReadOnlyList<PartSearchResult> Results, DateTimeOffset StoredAt, DateTimeOffset ExpiresAt);
}
