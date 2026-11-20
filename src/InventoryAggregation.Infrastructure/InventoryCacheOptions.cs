namespace InventoryAggregation.Infrastructure;

public sealed class InventoryCacheOptions
{
    public const string SectionName = "InventoryApi:Cache";

    /// <summary>How long a complete, validated dataset is reused. Zero disables storing (P-3).</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(5);
}
