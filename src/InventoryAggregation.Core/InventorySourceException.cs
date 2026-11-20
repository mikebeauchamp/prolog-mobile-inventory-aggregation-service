namespace InventoryAggregation.Core;

public enum InventorySourceFailureKind
{
    /// <summary>Upstream unreachable, erroring, throttled, or its circuit is open (HTTP 503).</summary>
    Unavailable,

    /// <summary>Upstream did not answer in time (HTTP 504).</summary>
    Timeout,

    /// <summary>Upstream answered, but the data is incomplete, inconsistent or unreadable (HTTP 502).</summary>
    BadData,

    /// <summary>Upstream rejected our credentials (HTTP 502).</summary>
    Rejected,
}

/// <summary>
/// The only exception an <see cref="IInventoryRecordSource"/> raises for upstream problems.
/// The message is for server-side logs; it never contains configuration values.
/// </summary>
public sealed class InventorySourceException : Exception
{
    public InventorySourceException(InventorySourceFailureKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public InventorySourceFailureKind Kind { get; }
}
