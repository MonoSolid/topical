using JetBrains.Annotations;

namespace topical;

/// <summary>
/// Pub/sub topic where subscribers register interest in a specific <typeparamref name="TKey"/>.
/// </summary>
/// <typeparam name="TKey">The routing key type.</typeparam>
/// <typeparam name="T">The update value type.</typeparam>
[PublicAPI]
public interface IKeyedTopic<TKey, T>
    where TKey : notnull
    where T : notnull
{
    /// <summary>
    /// Subscribes a handler for the given key.
    /// Dispose the returned <see cref="IDisposable"/> to unsubscribe.
    /// </summary>
    IDisposable Subscribe(TKey key, TopicHandler<T> handler);

    /// <summary>
    /// Invokes all handlers registered for <paramref name="key"/> in subscription order.
    /// </summary>
    void Invoke(TKey key, T value);
}
