using Microsoft.Extensions.DependencyInjection;
using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Services;
using Poe2DesktopClock.Infrastructure.Windows.Runtime;
using Poe2DesktopClock.Infrastructure.Windows.Monitoring;
using Poe2DesktopClock.Infrastructure.Storage.PublicStash;
using Poe2DeskTracker.Capture;
using Poe2DeskTracker.Game;
using Poe2DeskTracker.Pricing;
using Poe2DeskTracker.PublicStash;

namespace Poe2DesktopClock.Composition;

public static class Poe2DesktopClockComposition
{
    public static IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();

        services.AddSingleton<PoeProcessLocator>();
        services.AddSingleton<WindowsGraphicsCaptureService>();
        services.AddSingleton<TradeApiClient>();
        services.AddSingleton<PoeNinjaPriceClient>();
        services.AddSingleton<IPriceSnapshotProvider, PoeNinjaPriceSnapshotProvider>();
        services.AddSingleton<ICurrencyValuationReader, WindowsCurrencyValuationReader>();
        services.AddSingleton<ICurrencyChangeMonitor, WindowsCurrencyChangeMonitor>();
        services.AddSingleton<IGameStatusReader, WindowsGameStatusReader>();
        services.AddSingleton<IPublicTabsValuationReader, PublicTabsValuationReader>();
        services.AddSingleton<IPublicTabMarkerProvider, StoredPublicTabMarkerProvider>();
        services.AddSingleton<IClockSnapshotComposer, ClockSnapshotComposer>();
        services.AddSingleton<ITrackerSnapshotPublisher, TrackerSnapshotPublisher>();
        services.AddSingleton<DesktopClockRuntime>();
        services.AddSingleton<RefreshTrackerUseCase>();
        services.AddSingleton<CurrencyMonitoringUseCase>();

        services.AddSingleton<ITrackerSettingsUseCase>(provider => provider.GetRequiredService<DesktopClockRuntime>());
        services.AddSingleton<ITrackerRefreshUseCase>(provider => provider.GetRequiredService<RefreshTrackerUseCase>());
        services.AddSingleton<ITrackerMonitoringUseCase>(provider => provider.GetRequiredService<CurrencyMonitoringUseCase>());
        services.AddSingleton<ICurrencySetupUseCase>(provider => provider.GetRequiredService<DesktopClockRuntime>());
        services.AddSingleton<ILeagueCatalog>(provider => provider.GetRequiredService<TradeApiClient>());
        return services;
    }
}
