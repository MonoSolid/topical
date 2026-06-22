using System.Collections.Immutable;
using JetBrains.Annotations;

namespace topical;

/// <summary>
/// A pub/sub topic where subscribers optionally provide a predicate to filter which updates they receive.
/// All matching handlers are invoked on every <see cref="Invoke"/> call (O(n) subscribers).
/// Exceptions propagate to the <see cref="Invoke"/> caller, matching standard C# event behaviour.
/// Thread-safe: subscribe and dispose may be called concurrently with <see cref="Invoke"/>.
/// </summary>
/// <typeparam name="T">The update value type.</typeparam>
public abstract class FilterableTopic<T>
    where T : notnull
{
    private ImmutableArray<(Func<T, bool> Condition, TopicHandler<T> Handler)> subscriptions = [];

    /// <summary>Subscribes a handler that receives all updates without filtering.</summary>
    public IDisposable Subscribe(TopicHandler<T> handler) => Subscribe(_ => true, handler);

    /// <summary>
    /// Subscribes a handler that only receives updates matching <paramref name="where"/>.
    /// Dispose the returned <see cref="IDisposable"/> to unsubscribe.
    /// </summary>
    [PublicAPI]
    public IDisposable Subscribe(
        Func<T, bool> where,
        TopicHandler<T> handler
    )
    {
        var subscription = (where, handler);
        ImmutableInterlocked.Update(ref subscriptions, s => s.Add(subscription));

        return new TopicDisposable(() => ImmutableInterlocked.Update(ref subscriptions, s => s.Remove(subscription)));
    }

    /// <summary>
    /// Invokes all handlers whose predicate matches <paramref name="value"/>, in subscription order.
    /// Exceptions propagate immediately — remaining matching handlers are skipped.
    /// </summary>
    [PublicAPI]
    public void Invoke(T value)
    {
        foreach (var (condition, handler) in subscriptions)
            if (condition(value))
                handler(value);
    }
}