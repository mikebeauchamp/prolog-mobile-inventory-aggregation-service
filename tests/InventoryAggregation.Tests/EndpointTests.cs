using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using InventoryAggregation.Core;
using InventoryAggregation.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InventoryAggregation.Tests;

// AC-9, AC-10, AC-11, AC-12 to AC-15: the endpoint through the real ASP.NET Core test host.
public class EndpointTests
{
    private const string Route = "/api/inventory/categories";
    private const string Leak = "secret-internal-detail";

    private static WebApplicationFactory<Program> Factory(
        Action<IServiceCollection> configure,
        string environment = "Development",
        params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = new Dictionary<string, string?> { [InventoryApiOptions.ApiKeySettingName] = ClientTests.ApiKey };
                foreach (var (key, value) in settings)
                {
                    values[key] = value;
                }

                config.AddInMemoryCollection(values);
            });
            builder.ConfigureTestServices(configure);
        });

    private static Action<IServiceCollection> Returns(params InventoryRecord[] records) =>
        services =>
        {
            services.RemoveAll<IInventoryRecordSource>();
            services.AddSingleton<IInventoryRecordSource>(new FakeSource(_ => Task.FromResult<IReadOnlyList<InventoryRecord>>(records)));
        };

    private static Action<IServiceCollection> Throws(Exception exception) =>
        services =>
        {
            services.RemoveAll<IInventoryRecordSource>();
            services.AddSingleton<IInventoryRecordSource>(new FakeSource(_ => Task.FromException<IReadOnlyList<InventoryRecord>>(exception)));
        };

    private static InventoryRecord Rec(
        long id, string category = "Electronics", string name = "Item", decimal price = 1m, int quantity = 1, string date = "2026-10-15T12:30:00Z") =>
        new(id, name, category, price, quantity, DateTimeOffset.Parse(date, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public async Task Returns200_WithSampleShape()
    {
        using var factory = Factory(Returns(Rec(1001, "Electronics", "Thermal Receipt Printer", 149.99m, 12)));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var actual = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        var expected = JsonNode.Parse(
            """
            {
              "categories": [
                {
                  "category": "Electronics",
                  "totalInventoryValue": 1799.88,
                  "availableRecordCount": 1,
                  "mostRecentlyUpdated": {
                    "id": 1001,
                    "name": "Thermal Receipt Printer",
                    "lastUpdated": "2026-10-15T12:30:00Z"
                  },
                  "items": [
                    {
                      "id": 1001,
                      "name": "Thermal Receipt Printer",
                      "price": 149.99,
                      "quantity": 12,
                      "lastUpdated": "2026-10-15T12:30:00Z"
                    }
                  ]
                }
              ]
            }
            """);
        Assert.True(JsonNode.DeepEquals(expected, actual), $"Unexpected body: {actual}");
    }

    [Fact]
    public async Task DatesAreUtcWithZ_EvenWhenTheSourceOffsetIsNotUtc()
    {
        using var factory = Factory(Returns(Rec(1, date: "2026-10-15T14:30:00+02:00")));
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync(Route);

        using var doc = JsonDocument.Parse(body);
        var category = doc.RootElement.GetProperty("categories")[0];
        Assert.Equal("2026-10-15T12:30:00Z", category.GetProperty("items")[0].GetProperty("lastUpdated").GetString());
        Assert.Equal("2026-10-15T12:30:00Z", category.GetProperty("mostRecentlyUpdated").GetProperty("lastUpdated").GetString());
    }

    [Fact]
    public async Task CategoriesAreAToZ_AndItemsAreById_AndZeroQuantityIsExcluded()
    {
        using var factory = Factory(Returns(
            Rec(9, "zebra"), Rec(3, "Apple"), Rec(7, "apple "), Rec(1, "Mango"), Rec(2, "zebra", quantity: 0)));
        using var client = factory.CreateClient();

        var node = JsonNode.Parse(await client.GetStringAsync(Route))!;

        var categories = node["categories"]!.AsArray();
        Assert.Equal(["Apple", "Mango", "Zebra"], categories.Select(c => c!["category"]!.GetValue<string>()));
        Assert.Equal([3L, 7], categories[0]!["items"]!.AsArray().Select(i => i!["id"]!.GetValue<long>()));
        Assert.Equal([9L], categories[2]!["items"]!.AsArray().Select(i => i!["id"]!.GetValue<long>()));
    }

    [Fact]
    public async Task NoInStockRecords_Returns200_WithEmptyCategories()
    {
        using var factory = Factory(Returns(Rec(1, quantity: 0)));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"categories":[]}"""), JsonNode.Parse(await response.Content.ReadAsStringAsync())));
    }

    [Theory]
    [InlineData(InventorySourceFailureKind.Unavailable, 503)]
    [InlineData(InventorySourceFailureKind.Timeout, 504)]
    [InlineData(InventorySourceFailureKind.BadData, 502)]
    [InlineData(InventorySourceFailureKind.Rejected, 502)]
    public async Task UpstreamFailure_ReturnsProblemDetails_NoInternals(InventorySourceFailureKind kind, int status)
    {
        var inner = new HttpRequestException($"{Leak} {ClientTests.ApiKey}");
        using var factory = Factory(Throws(new InventorySourceException(kind, $"{Leak} {ClientTests.ApiKey}", inner)));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Route);

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(status, doc.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("detail").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain(Leak, body);
        Assert.DoesNotContain(ClientTests.ApiKey, body);
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("stg.prologmobile.com", body);
    }

    [Fact]
    public async Task RejectedKey_LooksTheSameAsUnusableData_ToTheCaller()
    {
        async Task<string> DetailFor(InventorySourceFailureKind kind)
        {
            using var factory = Factory(Throws(new InventorySourceException(kind, "x")));
            using var client = factory.CreateClient();
            using var response = await client.GetAsync(Route);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("detail").GetString()!;
        }

        Assert.Equal(await DetailFor(InventorySourceFailureKind.BadData), await DetailFor(InventorySourceFailureKind.Rejected));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task UnexpectedException_Returns500_GenericProblemDetails(string environment)
    {
        using var factory = Factory(Throws(new InvalidOperationException(Leak)), environment);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(500, doc.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain(Leak, body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("   at ", body);
    }

    [Fact]
    public async Task ThroughRealClientAndCache_SecondRequestIsServedFromCache()
    {
        var handler = new ScriptedHandler((request, _) =>
        {
            var page = int.Parse(System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["page"]!);
            return Steps.Ok(
                $$"""{"records":[{"id":{{page}},"name":"P{{page}}","category":"Books","price":2.5,"quantity":4,"lastUpdated":"2026-10-15T12:30:00Z"}],"page":{{page}},"pageSize":25,"totalPages":2,"totalRecords":2}""")(request, CancellationToken.None);
        });
        using var factory = Factory(services => services.AddHttpClient<IInventoryPageClient, InventoryApiClient>()
            .ConfigurePrimaryHttpMessageHandler(() => handler));
        using var client = factory.CreateClient();

        var first = await client.GetStringAsync(Route);
        var second = await client.GetStringAsync(Route);

        Assert.Equal(first, second);
        Assert.Equal(2, handler.Calls);
        using var doc = JsonDocument.Parse(first);
        var category = doc.RootElement.GetProperty("categories")[0];
        Assert.Equal("Books", category.GetProperty("category").GetString());
        Assert.Equal(20m, category.GetProperty("totalInventoryValue").GetDecimal());
        Assert.Equal(2, category.GetProperty("availableRecordCount").GetInt32());
    }

    [Theory]
    [InlineData(500, 503)]
    [InlineData(504, 504)]
    [InlineData(401, 502)]
    [InlineData(404, 502)]
    public async Task UpstreamErrorStatus_BecomesSensibleResponse_AndIsNotCached(int upstreamStatus, int expected)
    {
        var handler = new ScriptedHandler(Steps.Status((HttpStatusCode)upstreamStatus));
        using var factory = Factory(
            services => services.AddHttpClient<IInventoryPageClient, InventoryApiClient>()
                .ConfigurePrimaryHttpMessageHandler(() => handler),
            "Development",
            ("InventoryApi:Resilience:RetryBaseDelay", "00:00:00"));
        using var client = factory.CreateClient();

        using var first = await client.GetAsync(Route);
        var callsAfterFirst = handler.Calls;
        using var second = await client.GetAsync(Route);

        Assert.Equal(expected, (int)first.StatusCode);
        Assert.Equal(expected, (int)second.StatusCode);
        Assert.True(handler.Calls > callsAfterFirst, "a failure must not be cached");
        var body = await first.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ClientTests.ApiKey, body);
        Assert.DoesNotContain("stg.prologmobile.com", body);
    }

    [Fact]
    public async Task UnreachableUpstream_Returns503()
    {
        var handler = new ScriptedHandler(Steps.Throw(new HttpRequestException("connection refused")));
        using var factory = Factory(
            services => services.AddHttpClient<IInventoryPageClient, InventoryApiClient>()
                .ConfigurePrimaryHttpMessageHandler(() => handler),
            "Development",
            ("InventoryApi:Resilience:RetryBaseDelay", "00:00:00"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public void ServicesResolve_ThroughTheContainer()
    {
        using var factory = Factory(_ => { });
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        Assert.IsType<CachingInventoryRecordSource>(services.GetRequiredService<IInventoryRecordSource>());
        Assert.NotNull(services.GetRequiredService<InventoryAggregator>());
        Assert.IsType<InventoryApiClient>(services.GetRequiredService<IInventoryPageClient>());
        Assert.NotNull(services.GetRequiredService<IHttpClientFactory>());
    }
}
