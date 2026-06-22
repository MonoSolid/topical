using JetBrains.Annotations;
using topical.handlers;

namespace topical.async.keyed;

/// <summary>
/// Async pub/sub topic where subscribers register interest in a specific <typeparamref name="TKey"/>.
/// </summary>
/// <typeparam name="TKey">The routing key type.</typeparam>
/// <typeparam name="T">The update value type.</typeparam>
[PublicAPI]
public interface IAsyncKeyedTopic<TKey, T>
    where TKey : notnull
    where T : notnull
{
    /// <summary>Subscribes all entries in a dictionary, one async handler per key.</summary>
    IEnumerable<IDisposable> Subscribe(IReadOnlyDictionary<TKey, AsyncTopicHandler<T>> update);

    /// <summary>Subscribes all entries in a dictionary, one sync handler per key.</summary>
    IEnumerable<IDisposable> Subscribe(IReadOnlyDictionary<TKey, TopicHandler<T>> update);

    /// <summary>
    /// Subscribes a synchronous handler for the given key.
    /// Dispose the returned <see cref="IDisposable"/> to unsubscribe and cancel any in-flight invocation.
    /// </summary>
    IDisposable Subscribe(TKey key, TopicHandler<T> handler);

    /// <summary>
    /// Subscribes an async handler for the given key.
    /// Dispose the returned <see cref="IDisposable"/> to unsubscribe and cancel any in-flight invocation.
    /// </summary>
    IDisposable Subscribe(TKey key, AsyncTopicHandler<T> asyncHandler);

    /// <summary>
    /// Invokes all handlers registered for <paramref name="key"/> concurrently.
    /// </summary>
    Task InvokeAsync(TKey key, T value, CancellationToken cancellationToken = default);
}
