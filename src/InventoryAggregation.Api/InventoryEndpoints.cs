using InventoryAggregation.Core;

namespace InventoryAggregation.Api;

public static class InventoryEndpoints
{
    public const string CategoriesRoute = "/api/inventory/categories";

    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(CategoriesRoute, GetCategoriesAsync);
        return app;
    }

    private static async Task<IResult> GetCategoriesAsync(
        IInventoryRecordSource source,
        InventoryAggregator aggregator,
        ILogger<InventoryAggregator> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var records = await source.GetAllRecordsAsync(cancellationToken);
            return Results.Ok(CategoriesResponse.From(Aggregate(aggregator, records)));
        }
        catch (InventorySourceException ex)
        {
            // Details stay in the server log; the caller only gets a generic problem response (D-11, P-3).
            // A rejected key needs an operator to fix it, so it is logged as an error rather than a warning (D-16).
            var level = ex.Kind == InventorySourceFailureKind.Rejected ? LogLevel.Error : LogLevel.Warning;
            logger.Log(level, ex, "Inventory source failed ({Kind}).", ex.Kind);
            return ProblemFor(ex.Kind);
        }
    }

    // decimal arithmetic throws on overflow instead of saturating, so absurdly large upstream values are treated as unusable data (D-15).
    private static IReadOnlyList<CategorySummary> Aggregate(InventoryAggregator aggregator, IReadOnlyList<InventoryRecord> records)
    {
        try
        {
            return aggregator.Aggregate(records);
        }
        catch (OverflowException ex)
        {
            throw new InventorySourceException(InventorySourceFailureKind.BadData, "Inventory values are too large to total.", ex);
        }
    }

    private static IResult ProblemFor(InventorySourceFailureKind kind) => kind switch
    {
        InventorySourceFailureKind.Unavailable => Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Upstream inventory service unavailable",
            detail: "The upstream inventory service is unavailable. Try again later."),
        InventorySourceFailureKind.Timeout => Results.Problem(
            statusCode: StatusCodes.Status504GatewayTimeout,
            title: "Upstream inventory service timed out",
            detail: "The upstream inventory service did not respond in time."),
        _ => Results.Problem(
            statusCode: StatusCodes.Status502BadGateway,
            title: "Upstream inventory service returned an unusable response",
            detail: "The upstream inventory service returned an unusable response."),
    };
}
