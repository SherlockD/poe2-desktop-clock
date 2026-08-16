namespace Poe2DeskTracker.PublicStash;

/// <summary>
/// Описывает восемь публичных вкладок, которые участвуют в оценке часов.
/// Уникальная цена — техническая метка Trade API, а не цена предметов.
/// </summary>
internal static class PublicTabMarkerCatalog
{
    private static readonly string[] Labels =
    [
        "Разлом",
        "Бездна",
        "Ритуал",
        "Экспедиция",
        "Делириум",
        "Сущности",
        "Руны",
        "Фрагменты",
    ];

    internal static IReadOnlyList<PublicStashTabMarker> CreateDefaultMarkers() =>
        Labels.Select((label, index) =>
        {
            var amount = 1001m + index;
            return new PublicStashTabMarker(label, $"~price {amount:0.####} mirror", amount, "mirror");
        }).ToArray();
}
