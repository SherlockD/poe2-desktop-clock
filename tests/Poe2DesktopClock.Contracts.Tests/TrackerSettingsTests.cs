using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Domain.Tracking;
using Xunit;

namespace Poe2DesktopClock.Contracts.Tests;

public sealed class TrackerSettingsTests
{
    [Fact]
    public void Normalize_trims_values_and_clamps_invalid_intervals()
    {
        var settings = new TrackerSettings(
            "  account#1  ",
            "  League  ",
            CurrencyScreensPerSecond: 99,
            IsCurrencyMonitoringEnabled: true,
            IsAutomaticPublicRefreshEnabled: true,
            PublicRefreshIntervalMinutes: 1,
            PriceRefreshIntervalMinutes: 0,
            StartMinimized: false);

        var normalized = settings.Normalize();

        Assert.Equal("account#1", normalized.AccountName);
        Assert.Equal("League", normalized.League);
        Assert.Equal(2, normalized.CurrencyScreensPerSecond);
        Assert.True(normalized.IsAutomaticPublicRefreshEnabled);
        Assert.Equal(2, normalized.PublicRefreshIntervalMinutes);
        Assert.Equal(30, normalized.PriceRefreshIntervalMinutes);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Normalize_preserves_supported_capture_rate(int captureRate)
    {
        var normalized = TrackerSettings.Default with { CurrencyScreensPerSecond = captureRate };

        Assert.Equal(captureRate, normalized.Normalize().CurrencyScreensPerSecond);
    }

    [Fact]
    public void Default_public_tabs_have_stable_unique_marker_names()
    {
        var tabs = PublicTabDefaults.Items;

        Assert.Equal(8, tabs.Count);
        Assert.Equal(tabs.Count, tabs.Select(tab => tab.RequiredTabName).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("~price 1001 mirror", tabs[0].RequiredTabName);
        Assert.Equal("~price 1008 mirror", tabs[^1].RequiredTabName);
    }
}
