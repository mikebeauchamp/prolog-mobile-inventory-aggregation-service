using InventoryAggregation.Core;

namespace InventoryAggregation.Infrastructure;

/// <summary>
/// Reuses one complete, validated dataset for a configurable time (D-7, P-3). Concurrent callers share a
/// single in-flight fetch; failures are never cached.
/// </summary>
public sealed class CachingInventoryRecordSource : IInventoryRecordSource
{
    private readonly Func<IInventoryRecordSource> _innerFactory;
    private readonly TimeSpan _ttl;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private Snapshot? _snapshot;
    private Task<IReadOnlyList<InventoryRecord>>? _inFlight;

    public CachingInventoryRecordSource(Func<IInventoryRecordSource> innerFactory, TimeSpan ttl, TimeProvider time)
    {
        _innerFactory = innerFactory;
        _ttl = ttl;
        _time = time;
    }

    public Task<IReadOnlyList<InventoryRecord>> GetAllRecordsAsync(CancellationToken cancellationToken)
    {
        Task<IReadOnlyList<InventoryRecord>> fetch;
        lock (_gate)
        {
            if (_snapshot is { } snapshot && _time.GetUtcNow() < snapshot.ExpiresAt)
            {
                return Task.FromResult(snapshot.Records);
            }

            // Task.Run guarantees the fetch cannot finish (and clear _inFlight) before it is assigned: the gate is held.
            fetch = _inFlight ??= Task.Run(FetchAsync, CancellationToken.None);
        }

        // Each caller can stop waiting on its own without cancelling the shared fetch.
        return fetch.WaitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<InventoryRecord>> FetchAsync()
    {
        try
        {
            var records = await _innerFactory().GetAllRecordsAsync(CancellationToken.None);

            if (_ttl > TimeSpan.Zero)
            {
                lock (_gate)
                {
                    _snapshot = new Snapshot(records, _time.GetUtcNow() + _ttl);
                }
            }

            return records;
        }
        finally
        {
            lock (_gate)
            {
                _inFlight = null;
            }
        }
    }

    private sealed record Snapshot(IReadOnlyList<InventoryRecord> Records, DateTimeOffset ExpiresAt);
}
