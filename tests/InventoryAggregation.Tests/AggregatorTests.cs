using InventoryAggregation.Core;

namespace InventoryAggregation.Tests;

// AC-1 to AC-6, AC-16, AC-19: aggregation is pure and testable without HTTP.
public class AggregatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 12, 30, 0, TimeSpan.Zero);
    private readonly InventoryAggregator _aggregator = new();

    private static InventoryRecord Rec(
        long id,
        string category = "Electronics",
        decimal price = 10m,
        int quantity = 1,
        DateTimeOffset? lastUpdated = null,
        string? name = null) =>
        new(id, name ?? $"Item {id}", category, price, quantity, lastUpdated ?? T0);

    private IReadOnlyList<CategorySummary> Run(params InventoryRecord[] records) => _aggregator.Aggregate(records);

    [Fact]
    public void ExcludesZeroQuantity()
    {
        var result = Run(Rec(1, quantity: 5, price: 2m), Rec(2, quantity: 0, price: 1000m));

        var category = Assert.Single(result);
        Assert.Equal(10m, category.TotalInventoryValue);
        Assert.Equal(1, category.AvailableRecordCount);
        Assert.Equal(1, category.MostRecentlyUpdated.Id);
        Assert.Equal([1L], category.Items.Select(i => i.Id));
    }

    [Fact]
    public void AllZeroCategoryIsOmitted()
    {
        var result = Run(Rec(1, "Books", quantity: 0), Rec(2, "Toys", quantity: 3));

        var category = Assert.Single(result);
        Assert.Equal("Toys", category.Category);
    }

    [Fact]
    public void EmptyInputGivesEmptyList() => Assert.Empty(Run());

    [Fact]
    public void NullInputIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => _aggregator.Aggregate(null!));

    [Fact]
    public void GroupsIgnoringCaseAndSpaces()
    {
        var result = Run(
            Rec(1, "Electronics"),
            Rec(2, "  electronics "),
            Rec(3, "ELECTRONICS"),
            Rec(4, "Home"));

        Assert.Equal(2, result.Count);
        var electronics = Assert.Single(result, c => c.Category == "Electronics");
        Assert.Equal(3, electronics.AvailableRecordCount);
    }

    [Fact]
    public void DisplayNameFromLowestId_Trimmed()
    {
        var result = Run(
            Rec(30, "ELECTRONICS"),
            Rec(10, "  Electronics  "),
            Rec(20, "electronics"));

        Assert.Equal("Electronics", Assert.Single(result).Category);
    }

    [Fact]
    public void DisplayNameIgnoresExcludedZeroQuantityRecords()
    {
        var result = Run(
            Rec(1, "ZERO-STOCK-NAME", quantity: 0),
            Rec(2, "Real Name", quantity: 1));

        Assert.Equal("Real Name", Assert.Single(result).Category);
    }

    [Fact]
    public void SumsPriceTimesQuantity_Exact()
    {
        var result = Run(
            Rec(1, price: 149.99m, quantity: 12),
            Rec(2, price: 0.1m, quantity: 3),
            Rec(3, price: 0.2m, quantity: 3));

        Assert.Equal(1799.88m + 0.3m + 0.6m, Assert.Single(result).TotalInventoryValue);
    }

    [Fact]
    public void PriceZeroIsAvailableAndContributesNothing()
    {
        var category = Assert.Single(Run(Rec(1, price: 0m, quantity: 4), Rec(2, price: 5m, quantity: 1)));

        Assert.Equal(5m, category.TotalInventoryValue);
        Assert.Equal(2, category.AvailableRecordCount);
    }

    [Fact]
    public void RoundsOnlyFinalTotal()
    {
        // Rounding each line first would give 0.00 + 0.00; rounding the sum gives 0.01.
        var category = Assert.Single(Run(Rec(1, price: 0.004m, quantity: 1), Rec(2, price: 0.004m, quantity: 1)));

        Assert.Equal(0.01m, category.TotalInventoryValue);
    }

    [Theory]
    [InlineData("0.005", "0.01")]
    [InlineData("0.015", "0.02")]
    [InlineData("0.0049", "0.00")]
    public void FinalTotalRoundsMidpointsAwayFromZero(string total, string expected)
    {
        var category = Assert.Single(Run(Rec(1, price: decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture), quantity: 1)));

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), category.TotalInventoryValue);
    }

    [Fact]
    public void CountsOnlyAvailableRecords()
    {
        var category = Assert.Single(Run(
            Rec(1, quantity: 1), Rec(2, quantity: 0), Rec(3, quantity: 7), Rec(4, quantity: 0)));

        Assert.Equal(2, category.AvailableRecordCount);
    }

    [Fact]
    public void PicksMostRecent()
    {
        var category = Assert.Single(Run(
            Rec(1, lastUpdated: T0),
            Rec(2, lastUpdated: T0.AddDays(3)),
            Rec(3, lastUpdated: T0.AddDays(1))));

        Assert.Equal(2, category.MostRecentlyUpdated.Id);
    }

    [Fact]
    public void MostRecentIgnoresExcludedZeroQuantityRecords()
    {
        var category = Assert.Single(Run(
            Rec(1, lastUpdated: T0),
            Rec(2, quantity: 0, lastUpdated: T0.AddYears(1))));

        Assert.Equal(1, category.MostRecentlyUpdated.Id);
    }

    [Fact]
    public void TieGoesToHighestId()
    {
        var category = Assert.Single(Run(
            Rec(5, lastUpdated: T0),
            Rec(9, lastUpdated: T0),
            Rec(7, lastUpdated: T0)));

        Assert.Equal(9, category.MostRecentlyUpdated.Id);
    }

    [Fact]
    public void MostRecentComparesInstantsAcrossOffsets()
    {
        // Same instant expressed with different offsets is a tie, so the highest id wins.
        var sameInstantOtherOffset = T0.ToOffset(TimeSpan.FromHours(5));
        var category = Assert.Single(Run(
            Rec(1, lastUpdated: T0),
            Rec(2, lastUpdated: sameInstantOtherOffset)));

        Assert.Equal(2, category.MostRecentlyUpdated.Id);
    }

    [Theory]
    [InlineData("home & kitchen", "Home & Kitchen")]
    [InlineData("TOYS", "Toys")]
    [InlineData("outdoor", "Outdoor")]
    [InlineData("Electronics", "Electronics")]
    [InlineData("Office Supplies", "Office Supplies")]
    [InlineData("  tv & audio  ", "Tv & Audio")]
    [InlineData("arts and crafts", "Arts And Crafts")]
    [InlineData("hOmE gArDeN", "Home Garden")]
    [InlineData("men's wear", "Men's Wear")]
    public void DisplayNameIsTitleCased(string source, string expected)
    {
        var category = Assert.Single(Run(Rec(1, source)));

        Assert.Equal(expected, category.Category);
    }

    [Fact]
    public void TitleCasingIgnoresTheCurrentCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
        try
        {
            // Turkish lower-casing would turn "I" into a dotless "ı".
            var category = Assert.Single(Run(Rec(1, "ISLAND ITEMS")));

            Assert.Equal("Island Items", category.Category);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void CaseVariantsStillGroupTogether_UnderOneTitleCasedName()
    {
        var category = Assert.Single(Run(Rec(5, "TOYS"), Rec(2, "toys"), Rec(9, "Toys ")));

        Assert.Equal("Toys", category.Category);
        Assert.Equal(3, category.AvailableRecordCount);
    }

    [Fact]
    public void TitleCasingDoesNotChangeCategoryOrder()
    {
        var result = Run(Rec(1, "zebra"), Rec(2, "APPLE"), Rec(3, "mango"));

        Assert.Equal(["Apple", "Mango", "Zebra"], result.Select(c => c.Category));
    }

    [Fact]
    public void ItemsMatchFigures()
    {
        var category = Assert.Single(Run(
            Rec(1, price: 3m, quantity: 2),
            Rec(2, price: 4m, quantity: 0),
            Rec(3, price: 5m, quantity: 1)));

        Assert.Equal(category.Items.Sum(i => i.Price * i.Quantity), category.TotalInventoryValue);
        Assert.Equal(category.Items.Count, category.AvailableRecordCount);
        Assert.Contains(category.MostRecentlyUpdated, category.Items);
        Assert.All(category.Items, i => Assert.True(i.Quantity > 0));
    }

    [Fact]
    public void OrderingIsDeterministic()
    {
        var input = new[]
        {
            Rec(9, "zebra"), Rec(3, "Apple"), Rec(7, "apple"), Rec(1, "Mango"), Rec(2, "zebra"),
        };

        var forward = _aggregator.Aggregate(input);
        var reversed = _aggregator.Aggregate(input.Reverse());

        Assert.Equal(["Apple", "Mango", "Zebra"], forward.Select(c => c.Category));
        Assert.Equal([3L, 7L], forward[0].Items.Select(i => i.Id));
        Assert.Equal([2L, 9L], forward[2].Items.Select(i => i.Id));
        Assert.Equal(forward.Select(c => c.Category), reversed.Select(c => c.Category));
        Assert.Equal(forward.SelectMany(c => c.Items.Select(i => i.Id)), reversed.SelectMany(c => c.Items.Select(i => i.Id)));
    }

    [Fact]
    public void Core_HasNoHttpReference()
    {
        var referenced = typeof(InventoryAggregator).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(referenced, n => n.Contains("Http", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, n => n.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, n => n.StartsWith("System.Net", StringComparison.Ordinal));
    }
}
