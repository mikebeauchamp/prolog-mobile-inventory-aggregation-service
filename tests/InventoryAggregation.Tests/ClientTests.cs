using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using InventoryAggregation.Core;
using InventoryAggregation.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Polly;

namespace InventoryAggregation.Tests;

// AC-13, AC-14, AC-18, AC-20: the real typed client and resilience pipeline against a scripted fake handler.
public class ClientTests
{
    internal const string ApiKey = "test-api-key-value";

    private const string ValidBody =
        """{"records":[{"id":1001,"name":"Thermal Receipt Printer","category":"Electronics","price":149.99,"quantity":12,"lastUpdated":"2026-10-15T12:30:00Z"}],"page":2,"pageSize":25,"totalPages":8,"totalRecords":157}""";

    private static async Task<InventorySourceException> Fails(Task task)
    {
        var ex = await Assert.ThrowsAsync<InventorySourceException>(() => task);
        Assert.DoesNotContain(ApiKey, ex.ToString());
        return ex;
    }

    [Fact]
    public async Task ParsesPage_AndSendsKeyOnce()
    {
        using var h = ClientHarness.Create(Steps.Ok(ValidBody));

        var dto = await h.Client.GetPageAsync(2, 25, CancellationToken.None);

        Assert.Equal(2, dto.Page);
        Assert.Equal(25, dto.PageSize);
        Assert.Equal(8, dto.TotalPages);
        Assert.Equal(157, dto.TotalRecords);
        var record = Assert.Single(dto.Records!)!;
        Assert.Equal(1001, record.Id);
        Assert.Equal("Thermal Receipt Printer", record.Name);
        Assert.Equal("Electronics", record.Category);
        Assert.Equal(149.99m, record.Price);
        Assert.Equal(12, record.Quantity);
        Assert.Equal("2026-10-15T12:30:00Z", record.LastUpdated);

        var request = Assert.Single(h.Handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://upstream.test/api/records?page=2&pageSize=25", request.Uri);
        Assert.Equal([ApiKey], request.KeyValues);
    }

    [Fact]
    public async Task BaseUrlWithPathKeepsItsPath()
    {
        using var h = ClientHarness.Create(Steps.Ok(ValidBody), ("InventoryApi:BaseUrl", "https://upstream.test/v1"));

        await h.Client.GetPageAsync(1, 25, CancellationToken.None);

        Assert.Equal("https://upstream.test/v1/api/records?page=1&pageSize=25", Assert.Single(h.Handler.Requests).Uri);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task TransientStatus_ThenSuccess_IsRetried(HttpStatusCode status)
    {
        using var h = ClientHarness.Create(Steps.Status(status), Steps.Ok(ValidBody));

        var dto = await h.Client.GetPageAsync(2, 25, CancellationToken.None);

        Assert.Equal(157, dto.TotalRecords);
        Assert.Equal(2, h.Handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, InventorySourceFailureKind.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, InventorySourceFailureKind.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, InventorySourceFailureKind.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, InventorySourceFailureKind.Unavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout, InventorySourceFailureKind.Timeout)]
    [InlineData(HttpStatusCode.RequestTimeout, InventorySourceFailureKind.Timeout)]
    public async Task RetriesExhausted_MapsToFailureKind(HttpStatusCode status, InventorySourceFailureKind expected)
    {
        using var h = ClientHarness.Create(Steps.Status(status));

        var ex = await Fails(h.Client.GetPageAsync(1, 25, CancellationToken.None));

        Assert.Equal(expected, ex.Kind);
        Assert.Equal(4, h.Handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, InventorySourceFailureKind.BadData)]
    [InlineData(HttpStatusCode.NotFound, InventorySourceFailureKind.BadData)]
    [InlineData(HttpStatusCode.Found, InventorySourceFailureKind.BadData)]
    [InlineData(HttpStatusCode.Unauthorized, InventorySourceFailureKind.Rejected)]
    [InlineData(HttpStatusCode.Forbidden, InventorySourceFailureKind.Rejected)]
    public async Task NonRetryableStatus_IsNotRetried(HttpStatusCode status, InventorySourceFailureKind expected)
    {
        using var h = ClientHarness.Create(Steps.Status(status));

        var ex = await Fails(h.Client.GetPageAsync(1, 25, CancellationToken.None));

        Assert.Equal(expected, ex.Kind);
        Assert.Equal(1, h.Handler.Calls);
    }

    [Fact(Timeout = 20000)]
    public async Task RetryAfter_IsHonoured()
    {
        using var h = ClientHarness.Create(Steps.Status(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(5)), Steps.Ok(ValidBody));

        var task = h.Client.GetPageAsync(2, 25, CancellationToken.None);
        await h.SettleAsync();
        Assert.Equal(1, h.Handler.Calls);

        h.Time.Advance(TimeSpan.FromSeconds(4));
        await h.SettleAsync();
        Assert.Equal(1, h.Handler.Calls);

        h.Time.Advance(TimeSpan.FromSeconds(1));
        var dto = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, h.Handler.Calls);
        Assert.Equal(157, dto.TotalRecords);
    }

    [Fact]
    public async Task TransportError_ThenSuccess_IsRetried()
    {
        using var h = ClientHarness.Create(Steps.Throw(new HttpRequestException("connection reset")), Steps.Ok(ValidBody));

        await h.Client.GetPageAsync(2, 25, CancellationToken.None);

        Assert.Equal(2, h.Handler.Calls);
    }

    [Fact]
    public async Task TransportError_Persistent_BecomesUnavailable()
    {
        using var h = ClientHarness.Create(Steps.Throw(new HttpRequestException("connection reset")));

        var ex = await Fails(h.Client.GetPageAsync(1, 25, CancellationToken.None));

        Assert.Equal(InventorySourceFailureKind.Unavailable, ex.Kind);
        Assert.IsType<HttpRequestException>(ex.InnerException);
        Assert.Equal(4, h.Handler.Calls);
    }

    [Fact(Timeout = 20000)]
    public async Task Timeout_IsRetriedThenBecomesTimeout()
    {
        using var h = ClientHarness.Create(Steps.Hang());

        var ex = await Fails(h.DriveAsync(h.Client.GetPageAsync(1, 25, CancellationToken.None)));

        Assert.Equal(InventorySourceFailureKind.Timeout, ex.Kind);
        Assert.True(h.Handler.Calls > 1, "a timed-out attempt should be retried");
    }

    [Fact]
    public async Task HttpClientOwnTimeout_BecomesTimeout()
    {
        // HttpClient.Timeout surfaces as a TaskCanceledException that the caller did not request.
        using var h = ClientHarness.Create(
            Steps.Throw(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException())));

        var ex = await Fails(h.Client.GetPageAsync(1, 25, CancellationToken.None));

        Assert.Equal(InventorySourceFailureKind.Timeout, ex.Kind);
        Assert.IsType<TaskCanceledException>(ex.InnerException);
    }

