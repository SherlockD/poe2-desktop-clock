using System.IO;
using Poe2DeskTracker.Currency;
using Poe2DeskTracker.Regions;
using Xunit;

namespace Poe2DesktopClock.Infrastructure.Windows.Tests;

public sealed class PersistenceRecoveryTests
{
    [Fact]
    public void Region_store_recovers_corrupted_json_and_accepts_new_calibration()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "regions.json");
            File.WriteAllText(path, "[{ broken json");
            var store = new RegionStore(path);

            Assert.Empty(store.GetAll());
            AssertCorruptedBackup(directory, "regions.json");

            store.Upsert(new RegionDefinition("currency", 0.1, 0.2, 0.7, 0.6, 1920, 1080));
            Assert.Single(new RegionStore(path).GetAll());
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Currency_layout_store_recovers_json_with_missing_slots()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "currency-layouts.json");
            File.WriteAllText(path, """
                {
                  "currency": {
                    "RegionName": "currency",
                    "ReferenceWidth": 1200,
                    "ReferenceHeight": 800
                  }
                }
                """);
            var store = new CurrencyLayoutStore(path);

            Assert.Null(store.Get("currency"));
            AssertCorruptedBackup(directory, "currency-layouts.json");

            store.Upsert(new CurrencyLayout(
                "currency",
                1200,
                800,
                [new CurrencySlotDefinition("slot-1", 0.1, 0.1, 0.05, 0.05, 1d)]));
            Assert.NotNull(new CurrencyLayoutStore(path).Get("currency"));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
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
