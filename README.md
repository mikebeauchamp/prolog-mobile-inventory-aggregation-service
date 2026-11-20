**Full disclosure:** I used GitHub Copilot in VS Code to create this project. The company I work for takes an AI-first approach to development, as does yours. I wanted to show you what I do daily when working with agents, how my harness works, and the results it produces. The project was created with Sonnet 5.5 (so it could probably use some improving). The agents I used were quickly generated from the agentic harness I built at my current workplace. I currently develop in Python and React, so I had to refactor the harness for C#. I used the three-hour window to create this project, as instructed.

**Agents and workflow**

- I use a planning agent to plan my work. It gathers all requirements and creates acceptance criteria (AC), which are then used to write the tests (TDD).
- The coding agent implements the plan and checks off items on the checklist as they are completed. This lets a coding agent in a later session know where the previous one left off, which keeps context windows small and reduces token consumption.
- The code review agent was run once to catch errors and find improvements after the first implementation pass (these changes are included in the second commit in the repo).

**Where to find it**

- [`harness/`](harness/) is a copy of my entire harness: all 22 files from the `.github` folder I used in VS Code (nine agents, `copilot-instructions.md`, seven instruction files and four skills). These are quick, rough copies of what I use at work, refactored for C#. They are here to be read. Copilot only loads them from a folder named `.github`, so they do not run from `harness/`, and a few files still refer to the original `.\.github\...` paths.
- [`docs/IMPLEMENTATION-PLAN.md`](docs/IMPLEMENTATION-PLAN.md) is what the planning agent produced: the acceptance criteria, the source contract, the decision register and the AC-to-test mapping.
- [`docs/IMPLEMENTATION-CHECKLIST.md`](docs/IMPLEMENTATION-CHECKLIST.md) is what the coding agent worked from and ticked off across sessions, including the decisions I approved, the red/green test evidence, and the changes made after the code review.

---

# Inventory Aggregation Service

A small .NET 10 service that reads every inventory record from the Inventory API (paginated, not guaranteed to be well behaved), aggregates the in-stock records by category, and exposes the result on its own HTTP endpoint.

