namespace Poe2DesktopClock.Desktop.ViewModels;

public sealed class PublicTabMarkerViewModel : ViewModelBase
{
    public PublicTabMarkerViewModel(string label, string requiredName)
    {
        Label = label;
        RequiredName = requiredName;
    }

    public string Label { get; }

    public string RequiredName { get; }
}
