namespace Poe2DesktopClock.Domain.Tracking;

/// <summary>
/// Default public tabs and their unique Trade API marker prices.
/// This is product configuration shared by every UI, not presentation data.
/// </summary>
public static class PublicTabDefaults
{
    public static IReadOnlyList<PublicTabDefinition> Items { get; } =
    [
        new("Разлом", 1001m, "mirror"),
        new("Бездна", 1002m, "mirror"),
        new("Ритуал", 1003m, "mirror"),
        new("Экспедиция", 1004m, "mirror"),
        new("Делириум", 1005m, "mirror"),
        new("Сущности", 1006m, "mirror"),
        new("Руны", 1007m, "mirror"),
        new("Фрагменты", 1008m, "mirror"),
    ];
}

public sealed record PublicTabDefinition(string Label, decimal MarkerPrice, string MarkerCurrency)
{
    public string RequiredTabName => $"~price {MarkerPrice:0.####} {MarkerCurrency}";
}
