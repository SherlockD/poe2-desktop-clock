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
            return await operation(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
