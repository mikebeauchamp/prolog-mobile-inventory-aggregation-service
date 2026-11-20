using System.Globalization;
using InventoryAggregation.Core;

namespace InventoryAggregation.Infrastructure;

// Failure messages identify the position and the rule that failed, never the offending values.
internal static class InventoryRecordValidator
{
    public static InventoryRecord Validate(InventoryRecordDto? dto, int page, int index)
    {
        var where = $"Record {index + 1} on page {page}";

        if (dto is null)
        {
            throw BadData($"{where} is null.");
        }

        if (dto.Id is not { } id)
        {
            throw BadData($"{where} has no id.");
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw BadData($"{where} (id {id}) has a blank name.");
        }

        if (string.IsNullOrWhiteSpace(dto.Category))
        {
            throw BadData($"{where} (id {id}) has a blank category.");
        }

        if (dto.Price is not { } price || price < 0)
        {
            throw BadData($"{where} (id {id}) has a missing or negative price.");
        }

        if (dto.Quantity is not { } quantity || quantity < 0)
        {
            throw BadData($"{where} (id {id}) has a missing or negative quantity.");
        }

        // The contract says lastUpdated is UTC, so a value without an offset is read as UTC.
        if (!DateTimeOffset.TryParse(
                dto.LastUpdated,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var lastUpdated))
        {
            throw BadData($"{where} (id {id}) has a missing or unreadable lastUpdated.");
        }

        return new InventoryRecord(id, dto.Name, dto.Category, price, quantity, lastUpdated);
    }

    public static InventorySourceException BadData(string message) =>
        new(InventorySourceFailureKind.BadData, message);
}
