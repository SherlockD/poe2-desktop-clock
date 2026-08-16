using Poe2DesktopClock.Core.Models;

namespace Poe2DesktopClock.Core.Interfaces;

/// <summary>
/// Общая точка входа сценариев трекера для desktop-приложения и debug-консоли.
/// </summary>
public interface IClockRuntime : IAsyncDisposable
{
    event EventHandler<ClockSnapshot>? ClockSnapshotChanged;

    event EventHandler<ClockMonitorStatus>? MonitorStatusChanged;

    TrackerSettings GetSettings();

    void SaveSettings(TrackerSettings settings);

    GameStatus GetGameStatus();

    CurrencySetupStatus GetCurrencySetupStatus();

    Task<IReadOnlyList<string>> GetPoe2LeaguesAsync(CancellationToken cancellationToken = default);

    Task SelectCurrencyRegionAsync(CancellationToken cancellationToken = default);

    Task CalibrateCurrencySlotsAsync(CancellationToken cancellationToken = default);

    Task<ClockSnapshot> RefreshAsync(bool refreshPublicTabs, IProgress<TrackerProgress>? progress = null, CancellationToken cancellationToken = default);

    Task StartCurrencyMonitoringAsync(CancellationToken cancellationToken = default);

    Task StopCurrencyMonitoringAsync();
}
