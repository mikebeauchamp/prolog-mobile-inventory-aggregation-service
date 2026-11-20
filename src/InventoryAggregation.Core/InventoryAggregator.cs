using System.Globalization;

namespace InventoryAggregation.Core;

public sealed class InventoryAggregator
{
    private const int TotalDecimalPlaces = 2;

    public IReadOnlyList<CategorySummary> Aggregate(IEnumerable<InventoryRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        return records
            .Where(r => r.Quantity > 0)
            .GroupBy(r => r.Category.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(Summarise)
            .ToList();
    }

    // P-5: lower-case first because ToTitleCase leaves all-uppercase words alone. Invariant culture keeps the result machine-independent.
    private static string ToDisplayName(string category) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(category.Trim().ToLowerInvariant());

    private static CategorySummary Summarise(IGrouping<string, InventoryRecord> group)
    {
        var items = group.OrderBy(r => r.Id).ToList();

        // Exact decimal sum; only the final total is rounded.
        var total = items.Sum(r => r.Price * r.Quantity);

        var mostRecent = items
            .OrderByDescending(r => r.LastUpdated)
            .ThenByDescending(r => r.Id)
            .First();

        return new CategorySummary(
            ToDisplayName(items[0].Category),
            Math.Round(total, TotalDecimalPlaces, MidpointRounding.AwayFromZero),
            items.Count,
            mostRecent,
            items);
    }
}
