using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Services;

/// <summary>
/// Эмулятор будущего устройства: немедленно подтверждает каждый снимок.
/// Предназначен для использования до появления транспорта для физических часов.
/// </summary>
public sealed class StubDeviceSynchronizationUseCase : IDeviceSynchronizationUseCase
{
    private readonly object _stateLock = new();
    private readonly TimeProvider _timeProvider;
    private DeviceSynchronizationState _currentState = DeviceSynchronizationState.WaitingForSnapshot;

    public StubDeviceSynchronizationUseCase(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event EventHandler<DeviceSynchronizationState>? SynchronizationStateChanged;

    public DeviceSynchronizationState CurrentState
    {
        get
        {
            lock (_stateLock)
            {
                return _currentState;
            }
        }
    }

    public Task<DeviceSynchronizationState> SynchronizeAsync(
        ClockSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<DeviceSynchronizationState>(cancellationToken);
        }

        var synchronizedState = new DeviceSynchronizationState(
            IsConnected: true,
            DeviceSynchronizationStatus.Synchronized,
            snapshot,
            _timeProvider.GetUtcNow());
        lock (_stateLock)
        {
            _currentState = synchronizedState;
        }

        SynchronizationStateChanged?.Invoke(this, synchronizedState);
        return Task.FromResult(synchronizedState);
    }
}
