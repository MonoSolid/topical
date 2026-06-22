using topical.handlers;

namespace topical.async.keyed;

/// <summary>Holds an async handler and the cancellation token tied to its subscription lifetime.</summary>
internal record AsyncKeyedTopicSubscription<T>(
    AsyncTopicHandler<T> AsyncHandler,
    CancellationToken CancellationToken
);