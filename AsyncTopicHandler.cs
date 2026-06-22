namespace topical;

/// <summary>
/// Async topic update handler
/// </summary>
/// <param name="ct">Cancels the handler. Cancellation is triggered by the publisher or the disposal of the subscription</param>
/// <typeparam name="T">Update type</typeparam>
/// <returns>Task</returns>
public delegate Task AsyncTopicHandler<in T>(
    T update,
    CancellationToken ct
);