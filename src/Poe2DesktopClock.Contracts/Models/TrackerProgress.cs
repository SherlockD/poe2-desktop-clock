namespace Poe2DesktopClock.Contracts.Models;

/// <summary>
/// Короткое русское сообщение о выполняемой операции для UI и debug-консоли.
/// </summary>
public sealed record TrackerProgress(string RussianSummary, int? Current = null, int? Total = null);
