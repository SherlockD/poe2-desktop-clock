namespace Poe2DesktopClock.Infrastructure.Windows.Monitoring;

/// <summary>
/// Decides when a verified Currency frame must be valued. Reopening the tab
/// always emits its first valid frame, even when its amount fingerprint equals
/// the frame observed before the tab was closed.
/// </summary>
internal sealed class CurrencyFrameObservationState
{
    private bool _isCurrencyTabVisible;
    private string? _lastFingerprint;

    internal bool ShouldPublish(bool isCurrencyTabVisible, string? fingerprint)
    {
        if (!isCurrencyTabVisible)
        {
            _isCurrencyTabVisible = false;
            _lastFingerprint = null;
            return false;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        var shouldPublish = !_isCurrencyTabVisible ||
                            !string.Equals(fingerprint, _lastFingerprint, StringComparison.Ordinal);
        _isCurrencyTabVisible = true;
        _lastFingerprint = fingerprint;
        return shouldPublish;
    }
}
