using System.Windows.Markup;

namespace Poe2DesktopClock.Desktop.Localization;

/// <summary>Resolves an application string from the RESX catalog in XAML.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension : MarkupExtension
{
    public TextExtension(string key)
    {
        Key = key;
    }

    public string Key { get; }

    public override object ProvideValue(IServiceProvider serviceProvider) => AppStrings.Get(Key);
}
