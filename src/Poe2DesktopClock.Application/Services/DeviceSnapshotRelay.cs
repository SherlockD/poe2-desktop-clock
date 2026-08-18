using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Services;

/// <summary>
/// Forwards tracker snapshots to the configured display device without holding
/// up capture, pricing or valuation. When several values arrive during one
/// delivery, only the newest one is sent next.
/// </summary>
public sealed class DeviceSnapshotRelay : IAsyncDisposable
{
    private readonly ITrackerSnapshotPublisher _publisher;
    private readonly IDeviceSynchronizationUseCase _device;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _sync = new();
    private ClockSnapshot? _pendingSnapshot;
    private Task? _deliveryTask;
    private bool _disposed;

    public DeviceSnapshotRelay(
        ITrackerSnapshotPublisher publisher,
        IDeviceSynchronizationUseCase device)
    {
        _publisher = publisher;
        _device = device;
        _publisher.ClockSnapshotChanged += OnClockSnapshotChanged;
    }

    /// <summary>Queues an existing persisted snapshot during application startup.</summary>
    public void QueueSnapshot(ClockSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        QueueLatest(snapshot);
    }

    public async ValueTask DisposeAsync()
    {
        Task? deliveryTask;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pendingSnapshot = null;
            _publisher.ClockSnapshotChanged -= OnClockSnapshotChanged;
            _shutdown.Cancel();
            deliveryTask = _deliveryTask;
        }

        try
        {
            if (deliveryTask is not null)
            {
                await deliveryTask;
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Expected shutdown path.
        }
        finally
        {
            _shutdown.Dispose();
        }
    }

    private void OnClockSnapshotChanged(object? sender, ClockSnapshot snapshot) => QueueLatest(snapshot);

    private void QueueLatest(ClockSnapshot snapshot)
    {
        lock (_sync)
        {
            if (_disposed || _shutdown.IsCancellationRequested)
            {
                return;
            }

            _pendingSnapshot = snapshot;
            if (_deliveryTask is not { IsCompleted: false })
            {
                _deliveryTask = DeliverPendingSnapshotsAsync(_shutdown.Token);
            }
        }
    }

    private async Task DeliverPendingSnapshotsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            ClockSnapshot? snapshot;
            lock (_sync)
            {
                if (_disposed || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                snapshot = _pendingSnapshot;
                _pendingSnapshot = null;
                if (snapshot is null)
                {
                    return;
                }
            }

            try
            {
                await _device.SynchronizeAsync(snapshot, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // A future transport owns its own failure state. The next
                // tracker snapshot will attempt a fresh delivery.
            }
        }
    }
}
