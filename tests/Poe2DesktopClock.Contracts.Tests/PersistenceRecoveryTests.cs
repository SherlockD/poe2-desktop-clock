using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Infrastructure.Storage.PublicStash;
using Poe2DesktopClock.Infrastructure.Storage.Settings;
using Poe2DeskTracker.PublicStash;
using Xunit;

namespace Poe2DesktopClock.Contracts.Tests;

public sealed class PersistenceRecoveryTests
{
    [Fact]
    public void Desktop_settings_are_recovered_and_backed_up_when_json_is_corrupted()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "{ broken json");
            var fallback = TrackerSettings.Default with { AccountName = "fallback" };
            var store = new DesktopSettingsStore(path);

            var recovered = store.Get(fallback);

            Assert.Equal("fallback", recovered.AccountName);
            AssertCorruptedBackup(directory, "settings.json");

            store.Save(recovered with { AccountName = "restored" });
            Assert.Equal("restored", new DesktopSettingsStore(path).Get(fallback).AccountName);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Public_stash_settings_are_recovered_when_required_fields_are_missing()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "public-stash.json");
            File.WriteAllText(path, "{}");
            var store = new PublicStashSettingsStore(path);

            Assert.Null(store.Get());
            AssertCorruptedBackup(directory, "public-stash.json");

            store.Save(new PublicStashSettings(
                "account",
                "league",
                ["~price 1001 mirror"],
                [new PublicStashTabMarker("Test", "~price 1001 mirror", 1001m, "mirror")]));
            Assert.Equal("account", new PublicStashSettingsStore(path).Get()!.AccountName);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Public_tabs_snapshot_is_backed_up_when_json_is_corrupted()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "public-tabs-snapshot.json");
            File.WriteAllText(path, "not json");

            Assert.Null(new PublicTabsSnapshotStore(path).Get());
            AssertCorruptedBackup(directory, "public-tabs-snapshot.json");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"poe2-clock-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void AssertCorruptedBackup(string directory, string fileName)
    {
        Assert.False(File.Exists(Path.Combine(directory, fileName)));
        Assert.Single(Directory.GetFiles(directory, $"{fileName}.corrupt-*.bak"));
    }
}
