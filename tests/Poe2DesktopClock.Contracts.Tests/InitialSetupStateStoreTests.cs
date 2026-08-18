using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Infrastructure.Storage.Onboarding;
using Xunit;

namespace Poe2DesktopClock.Contracts.Tests;

public sealed class InitialSetupStateStoreTests
{
    [Fact]
    public void Missing_state_starts_the_initial_setup_at_the_currency_step()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var state = new InitialSetupStateStore(Path.Combine(directory, "onboarding.json")).Get();

            Assert.Equal(InitialSetupState.NotStarted, state);
            Assert.False(state.IsCompleted);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void State_is_persisted_atomically_and_restored()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "onboarding.json");
            var expected = new InitialSetupState(
                InitialSetupState.CurrentSchemaVersion,
                InitialSetupState.CurrentSetupVersion,
                InitialSetupStep.DeviceConnection);

            new InitialSetupStateStore(path).Save(expected);

            var restored = new InitialSetupStateStore(path).Get();
            Assert.Equal(expected, restored);
            Assert.True(restored.IsCompleted);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Corrupted_state_is_backed_up_and_recovered_as_not_started()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "onboarding.json");
            File.WriteAllText(path, "{ broken json");

            var recovered = new InitialSetupStateStore(path).Get();

            Assert.Equal(InitialSetupState.NotStarted, recovered);
            Assert.False(File.Exists(path));
            Assert.Single(Directory.GetFiles(directory, "onboarding.json.corrupt-*.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Invalid_state_is_backed_up_and_recovered_as_not_started()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "onboarding.json");
            File.WriteAllText(path, "{} ");

            var recovered = new InitialSetupStateStore(path).Get();

            Assert.Equal(InitialSetupState.NotStarted, recovered);
            Assert.False(File.Exists(path));
            Assert.Single(Directory.GetFiles(directory, "onboarding.json.corrupt-*.bak"));
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
}
