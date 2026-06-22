using System.Collections.Concurrent;
using System.Collections.Immutable;
using JetBrains.Annotations;
using topical.disposables;
using topical.handlers;

namespace topical.sync.keyed;

/// <summary>
/// A pub/sub topic where subscribers register interest in a specific <typeparamref name="TKey"/>.
/// Dispatch is O(1) per key — only handlers registered for the exact key are invoked.
/// Exceptions propagate to the <see cref="Invoke"/> caller, matching standard C# event behaviour.
/// Multiple handlers per key are supported.
/// </summary>
/// <typeparam name="TKey">The routing key type.</typeparam>
/// <typeparam name="T">The update value type.</typeparam>
public abstract class KeyedTopic<TKey, T> : IKeyedTopic<TKey, T>
    where TKey : notnull
    where T : notnull
{
    private readonly ConcurrentDictionary<TKey, ImmutableArray<TopicHandler<T>>> handlers = new();

    /// <summary>
    /// Subscribes a handler for the given key. Multiple handlers per key are supported.
    /// Dispose the returned <see cref="IDisposable"/> to unsubscribe.
    /// </summary>
    [PublicAPI]
    public IDisposable Subscribe(
        TKey key,
        TopicHandler<T> handler
    )
    {
        handlers.AddOrUpdate(
            key,
            _ => [handler],
            (
                _,
                listOfHandlers
            ) => listOfHandlers.Add(handler)
        );

        return new TopicDisposable(() => handlers.AddOrUpdate(
                key,
                _ => [],
                (
                    _,
                    listOfHandlers
                ) => listOfHandlers.Remove(handler)
            )
        );
    }

    /// <summary>
    /// Invokes all handlers registered for <paramref name="key"/> in subscription order.
    /// Exceptions propagate immediately — remaining handlers are skipped.
    /// </summary>
    [PublicAPI]
    public void Invoke(
        TKey key,
        T value
    )
    {
        if (!handlers.TryGetValue(key, out var list)) return;
        foreach (var handler in list) handler(value);
    }
}