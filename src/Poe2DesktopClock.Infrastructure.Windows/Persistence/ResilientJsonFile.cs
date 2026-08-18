using System.Text.Json;

namespace Poe2DesktopClock.Infrastructure.Windows.Persistence;

internal static class ResilientJsonFile
{
    internal static T? ReadOrBackupCorrupted<T>(
        string path,
        JsonSerializerOptions options,
        Func<T, bool>? isValid = null)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), options);
            if (value is not null && (isValid?.Invoke(value) ?? true))
            {
                return value;
            }
        }
        catch (JsonException)
        {
        }
        catch (NotSupportedException)
        {
        }

        TryBackupCorruptedFile(path);
        return null;
    }

    internal static void WriteAtomically<T>(string path, T value, JsonSerializerOptions options)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Не удалось определить папку JSON-файла.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, options));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryBackupCorruptedFile(string path)
    {
        var backupPath = $"{path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak";
        try
        {
            File.Move(path, backupPath);
        }
        catch (IOException)
        {
            // Recovery must not prevent application startup if backup is unavailable.
        }
        catch (UnauthorizedAccessException)
        {
            // Recovery must not prevent application startup if backup is unavailable.
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
