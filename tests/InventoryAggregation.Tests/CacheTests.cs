using InventoryAggregation.Core;
using InventoryAggregation.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace InventoryAggregation.Tests;

// AC-7, AC-18 (D-7, P-3): complete results only, shared in-flight fetch, failures never cached.
public class CacheTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private static InventoryRecord Rec(long id) => new(id, "n", "c", 1m, 1, DateTimeOffset.UnixEpoch);

    private static IReadOnlyList<InventoryRecord> Data(params long[] ids) => ids.Select(Rec).ToList();

    private static TaskCompletionSource<IReadOnlyList<InventoryRecord>> NewGate() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static (CachingInventoryRecordSource Cache, FakeSource Source, FakeTimeProvider Time) Build(
        TimeSpan ttl, Func<int, Task<IReadOnlyList<InventoryRecord>>> behaviour)
    {
        var source = new FakeSource(behaviour);
        var time = new FakeTimeProvider();
        return (new CachingInventoryRecordSource(() => source, ttl, time), source, time);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "condition was not met in time");
    }

    [Fact]
    public async Task SecondCallIsServedFromCache()
    {
        var (cache, source, _) = Build(Ttl, _ => Task.FromResult(Data(1, 2)));

        var first = await cache.GetAllRecordsAsync(CancellationToken.None);
        var second = await cache.GetAllRecordsAsync(CancellationToken.None);

        Assert.Equal(1, source.Calls);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task EntryIsStillFreshJustBeforeTtl_AndRefetchedAtTtl()
    {
        var (cache, source, time) = Build(Ttl, n => Task.FromResult(Data(n)));

        await cache.GetAllRecordsAsync(CancellationToken.None);
        time.Advance(Ttl - TimeSpan.FromMilliseconds(1));
        var stillCached = await cache.GetAllRecordsAsync(CancellationToken.None);
        Assert.Equal(1, source.Calls);
        Assert.Equal(1, stillCached[0].Id);

        time.Advance(TimeSpan.FromMilliseconds(1));
        var refreshed = await cache.GetAllRecordsAsync(CancellationToken.None);

        Assert.Equal(2, source.Calls);
        Assert.Equal(2, refreshed[0].Id);
    }

    [Fact]
    public async Task FailureIsNotCached()
    {
        var (cache, source, _) = Build(Ttl, n => n == 1
            ? Task.FromException<IReadOnlyList<InventoryRecord>>(new InventorySourceException(InventorySourceFailureKind.Unavailable, "down"))
            : Task.FromResult(Data(1)));

        var ex = await Assert.ThrowsAsync<InventorySourceException>(() => cache.GetAllRecordsAsync(CancellationToken.None));
        var recovered = await cache.GetAllRecordsAsync(CancellationToken.None);
        var cached = await cache.GetAllRecordsAsync(CancellationToken.None);

        Assert.Equal(InventorySourceFailureKind.Unavailable, ex.Kind);
        Assert.Equal(2, source.Calls);
        Assert.Same(recovered, cached);
    }

    [Fact(Timeout = 15000)]
    public async Task ConcurrentCallersShareOneFetch()
    {
        var gate = NewGate();
        var (cache, source, _) = Build(Ttl, _ => gate.Task);

        var callers = Enumerable.Range(0, 5).Select(_ => cache.GetAllRecordsAsync(CancellationToken.None)).ToArray();
        await WaitUntil(() => source.Calls >= 1);
        gate.SetResult(Data(1, 2, 3));
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, source.Calls);
        Assert.All(results, r => Assert.Equal([1L, 2, 3], r.Select(x => x.Id)));
    }

    [Fact(Timeout = 15000)]
    public async Task ConcurrentCallersShareTheFailure_ThenTheNextCallRetries()
    {
        var gate = NewGate();
        var (cache, source, _) = Build(Ttl, n => n == 1 ? gate.Task : Task.FromResult(Data(9)));

        var callers = Enumerable.Range(0, 4).Select(_ => cache.GetAllRecordsAsync(CancellationToken.None)).ToArray();
        await WaitUntil(() => source.Calls >= 1);
        gate.SetException(new InventorySourceException(InventorySourceFailureKind.Timeout, "slow"));

        foreach (var caller in callers)
        {
            var ex = await Assert.ThrowsAsync<InventorySourceException>(() => caller);
            Assert.Equal(InventorySourceFailureKind.Timeout, ex.Kind);
        }

        Assert.Equal(1, source.Calls);
        var next = await cache.GetAllRecordsAsync(CancellationToken.None);
        Assert.Equal(2, source.Calls);
        Assert.Equal(9, next[0].Id);
    }

    [Fact]
    public async Task TtlZero_DoesNotStore()
    {
        var (cache, source, _) = Build(TimeSpan.Zero, n => Task.FromResult(Data(n)));

        await cache.GetAllRecordsAsync(CancellationToken.None);
        await cache.GetAllRecordsAsync(CancellationToken.None);

        Assert.Equal(2, source.Calls);
    }

    [Fact(Timeout = 15000)]
    public async Task TtlZero_StillSharesAnInFlightFetch()
    {
        var gate = NewGate();
        var (cache, source, _) = Build(TimeSpan.Zero, _ => gate.Task);

        var callers = Enumerable.Range(0, 3).Select(_ => cache.GetAllRecordsAsync(CancellationToken.None)).ToArray();
        await WaitUntil(() => source.Calls >= 1);
        gate.SetResult(Data(1));
        await Task.WhenAll(callers);

        Assert.Equal(1, source.Calls);
    }

    [Fact(Timeout = 15000)]
    public async Task OneCallerCancelling_DoesNotAffectTheOthers()
    {
        var gate = NewGate();
        var (cache, source, _) = Build(Ttl, _ => gate.Task);
        using var cts = new CancellationTokenSource();

        var cancelled = cache.GetAllRecordsAsync(cts.Token);
        var patient = cache.GetAllRecordsAsync(CancellationToken.None);
        await WaitUntil(() => source.Calls >= 1);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.False(patient.IsCompleted);

        gate.SetResult(Data(1));
        var result = await patient;

        Assert.Equal(1, source.Calls);
        Assert.Single(result);
        Assert.All(source.Tokens, token => Assert.False(token.CanBeCanceled));
    }

    [Fact(Timeout = 15000)]
    public async Task FetchOutlivingAllCallers_IsStillCached()
    {
        var gate = NewGate();
        var (cache, source, _) = Build(Ttl, _ => gate.Task);
        using var cts = new CancellationTokenSource();

        var abandoned = cache.GetAllRecordsAsync(cts.Token);
        await WaitUntil(() => source.Calls >= 1);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

        gate.SetResult(Data(1, 2));
        IReadOnlyList<InventoryRecord>? result = null;
        await WaitUntil(() => cache.GetAllRecordsAsync(CancellationToken.None).IsCompletedSuccessfully);
        result = await cache.GetAllRecordsAsync(CancellationToken.None);

        Assert.Equal(1, source.Calls);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task AlreadyCancelledCaller_StillGetsAFreshCacheHit()
    {
        var (cache, source, _) = Build(Ttl, _ => Task.FromResult(Data(1)));
        await cache.GetAllRecordsAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await cache.GetAllRecordsAsync(cts.Token);

        Assert.Single(result);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task ThroughTheContainer_UsesTheFiveMinuteDefault_AndASingleInstance()
    {
        static string PageBody() =>
            """{"records":[{"id":1,"name":"P","category":"Books","price":2.5,"quantity":4,"lastUpdated":"2026-10-15T12:30:00Z"}],"page":1,"pageSize":25,"totalPages":1,"totalRecords":1}""";

        using var h = ClientHarness.Create(Steps.Ok(PageBody()));
        var source = h.Provider.GetRequiredService<IInventoryRecordSource>();

        Assert.IsType<CachingInventoryRecordSource>(source);
        Assert.Same(source, h.Provider.GetRequiredService<IInventoryRecordSource>());

        await source.GetAllRecordsAsync(CancellationToken.None);
        h.Time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        await source.GetAllRecordsAsync(CancellationToken.None);
        Assert.Equal(1, h.Handler.Calls);

        h.Time.Advance(TimeSpan.FromSeconds(2));
        await source.GetAllRecordsAsync(CancellationToken.None);
        Assert.Equal(2, h.Handler.Calls);
    }

    [Fact]
    public async Task ThroughTheContainer_TtlSettingCanDisableStoring()
    {
        static string PageBody() =>
            """{"records":[],"page":1,"pageSize":25,"totalPages":0,"totalRecords":0}""";

        using var h = ClientHarness.Create([Steps.Ok(PageBody())], ("InventoryApi:Cache:Ttl", "00:00:00"));
        var source = h.Provider.GetRequiredService<IInventoryRecordSource>();

        await source.GetAllRecordsAsync(CancellationToken.None);
        await source.GetAllRecordsAsync(CancellationToken.None);

        Assert.Equal(2, h.Handler.Calls);
    }
}

internal sealed class FakeSource : IInventoryRecordSource
{
    private readonly Func<int, Task<IReadOnlyList<InventoryRecord>>> _behaviour;
    private int _calls;

    public FakeSource(Func<int, Task<IReadOnlyList<InventoryRecord>>> behaviour)
    {
        _behaviour = behaviour;
    }

    public int Calls => Volatile.Read(ref _calls);

    public List<CancellationToken> Tokens { get; } = [];

    public Task<IReadOnlyList<InventoryRecord>> GetAllRecordsAsync(CancellationToken cancellationToken)
    {
        var n = Interlocked.Increment(ref _calls);
        lock (Tokens)
        {
            Tokens.Add(cancellationToken);
        }

        return _behaviour(n);
    }
}
