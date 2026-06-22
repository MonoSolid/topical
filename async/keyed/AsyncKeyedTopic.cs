using System.Collections.Concurrent;
using System.Collections.Immutable;
using JetBrains.Annotations;
using topical.disposables;
using topical.handlers;

namespace topical.async.keyed;

/// <summary>
/// A pub/sub topic where subscribers register interest in a specific <typeparamref name="TKey"/>.
/// Dispatch is O(1) per key — only handlers registered for the exact key are invoked.
/// Exceptions thrown by handlers propagate to the caller of <see cref="InvokeAsync"/>, matching standard C# event behaviour.
/// </summary>
/// <typeparam name="TKey">The routing key type. Must match the key used when subscribing.</typeparam>
/// <typeparam name="T">The update value type.</typeparam>
public class AsyncKeyedTopic<TKey, T> : IAsyncKeyedTopic<TKey, T>
    where TKey : notnull
    where T : notnull
{
    private readonly ConcurrentDictionary<TKey, ImmutableArray<AsyncKeyedTopicSubscription<T>>> handlers = new();

    /// <summary>Subscribes all entries in a dictionary, one handler per key.</summary>
    [PublicAPI]
    public IEnumerable<IDisposable> Subscribe(IReadOnlyDictionary<TKey, AsyncTopicHandler<T>> update)
    {
        return update.Select(kvp => Subscribe(kvp.Key, kvp.Value));
    }

    /// <summary>Subscribes all entries in a dictionary, one sync handler per key.</summary>
    [PublicAPI]
    public IEnumerable<IDisposable> Subscribe(IReadOnlyDictionary<TKey, TopicHandler<T>> update)
    {
        return update.Select(kvp => Subscribe(kvp.Key, kvp.Value));
    }

    /// <summary>
    /// Subscribes a synchronous handler for the given key.
    /// The handler is adapted to the async pipeline; exceptions propagate as faulted tasks.
    /// Dispose the returned <see cref="IDisposable"/> to unsubscribe and cancel any in-flight invocation.
    /// </summary>
    [PublicAPI]
    public IDisposable Subscribe(
        TKey key,
        TopicHandler<T> handler
    )
    {
        return Subscribe(
            key,
            (
                update,
                _
            ) =>
            {
                try
                {
                    handler(update);
                    return Task.CompletedTask;
                }
                catch (Exception exception)
                {
                    return Task.FromException(exception);
                }
            }
        );
    }

    /// <summary>
    /// Subscribes an async handler for the given key. Multiple handlers per key are supported.
    /// Dispose the returned <see cref="IDisposable"/> to unsubscribe and cancel any in-flight invocation.
    /// </summary>
    [PublicAPI]
    public IDisposable Subscribe(
        TKey key,
        AsyncTopicHandler<T> asyncHandler
    )
    {
        CancellationTokenSource cts = new();

        var subscription = new AsyncKeyedTopicSubscription<T>(asyncHandler, cts.Token);

        handlers.AddOrUpdate(
            key,
            _ => [subscription],
            (
                _,
                listOfHandlers
            ) => listOfHandlers.Add(subscription)
        );

        return new TopicDisposable(() =>
            {
                cts.Cancel();
                cts.Dispose();

                handlers.AddOrUpdate(
                    key,
                    _ => [],
                    (
                        _,
                        listOfHandlers
                    ) => listOfHandlers.Remove(subscription)
                );
            }
        );
    }

    /// <summary>
    /// Invokes all handlers registered for <paramref name="key"/> concurrently.
    /// All handlers run to completion regardless of individual failures.
    /// The combined cancellation of <paramref name="cancellationToken"/> and each subscription's
    /// own token is passed to each handler.
    /// </summary>
    [PublicAPI]
    public async Task InvokeAsync(
        TKey key,
        T value,
        CancellationToken cancellationToken = default
    )
    {
        if (!handlers.TryGetValue(key, out var subscriptions)) return;

        var tasks = subscriptions.Select(async subscription =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    subscription.CancellationToken
                );

                await subscription
                    .AsyncHandler(value, cts.Token)
                    .ConfigureAwait(false);
            }
        );

        await Task
            .WhenAll(tasks)
            .ConfigureAwait(false);
    }
}