internal sealed record InventoryValuePart(
    decimal TotalDivines,
    int UnpricedItemTypes,
    int UnreadableItemTypes,
    bool IsComplete = true);
