using InventoryAggregation.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace InventoryAggregation.Infrastructure;

public static class ServiceCollectionExtensions
{
    public const string ApiKeyHeaderName = "X-Api-Key";

    public static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InventoryApiOptions>()
            .Bind(configuration.GetSection(InventoryApiOptions.SectionName))
            // The key comes only from the top-level setting; any InventoryApi:ApiKey value bound above is overwritten.
            .Configure(options => options.ApiKey = configuration[InventoryApiOptions.ApiKeySettingName])
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<InventoryApiOptions>, InventoryApiOptionsValidator>());
        services.AddOptions<InventoryApiResilienceOptions>()
            .Bind(configuration.GetSection(InventoryApiResilienceOptions.SectionName));

        services.AddHttpClient<IInventoryPageClient, InventoryApiClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<InventoryApiOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl!.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add(ApiKeyHeaderName, options.ApiKey);
        })
            // Custom headers survive redirects, so never follow one: the key must only go to the configured host.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RedactLoggedHeaders(_ => true)
            .AddStandardResilienceHandler()
            .Configure((options, serviceProvider) =>
                ApplyResilience(options, serviceProvider.GetRequiredService<IOptions<InventoryApiResilienceOptions>>().Value));
        services.AddOptions<InventoryCacheOptions>()
            .Bind(configuration.GetSection(InventoryCacheOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<InventoryCacheOptions>, InventoryCacheOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddTransient<InventoryRecordCollector>();
        // The cache is a singleton, so it creates a collector (and with it a factory-managed HttpClient) per fetch.
        services.AddSingleton<IInventoryRecordSource>(serviceProvider => new CachingInventoryRecordSource(
            () => serviceProvider.GetRequiredService<InventoryRecordCollector>(),
            serviceProvider.GetRequiredService<IOptions<InventoryCacheOptions>>().Value.Ttl,
            serviceProvider.GetRequiredService<TimeProvider>()));

        return services;
    }

    private static void ApplyResilience(HttpStandardResilienceOptions options, InventoryApiResilienceOptions settings)
    {
        options.Retry.MaxRetryAttempts = settings.MaxRetryAttempts;
        options.Retry.Delay = settings.RetryBaseDelay;
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;
        options.Retry.ShouldRetryAfterHeader = true;

        options.AttemptTimeout.Timeout = settings.AttemptTimeout;
        options.TotalRequestTimeout.Timeout = settings.TotalTimeout;

        options.CircuitBreaker.FailureRatio = settings.CircuitBreakerFailureRatio;
        options.CircuitBreaker.MinimumThroughput = settings.CircuitBreakerMinimumThroughput;
        options.CircuitBreaker.SamplingDuration = settings.CircuitBreakerSamplingDuration;
        options.CircuitBreaker.BreakDuration = settings.CircuitBreakerBreakDuration;
    }
}
