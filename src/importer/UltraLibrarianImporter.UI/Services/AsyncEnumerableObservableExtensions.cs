using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace UltraLibrarianImporter.UI.Services;

public static class AsyncEnumerableObservableExtensions
{
    /// <summary>
    /// Exposes an async stream as an observable. Each subscription enumerates
    /// <paramref name="source"/> afresh, and disposing the subscription cancels the token passed to
    /// the enumeration - for <see cref="Interfaces.IPartAggregatorService.StreamAllProvidersAsync"/>
    /// that stops the provider calls still running. The stream ending raises OnCompleted; the stream
    /// throwing raises OnError, unless the subscription was already disposed.
    /// </summary>
    /// <remarks>
    /// Neither System.Reactive 6.1 nor ReactiveUI 23 ships this conversion, and the package that does
    /// (System.Linq.Async) overlaps the <c>System.Linq.AsyncEnumerable</c> built into .NET 10.
    /// Items are raised on whatever thread the enumeration resumes on, so a UI consumer must
    /// <c>ObserveOn</c> its own scheduler.
    /// </remarks>
    public static IObservable<T> ToObservable<T>(this IAsyncEnumerable<T> source) =>
        Observable.Create<T>(async (observer, cancellationToken) =>
        {
            await foreach (T item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                observer.OnNext(item);
            }
        });
}
