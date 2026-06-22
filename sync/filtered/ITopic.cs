using JetBrains.Annotations;
using topical.handlers;

namespace topical.sync.filtered;

/// <summary>
/// Pub/sub topic where subscribers optionally provide a predicate to filter which updates they receive.
/// </summary>
/// <typeparam name="T">The update value type.</typeparam>
[PublicAPI]
public interface ITopic<T>
    where T : notnull
{
    /// <summary>Subscribes a handler that receives all updates without filtering.</summary>
    IDisposable Subscribe(TopicHandler<T> handler);

    /// <summary>
    /// Subscribes a handler that only receives updates matching <paramref name="where"/>.
    /// Dispose the returned <see cref="IDisposable"/> to unsubscribe.
    /// </summary>
    IDisposable Subscribe(Func<T, bool> where, TopicHandler<T> handler);

    /// <summary>
    /// Invokes all handlers whose predicate matches <paramref name="value"/>.
    /// </summary>
    void Invoke(T value);
}
