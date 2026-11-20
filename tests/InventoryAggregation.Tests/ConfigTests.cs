using InventoryAggregation.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InventoryAggregation.Tests;

// AC-17: the API key is configuration, and a missing key fails startup without leaking values.
public class ConfigTests
{
    private const string SecretLookingValue = "super-secret-value-should-never-appear";

    private static WebApplicationFactory<Program> FactoryWith(params (string Key, string? Value)[] settings)
    {
        // The key setting is blanked first so a machine-level variable cannot leak into these tests.
        var values = new Dictionary<string, string?> { [InventoryApiOptions.ApiKeySettingName] = null };
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(values)));
    }

    [Fact]
    public void ApiKey_IsReadFromTheProlog_Mobile_API_KeySetting()
    {
        using var factory = FactoryWith((InventoryApiOptions.ApiKeySettingName, SecretLookingValue));
        using var client = factory.CreateClient();

        Assert.Equal(SecretLookingValue, factory.Services.GetRequiredService<IOptions<InventoryApiOptions>>().Value.ApiKey);
    }

    [Fact]
    public void OldInventoryApiApiKeySetting_IsNoLongerRead()
    {
        using var factory = FactoryWith(("InventoryApi:ApiKey", SecretLookingValue));

        var ex = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains(InventoryApiOptions.ApiKeySettingName, ex.Message);
        Assert.DoesNotContain(SecretLookingValue, ex.ToString());
    }

    [Fact]
    public void ApiKey_CanBeSuppliedByRealEnvironmentVariable()
    {
        var variable = InventoryApiOptions.ApiKeySettingName;
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, SecretLookingValue);
        try
        {
            using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();

            Assert.Equal(SecretLookingValue, factory.Services.GetRequiredService<IOptions<InventoryApiOptions>>().Value.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingKey_FailsStartup(string? apiKey)
    {
        using var factory = FactoryWith((InventoryApiOptions.ApiKeySettingName, apiKey));

        var ex = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains(InventoryApiOptions.ApiKeySettingName, ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://stg.prologmobile.com/")]
    public void InvalidBaseUrl_FailsStartup_WithoutPrintingValues(string baseUrl)
    {
        using var factory = FactoryWith(
            (InventoryApiOptions.ApiKeySettingName, SecretLookingValue),
            ("InventoryApi:BaseUrl", baseUrl));

        var ex = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("BaseUrl", ex.Message);
        Assert.DoesNotContain(SecretLookingValue, ex.ToString());
        if (baseUrl.Length > 0)
        {
            Assert.DoesNotContain(baseUrl, ex.Message);
        }
    }

    [Theory]
    [InlineData("key\nwith-newline")]
    [InlineData("key\rwith-return")]
    [InlineData("key\u0000with-null")]
    public void ApiKeyWithControlCharacters_FailsStartup_WithoutPrintingIt(string apiKey)
    {
        using var factory = FactoryWith((InventoryApiOptions.ApiKeySettingName, apiKey));

        var ex = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains(InventoryApiOptions.ApiKeySettingName, ex.Message);
        Assert.DoesNotContain("with-", ex.ToString());
    }

    [Fact]
    public void NegativeCacheTtl_FailsStartup()
    {
        using var factory = FactoryWith(
            (InventoryApiOptions.ApiKeySettingName, SecretLookingValue),
            ("InventoryApi:Cache:Ttl", "-00:00:01"));

        var ex = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Cache:Ttl", ex.Message);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:01:00")]
    public void ZeroOrPositiveCacheTtl_Starts(string ttl)
    {
        using var factory = FactoryWith(
            (InventoryApiOptions.ApiKeySettingName, SecretLookingValue),
            ("InventoryApi:Cache:Ttl", ttl));

        using var client = factory.CreateClient();

        Assert.Equal(TimeSpan.Parse(ttl), factory.Services.GetRequiredService<IOptions<InventoryCacheOptions>>().Value.Ttl);
    }

    [Fact]
    public void ValidConfiguration_StartsAndBindsOptions()
    {
        using var factory = FactoryWith((InventoryApiOptions.ApiKeySettingName, SecretLookingValue));
        using var client = factory.CreateClient();

        var options = factory.Services.GetRequiredService<IOptions<InventoryApiOptions>>().Value;

        Assert.Equal("https://stg.prologmobile.com/", options.BaseUrl);
        Assert.Equal(SecretLookingValue, options.ApiKey);
    }
}
