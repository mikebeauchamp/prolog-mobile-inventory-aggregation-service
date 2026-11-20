# Implementation Plan – Inventory Aggregation Service

Source: "Take-Home Assignment – Inventory Aggregation Service" (PrologMobile, 2026-11-17).
Status: plan only. All decisions below were approved by the user. Do not change them without asking.

## 1. Acceptance criteria (26, derived from the PDF)

| # | Requirement |
|---|---|
| AC-1 | Exclude records with quantity 0 from every figure |
| AC-2 | Group remaining records by category |
| AC-3 | Total inventory value per category = sum of price x quantity |
| AC-4 | Available record count per category |
| AC-5 | Most recently updated record per category, by `lastUpdated` |
| AC-6 | Each category carries the records its figures were computed from |
| AC-7 | Results reflect the complete dataset (all pages) |
| AC-8 | Open decisions written down in the README |
| AC-9 | Own GET endpoint over HTTP |
| AC-10 | Any deviation from the sample response shape explained in the README |
| AC-11 | Upstream unreachable or erroring gives a sensible response, never an unhandled exception |
| AC-12 | C# on a currently supported .NET version |
| AC-13 | `HttpClient` from `IHttpClientFactory` |
| AC-14 | Async throughout, no blocking |
| AC-15 | Dependency injection for our own services |
| AC-16 | API communication separate from business logic; aggregation testable without HTTP |
| AC-17 | API key outside source control |
| AC-18 | Non-success responses and transport errors handled, including statuses not listed in the PDF |
| AC-19 | Unit tests for the transformation logic |
| AC-20 | Unit tests for the API client's resilience behaviour |
| AC-21 | README with setup and run steps that work on a clean machine |
| AC-22 | Git repo with history that builds and runs from a clean clone |
| AC-23 | README covers: API key, running the app, running the tests, the endpoint |
| AC-24 | Half-page architecture note |
| AC-25 | Assumptions and known limitations, including what to do next |
| AC-26 | A 200 is not trusted: code checks completeness and consistency |

## 1a. Source contract (extracted from the assignment PDF on 2026-11-20; the PDF is no longer needed)

**Upstream request:** `GET {BaseUrl}/api/records?page={n}&pageSize={m}`. `page` is an integer, default 1, 1-based. `pageSize` is an integer, default 20, accepted range 1-25. The API key is sent in the `X-Api-Key` header (verified by the live 401 in section 2). The base URL is provided separately (section 2). The dataset is static: it does not change between runs.

**Upstream page response (JSON):**

```json
{
  "records": [
    { "id": 1001, "name": "Thermal Receipt Printer", "category": "Electronics",
      "price": 149.99, "quantity": 12, "lastUpdated": "2026-10-15T12:30:00Z" }
  ],
  "page": 1, "pageSize": 20, "totalPages": 8, "totalRecords": 157
}
```

| Record field | Type | Notes |
|---|---|---|
| `id` | integer | Unique identifier |
| `name` | string | Product name |
| `category` | string | Exactly one category per record |
| `price` | decimal | Unit price, USD |
| `quantity` | integer | Units on hand; may be 0 |
| `lastUpdated` | string | ISO 8601, UTC |

**Upstream errors listed by the PDF:** 400 (page or pageSize out of range) and 500. The PDF says the list is not a guarantee and any non-success status needs a considered answer. A 200 is not a guarantee either: completeness and internal consistency must be checked in code.

**Our endpoint's expected shape (the PDF calls it "roughly what we expect", illustrative values; D-8b adopts it):**

```json
{
  "categories": [
    {
      "category": "Electronics",
      "totalInventoryValue": 1799.88,
      "availableRecordCount": 1,
      "mostRecentlyUpdated": { "id": 1001, "name": "Thermal Receipt Printer", "lastUpdated": "2026-10-15T12:30:00Z" },
      "items": [
        { "id": 1001, "name": "Thermal Receipt Printer", "price": 149.99, "quantity": 12, "lastUpdated": "2026-10-15T12:30:00Z" }
      ]
    }
  ]
}
```

Notes: `items` do not carry `category` (it is on the parent). `mostRecentlyUpdated` holds only `id`, `name`, `lastUpdated`. The sample's 1799.88 equals 149.99 x 12. The PDF allows a deliberate deviation if explained in the README (AC-10).

