namespace KiCadUltra.Services;

/// <summary>
/// How <see cref="AsyncEnumerableObservableExtensions.ToObservable{T}"/> ends the observable when the
/// async stream is cancelled by something other than the subscriber.
/// </summary>
/// <remarks>
/// A subscriber disposing its subscription also cancels the stream, but that is never reported: the
/// subscriber has left, and Rx delivers nothing after <c>Dispose</c>. This choice applies only to a
/// stream that ends with an <see cref="System.OperationCanceledException"/> while it is still being
/// observed.
/// </remarks>
public enum AsyncStreamCancellation
{
    /// <summary>
    /// Raise <c>OnError</c> with the stream's own <see cref="System.OperationCanceledException"/>. The
    /// stream stopped early, so its observers should not mistake the end for a complete sequence.
    /// </summary>
    Error,

    /// <summary>Raise <c>OnCompleted</c>: the cancellation ends the sequence like its natural end.</summary>
    Complete,
}
