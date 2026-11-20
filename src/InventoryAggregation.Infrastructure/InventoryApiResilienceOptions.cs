namespace InventoryAggregation.Infrastructure;

/// <summary>
/// Resilience settings for the upstream client. Defaults are the official standard-handler values
/// (Microsoft Learn: "Build resilient HTTP apps"), made explicit so nothing relies on library defaults (P-2).
/// </summary>
public sealed class InventoryApiResilienceOptions
{
    public const string SectionName = "InventoryApi:Resilience";

    public int MaxRetryAttempts { get; set; } = 3;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public double CircuitBreakerFailureRatio { get; set; } = 0.1;

    public int CircuitBreakerMinimumThroughput { get; set; } = 100;

    public TimeSpan CircuitBreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(5);
}
