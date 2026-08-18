namespace Poe2DesktopClock.Desktop.ViewModels;

/// <summary>
/// Presentation state for one optional public-tab source in the initial setup.
/// The application use case owns validation; this type only exposes its result
/// in a WPF-friendly shape.
/// </summary>
public sealed class InitialSetupPublicTabViewModel : ViewModelBase
{
    private bool _isIncluded = true;
    private string _status = "Ожидает синхронизации";
    private string _detail = "";
    private bool _isSynchronized;

    public InitialSetupPublicTabViewModel(
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
        set
        {
            if (!SetProperty(ref _isIncluded, value))
            {
                return;
            }

            if (!value)
            {
                SetSynchronizationResult("Исключена", "Вкладка не будет синхронизироваться.", false);
            }
            else
            {
                SetSynchronizationResult("Ожидает синхронизации", "", false);
            }
        }
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

    public void SetSynchronizationResult(string status, string detail, bool isSynchronized)
    {
        Status = status;
        Detail = detail;
        IsSynchronized = isSynchronized;
    }
}
