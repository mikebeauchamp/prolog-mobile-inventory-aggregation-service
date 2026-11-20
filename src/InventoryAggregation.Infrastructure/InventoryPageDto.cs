namespace InventoryAggregation.Infrastructure;

/// <summary>One upstream page exactly as received. Every field is nullable so that missing data is detected by validation, not by the serializer.</summary>
public sealed record InventoryPageDto(
    IReadOnlyList<InventoryRecordDto?>? Records,
    int? Page,
    int? PageSize,
    int? TotalPages,
    int? TotalRecords);

public sealed record InventoryRecordDto(
    long? Id,
    string? Name,
    string? Category,
    decimal? Price,
    int? Quantity,
    string? LastUpdated);
