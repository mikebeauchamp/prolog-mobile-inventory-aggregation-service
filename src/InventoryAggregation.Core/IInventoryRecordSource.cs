namespace InventoryAggregation.Core;

/// <summary>
/// Supplies the complete, validated inventory dataset. Implementations either return every
/// record or fail; they never return a partial dataset.
/// </summary>
public interface IInventoryRecordSource
{
    Task<IReadOnlyList<InventoryRecord>> GetAllRecordsAsync(CancellationToken cancellationToken);
}