**PDF technical requirements not already in the AC list:** .NET 8 LTS or newer; `HttpClient` from `IHttpClientFactory` (not per call, not a self-managed static); async throughout; DI for our own services; API key via configuration, environment variable or user secrets; be ready to explain any resilience library. The follow-up interview (walkthrough, debugging, live change) means the code must be explainable.

## 2. Verified facts

| Fact | Evidence |
|---|---|
| The API needs an `X-Api-Key` header | Live unauthenticated GET returned 401: "A valid API key is required in the X-Api-Key header." |
| Base URL: `https://stg.prologmobile.com/` (not a secret) | User's assignment email |
| 401 body is ProblemDetails with a `traceId` | Same live call |
| .NET 8 support ended Nov 10, 2026; .NET 10 is LTS to Nov 2028 | Microsoft Learn |
| Standard resilience handler retries 3 times and honours `Retry-After` | Microsoft Learn |
| .NET 9+ hides header values and query strings in `IHttpClientFactory` logs by default | Microsoft Learn |

Unverified (do not state as fact): the real API's quirks (duplicates, missing pages, shifting totals, 429s, 5xx); the library's default timeouts and circuit-breaker numbers; that minimal APIs emit camelCase, UTC `Z` dates and `decimal` exactly as the sample shows (a test covers this).

## 3. Decision register (all approved)

| ID | Decision |
|---|---|
| D-1 | .NET 10 (LTS) |
| D-2 | `Microsoft.Extensions.Http.Resilience`, all values set explicitly |
| D-3 | If the full dataset cannot be collected after retries, return an error; never partial figures |
| D-4 | Identical duplicate `id`: keep one. Conflicting duplicates: D-3 error |
| D-5 | Group categories ignoring case and stray spaces; display name from the lowest-`id` record, trimmed |
| D-6 | Any invalid record = D-3 error. Invalid: blank category or name, negative or unreadable price or quantity, unreadable date. Price 0 is valid. Quantity 0 is valid but excluded |
| D-7 | In-memory cache of complete validated results only; 5 min default; TTL configurable, 0 = off; failures never cached; concurrent requests share one fetch |
| D-8a | Four projects: Core, Infrastructure, Api, Tests. Core has no HTTP reference |
| D-8b | `GET /api/inventory/categories`; response matches the PDF sample; categories A-Z ignoring case, items by `id`; no in-stock records returns `{"categories": []}` with 200 |
| D-8c | xUnit.net, hand-written fake HTTP handlers, ASP.NET Core test host for one endpoint test |
| D-9 | Fetch pages one at a time, page size 25, stop at the first failed page |
| D-10a | If records tie for latest `lastUpdated`, the highest `id` wins (README states this assumes ids grow over time) |
| D-10b | Exact `decimal` maths; round only the final category total to 2 dp |
| D-11 | 503 upstream unavailable; 504 upstream timeout; 502 unusable data or rejected key (401/403); 500 only for our own bugs. Plain ProblemDetails, no internals |
| D-12 | No GitHub Actions workflow; listed as a next step in the README |
| D-13 | Up to 3 retries, growing jittered waits, `Retry-After` honoured, 10 s per attempt, 30 s per page. Retry only transient failures (network, timeout, 5xx, 429). Never retry 400 or 401. Keep the library's circuit-breaker settings after reading the official values |

Decisions added during implementation (all approved by the user; full wording is in the register in IMPLEMENTATION-CHECKLIST.md):

| ID | Decision |
|---|---|
| P-1 | Final category total rounds half away from zero |
| P-2 | Resilience values set explicitly, equal to the official standard-handler defaults (2 s base delay, breaker 10% / 100 / 30 s / 5 s) |
| P-3 | Single-dataset cache with its own expiry, shared in-flight fetch, setting `InventoryApi:Cache:Ttl`, generic error wording (same 502 text for bad data and rejected key) |
| P-4 | The API key is read only from the top-level setting `Prolog-Mobile-API-Key` (user secret or environment variable). `InventoryApi:ApiKey` is no longer read |
| P-5 | The category display name is title-cased (invariant culture), e.g. `TOYS` becomes `Toys`, `home & kitchen` becomes `Home & Kitchen`. Acronyms are not preserved (`TV` becomes `Tv`) |

## 4. Layout

