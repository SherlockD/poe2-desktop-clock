using Poe2DeskTracker.Capture;
using Xunit;

namespace Poe2DesktopClock.Infrastructure.Windows.Tests;

public sealed class CaptureOperationQueueTests
{
    [Fact]
    public async Task RunAsync_serializes_operations_and_cancels_waiting_call()
    {
        using var queue = new CaptureOperationQueue();
        using var waitingCancellation = new CancellationTokenSource();
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceledCallStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = queue.RunAsync(
            async _ =>
            {
                firstStarted.TrySetResult(true);
                await releaseFirst.Task;
                return 1;
            },
            CancellationToken.None);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var second = queue.RunAsync(
            _ =>
            {
                secondStarted.TrySetResult(true);
                return Task.FromResult(2);
            },
            CancellationToken.None);

        var canceledCall = queue.RunAsync(
            _ =>
            {
                canceledCallStarted.TrySetResult(true);
                return Task.FromResult(3);
            },
            waitingCancellation.Token);

        Assert.False(secondStarted.Task.IsCompleted);
        Assert.False(canceledCallStarted.Task.IsCompleted);
        waitingCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledCall);

        releaseFirst.TrySetResult(true);
        Assert.Equal(1, await first.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(2, await second.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.True(secondStarted.Task.IsCompletedSuccessfully);
        Assert.False(canceledCallStarted.Task.IsCompleted);
    }
}
