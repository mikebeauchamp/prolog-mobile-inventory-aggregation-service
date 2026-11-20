using InventoryAggregation.Core;
using InventoryAggregation.Infrastructure;

namespace InventoryAggregation.Tests;

// AC-7, AC-14, AC-18, AC-26: the collector gathers the complete dataset and does not trust a 200.
public class CollectorTests
{
    private static InventoryRecordDto Dto(
        long? id = 1,
        string? name = "Item",
        string? category = "Electronics",
        decimal? price = 10m,
        int? quantity = 1,
        string? lastUpdated = "2026-10-15T12:30:00Z") =>
        new(id, name, category, price, quantity, lastUpdated);

    private static InventoryPageDto Page(int page, int? totalPages, int? totalRecords, params InventoryRecordDto?[] records) =>
        new(records, page, InventoryRecordCollector.PageSize, totalPages, totalRecords);

    private static InventoryRecordDto[] Dtos(int firstId, int count) =>
        Enumerable.Range(firstId, count).Select(id => Dto(id)).ToArray();

    // A consistent dataset of ids 1..totalRecords, split into pages of PageSize.
    private static FakePageClient Dataset(int totalRecords)
    {
        var totalPages = totalRecords == 0 ? 0 : (totalRecords - 1) / InventoryRecordCollector.PageSize + 1;
        var pages = Enumerable.Range(1, Math.Max(totalPages, 1)).Select(page =>
            Page(page, totalPages, totalRecords, Dtos((page - 1) * InventoryRecordCollector.PageSize + 1,
                Math.Max(0, Math.Min(InventoryRecordCollector.PageSize, totalRecords - (page - 1) * InventoryRecordCollector.PageSize)))));
        return new FakePageClient(pages.ToArray());
    }

    private static async Task<InventorySourceException> AssertBadData(Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<InventorySourceException>(act);
        Assert.Equal(InventorySourceFailureKind.BadData, ex.Kind);
        return ex;
    }

    private static Task<IReadOnlyList<InventoryRecord>> Collect(FakePageClient client) =>
        new InventoryRecordCollector(client).GetAllRecordsAsync(CancellationToken.None);

    [Fact]
    public async Task FetchesAllPages_InOrder()
    {
        var client = Dataset(51);

        var result = await Collect(client);

        Assert.Equal([1, 2, 3], client.RequestedPages);
        Assert.All(client.RequestedPageSizes, size => Assert.Equal(25, size));
        Assert.Equal(Enumerable.Range(1, 51).Select(i => (long)i), result.Select(r => r.Id));
    }

    [Fact]
    public async Task FetchesPagesOneAtATime()
    {
        var client = Dataset(51);

        await Collect(client);

        Assert.Equal(1, client.MaxConcurrentCalls);
    }

    [Fact]
    public async Task ForwardsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var client = new FakePageClient(Page(1, 1, 1, Dto(1)));

        await new InventoryRecordCollector(client).GetAllRecordsAsync(cts.Token);

