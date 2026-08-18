using Poe2DesktopClock.Application.Services;
using Poe2DesktopClock.Contracts.Models;
using Xunit;

namespace Poe2DesktopClock.Infrastructure.Windows.Tests;

public sealed class DeviceSnapshotRelayTests
{
    [Fact]
    public async Task Published_snapshot_is_delivered_to_the_device_emulator()
    {
        var publisher = new TrackerSnapshotPublisher();
        var device = new StubDeviceSynchronizationUseCase();
        await using var relay = new DeviceSnapshotRelay(publisher, device);
        var snapshot = CreateSnapshot(125.37m);
        var delivered = new TaskCompletionSource<DeviceSynchronizationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        device.SynchronizationStateChanged += (_, state) => delivered.TrySetResult(state);

        publisher.Publish(snapshot);

        var state = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(state.IsSynchronized);
        Assert.Same(snapshot, state.LastSnapshot);
        Assert.Equal(state, device.CurrentState);
    }

    [Fact]
    public async Task Persisted_snapshot_can_be_queued_before_new_tracker_events()
    {
        var publisher = new TrackerSnapshotPublisher();
        var device = new StubDeviceSynchronizationUseCase();
        await using var relay = new DeviceSnapshotRelay(publisher, device);
        var snapshot = CreateSnapshot(77m);
        var delivered = new TaskCompletionSource<DeviceSynchronizationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        device.SynchronizationStateChanged += (_, state) => delivered.TrySetResult(state);

        relay.QueueSnapshot(snapshot);

        var state = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Same(snapshot, state.LastSnapshot);
    }

    private static ClockSnapshot CreateSnapshot(decimal totalDivines) => new(
        TotalDivines: totalDivines,
        CurrencyTabDivines: totalDivines,
        PublicTabsDivines: 0m,
        CurrencyUpdatedAt: DateTimeOffset.UtcNow,
        PublicTabsUpdatedAt: null,
        PricesUpdatedAt: null,
        IsComplete: false,
        RussianSummary: "Частичная оценка.");
}
