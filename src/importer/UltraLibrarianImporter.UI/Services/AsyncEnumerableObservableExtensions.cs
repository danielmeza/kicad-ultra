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
    /// throwing raises OnError with that exception.
    /// </summary>
    /// <param name="source">The stream to observe.</param>
    /// <param name="whenCancelled">
    /// How to end the observable when the stream is cancelled by something other than the subscriber:
    /// raise OnError with the stream's own <see cref="OperationCanceledException"/>, or raise
    /// OnCompleted.
    /// </param>
    /// <param name="onCancelled">
    /// Called with that <see cref="OperationCanceledException"/> before the observable ends, whichever
    /// <paramref name="whenCancelled"/> says. If it throws, the observable fails with what it threw.
    /// </param>
    /// <remarks>
    /// <para>
    /// Cancellation has two sources, and only one of them is reported. The subscriber disposing its
    /// subscription cancels the stream through the token this method passes it. That is the subscriber
    /// leaving, not an event of the sequence, so nothing is raised and <paramref name="onCancelled"/>
    /// is not called - which is what lets <c>Switch()</c> drop a superseded search silently. Anything
    /// else that cancels the stream while it is still observed - a token the source holds itself, a
    /// timeout - is governed by <paramref name="whenCancelled"/> and <paramref name="onCancelled"/>.
    /// </para>
    /// <para>
    /// The cancellation is caught here rather than left to escape. Escaping an async
    /// <c>Observable.Create</c> body, it would end the task Canceled, and Rx reports that as a
    /// <see cref="TaskCanceledException"/> ("A task was canceled.") that replaces
    /// the stream's own exception and cannot be told apart from any other failure.
    /// </para>
    /// <para>
    /// Neither System.Reactive 6.1 nor ReactiveUI 23 ships this conversion, and the package that does
    /// (System.Linq.Async) overlaps the <c>System.Linq.AsyncEnumerable</c> built into .NET 10.
    /// Items are raised on whatever thread the enumeration resumes on, so a UI consumer must
    /// <c>ObserveOn</c> its own scheduler.
    /// </para>
    /// </remarks>
    public static IObservable<T> ToObservable<T>(
        this IAsyncEnumerable<T> source,
        AsyncStreamCancellation whenCancelled = AsyncStreamCancellation.Error,
        Action<OperationCanceledException>? onCancelled = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Observable.Create<T>(async (observer, unsubscribed) =>
        {
            try
            {
                await foreach (T item in source.WithCancellation(unsubscribed).ConfigureAwait(false))
                {
                    observer.OnNext(item);
                }
            }
            catch (OperationCanceledException) when (unsubscribed.IsCancellationRequested)
            {
                // The subscriber disposed its subscription. Returning completes the task, and Rx drops
                // that completion: Dispose stopped the observer before it cancelled the token.
            }
            catch (OperationCanceledException cancelled)
            {
                onCancelled?.Invoke(cancelled);

                if (whenCancelled == AsyncStreamCancellation.Error)
                {
                    // Raised here rather than rethrown: rethrowing would end the task Canceled, and Rx
                    // would replace this exception with a TaskCanceledException.
                    observer.OnError(cancelled);
                }

                // Returning completes the task: after OnError Rx ignores that; for Complete it raises
                // OnCompleted.
            }
        });
    }
}
