using System.Globalization;
using System.Resources;

namespace Poe2DesktopClock.Desktop.Localization;

/// <summary>Provides presentation text stored in the application's RESX resources.</summary>
public static class AppStrings
{
    private static readonly ResourceManager ResourceManager = new(
        "Poe2DesktopClock.Desktop.Resources.AppStrings",
        typeof(AppStrings).Assembly);

    public static string Get(string key) =>
        ResourceManager.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new MissingManifestResourceException($"UI resource '{key}' was not found.");

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);
}
