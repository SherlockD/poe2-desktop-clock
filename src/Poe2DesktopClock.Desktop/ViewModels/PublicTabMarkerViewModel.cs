namespace Poe2DesktopClock.Desktop.ViewModels;

public sealed class PublicTabMarkerViewModel : ViewModelBase
{
    private bool _isIncluded;
    private string _status = string.Empty;
    private string _detail = string.Empty;
    private bool _isSynchronized;

    public PublicTabMarkerViewModel(
        string label,
        string requiredName,
        decimal priceAmount,
        string priceCurrency,
        bool isIncluded)
    {
        Label = label;
        RequiredName = requiredName;
        PriceAmount = priceAmount;
        PriceCurrency = priceCurrency;
        _isIncluded = isIncluded;
    }

    public string Label { get; }

    public string RequiredName { get; }

    public decimal PriceAmount { get; }

    public string PriceCurrency { get; }

    public bool IsIncluded
    {
        get => _isIncluded;
        set => SetProperty(ref _isIncluded, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    public bool IsSynchronized
    {
        get => _isSynchronized;
        private set => SetProperty(ref _isSynchronized, value);
    }

    public void SetState(string status, string detail, bool isSynchronized)
    {
        Status = status;
        Detail = detail;
        IsSynchronized = isSynchronized;
    }
}
