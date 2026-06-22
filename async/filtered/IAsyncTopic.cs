using JetBrains.Annotations;
using topical.handlers;

namespace topical.async.filtered;

/// <summary>
/// Async pub/sub topic where subscribers optionally provide a predicate to filter which updates they receive.
/// </summary>
/// <typeparam name="T">The update value type.</typeparam>
[PublicAPI]
public interface IAsyncTopic<T>
    where T : notnull
{
    /// <summary>Subscribes a sync handler that receives all updates without filtering.</summary>
    IDisposable Subscribe(TopicHandler<T> handler);

    /// <summary>Subscribes an async handler that receives all updates without filtering.</summary>
    IDisposable Subscribe(AsyncTopicHandler<T> asyncHandler);

    /// <summary>Subscribes a sync handler that only receives updates matching <paramref name="where"/>.</summary>
    IDisposable Subscribe(Func<T, bool> where, TopicHandler<T> handler);

    /// <summary>
    /// Subscribes an async handler that only receives updates matching <paramref name="where"/>.
    /// Dispose the returned <see cref="IDisposable"/> to unsubscribe and cancel any in-flight invocation.
    /// </summary>
    IDisposable Subscribe(Func<T, bool> where, AsyncTopicHandler<T> asyncHandler);

    /// <summary>
    /// Invokes all matching handlers concurrently.
    /// </summary>
    Task InvokeAsync(T value, CancellationToken cancellationToken = default);
}
