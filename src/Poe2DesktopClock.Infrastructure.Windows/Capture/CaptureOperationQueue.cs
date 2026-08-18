namespace Poe2DeskTracker.Capture;

/// <summary>Serializes access to the shared Direct3D immediate context.</summary>
internal sealed class CaptureOperationQueue : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Windows Graphics Capture and its Direct3D context stay on the
            // caller's capture context. The PNG encoder is dispatched by the
            // capture service after the pixels have been copied from the frame.
            return await operation(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
