using InventoryAggregation.Core;

namespace InventoryAggregation.Api;

/// <summary>Response shape of GET /api/inventory/categories; matches the assignment's sample (D-8b).</summary>
public sealed record CategoriesResponse(IReadOnlyList<CategoryResponse> Categories)
{
    public static CategoriesResponse From(IReadOnlyList<CategorySummary> summaries) =>
        new(summaries.Select(s => new CategoryResponse(
            s.Category,
            s.TotalInventoryValue,
            s.AvailableRecordCount,
            new MostRecentResponse(s.MostRecentlyUpdated.Id, s.MostRecentlyUpdated.Name, s.MostRecentlyUpdated.LastUpdated.UtcDateTime),
            s.Items.Select(i => new ItemResponse(i.Id, i.Name, i.Price, i.Quantity, i.LastUpdated.UtcDateTime)).ToList())).ToList());
}

public sealed record CategoryResponse(
    string Category,
    decimal TotalInventoryValue,
    int AvailableRecordCount,
    MostRecentResponse MostRecentlyUpdated,
    IReadOnlyList<ItemResponse> Items);

// UtcDateTime (Kind = Utc) is written as "...Z"; a DateTimeOffset would be written as "+00:00".
public sealed record MostRecentResponse(long Id, string Name, DateTime LastUpdated);

public sealed record ItemResponse(long Id, string Name, decimal Price, int Quantity, DateTime LastUpdated);
