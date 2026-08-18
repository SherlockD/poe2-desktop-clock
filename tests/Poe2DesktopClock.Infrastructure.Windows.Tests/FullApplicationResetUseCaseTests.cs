using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Services;
using Xunit;

namespace Poe2DesktopClock.Infrastructure.Windows.Tests;

public sealed class FullApplicationResetUseCaseTests
{
    [Fact]
    public async Task Reset_delegates_to_the_persisted_data_resetter()
    {
        var resetter = new TestApplicationDataResetter();
        var useCase = new FullApplicationResetUseCase(resetter);

        await useCase.ResetAsync();

        Assert.Equal(1, resetter.ResetCalls);
    }

    [Fact]
    public async Task Cancelled_reset_does_not_touch_persisted_data()
    {
        var resetter = new TestApplicationDataResetter();
        var useCase = new FullApplicationResetUseCase(resetter);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => useCase.ResetAsync(cancellation.Token));

        Assert.Equal(0, resetter.ResetCalls);
    }

    private sealed class TestApplicationDataResetter : IApplicationDataResetter
    {
        public int ResetCalls { get; private set; }

        public void Reset() => ResetCalls++;
    }
}
