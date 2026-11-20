using Microsoft.Extensions.Options;

namespace InventoryAggregation.Infrastructure;

internal sealed class InventoryCacheOptionsValidator : IValidateOptions<InventoryCacheOptions>
{
    public ValidateOptionsResult Validate(string? name, InventoryCacheOptions options) =>
        options.Ttl < TimeSpan.Zero
            ? ValidateOptionsResult.Fail($"{InventoryCacheOptions.SectionName}:{nameof(InventoryCacheOptions.Ttl)} must not be negative. Use 00:00:00 to disable caching.")
            : ValidateOptionsResult.Success;
}
