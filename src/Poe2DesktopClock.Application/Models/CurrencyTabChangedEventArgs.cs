namespace Poe2DesktopClock.Application.Models;

public sealed class CurrencyTabChangedEventArgs(CurrencyTabFrame frame) : EventArgs
{
    public CurrencyTabFrame Frame { get; } = frame ?? throw new ArgumentNullException(nameof(frame));
}