- `src\InventoryAggregation.Core`: `InventoryRecord`, `CategorySummary`, `IInventoryRecordSource`, `InventoryAggregator`. Nothing HTTP-related.
- `src\InventoryAggregation.Infrastructure`: typed `InventoryApiClient`, DTOs, options, resilience setup, page collector and validator, cache decorator, typed failure.
- `src\InventoryAggregation.Api`: minimal API, DI wiring, ProblemDetails mapping.
- `tests\InventoryAggregation.Tests`: xUnit tests.

## 5. Implementation items

| Item | ACs | Change | Check |
|---|---|---|---|
| P-1 | 12, 15, 17, 22 | Solution, projects, `.gitignore`, startup validation of base URL and key | Clean build; missing key fails startup without printing values |
| P-2 | 1-6, 16 | Aggregator with the D-5, D-10a, D-10b rules | Aggregator tests |
| P-3 | 7, 13, 14, 18, 26 | Typed client, sequential paging, validation (D-3, D-4, D-6), resilience (D-13) | Client and collector tests |
| P-4 | 9-11 | Endpoint, error mapping (D-11), cache (D-7) | Endpoint and cache tests |
| P-5 | 19, 20, 26 | All tests, with failing-without-the-fix proofs | `dotnet test` green |
| P-6 | 8, 10, 21, 23-25 | README, architecture note, assumptions and limitations | Clean-clone walkthrough |

## 6. AC-to-test mapping

| AC | Test | Type | Priority |
|---|---|---|---|
| 1 | `AggregatorTests.ExcludesZeroQuantity`, `.AllZeroCategoryIsOmitted` | Unit | P0 |
| 2 | `.GroupsIgnoringCaseAndSpaces`, `.DisplayNameFromLowestId` | Unit | P0 |
| 3 | `.SumsPriceTimesQuantity_Exact`, `.RoundsOnlyFinalTotal` | Unit | P0 |
| 4 | `.CountsOnlyAvailableRecords` | Unit | P0 |
| 5 | `.PicksMostRecent`, `.TieGoesToHighestId` | Unit | P0 |
| 6 | `.ItemsMatchFigures`, `.OrderingIsDeterministic`, `.EmptyInputGivesEmptyList` | Unit | P0 |
| 7 | `CollectorTests.FetchesAllPages_InOrder` | Unit | P0 |
| 8, 10, 21, 23-25 | README review against the checklist | Manual | P1 |
| 9, 10 | `EndpointTests.Returns200_WithSampleShape` (field names, `Z` dates) | Integration | P0 |
| 11 | `EndpointTests.UpstreamFailure_ReturnsProblemDetails_NoInternals` for 502/503/504; unexpected exception gives 500 | Integration | P0 |
| 12-15 | `DiTests.ResolvesServices`; build check | Smoke | P1 |
| 16 | Core has no HTTP reference (project-reference check) | Structural | P0 |
| 17 | `ConfigTests.MissingKey_FailsStartup`; manual check that no tracked file holds the key | Unit, manual | P0 |
| 18 | `ClientTests`: 500 then success, retries exhausted, 429 with `Retry-After`, 400 and 401 not retried, timeout, `HttpRequestException`, malformed JSON, missing required field, cancellation | Unit | P0 |
| 19 | Aggregator tests above | Unit | P0 |
| 20 | `ClientTests` with no real waiting, plus the circuit breaker opening | Unit | P0 |
| 26 | `CollectorTests`: identical duplicate dropped, conflicting duplicate fails, `totalRecords` mismatch, `totalPages` change, page echo mismatch, each invalid-field case, stop at first failed page | Unit | P0 |
| 7, 18 (D-7) | `CacheTests`: hit, failure not cached, concurrent requests share one fetch, TTL 0 disables | Unit | P1 |

## 7. Risks

| Risk | Level | Mitigation |
|---|---|---|
| Real API quirks differ from the hypotheses in section 2 | Medium | Review the first real run, then adjust the validator and tests, and ask the user before changing any decision |
| Library default timeouts and circuit-breaker numbers unverified | Low | Set every value explicitly; read the official defaults page |
| Strictness (D-3, D-6): one bad record fails the whole endpoint | Accepted | Documented in the README |

## 8. Open items

1. The API key is held by the user. It goes only in user secrets or an environment variable, never in any file, chat, log or test.
2. A `.playwright-mcp` folder may exist from a planning-time browser check (contains only the 401 response). Delete it and make sure it is not committed.
