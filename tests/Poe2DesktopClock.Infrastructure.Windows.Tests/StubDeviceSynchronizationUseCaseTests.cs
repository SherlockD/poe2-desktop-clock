using Poe2DesktopClock.Application.Services;
using Poe2DesktopClock.Contracts.Models;
using Xunit;

namespace Poe2DesktopClock.Infrastructure.Windows.Tests;

public sealed class StubDeviceSynchronizationUseCaseTests
{
    [Fact]
    public void Initial_state_waits_for_the_first_snapshot()
    {
        var synchronizer = new StubDeviceSynchronizationUseCase();

        var state = synchronizer.CurrentState;

        Assert.True(state.IsConnected);
        Assert.False(state.IsSynchronized);
        Assert.Equal(DeviceSynchronizationStatus.WaitingForSnapshot, state.Status);
        Assert.Null(state.LastSnapshot);
        Assert.Null(state.LastSynchronizedAt);
    }

    [Fact]
    public async Task Synchronize_immediately_confirms_and_remembers_the_snapshot()
    {
        var synchronizedAt = new DateTimeOffset(2026, 8, 18, 12, 30, 0, TimeSpan.Zero);
        var synchronizer = new StubDeviceSynchronizationUseCase(new FixedTimeProvider(synchronizedAt));
        var snapshot = CreateSnapshot(125.37m);
        DeviceSynchronizationState? notifiedState = null;
        synchronizer.SynchronizationStateChanged += (_, state) => notifiedState = state;

        var synchronization = synchronizer.SynchronizeAsync(snapshot);
        var state = await synchronization;

        Assert.True(synchronization.IsCompletedSuccessfully);
        Assert.True(state.IsConnected);
        Assert.True(state.IsSynchronized);
        Assert.Equal(DeviceSynchronizationStatus.Synchronized, state.Status);
        Assert.Same(snapshot, state.LastSnapshot);
        Assert.Equal(synchronizedAt, state.LastSynchronizedAt);
        Assert.Equal(state, synchronizer.CurrentState);
        Assert.Equal(state, notifiedState);
    }

    [Fact]
    public async Task Cancelled_synchronization_keeps_the_last_confirmed_state()
    {
        var firstTime = new DateTimeOffset(2026, 8, 18, 12, 30, 0, TimeSpan.Zero);
        var synchronizer = new StubDeviceSynchronizationUseCase(new FixedTimeProvider(firstTime));
        var firstSnapshot = CreateSnapshot(10m);
        var confirmedState = await synchronizer.SynchronizeAsync(firstSnapshot);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => synchronizer.SynchronizeAsync(CreateSnapshot(20m), cancellation.Token));

        Assert.Equal(confirmedState, synchronizer.CurrentState);
    }

    private static ClockSnapshot CreateSnapshot(decimal totalDivines) => new(
        totalDivines,
        CurrencyTabDivines: totalDivines,
        PublicTabsDivines: 0m,
        CurrencyUpdatedAt: null,
        PublicTabsUpdatedAt: null,
        PricesUpdatedAt: null,
        IsComplete: true,
        RussianSummary: string.Empty);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
