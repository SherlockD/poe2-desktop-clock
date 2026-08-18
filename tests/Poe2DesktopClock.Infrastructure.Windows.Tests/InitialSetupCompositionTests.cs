using Microsoft.Extensions.DependencyInjection;
using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Composition;
using Xunit;

namespace Poe2DesktopClock.Infrastructure.Windows.Tests;

public sealed class InitialSetupCompositionTests
{
    [Fact]
    public async Task Composition_resolves_the_initial_setup_ports_from_shared_stores()
    {
        var services = Poe2DesktopClockComposition.CreateServiceCollection();
        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IInitialSetupStateStore>());
        Assert.NotNull(provider.GetRequiredService<IPublicTabsSetupUseCase>());
        Assert.NotNull(provider.GetRequiredService<ITrackerSettingsUseCase>());
        Assert.NotNull(provider.GetRequiredService<IPublicTabMarkerProvider>());
    }
}