    [Fact(Timeout = 20000)]
    public async Task Cancellation_Propagates_AndIsNotRetried()
    {
        using var h = ClientHarness.Create(Steps.Hang());
        using var cts = new CancellationTokenSource();

        var task = h.Client.GetPageAsync(1, 25, cts.Token);
        await h.SettleAsync();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1, h.Handler.Calls);
    }

    [Fact]
    public async Task AlreadyCancelledToken_Propagates()
    {
        using var h = ClientHarness.Create(Steps.Ok(ValidBody));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Client.GetPageAsync(1, 25, cts.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"records":"not an array"}""")]
    [InlineData("""{"records":[{"id":"abc"}]}""")]
    [InlineData("""{"records":[{"quantity":1.5}]}""")]
    public async Task UnreadableBody_BecomesBadData_AndIsNotRetried(string body)
    {
        using var h = ClientHarness.Create(Steps.Ok(body));

        var ex = await Fails(h.Client.GetPageAsync(1, 25, CancellationToken.None));

        Assert.Equal(InventorySourceFailureKind.BadData, ex.Kind);
        Assert.Equal(1, h.Handler.Calls);
    }

    [Fact]
    public async Task NonJsonContentType_BecomesBadData()
    {
        using var h = ClientHarness.Create(Steps.Ok("<html>maintenance</html>", "text/html"));

        var ex = await Fails(h.Client.GetPageAsync(1, 25, CancellationToken.None));

        Assert.Equal(InventorySourceFailureKind.BadData, ex.Kind);
    }

    [Fact]
    public async Task MissingFields_AreLeftNull_ForTheCollectorToReject()
    {
        using var h = ClientHarness.Create(Steps.Ok("{}"));

        var dto = await h.Client.GetPageAsync(1, 25, CancellationToken.None);

        Assert.Null(dto.Records);
        Assert.Null(dto.Page);
        Assert.Null(dto.TotalPages);
        Assert.Null(dto.TotalRecords);
    }

    [Fact(Timeout = 20000)]
    public async Task CircuitBreaker_Opens_ThenRecovers()
    {
        using var h = ClientHarness.Create(
            [Steps.Status(HttpStatusCode.InternalServerError), Steps.Status(HttpStatusCode.InternalServerError), Steps.Ok(ValidBody)],
            ("InventoryApi:Resilience:MaxRetryAttempts", "1"),
            ("InventoryApi:Resilience:CircuitBreakerMinimumThroughput", "2"),
            ("InventoryApi:Resilience:CircuitBreakerFailureRatio", "0.5"),
            ("InventoryApi:Resilience:CircuitBreakerBreakDuration", "00:00:05"));

        var first = await Fails(h.Client.GetPageAsync(1, 25, CancellationToken.None));
        Assert.Equal(InventorySourceFailureKind.Unavailable, first.Kind);
        Assert.Equal(2, h.Handler.Calls);

        var blocked = await Fails(h.Client.GetPageAsync(1, 25, CancellationToken.None));
        Assert.Equal(InventorySourceFailureKind.Unavailable, blocked.Kind);
        Assert.Equal(2, h.Handler.Calls);

        h.Time.Advance(TimeSpan.FromSeconds(6));
        var dto = await h.Client.GetPageAsync(2, 25, CancellationToken.None);

        Assert.Equal(157, dto.TotalRecords);
        Assert.Equal(3, h.Handler.Calls);
    }

    [Fact]
    public async Task ApiKey_NeverAppearsInLogs()
    {
        using var h = ClientHarness.Create(Steps.Status(HttpStatusCode.InternalServerError), Steps.Ok(ValidBody));

        await h.Client.GetPageAsync(2, 25, CancellationToken.None);

        Assert.NotEmpty(h.Logs.Messages);
        Assert.DoesNotContain(h.Logs.Messages, m => m.Contains(ApiKey, StringComparison.Ordinal));
    }

    [Fact]
    public void RedirectsAreNotFollowed_SoTheKeyCannotLeaveTheConfiguredHost()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [InventoryApiOptions.ApiKeySettingName] = ApiKey,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInventoryInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        var factoryOptions = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get("IInventoryPageClient");
        var builder = new RecordingHandlerBuilder(provider);
        foreach (var action in factoryOptions.HttpMessageHandlerBuilderActions)
        {
            action(builder);
        }

        var handler = Assert.IsType<SocketsHttpHandler>(builder.PrimaryHandler);
        Assert.False(handler.AllowAutoRedirect);
    }

    private sealed class RecordingHandlerBuilder(IServiceProvider services) : HttpMessageHandlerBuilder
    {
        public override string? Name { get; set; }

        public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();

        public override IList<DelegatingHandler> AdditionalHandlers { get; } = [];

        public override IServiceProvider Services { get; } = services;

        public override HttpMessageHandler Build() => PrimaryHandler;
    }

    [Fact]
    public void ConfiguredValuesAreApplied_ToTheHandler()
    {
        using var h = ClientHarness.Create(
            [Steps.Ok(ValidBody)],
            ("InventoryApi:Resilience:MaxRetryAttempts", "5"),
            ("InventoryApi:Resilience:RetryBaseDelay", "00:00:07"),
            ("InventoryApi:Resilience:AttemptTimeout", "00:00:04"),
            ("InventoryApi:Resilience:TotalTimeout", "00:00:44"),
            ("InventoryApi:Resilience:CircuitBreakerFailureRatio", "0.7"),
            ("InventoryApi:Resilience:CircuitBreakerMinimumThroughput", "9"),
            ("InventoryApi:Resilience:CircuitBreakerSamplingDuration", "00:00:55"),
            ("InventoryApi:Resilience:CircuitBreakerBreakDuration", "00:00:11"));

        var o = h.Provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get("IInventoryPageClient-standard");

        Assert.Equal(5, o.Retry.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(7), o.Retry.Delay);
        Assert.Equal(TimeSpan.FromSeconds(4), o.AttemptTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(44), o.TotalRequestTimeout.Timeout);
        Assert.Equal(0.7, o.CircuitBreaker.FailureRatio);
        Assert.Equal(9, o.CircuitBreaker.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(55), o.CircuitBreaker.SamplingDuration);
        Assert.Equal(TimeSpan.FromSeconds(11), o.CircuitBreaker.BreakDuration);
    }

    [Fact]
    public void AppliesTheApprovedResilienceValues()
    {
        using var h = ClientHarness.Create(Steps.Ok(ValidBody), withoutTestOverrides: true);

        var o = h.Provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get("IInventoryPageClient-standard");

        Assert.Equal(3, o.Retry.MaxRetryAttempts);
        Assert.Equal(DelayBackoffType.Exponential, o.Retry.BackoffType);
        Assert.True(o.Retry.UseJitter);
        Assert.True(o.Retry.ShouldRetryAfterHeader);
        Assert.Equal(TimeSpan.FromSeconds(2), o.Retry.Delay);
        Assert.Equal(TimeSpan.FromSeconds(10), o.AttemptTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(30), o.TotalRequestTimeout.Timeout);
        Assert.Equal(0.1, o.CircuitBreaker.FailureRatio);
        Assert.Equal(100, o.CircuitBreaker.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(30), o.CircuitBreaker.SamplingDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), o.CircuitBreaker.BreakDuration);
    }

    [Fact]
    public async Task Collector_GathersAllPages_ThroughTheRealClient()
    {
        using var h = ClientHarness.Create(request =>
        {
            var page = int.Parse(System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["page"]!);
            return Steps.Ok(PageBodies.Books(page, totalRecords: 51))(request, CancellationToken.None);
        });

        var source = h.Provider.GetRequiredService<IInventoryRecordSource>();
        var records = await source.GetAllRecordsAsync(CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, 51).Select(i => (long)i), records.Select(r => r.Id));
        Assert.Equal(3, h.Handler.Calls);
    }
}

// Consistent upstream pages of 25 records each: id, quantity 4 and price 2.5 for every record.
internal static class PageBodies
{
    public static string Books(int page, int totalRecords)
    {
        const int pageSize = 25;
        var totalPages = (totalRecords - 1) / pageSize + 1;
        var first = (page - 1) * pageSize + 1;
        var last = Math.Min(page * pageSize, totalRecords);
        var records = string.Join(",", Enumerable.Range(first, last - first + 1).Select(id =>
            $$"""{"id":{{id}},"name":"P{{id}}","category":"Books","price":2.5,"quantity":4,"lastUpdated":"2026-10-15T12:30:00Z"}"""));
        return $$"""{"records":[{{records}}],"page":{{page}},"pageSize":{{pageSize}},"totalPages":{{totalPages}},"totalRecords":{{totalRecords}}}""";
    }
}

internal static class Steps
{
    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Ok(
        string body, string mediaType = "application/json") =>
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        });

    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Status(
        HttpStatusCode status, TimeSpan? retryAfter = null) =>
        (_, _) =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            if (retryAfter is { } delay)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(delay);
            }

            return Task.FromResult(response);
        };

    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Throw(Exception exception) =>
        (_, _) => Task.FromException<HttpResponseMessage>(exception);

    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Hang() =>
        async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
}

internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] _steps;
    private int _calls;

    public ScriptedHandler(params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] steps)
    {
        _steps = steps;
    }

    public int Calls => Volatile.Read(ref _calls);

    public List<RecordedRequest> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var index = Interlocked.Increment(ref _calls) - 1;
        lock (Requests)
        {
            var keyValues = request.Headers.TryGetValues(ServiceCollectionExtensions.ApiKeyHeaderName, out var values)
                ? values.ToArray()
                : [];
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri?.ToString(), keyValues));
        }

        return _steps[Math.Min(index, _steps.Length - 1)](request, cancellationToken);
    }
}

internal sealed record RecordedRequest(HttpMethod Method, string? Uri, string[] KeyValues);

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public ConcurrentQueue<(string Category, LogLevel Level)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Messages, Entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(
        string category,
        ConcurrentQueue<string> messages,
        ConcurrentQueue<(string Category, LogLevel Level)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            messages.Enqueue(formatter(state, exception) + exception);
            entries.Enqueue((category, logLevel));
        }
    }
}

internal sealed class ClientHarness : IDisposable
{
    private readonly ServiceProvider _provider;

    private ClientHarness(ServiceProvider provider, ScriptedHandler handler, FakeTimeProvider time, CapturingLoggerProvider logs)
    {
        _provider = provider;
        Handler = handler;
        Time = time;
        Logs = logs;
    }

    public ScriptedHandler Handler { get; }

    public FakeTimeProvider Time { get; }

    public CapturingLoggerProvider Logs { get; }

    public IServiceProvider Provider => _provider;

    public IInventoryPageClient Client => _provider.GetRequiredService<IInventoryPageClient>();

    public static ClientHarness Create(params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] steps) =>
        Create(steps, []);

    public static ClientHarness Create(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) =>
        Create([(request, _) => respond(request)], []);

    public static ClientHarness Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> step,
        params (string Key, string Value)[] overrides) =>
        Create([step], overrides);

    public static ClientHarness Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] steps,
        params (string Key, string Value)[] overrides) =>
        Create(steps, overrides, withoutTestOverrides: false);

    public static ClientHarness Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> step,
        bool withoutTestOverrides) =>
        Create([step], [], withoutTestOverrides);

    private static ClientHarness Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] steps,
        (string Key, string Value)[] overrides,
        bool withoutTestOverrides)
    {
        var settings = new Dictionary<string, string?>
        {
            [InventoryApiOptions.ApiKeySettingName] = ClientTests.ApiKey,
            ["InventoryApi:BaseUrl"] = "https://upstream.test/",
        };
        if (!withoutTestOverrides)
        {
            settings["InventoryApi:Resilience:RetryBaseDelay"] = "00:00:00";
        }

        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var handler = new ScriptedHandler(steps);
        var time = new FakeTimeProvider();
        var logs = new CapturingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddSingleton<TimeProvider>(time);
        services.AddInventoryInfrastructure(configuration);
        services.AddHttpClient<IInventoryPageClient, InventoryApiClient>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        return new ClientHarness(services.BuildServiceProvider(), handler, time, logs);
    }

    public Task SettleAsync() => Task.Delay(100);

    public async Task<T> DriveAsync<T>(Task<T> task)
    {
        for (var i = 0; i < 500 && !task.IsCompleted; i++)
        {
            await Task.Delay(10);
            Time.Advance(TimeSpan.FromSeconds(1));
        }

        return await task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public void Dispose() => _provider.Dispose();
}
