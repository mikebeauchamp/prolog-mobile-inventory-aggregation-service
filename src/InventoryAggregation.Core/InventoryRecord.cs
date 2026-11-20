namespace InventoryAggregation.Core;

/// <summary>One inventory record as supplied by the source, already validated.</summary>
public sealed record InventoryRecord(
    long Id,
    string Name,
    string Category,
    decimal Price,
    int Quantity,
    DateTimeOffset LastUpdated);
