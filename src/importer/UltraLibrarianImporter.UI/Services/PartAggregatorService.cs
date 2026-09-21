using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services;

public class PartAggregatorService : IPartAggregatorService
{
    private readonly IComponentProviderRegistry _registry;
    private readonly ProviderResponseCache _cache;
    private readonly ProviderRateLimiter _rateLimiter;
    private readonly ILogger<PartAggregatorService> _logger;

    public PartAggregatorService(
        IComponentProviderRegistry registry,
        ProviderResponseCache cache,
        ProviderRateLimiter rateLimiter,
        ILogger<PartAggregatorService> logger)
    {
        _registry = registry;
        _cache = cache;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async IAsyncEnumerable<PartSearchResult> StreamAllProvidersAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var normalized = SearchQueryNormalizer.Normalize(query);
        if (normalized.Length == 0)
        {
            yield break;
        }

        var apiProviders = _registry.Providers.Where(p => p.SupportsDirectApi).ToList();
        if (apiProviders.Count == 0)
        {
            _logger.LogWarning("No providers support direct API search.");
            yield break;
        }

        _logger.LogInformation("Querying {Count} component provider(s) for '{Query}'", apiProviders.Count, normalized);

        // Each provider writes its whole answer as one batch the moment it has it, so batches arrive in
        // completion order and a slow provider holds up nobody but itself.
        var answers = Channel.CreateUnbounded<IReadOnlyList<PartSearchResult>>(
            new UnboundedChannelOptions { SingleReader = true });

        // Linked, so that a consumer who stops enumerating early also stops the provider calls it will
        // never read - not only a consumer who cancels its token.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task providers = QueryProvidersAsync(apiProviders, normalized, answers.Writer, stop.Token);

        var count = 0;
        try
        {
            await foreach (IReadOnlyList<PartSearchResult> batch in answers.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (PartSearchResult part in batch)
                {
                    count++;
                    yield return part;
                }
            }
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            // No provider call outlives the enumeration that started it. A cancelled call ends in an
            // OperationCanceledException, which is expected here rather than an error.
            await providers.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        _logger.LogInformation("Streamed {Count} part(s) across providers for '{Query}'", count, normalized);
    }

    public async Task<IReadOnlyList<PartSearchResult>> SearchAllProvidersAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = new List<PartSearchResult>();
        await foreach (PartSearchResult part in StreamAllProvidersAsync(query, cancellationToken).ConfigureAwait(false))
        {
            results.Add(part);
        }

        // The order this method returned before results were streamed.
        return results
            .OrderByDescending(r => r.Stock ?? 0)
            .ThenBy(r => r.BestPrice ?? decimal.MaxValue)
            .ToList();
    }

    private async Task QueryProvidersAsync(
        IReadOnlyList<IComponentProvider> providers,
        string query,
        ChannelWriter<IReadOnlyList<PartSearchResult>> answers,
        CancellationToken cancellationToken)
    {
        try
        {
            // Task.Run keeps each provider's synchronous work - building the request, parsing the
            // response - off the caller's thread, which for the Part Explorer is the UI thread.
            await Task.WhenAll(providers.Select(provider =>
                    Task.Run(() => QueryProviderAsync(provider, query, answers, cancellationToken), cancellationToken)))
                .ConfigureAwait(false);
        }
        finally
        {
            _ = answers.TryComplete();
        }
    }

    private async Task QueryProviderAsync(
        IComponentProvider provider,
        string query,
        ChannelWriter<IReadOnlyList<PartSearchResult>> answers,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGet(provider.Id, query, out IReadOnlyList<PartSearchResult>? cached))
        {
            _logger.LogDebug("{Provider}: {Count} cached result(s) for '{Query}'", provider.DisplayName, cached.Count, query);
            Publish(answers, cached);
            return;
        }

        // A cache hit above is not a request, so it never waits here.
        using RateLimitLease lease = await _rateLimiter.AcquireAsync(provider.Id, cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            _logger.LogWarning("{Provider} left out of the search for '{Query}': too many requests are already waiting for its rate limit",
                provider.DisplayName, query);
            return;
        }

        IReadOnlyList<PartSearchResult> results;
        try
        {
            results = await provider.SearchPartsAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Superseded or abandoned: nobody is waiting for this answer, and it is incomplete.
            throw;
        }
        catch (ProviderNotConfiguredException ex)
        {
            _logger.LogDebug("{Provider} skipped for '{Query}': {Reason}", provider.DisplayName, query, ex.Message);
            return;
        }
        catch (Exception ex)
        {
            // The isolation boundary, so deliberately broad: an HTTP error, a timeout (HttpClient
            // reports one as a TaskCanceledException nobody asked for), an unexpected body or a bug in
            // one provider must not cost the user the other providers' results. Nothing is cached, so
            // the next search asks this provider again. Providers log the detail where they catch it,
            // so this records only the consequence.
            _logger.LogWarning("{Provider} left out of the results for '{Query}' and not cached: {ErrorType}: {Error}",
                provider.DisplayName, query, ex.GetType().Name, ex.Message);
            return;
        }

        _cache.Set(provider.Id, query, results);
        _logger.LogDebug("{Provider}: {Count} result(s) for '{Query}'", provider.DisplayName, results.Count, query);
        Publish(answers, results);
    }

    private static void Publish(ChannelWriter<IReadOnlyList<PartSearchResult>> answers, IReadOnlyList<PartSearchResult> results)
    {
        if (results.Count > 0)
        {
            // Unbounded, and completed only after every provider has finished, so this cannot fail.
            _ = answers.TryWrite(results);
        }
    }
}
