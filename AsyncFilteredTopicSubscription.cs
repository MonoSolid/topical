namespace topical;

/// <summary>Holds a filter predicate, an async handler, and the cancellation token tied to its subscription lifetime.</summary>
public record AsyncFilteredTopicSubscription<T>(
    Func<T, bool> Condition,
    AsyncTopicHandler<T> AsyncHandler,
    CancellationToken CancellationToken
);