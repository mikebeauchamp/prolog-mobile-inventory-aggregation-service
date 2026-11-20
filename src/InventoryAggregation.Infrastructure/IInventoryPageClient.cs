namespace InventoryAggregation.Infrastructure;

/// <summary>Fetches one raw page from the upstream API. Failures surface as <see cref="Core.InventorySourceException"/>.</summary>
public interface IInventoryPageClient
{
    Task<InventoryPageDto> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken);
}
