using System.Collections.Immutable;
using JetBrains.Annotations;

namespace topical;

/// <summary>
/// A pub/sub topic where subscribers optionally provide a predicate to filter which updates they receive.
/// Supports both sync and async handlers. All matching handlers run concurrently via
/// <see cref="InvokeAsync"/> or sequentially via <see cref="InvokeSequentialAsync"/>.
/// Exceptions propagate to the invoke caller, matching standard C# event behaviour.
/// Thread-safe: subscribe and dispose may be called concurrently with invoke methods.
/// </summary>
/// <typeparam name="T">The update value type.</typeparam>
public class AsyncFilterableTopic<T>
    where T : notnull
{
    private ImmutableArray<AsyncFilteredTopicSubscription<T>> subscriptions = [];

    /// <summary>Subscribes a sync handler that receives all updates without filtering.</summary>
    [PublicAPI]
    public IDisposable Subscribe(TopicHandler<T> handler)
    {
        return Subscribe((
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

    /// <summary>Subscribes an async handler that receives all updates without filtering.</summary>
    [PublicAPI]
    public IDisposable Subscribe(AsyncTopicHandler<T> asyncHandler)
    {
        return Subscribe(_ => true, asyncHandler);
    }

    /// <summary>
    /// Subscribes a sync handler that only receives updates matching <paramref name="where"/>.
    /// Dispose the returned <see cref="IDisposable"/> to unsubscribe.
    /// </summary>
    [PublicAPI]
    public IDisposable Subscribe(
        Func<T, bool> where,
        TopicHandler<T> handler
    )
    {
        return Subscribe(
            where,
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
    /// Subscribes an async handler that only receives updates matching <paramref name="where"/>.
    /// Dispose the returned <see cref="IDisposable"/> to unsubscribe and cancel any in-flight invocation.
    /// </summary>
    [PublicAPI]
    public IDisposable Subscribe(
        Func<T, bool> where,
        AsyncTopicHandler<T> asyncHandler
    )
    {
        CancellationTokenSource cts = new();

        var filteredSubscription = new AsyncFilteredTopicSubscription<T>(where, asyncHandler, cts.Token);
        ImmutableInterlocked.Update(ref subscriptions, s => s.Add(filteredSubscription));

        return new TopicDisposable(() =>
            {
                cts.Cancel();
                cts.Dispose();
                ImmutableInterlocked.Update(ref subscriptions, s => s.Remove(filteredSubscription));
            }
        );
    }

    /// <summary>
    /// Invokes all handlers whose predicate matches <paramref name="value"/> concurrently.
    /// All matching handlers run to completion regardless of individual failures.
    /// </summary>
    [PublicAPI]
    public async Task InvokeAsync(
        T value,
        CancellationToken cancellationToken = default
    )
    {
        var tasks = subscriptions
            .Where(e => e.Condition(value))
            .Select(async subscription =>
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

    /// <summary>
    /// Invokes matching handlers one at a time in subscription order.
    /// An exception from one handler stops subsequent handlers from running.
    /// </summary>
    [PublicAPI]
    public async Task InvokeSequentialAsync(
        T value,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var subscription in subscriptions.Where(e => e.Condition(value)))
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                subscription.CancellationToken
            );

            await subscription
                .AsyncHandler(value, cts.Token)
                .ConfigureAwait(false);
        }
    }
}