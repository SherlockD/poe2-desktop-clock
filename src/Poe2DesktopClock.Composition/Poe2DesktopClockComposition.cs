using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Services;
using Poe2DesktopClock.Infrastructure.Windows.Runtime;
using Poe2DesktopClock.Infrastructure.Windows.Monitoring;
using Poe2DesktopClock.Infrastructure.Storage.Snapshots;
using Poe2DesktopClock.Infrastructure.Storage.PublicStash;
using Poe2DeskTracker.Capture;
using Poe2DeskTracker.Currency;
using Poe2DeskTracker.Game;
using Poe2DeskTracker.Pricing;
using Poe2DeskTracker.PublicStash;
using Poe2DeskTracker.Regions;

namespace Poe2DesktopClock.Composition;

public static class Poe2DesktopClockComposition
{
    private static readonly Uri TradeApiBaseUri = new("https://www.pathofexile.com/");
    private static readonly Uri PoeNinjaBaseUri = new("https://poe.ninja/");

    public static IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        var desktopDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Poe2DesktopClock");
        var legacyDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Poe2DeskTracker");

        services.AddSingleton<PoeProcessLocator>();
        services.AddSingleton<WindowsGraphicsCaptureService>();
        services.AddSingleton(new RegionStore(Path.Combine(legacyDataDirectory, "regions.json")));
        services.AddSingleton(new CurrencyLayoutStore(Path.Combine(legacyDataDirectory, "currency-layouts.json")));
        services.AddHttpClient(TradeApiClient.HttpClientName, client => ConfigurePoeApiClient(client, TradeApiBaseUri));
        services.AddHttpClient(PoeNinjaPriceClient.HttpClientName, client => ConfigurePoeApiClient(client, PoeNinjaBaseUri));
        services.AddSingleton<TradeApiClient>();
        services.AddSingleton<PoeNinjaPriceClient>();
        services.AddSingleton<IPriceSnapshotProvider, PoeNinjaPriceSnapshotProvider>();
        services.AddSingleton<ICurrencyValuationReader, WindowsCurrencyValuationReader>();
        services.AddSingleton<ICurrencyChangeMonitor, WindowsCurrencyChangeMonitor>();
        services.AddSingleton<IGameStatusReader, WindowsGameStatusReader>();
        services.AddSingleton<IPublicTabsValuationReader, PublicTabsValuationReader>();
        services.AddSingleton<IPublicTabMarkerProvider, StoredPublicTabMarkerProvider>();
        services.AddSingleton(new PublicTabsSnapshotStore(Path.Combine(desktopDataDirectory, "public-tabs-snapshot.json")));
        services.AddSingleton<ILastClockSnapshotStore>(
            new LastClockSnapshotStore(Path.Combine(desktopDataDirectory, "last-clock-snapshot.json")));
        services.AddSingleton<IClockSnapshotComposer, ClockSnapshotComposer>();
        services.AddSingleton<ITrackerSnapshotPublisher, TrackerSnapshotPublisher>();
        services.AddSingleton<IDeviceSynchronizationUseCase, StubDeviceSynchronizationUseCase>();
        services.AddSingleton<DeviceSnapshotRelay>();
        services.AddSingleton<IGameSessionUseCase, GameSessionUseCase>();
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

    private static void ConfigurePoeApiClient(HttpClient client, Uri baseAddress)
    {
        client.BaseAddress = baseAddress;
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Poe2DeskTracker", "0.1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
