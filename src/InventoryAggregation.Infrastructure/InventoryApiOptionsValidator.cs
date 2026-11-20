using Microsoft.Extensions.Options;

namespace InventoryAggregation.Infrastructure;

// Failure messages name settings only; they never echo configured values (AC-17).
internal sealed class InventoryApiOptionsValidator : IValidateOptions<InventoryApiOptions>
{
    public ValidateOptionsResult Validate(string? name, InventoryApiOptions options)
    {
        var failures = new List<string>();

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl)
            || (baseUrl.Scheme != Uri.UriSchemeHttps && baseUrl.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"{InventoryApiOptions.SectionName}:{nameof(InventoryApiOptions.BaseUrl)} must be an absolute http or https URL.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add(
                $"{InventoryApiOptions.ApiKeySettingName} is not configured. " +
                "Set it as a user secret or as an environment variable with that name.");
        }
        else if (options.ApiKey.Any(char.IsControl))
        {
            failures.Add($"{InventoryApiOptions.ApiKeySettingName} contains control characters, which cannot be sent in an HTTP header.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
