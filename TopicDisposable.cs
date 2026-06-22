namespace topical;

/// <summary>
/// An <see cref="IDisposable"/> that executes a cleanup action exactly once on first disposal.
/// Used to return unsubscribe handles from topic <c>Subscribe</c> methods.
/// </summary>
public sealed class TopicDisposable(Action action) : IDisposable
{
    private bool disposedValue;

    public void Dispose()
    {
        if (!disposedValue) action();
        disposedValue = true;
    }
}