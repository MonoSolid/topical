namespace topical;

/// <summary>Holds an async handler and the cancellation token tied to its subscription lifetime.</summary>
internal record AsyncTopicSubscription<T>(
    AsyncTopicHandler<T> AsyncHandler,
    CancellationToken CancellationToken
);