- [Prerequisites](#prerequisites)
- [Configure the API key](#configure-the-api-key)
- [Run the app](#run-the-app)
- [The endpoint](#the-endpoint)
- [Run the tests](#run-the-tests)
- [Configuration reference](#configuration-reference)
- [Architecture](#architecture)
- [Judgement calls (open decisions)](#judgement-calls-open-decisions)
- [Assumptions and known limitations](#assumptions-and-known-limitations)
- [What I would do next](#what-i-would-do-next)

## Prerequisites

- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (.NET 10 is the current LTS release; .NET 8 is out of support). Check with `dotnet --version`.
- An API key for the Inventory API (provided separately). It is never stored in this repository.

No other tools are needed. Packages restore automatically on the first build.

## Configure the API key

The app reads the key from **one** setting: `Prolog-Mobile-API-Key`. The app refuses to start without it, and the startup message names the setting but never prints any value.

Use one of these (replace the placeholder with your own key; do not paste a real key into a file you commit):

**Option A: environment variable** (works in every environment)

```powershell
# PowerShell, current session only
${env:Prolog-Mobile-API-Key} = "<your key>"
```

On Windows you can also add it permanently under _System Properties > Environment Variables_; restart your terminal or editor afterwards so it picks the variable up.

**Option B: .NET user secrets** (local development only; stored outside the repo in your user profile, not encrypted)

```powershell
dotnet user-secrets set "Prolog-Mobile-API-Key" "<your key>" --project src\InventoryAggregation.Api
```

User secrets are only loaded when the environment is `Development`. The launch profile below sets that for you.

Do not put the key in `appsettings*.json`. The files `.env`, `.env.*` and `secrets.json` are listed in `.gitignore` as a safety net, but this project does not read `.env` files.

## Run the app

```powershell
dotnet run --project src\InventoryAggregation.Api --launch-profile http
```

The app listens on `http://localhost:5261`. The launch profile may open a browser tab at `/`, which has no route and shows a 404. That is expected: call the endpoint below instead.

The first request fetches the whole dataset from the upstream API one page at a time, so a cold request can take several seconds (about 11 seconds in my one run against the real API). Later requests are served from an in-memory cache for 5 minutes.

## The endpoint

`GET http://localhost:5261/api/inventory/categories`

```powershell
Invoke-RestMethod http://localhost:5261/api/inventory/categories | ConvertTo-Json -Depth 5
```

Example response (illustrative values):

```json
{
  "categories": [
    {
      "category": "Electronics",
      "totalInventoryValue": 1799.88,
      "availableRecordCount": 1,
      "mostRecentlyUpdated": {
        "id": 1001,
        "name": "Thermal Receipt Printer",
        "lastUpdated": "2026-10-15T12:30:00Z"
      },
      "items": [
        {
          "id": 1001,
          "name": "Thermal Receipt Printer",
          "price": 149.99,
          "quantity": 12,
          "lastUpdated": "2026-10-15T12:30:00Z"
        }
      ]
    }
  ]
}
```

**Response shape: no deviation.** It matches the sample in the assignment (property names, nesting, camelCase, UTC timestamps ending in `Z`). A test compares the whole body with that sample. The details the assignment left open are in [Judgement calls](#judgement-calls-open-decisions).

Rules applied to the data:

1. Records with quantity 0 are excluded from every figure.
2. The rest are grouped by category, ignoring case and surrounding spaces.
3. `totalInventoryValue` is the sum of price x quantity, rounded once to 2 decimal places.
4. `availableRecordCount` counts the in-stock records.
5. `mostRecentlyUpdated` is the record with the latest `lastUpdated`.
6. `items` are exactly the records the figures were computed from.

Categories are sorted A to Z (ignoring case) and items by `id`. If nothing is in stock the response is `200` with `{"categories": []}`.

### Error responses

Failures return a plain [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457) JSON body (`status`, `title` and `traceId`, plus a `detail` sentence for upstream failures). Details are logged on the server only; the body never contains stack traces, the API key or upstream URLs.

| Status | When                                                                                                                                                                                                |
| ------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `503`  | Upstream unreachable, erroring (5xx), throttled (429), or the circuit breaker is open                                                                                                               |
| `504`  | Upstream timed out                                                                                                                                                                                  |
| `502`  | Upstream data is incomplete, inconsistent, unreadable or too large to total, or the upstream rejected our key (401/403). Both cases use the same text so a caller cannot learn the credential state |
| `500`  | A bug in this service                                                                                                                                                                               |

## Run the tests

```powershell
dotnet test InventoryAggregation.slnx
```

With coverage (needs nothing extra; the collector is already referenced):

```powershell
dotnet test InventoryAggregation.slnx --collect:"XPlat Code Coverage"
```

The tests use xUnit, hand-written fake HTTP handlers, a fake clock (so no test waits for a retry delay) and ASP.NET Core's in-process test host. They never call the real API and need no API key (they set their own placeholder value). The last run in this repository had 194 tests passing with 100% line and branch coverage on all three source projects.

| Test file         | What it covers                                                                                                                                                                             |
| ----------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `AggregatorTests` | Zero-quantity exclusion, grouping, exact decimal totals and rounding, counts, most-recent and tie rules, ordering, title-cased names, Core has no HTTP reference                           |
| `CollectorTests`  | All pages in order, duplicates, `totalRecords`/`totalPages` checks (including page count versus record count), a page that carries an extra duplicate record, page echo, every invalid-field case, stop at first failure |
| `ClientTests`     | The real typed client and resilience pipeline: retries, `Retry-After`, 400/401 not retried, timeouts, transport errors, bad JSON, cancellation, circuit breaker, key never logged          |
| `CacheTests`      | Cache hit and expiry, failures not cached, concurrent callers share one fetch, TTL 0                                                                                                       |
| `EndpointTests`   | 200 shape, 502/503/504/500 problem details, values too large to total, log levels, end-to-end through the real client and cache                                                            |
| `ConfigTests`     | Missing or invalid key, base URL (including plain `http` to a non-local host) and cache settings fail startup without printing values                                                      |

## Configuration reference

| Setting                     | Environment variable          | Default                         | Notes                                                                                                                                                        |
| --------------------------- | ----------------------------- | ------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Prolog-Mobile-API-Key`     | `Prolog-Mobile-API-Key`       | none (required)                 | Sent as the `X-Api-Key` header                                                                                                                               |
| `InventoryApi:BaseUrl`      | `InventoryApi__BaseUrl`       | `https://stg.prologmobile.com/` | Must be an absolute https URL. Plain `http` is accepted only for a local address (`localhost`, `127.0.0.1`, `[::1]`) because the API key is sent in a header |
| `InventoryApi:Cache:Ttl`    | `InventoryApi__Cache__Ttl`    | `00:05:00`                      | `00:00:00` turns storing off; negative fails startup                                                                                                         |
| `InventoryApi:Resilience:*` | `InventoryApi__Resilience__*` | see below                       | `MaxRetryAttempts`, `RetryBaseDelay`, `AttemptTimeout`, `TotalTimeout`, `CircuitBreaker*`                                                                    |

Resilience defaults (set explicitly in code, equal to the official standard-handler values in Microsoft's documentation): 3 retries with exponential backoff and jitter from a 2 s base delay, 10 s per attempt, 30 s total per page, circuit breaker at 10% failures over 30 s with a minimum of 100 calls and a 5 s break. These values were read from Microsoft Learn; I did not measure the library's behaviour beyond what the tests exercise.

## Architecture

Four projects, split so the business rules can be tested without HTTP and the HTTP code can change without touching the rules:

- **`InventoryAggregation.Core`**: the models (`InventoryRecord`, `CategorySummary`), the `IInventoryRecordSource` abstraction, `InventoryAggregator` (all the grouping and arithmetic) and the typed `InventorySourceException`. It references no HTTP or ASP.NET package, and a test enforces that.
- **`InventoryAggregation.Infrastructure`**: everything about talking to the upstream API. `InventoryApiClient` is a typed client from `IHttpClientFactory` with the Microsoft resilience handler (built on Polly v8) and maps every HTTP or transport failure to the typed exception. `InventoryRecordCollector` pages through the dataset one page at a time and refuses to trust a `200`: it checks page echo, stable totals, that the page count matches the record count, the final record count, duplicates and every field. `CachingInventoryRecordSource` wraps the collector with a time-limited cache. Options and validation live here too.
- **`InventoryAggregation.Api`**: a minimal API with one route. It calls the source, runs the aggregator, shapes the JSON, and turns the typed failures into problem responses.
- **`tests/InventoryAggregation.Tests`**: xUnit tests for all of the above.

The flow is: endpoint -> cache -> collector -> client -> upstream API, and the aggregator runs on the validated records. Dependencies point inward (Api and Infrastructure depend on Core, never the other way), so Core can be reused or tested alone. The cache sits between the endpoint and the collector so a failure is never stored and concurrent callers share one fetch.

## Judgement calls (open decisions)

The assignment left these open. Each was discussed and approved before it was implemented.

| ID   | Decision                                                                                                                                                                                                                                                                                                                |
| ---- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| D-1  | Target .NET 10 (LTS). .NET 8 support ended 10 November 2026                                                                                                                                                                                                                                                             |
| D-2  | Use `Microsoft.Extensions.Http.Resilience` with the retry, timeout and circuit-breaker values set explicitly (the rate limiter keeps the library default of 1,000 permits and no queue; see P-2). It adds retries, a per-attempt timeout, a total timeout, a circuit breaker and a rate limiter around the `HttpClient` |
| D-3  | If the full dataset cannot be collected, return an error. Never return partial figures                                                                                                                                                                                                                                  |
| D-4  | A repeated `id` with identical content is kept once. Conflicting content for the same `id` is an error                                                                                                                                                                                                                  |
| D-5  | Categories are grouped ignoring case and surrounding spaces. The display name comes from the lowest-`id` in-stock record                                                                                                                                                                                                |
| D-6  | Any invalid record is an error: blank category or name, negative or unreadable price or quantity, unreadable date. Price 0 is valid (a free item still counts as available). Quantity 0 is valid but excluded. A `lastUpdated` without an offset is read as UTC because the contract says the field is UTC              |
| D-7  | In-memory cache of complete, validated results only: 5 minutes by default, `InventoryApi:Cache:Ttl` to change it, `00:00:00` to turn it off. Failures are never cached. Concurrent requests share one fetch                                                                                                             |
| D-8  | Route `GET /api/inventory/categories`; response shaped like the assignment sample                                                                                                                                                                                                                                       |
| D-9  | Fetch pages one at a time with page size 25, and stop at the first failed page                                                                                                                                                                                                                                          |
| D-10 | If records tie for the latest `lastUpdated`, the highest `id` wins. **This assumes ids grow over time**, which I could not verify. Money is summed exactly in `decimal` and only the final category total is rounded                                                                                                    |
| D-11 | Upstream unavailable is 503, timeout is 504, unusable data or a rejected key is 502, and 500 is only for our own bugs                                                                                                                                                                                                   |
| D-12 | No GitHub Actions workflow (listed under next steps)                                                                                                                                                                                                                                                                    |
| D-13 | Up to 3 retries with jittered growing waits, `Retry-After` honoured, 10 s per attempt, 30 s per page. Only transient failures are retried (network errors, timeouts, 408, 429, 5xx). 400, 401, 403 and 404 are never retried                                                                                            |
| D-14 | `totalPages` must equal ceil(`totalRecords` / 25) (an empty dataset may report 0 or 1 pages); otherwise the data is unusable (502). This bounds the paging loop by the data's own consistency. There is no page cap or overall deadline, and no limit on records per page: the real API returns 26 records on some pages because it repeats an earlier record, which is dropped as an identical duplicate (D-4)                                      |
| D-15 | `decimal` overflow while totalling (absurdly large prices or quantities) is treated as unusable upstream data (502), not as a bug in this service (500). There is no maximum price                                                                                                                                      |
| D-16 | A rejected key (401/403) is logged at Error; other upstream failures stay at Warning. The caller's response is unchanged                                                                                                                                                                                                |
| D-17 | `InventoryApi:BaseUrl` must be https, except for a local address (`localhost`, `127.0.0.1`, `[::1]`), so the key is never sent in clear text across a network                                                                                                                                                           |
| P-1  | The final total rounds half away from zero (0.005 becomes 0.01)                                                                                                                                                                                                                                                         |
| P-2  | The resilience values equal Microsoft's standard defaults (see above)                                                                                                                                                                                                                                                   |
| P-3  | The cache is one dataset with its own expiry rather than `IMemoryCache`, because `GetOrCreateAsync` is not atomic and a lock would be needed anyway. One caller cancelling does not cancel the shared fetch                                                                                                             |
| P-4  | The API key setting is `Prolog-Mobile-API-Key`, and only that name                                                                                                                                                                                                                                                      |
| P-5  | Category display names are title-cased (`TOYS` becomes `Toys`, `home & kitchen` becomes `Home & Kitchen`)                                                                                                                                                                                                               |

## Assumptions and known limitations

**Strictness.** One bad record fails the whole endpoint (D-3, D-6). That is deliberate: a wrong figure is worse than an error. The cost is that one malformed record upstream makes the endpoint return 502 until it is fixed. There is no partial-results mode.

**Cache staleness.** Results can be up to 5 minutes old, and the cache is per process, so two instances would each fetch. The dataset is described as static, so this is a performance choice rather than a correctness risk.

**Speed.** Pages are fetched sequentially, so a cold request is slow (about 11 s in my one run against the real API). Parallel fetching is faster but makes the consistency checks harder; I did not measure whether it is needed.

**Resilience caveats.**

- The circuit breaker needs 100 calls in 30 s before it can open. This service makes about one call per page, so in practice it will almost never open. It is configured, and tests lower the thresholds to prove it opens and recovers, but it adds little protection at this volume.
- A long `Retry-After` counts against the 30 s per-page budget, so a value longer than the remaining budget should end the page as a timeout (504). I did not test that exact case.
- Invalid resilience option values fail at startup, not on the first request. I checked 11 invalid values (out-of-range numbers, a total timeout not greater than the attempt timeout, unparsable text); all stopped startup. Most of that validation comes from the resilience library, not from this repository's code.
- The library defaults come from documentation. Against the real API I observed repeated records (some pages carry one extra, identical record from an earlier page, which the collector drops), a correct `totalPages` for the record count, and throttling at 5 requests per 10 seconds (HTTP 429, seen when I called the API quickly by hand; the service's own retries honour `Retry-After`). I did not observe shifting totals or a failing page. Handling of those cases is covered by tests with fake responses, not by real failures.

**Data and display.**

- The tie rule assumes ids grow over time (D-10).
- Title casing is a simple rule: all-caps words are not preserved (`TV` becomes `Tv`), small words are capitalized (`Arts And Crafts`), and it is designed for English-style names. A list of exceptions would be the next step if acronyms appear.
- Grouping merges case and spacing variants but not spelling variants (`Electronic` and `Electronics` stay separate).
- Totals use the upstream `price` as given; no currency handling beyond the contract's "USD".
- The page-count rule (D-14) assumes the upstream reports `totalPages` as ceil(`totalRecords` / 25). It held against the real API in one run (7 pages for 157 records, HTTP 200, 136 in-stock records, about 12 s cold), and it is covered by tests with fake pages. It does not bound a dishonest upstream that inflates `totalRecords` too, and there is still no overall time limit across pages, only the 30 s per page.

**Not done.**

- The endpoint has no authentication, rate limiting or HTTPS redirection. It is meant to run behind whatever the host provides.
- There are no health checks, metrics or tracing beyond the framework's default logging.
- No CI pipeline.

## What I would do next

1. Add a GitHub Actions workflow that builds and runs the tests on every push (D-12).
2. Offer an opt-in partial-results mode that reports which pages or records were skipped, for callers who prefer degraded data to an error.
3. Replace the fixed cache lifetime with background refresh or explicit invalidation, and decide whether a stale copy may be served while a refresh fails.
4. Measure the cold fetch, and add bounded parallel paging only if it is needed and the consistency checks still hold.
5. Add the long-`Retry-After` test.
6. Add health checks and basic metrics (cache hits, upstream failures by kind).
7. Add an explicit exceptions list for acronym categories if real ones appear.
