using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InventoryAggregation.Core;
using Polly;
using Polly.Timeout;

namespace InventoryAggregation.Infrastructure;

public sealed class InventoryApiClient : IInventoryPageClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public InventoryApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<InventoryPageDto> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"api/records?page={page}&pageSize={pageSize}", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw FromStatus(response.StatusCode);
            }

            var dto = await response.Content.ReadFromJsonAsync<InventoryPageDto>(JsonOptions, cancellationToken);
            return dto ?? throw new InventorySourceException(InventorySourceFailureKind.BadData, "The API returned an empty JSON body.");
        }
        catch (InventorySourceException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutRejectedException ex)
        {
            throw new InventorySourceException(InventorySourceFailureKind.Timeout, "The API did not answer in time.", ex);
        }
        catch (OperationCanceledException ex)
        {
            // Not cancelled by the caller, so this is HttpClient's own timeout.
            throw new InventorySourceException(InventorySourceFailureKind.Timeout, "The API did not answer in time.", ex);
        }
        catch (ExecutionRejectedException ex)
        {
            // Circuit open or rate limiter rejection: the call never reached the API.
            throw new InventorySourceException(InventorySourceFailureKind.Unavailable, "The API call was rejected by the resilience pipeline.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InventorySourceException(InventorySourceFailureKind.Unavailable, "The API could not be reached.", ex);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InventorySourceException(InventorySourceFailureKind.BadData, "The API response could not be read as an inventory page.", ex);
        }
    }

    private static InventorySourceException FromStatus(HttpStatusCode status)
    {
        var kind = (int)status switch
        {
            401 or 403 => InventorySourceFailureKind.Rejected,
            408 or 504 => InventorySourceFailureKind.Timeout,
            429 or >= 500 => InventorySourceFailureKind.Unavailable,
            _ => InventorySourceFailureKind.BadData,
        };

        return new InventorySourceException(kind, $"The API returned HTTP {(int)status}.");
    }
}
