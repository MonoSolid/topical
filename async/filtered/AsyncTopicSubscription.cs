using topical.handlers;

namespace topical.async.filtered;

/// <summary>Holds a filter predicate, an async handler, and the cancellation token tied to its subscription lifetime.</summary>
internal record AsyncTopicSubscription<T>(
    Func<T, bool> Condition,
    AsyncTopicHandler<T> AsyncHandler,
    CancellationToken CancellationToken
);