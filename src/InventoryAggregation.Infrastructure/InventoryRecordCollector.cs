using InventoryAggregation.Core;

namespace InventoryAggregation.Infrastructure;

public sealed class InventoryRecordCollector : IInventoryRecordSource
{
    public const int PageSize = 25;

    private readonly IInventoryPageClient _client;

    public InventoryRecordCollector(IInventoryPageClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<InventoryRecord>> GetAllRecordsAsync(CancellationToken cancellationToken)
    {
        var first = await FetchAsync(1, cancellationToken);
        var totalPages = first.TotalPages!.Value;
        var totalRecords = first.TotalRecords!.Value;

        var recordsById = new Dictionary<long, InventoryRecord>();
        AddRecords(recordsById, first, 1);

        // Pages are fetched one at a time and the first failure stops the whole collection (D-9, D-3).
        for (var page = 2; page <= totalPages; page++)
        {
            var next = await FetchAsync(page, cancellationToken);

            if (next.TotalPages != totalPages)
            {
                throw InventoryRecordValidator.BadData($"totalPages changed while paging (page {page}).");
            }

            if (next.TotalRecords != totalRecords)
            {
                throw InventoryRecordValidator.BadData($"totalRecords changed while paging (page {page}).");
            }

            AddRecords(recordsById, next, page);
        }

        if (recordsById.Count != totalRecords)
        {
            throw InventoryRecordValidator.BadData(
                $"Collected {recordsById.Count} distinct records but the API reported totalRecords {totalRecords}.");
        }

        return recordsById.Values.OrderBy(r => r.Id).ToList();
    }

    private async Task<InventoryPageDto> FetchAsync(int page, CancellationToken cancellationToken)
    {
        var dto = await _client.GetPageAsync(page, PageSize, cancellationToken);

        if (dto.Records is null || dto.Page is null || dto.TotalPages is not >= 0 || dto.TotalRecords is not >= 0)
        {
            throw InventoryRecordValidator.BadData($"Page {page} is missing records or has invalid paging fields.");
        }

        if (dto.Page != page)
        {
            throw InventoryRecordValidator.BadData($"Requested page {page} but the API returned a different page number.");
        }

        return dto;
    }

    private static void AddRecords(Dictionary<long, InventoryRecord> recordsById, InventoryPageDto dto, int page)
    {
        for (var index = 0; index < dto.Records!.Count; index++)
        {
            var record = InventoryRecordValidator.Validate(dto.Records[index], page, index);

            if (!recordsById.TryAdd(record.Id, record) && recordsById[record.Id] != record)
            {
                throw InventoryRecordValidator.BadData($"Conflicting duplicate records for id {record.Id}.");
            }
        }
    }
}