        Assert.Equal(cts.Token, client.LastToken);
    }

    [Fact]
    public async Task MapsRecordFields()
    {
        var client = new FakePageClient(Page(1, 1, 1,
            Dto(7, "  Printer ", " Electronics ", 149.99m, 12, "2026-10-15T12:30:00Z")));

        var record = Assert.Single(await Collect(client));

        Assert.Equal(7, record.Id);
        Assert.Equal("  Printer ", record.Name);
        Assert.Equal(" Electronics ", record.Category);
        Assert.Equal(149.99m, record.Price);
        Assert.Equal(12, record.Quantity);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 12, 30, 0, TimeSpan.Zero), record.LastUpdated);
    }

    [Theory]
    [InlineData("2026-10-15T12:30:00", 12)]
    [InlineData("2026-10-15T12:30:00Z", 12)]
    [InlineData("2026-10-15T14:30:00+02:00", 12)]
    public async Task LastUpdatedIsNormalisedToUtc(string raw, int expectedUtcHour)
    {
        var client = new FakePageClient(Page(1, 1, 1, Dto(1, lastUpdated: raw)));

        var record = Assert.Single(await Collect(client));

        Assert.Equal(TimeSpan.Zero, record.LastUpdated.Offset);
        Assert.Equal(expectedUtcHour, record.LastUpdated.Hour);
    }

    [Fact]
    public async Task ZeroPriceAndZeroQuantityAreValid()
    {
        var client = new FakePageClient(Page(1, 1, 2, Dto(1, price: 0m), Dto(2, quantity: 0)));

        var result = await Collect(client);

        Assert.Equal(2, result.Count);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    public async Task EmptyDatasetGivesEmptyList(int totalPages, int totalRecords)
    {
        var client = new FakePageClient(Page(1, totalPages, totalRecords));

        var result = await Collect(client);

        Assert.Empty(result);
        Assert.Equal([1], client.RequestedPages);
    }

    [Fact]
    public async Task IdenticalDuplicateIsDropped()
    {
        var client = new FakePageClient(
            Page(1, 2, 26, Dtos(1, 25)),
            Page(2, 2, 26, Dto(25), Dto(26)));

        var result = await Collect(client);

        Assert.Equal(Enumerable.Range(1, 26).Select(i => (long)i), result.Select(r => r.Id));
    }

    [Fact]
    public async Task IdenticalDuplicateWithinOnePageIsDropped()
    {
        var client = new FakePageClient(Page(1, 1, 2, Dto(1), Dto(1), Dto(2)));

        var result = await Collect(client);

        Assert.Equal([1L, 2], result.Select(r => r.Id));
    }

    [Fact]
    public async Task DuplicatesCountOnceTowardsTotalRecords()
    {
        // totalRecords (27) includes the duplicate, so after dropping it only 26 distinct records remain.
        var client = new FakePageClient(
            Page(1, 2, 27, Dtos(1, 25)),
            Page(2, 2, 27, Dto(25), Dto(26)));

        var ex = await AssertBadData(() => Collect(client));

        Assert.Contains("distinct", ex.Message);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("category")]
    [InlineData("price")]
    [InlineData("quantity")]
    [InlineData("lastUpdated")]
    public async Task ConflictingDuplicateFails(string differingField)
    {
        var other = differingField switch
        {
            "name" => Dto(1, name: "Other"),
            "category" => Dto(1, category: "Other"),
            "price" => Dto(1, price: 99m),
            "quantity" => Dto(1, quantity: 99),
            _ => Dto(1, lastUpdated: "2027-01-01T00:00:00Z"),
        };
        var client = new FakePageClient(Page(1, 2, 26, Dtos(1, 25)), Page(2, 2, 26, other));

        var ex = await AssertBadData(() => Collect(client));

        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(26)]
    [InlineData(50)]
    public async Task TotalRecordsMismatchFails(int reportedTotal)
    {
        // 30 distinct records arrive; both reported totals still need 2 pages, so only the final count check can reject them.
        var client = new FakePageClient(
            Page(1, 2, reportedTotal, Dtos(1, 25)),
            Page(2, 2, reportedTotal, Dtos(26, 5)));

        var ex = await AssertBadData(() => Collect(client));

        Assert.Contains("distinct", ex.Message);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 25)]
    [InlineData(2, 26)]
    [InlineData(2, 50)]
    [InlineData(3, 51)]
    public async Task TotalPagesMatchingTotalRecordsIsAccepted(int totalPages, int totalRecords)
    {
        var client = Dataset(totalRecords);

        var result = await Collect(client);

        Assert.Equal(totalRecords, result.Count);
        Assert.Equal(Enumerable.Range(1, totalPages), client.RequestedPages);
    }

    [Theory]
    [InlineData(4, 3)]
    [InlineData(2, 25)]
    [InlineData(1, 26)]
    [InlineData(5000, 1)]
    [InlineData(2, 0)]
    [InlineData(0, 1)]
    public async Task TotalPagesInconsistentWithTotalRecordsFails_BeforeFetchingMore(int totalPages, int totalRecords)
    {
        var client = new FakePageClient(Page(1, totalPages, totalRecords, Dtos(1, Math.Min(totalRecords, 25))));

        var ex = await AssertBadData(() => Collect(client));

        Assert.Contains("totalPages", ex.Message);
        Assert.Equal([1], client.RequestedPages);
    }

    [Fact]
    public async Task PageHoldingAnExtraDuplicateRecord_IsAccepted()
    {
        // The live API returns 26 records on some pages: one is an identical repeat of a record from an earlier page.
        // 50 distinct records need 2 pages, so the totals are consistent once the repeat is dropped.
        var client = new FakePageClient(
            Page(1, 2, 50, Dtos(1, 25)),
            Page(2, 2, 50, Dtos(25, 26)));

        var result = await Collect(client);

        Assert.Equal(Enumerable.Range(1, 50).Select(i => (long)i), result.Select(r => r.Id));
        Assert.Equal([1, 2], client.RequestedPages);
    }

    [Fact]
    public async Task MissingPageFails()
    {
        // The API reports 3 pages (51 records), but the middle page comes back empty.
        var client = new FakePageClient(
            Page(1, 3, 51, Dtos(1, 25)),
            Page(2, 3, 51),
            Page(3, 3, 51, Dto(51)));

        var ex = await AssertBadData(() => Collect(client));

        Assert.Contains("distinct", ex.Message);
    }

    [Fact]
    public async Task TotalPagesChangeFails()
    {
        var client = new FakePageClient(
            Page(1, 2, 26, Dtos(1, 25)),
            Page(2, 3, 26, Dto(26)));

        var ex = await AssertBadData(() => Collect(client));

        Assert.Contains("totalPages changed", ex.Message);
    }

    [Fact]
    public async Task TotalRecordsChangeFails()
    {
        var client = new FakePageClient(
            Page(1, 2, 26, Dtos(1, 25)),
            Page(2, 2, 27, Dto(26)));

        var ex = await AssertBadData(() => Collect(client));

        Assert.Contains("totalRecords changed", ex.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PageEchoMismatchFails(int mismatchedPage)
    {
        var client = new FakePageClient(
            Page(1, 2, 26, Dtos(1, 25)),
            Page(2, 2, 26, Dto(26)));
        client.Override(mismatchedPage, p => p with { Page = 7 });

        var ex = await AssertBadData(() => Collect(client));

        Assert.Contains("different page number", ex.Message);
    }

    [Theory]
    [MemberData(nameof(BrokenEnvelopes))]
    public async Task InvalidEnvelopeFails(Func<InventoryPageDto, InventoryPageDto> breakIt)
    {
        var client = new FakePageClient(Page(1, 1, 1, Dto(1)));
        client.Override(1, breakIt);

        await AssertBadData(() => Collect(client));
    }

    public static TheoryData<Func<InventoryPageDto, InventoryPageDto>> BrokenEnvelopes() => new()
    {
        p => p with { Records = null },
        p => p with { Page = null },
        p => p with { TotalPages = null },
        p => p with { TotalRecords = null },
        p => p with { TotalPages = -1 },
        p => p with { TotalRecords = -1 },
    };

    [Theory]
    [MemberData(nameof(InvalidRecords))]
    public async Task InvalidRecordFails(InventoryRecordDto? bad)
    {
        var client = new FakePageClient(Page(1, 1, 2, Dto(1), bad));

        await AssertBadData(() => Collect(client));
    }

    public static TheoryData<InventoryRecordDto?> InvalidRecords() => new()
    {
        null,
        Dto(id: null, name: "Item"),
        Dto(2, name: null),
        Dto(2, name: ""),
        Dto(2, name: "   "),
        Dto(2, category: null),
        Dto(2, category: ""),
        Dto(2, category: "   "),
        Dto(2, price: null),
        Dto(2, price: -0.01m),
        Dto(2, quantity: null),
        Dto(2, quantity: -1),
        Dto(2, lastUpdated: null),
        Dto(2, lastUpdated: ""),
        Dto(2, lastUpdated: "not a date"),
    };

    [Fact]
    public async Task InvalidRecordMessageDoesNotEchoValues()
    {
        var client = new FakePageClient(Page(1, 1, 1, Dto(5, name: "secret-product-name", price: -1m)));

        var ex = await AssertBadData(() => Collect(client));

        Assert.DoesNotContain("secret-product-name", ex.Message);
    }

    [Fact]
    public async Task StopsAtFirstFailedPage()
    {
        var client = new FakePageClient(Page(1, 4, 76, Dtos(1, 25)));
        client.FailOn(2, new InventorySourceException(InventorySourceFailureKind.Unavailable, "boom"));

        var ex = await Assert.ThrowsAsync<InventorySourceException>(() => Collect(client));

        Assert.Equal(InventorySourceFailureKind.Unavailable, ex.Kind);
        Assert.Equal([1, 2], client.RequestedPages);
    }

    [Fact]
    public async Task InvalidRecordOnFirstPageStopsBeforeFetchingMore()
    {
        var client = new FakePageClient(Page(1, 3, 51, Dto(1, name: "")));

        var ex = await AssertBadData(() => Collect(client));

        Assert.Contains("blank name", ex.Message);
        Assert.Equal([1], client.RequestedPages);
    }
}

internal sealed class FakePageClient : IInventoryPageClient
{
    private readonly Dictionary<int, InventoryPageDto> _pages;
    private readonly Dictionary<int, Exception> _failures = [];
    private int _concurrent;

    public FakePageClient(params InventoryPageDto[] pages)
    {
        _pages = pages.ToDictionary(p => p.Page!.Value);
    }

    public List<int> RequestedPages { get; } = [];

    public List<int> RequestedPageSizes { get; } = [];

    public int MaxConcurrentCalls { get; private set; }

    public CancellationToken LastToken { get; private set; }

    public void Override(int page, Func<InventoryPageDto, InventoryPageDto> change) =>
        _pages[page] = change(_pages[page]);

    public void FailOn(int page, Exception exception) => _failures[page] = exception;

    public async Task<InventoryPageDto> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        RequestedPages.Add(page);
        RequestedPageSizes.Add(pageSize);
        LastToken = cancellationToken;
        MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, Interlocked.Increment(ref _concurrent));
        try
        {
            await Task.Yield();
            if (_failures.TryGetValue(page, out var failure))
            {
                throw failure;
            }

            return _pages.TryGetValue(page, out var dto)
                ? dto
                : throw new InvalidOperationException($"Test did not set up page {page}.");
        }
        finally
        {
            Interlocked.Decrement(ref _concurrent);
        }
    }
}
