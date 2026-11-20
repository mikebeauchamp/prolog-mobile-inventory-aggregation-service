namespace InventoryAggregation.Infrastructure;

public sealed class InventoryApiOptions
{
    public const string SectionName = "InventoryApi";

    /// <summary>The single top-level setting that holds the API key (user secret or environment variable). It is deliberately not under <see cref="SectionName"/>.</summary>
    public const string ApiKeySettingName = "Prolog-Mobile-API-Key";

    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }
}
