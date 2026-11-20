namespace InventoryAggregation.Core;

/// <summary>Figures for one category, computed only from <see cref="Items"/>.</summary>
public sealed record CategorySummary(
    string Category,
    decimal TotalInventoryValue,
    int AvailableRecordCount,
    InventoryRecord MostRecentlyUpdated,
    IReadOnlyList<InventoryRecord> Items);
