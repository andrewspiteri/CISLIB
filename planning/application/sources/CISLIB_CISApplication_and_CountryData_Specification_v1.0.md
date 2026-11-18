# CISLIB — CISApplication and CountryData

**Implementation specification, version 1.0**  
**Date:** 17 November 2026  
**Target:** CISLIB, C# / .NET 10 first; preserve common behaviour for future language bindings  
**Companion:** `CISLIB_CISApplication_and_CountryData_Implementation_Prompt.txt`

## 1. Required outcome

Provide `CISApplication` as the normal entry point for a CISLIB application. It must automatically compose the standard CIS services, dependency injection, startup, operational HTTP integration where selected, and shutdown. A consumer supplies application services, routes and intentional configuration choices.

Consolidate the existing developer examples into one maintained country-data application at `implementations/csharp/examples/CountryData`. It fetches country names and ISO2/ISO3 codes, serves equivalent lookups through `GET /country/{identifier}`, and refreshes its in-memory data through CIS Heartbeat once an hour.

**Removal of the superseded examples is part of this change and a completion condition.** The replacement must not be delivered alongside the old tutorial applications or with cleanup deferred to a follow-up. Preserve useful distinct verification through the existing native tests and minimal fixtures.

This document specifies new work. Proposed facade members are not evidence that those members already exist. Repository findings below were inspected at commit `7b5923c031027b932f730e1cf45b2d51270b17cc`. The implementation agent must inspect its actual checkout and integrate later accepted changes rather than reset to this baseline.

### Scope

- A small facade over the existing .NET host, Microsoft DI, CIS startup coordinator and module integration methods.
- Automatic integration of implemented core modules and supported installed optional integrations.
- One real CountryData application, its usage documentation, focused acceptance coverage and complete example cleanup.
- Small necessary improvements to module composition where existing APIs cannot satisfy the automatic startup contract.

The work does not introduce a DI container, scheduler, cache framework, routing framework, runtime plugin platform, test harness, or a new configuration backend. It does not implement other languages, change public module semantics, or run a new release/mutation/benchmark campaign. The separately specified REST client remains a dependency; this feature must not create a competing client.

## 2. Existing contracts to preserve

The inspected repository already has the following responsibilities. Reuse their actual implementations and current documentation [S1–S7].

| Responsibility | Existing contract |
|---|---|
| Type Manager | Complete `TypeManager.CreateAsync` before building the root provider; register that exact prepared instance. It supplies a type catalog and does not independently enroll application startup services. |
| Configuration | Discover through Type Manager, retain concrete configuration roots, and load initial common/application values before consumers are constructed. Configuration activation precedes dependent `IStartable` participants. |
| Startup | Construct all scheduled root startup services before invoking any participant start method; await registered `IStartable.StartAsync(CancellationToken)` in registration order. Preserve identity-based start-once and retained startup outcome semantics. |
| Healthcheck | The existing coordinator/host integration owns startup completion and stopping. The web readiness probe uses the existing 500/200 contract; the business-readiness gate uses 503 while closed. |
| Heartbeat | Same-instance work does not overlap. `Interval` runs from full execution settlement; `InitialDelay` defaults to zero. Shutdown/wait cancellation does not automatically cancel a running callback. |
| Metrics | Core consumer abstractions remain separate from optional `CIS.Metrics.OpenTelemetry`. Use one provider, bounded dimensions, central identity, native HTTP observations and existing callback-owner drain. |
| Packaging and verification | Preserve the approved Microsoft dependency closure, `Microsoft.AspNetCore.App`, explicit `CIS.*` namespaces, genuine Type Manager build machinery and native MSTest workflow. |

Configuration scope identity and telemetry identity are distinct. The host-project/entry assembly name determines the Configuration application scope. Metrics uses the authoritative `EnvironmentOptions.ApplicationName` and `EnvironmentOptions.EnvironmentName`. A telemetry-name override must not silently redirect configuration storage.

## 3. Consumer API and ownership

### APP-01 — Small application entry point

The default non-web usage must support the user's constructor form, including an optional customization callback:

```csharp
internal static async Task<int> Main(string[] args)
{
    await using CISApplication app = new(args, builder =>
    {
        builder.Services.AddSingleton<IStartable, ApplicationWorker>();
    });

    return await app.RunAsync();
}
```

Also support `new CISApplication(args)`, `new CISApplication()` and `new CISApplication(builder => { ... })` without ambiguous ordinary overload use. The callback exposes normal .NET `IServiceCollection` registration. C# hosting/DI integration may use platform types; the portable specification concerns responsibilities and lifecycle behaviour.

Use an explicit web factory so a non-web application does not acquire an HTTP listener merely by constructing the facade. The target web shape is:

```csharp
internal static async Task<int> Main(string[] args)
{
    await using CISApplication app = CISApplication.CreateWeb(
        args,
        builder =>
        {
            builder.Services.AddSingleton<CountryCatalog>();
            builder.Services.AddSingleton<IStartable>(
                provider => provider.GetRequiredService<CountryCatalog>());
        },
        web => web.MapGet("/country/{identifier}", CountryEndpoints.Get));

    return await app.RunAsync();
}
```

`CreateWeb` is part of this proposed facade. Its final callback receives the ordinary ASP.NET Core `WebApplication` and executes once after the final build and before host startup. It must use the same host and service provider. Keep the routing API native. The sample uses the actual implemented signatures and includes the necessary namespaces.

The canonical `Program` may contain application registrations and route mapping. It must not contain manual Type Manager preparation, configuration preparation, health construction, CIS hosted-service ordering, metrics installation, heartbeat engine startup, readiness signalling, or teardown barriers. Moving that boilerplate into a sample-local helper does not satisfy APP-01.

### APP-02 — Small lifecycle surface

| Member | Required behaviour |
|---|---|
| Constructor / `CreateWeb` | Collect and validate setup choices and registrations. No asynchronous bootstrap, upstream fetch, timers, listener startup or fire-and-forget initialization. |
| `StartAsync(CancellationToken)` | Await preparation and actual host/CIS startup. Return only after required initialization succeeds. Preserve the original failure/cancellation outcome for callers. |
| `RunAsync(CancellationToken)` returning `Task<int>` | Start the application, remain running until shutdown is requested, and complete orderly stopping. Return 0 for normal shutdown or recognized requested cancellation; return a nonzero result for failures actually observed on the facade's start/run/stop path, with the cause recorded. |
| `StopAsync(CancellationToken)` | Signal stopping and join the existing stop/drain operation. The token must not make live dependency disposal safe merely by ending a caller's wait. |
| `DisposeAsync()` | Complete safe asynchronous cleanup, including resources acquired before a failed build. Repeated cleanup is safe and joins the existing operation. |

Reuse ordinary host shutdown signals. Do not call `Environment.Exit` inside the library. Configuration/registration programming errors may throw synchronously before resource acquisition. `StartAsync` may surface startup exceptions; the REST client's result-based business-failure contract does not prohibit startup failure.

`DisposeAsync` failures propagate to its caller; a later disposal failure cannot retroactively change an already returned `RunAsync` value. Preserve the host's existing background-service failure policy. Do not add an arbitrary background-task failure detector merely to promise an exit code for failures the facade cannot observe.

One application instance owns one host lifetime. Repeated or concurrent starts must reuse the established single startup operation; stopping is terminal and cannot be followed by renewed readiness or restart. Do not add a general lifecycle state-machine framework.

### APP-03 — One provider and defined resource ownership

Use one real .NET host and one root service provider. Do not build temporary providers to obtain bootstrap dependencies. Pre-container Type Manager and Configuration resources must have explicit ownership, including when preparation or Build fails before DI can own them.

Use existing module disposal contracts. The startup coordinator does not separately dispose container-owned objects. Factories exposing singleton aliases must resolve the same object; cleanup of built-in and sample objects must tolerate repeated disposal calls without repeated destructive effects. Do not promise that native DI calls every aliased object's disposal method exactly once.

The facade must have one clear host-disposal path. If the chosen .NET run helper also disposes its host, account for that ownership or compose start/wait/stop directly. Do not race independent disposal paths. Existing externally supplied resource ownership remains unchanged.

## 4. Automatic composition

### APP-04 — Register standard capabilities internally

Use a readable internal composition method for shipped core modules. Reuse their existing registration methods and lifetimes. Application code must not repeat `AddCisHeartbeat`, `AddCisStartup`, health construction, configuration preparation or the normal metrics integration sequence.

Automatically compose implemented Logging, Type Manager, Configuration, Healthcheck, Heartbeat, Startup, Metrics abstractions and the implemented REST client. Preserve existing defaults and explicit disabling. Registering a client does not perform a request. User application classes retain their explicit registrations and lifetimes; do not scan arbitrary application types into singleton services.

Apply the callback before final composition is sealed. Reuse existing options and normal DI replacement mechanisms. A supported replacement must be used consistently by startup, health, metrics and shutdown; the facade must not silently restore a default after customization. `AddSingleton` and full replacement are different operations, especially for `IEnumerable<T>`. Do not introduce a new universal override container.

The configuration activation participant must remain before dependent application participants. Put the existing Metrics/Heartbeat/Startup hosted bridges in their required positions after collecting ordinary application hosted services; preserve the relative order of application registrations. Enforce the currently required sequential hosted starting/stopping and normal scope/build validation.

### APP-05 — Default configuration and optional integrations

**Configuration without an external store:** a basic application must run from initialized configuration-object defaults without requiring a storage service or custom bootstrap code. Use the Configuration module's existing support where available; otherwise add the smallest defaults-only adapter/mode that preserves its retained-root and reader contracts. This mode reads no external storage, runs no polling and acquires no external watcher. Its absence of external updates must be explicit, not an invented polling success or false reliability claim.

If the existing preparation path requires a store, a small internal adapter can return its ordinary configuration-not-found result. It must not claim to persist writes or create a second configuration implementation. Do not silently map host `appsettings.json` into CIS configuration roots.

Selecting an external bootstrap-capable store remains an application choice. The facade then owns Type Manager preparation, initial common/application loading, registration and activation using that store. Keep the existing validation, checksums, root identity, refresh policy and host-name semantics. Do not choose a database, file format or network configuration service automatically. Existing explicit store modes and their polling/criticality requirements remain intact.

A selected real store's error must follow the established failure policy, not silently switch the application into defaults-only mode. A legitimate missing configuration record remains distinct from a storage failure.

**Optional Metrics exporter:** keep `CIS.Metrics.OpenTelemetry` optional and outside Core's dependency graph. When the supported adapter is installed and enabled, the facade must automatically connect its existing service registration and HTTP integration. The canonical sample references this adapter so `/_metrics` is demonstrated without manual wiring in `Main`. Without the adapter, retain the existing Core disabled/no-op behaviour; do not claim an exporter or endpoint is active.

Reuse an existing integration hook if one exists. If necessary, add one narrow registration hook in Core for supported optional CISLIB integrations, implemented by the adapter and resolved through the already prepared Type Manager. Limit it to service registration and attaching existing host/pipeline integration. Use deterministic ordering and reject duplicate conflicting contributions. Do not add filesystem scanning, runtime plugin loading, a dependency-graph engine, or extra lifecycle machinery. Core's ordinary registrations remain an explicit list.

The registrar must be constructible without a service provider and perform registration only: no service startup, external I/O or temporary provider. Deduplicate its type and invoke it once before final Build. The current required optional contributor is the Metrics adapter; do not generalize beyond the small hook needed for it.

The implementation must document the small public configuration surface that selects stores, options, integration enablement and host settings. Do not multiply settings across `CISApplication`, EnvironmentOptions and module options.

### APP-06 — Preparation, startup and readiness order

Perform the following operations in order:

1. Collect host arguments, application registrations and deliberate option overrides; identify the actual host assembly.
2. Complete Type Manager preparation and retain its result.
3. Prepare initial configuration/default roots, including validation, before constructing configuration-consuming services.
4. Complete and validate automatic registrations, including supported installed integrations and the existing lifecycle bridge order; build one final provider/host.
5. Attach the reserved HTTP pipeline and application endpoints for web mode.
6. Through the existing startup coordinator, construct every scheduled root startup participant and dependency before invoking any participant start method.
7. Await the Configuration activation and other `IStartable.StartAsync` participants in their final registration order. Preserve identity-based deduplication and failure retention.
8. Complete the existing host/coordinator success path and readiness transition. Successful host startup and required initialization are prerequisites to reporting the application ready.

Do not eagerly resolve unrelated scoped/transient registrations. Constructor dependency order is normal DI order; the guaranteed sequential initialization order is the `IStartable` registration order.

Preserve the actual web lifecycle: the listener may already serve operational requests while Stage 2 is incomplete. Keep routing/metrics, health and then the business-readiness gate in the supported order. Preserve `/_health/ready` returning 500 while NotReady and 200 while Ready, and business requests returning 503 without business side effects while gated. Do not create another management host or change probe status contracts.

Required initialization must run directly in `StartAsync`; it must not wait for a Heartbeat callback whose eligibility depends on initialization completing. Metrics scrapes and health reporting must never trigger initialization, country refresh or remote I/O. Preserve management-access policy and existing optional pipe transport selection.

### APP-07 — Cancellation and stopping

Expose/reuse the existing application-stopping token through the host lifetime service. Owned application I/O must be able to observe shutdown. Link the appropriate operation/startup token and application-stopping token when executing CountryData refresh. Dispose temporary linked-token sources after the owned operation ends.

On stopping, use the existing health stopping boundary, close new work admission and signal cancellation to owned provider/REST operations before awaiting their barriers. Join actual startup, Configuration, Heartbeat and Metrics work using the existing integration, then dispose dependencies and flush/close logging in a safe order.

An expired wait, caller cancellation or failed scrape is not proof that protected work stopped. Do not dispose dependencies while that work still uses them or report successful shutdown before the actual barriers complete. This does not add forced termination: preserve the modules' cooperative cancellation semantics.

## 5. CountryData application

### DATA-01 — Endpoint and stable response

Expose `GET /country/{identifier}`. The following inputs return equivalent 200 JSON country data:

| Identifier | `name` | `iso2` | `iso3` |
|---|---|---|---|
| `MT` | Malta | MT | MLT |
| `MLT` | Malta | MT | MLT |
| `Malta` | Malta | MT | MLT |
| `mt`, `mlt`, `malta` | Malta | MT | MLT |

```json
{
  "name": "Malta",
  "iso2": "MT",
  "iso3": "MLT"
}
```

Trim the framework-decoded identifier and use ordinal case-insensitive exact matching. Preserve the provider's display name and normalize stored codes to uppercase. Multiword names use normal URL encoding. No fuzzy matching, arbitrary translations or prefix guessing is required.

An unknown identifier returns 404 with stable application error code `country.not_found`. An identifier that becomes empty after trimming returns 400 with `country.invalid_identifier`. A missing route segment may follow the framework's ordinary 404 routing behaviour. Before readiness, the existing business gate supplies 503. Do not make an upstream request for any lookup, including a miss.

The initial public country contract contains the three fields above. Additional provider fields are not necessary for acceptance. Do not expose the upstream envelope, credentials, raw transport errors or provider-specific property names through this endpoint.

### DATA-02 — Real REST source

Default source: `https://countriesnow.space/api/v0.1/countries/iso`, verified during specification preparation [S9]. Its JSON envelope contains `error`, `msg` and `data`; each data item includes `name`, `Iso2` and `Iso3`. Keep the source URL configurable and preserve this provider mapping separately from the public country contract.

Use the accepted, implemented CIS REST client and `RestResult<T>`. Inspect `Success`, `ErrorCode`, `ErrorMessage` and `Value` through its actual API. Map `error: true` to a failed logical result even if HTTP is 200. Reject a null, empty or malformed dataset. Validate nonempty names, valid two-/three-letter codes and conflicting aliases; do not silently let the last duplicate record win. Collapse only duplicates that represent the exact same canonical record, or reject the candidate consistently.

Configure the existing transient GET retry policy explicitly and use one overall request timeout, default 30 seconds, including attempts, waits and body/mapping work under the REST contract. Use the accepted response-size policy. Do not add a sample-owned retry loop, timer race, alternate HTTP stack or detached timed-out operation. Provider business errors and invalid data do not become transport-retry candidates merely because refresh failed.

If the approved REST module is not yet in the target checkout, record that concrete dependency and sequence the affected work with its existing owner. Continue independent facade and cleanup preparation tasks. Do not implement an abbreviated REST client inside the sample, revive the old hardcoded source as the delivered live mode, or claim the integrated sample complete until the actual dependency is available.

### DATA-03 — One cache and atomic publication

Use one application-owned `CountryCatalog` instance for startup, endpoint lookup and Heartbeat refresh. A small privately immutable snapshot is sufficient: canonical country records, their lookup index, distinct country count and last successful publication timestamp. This is application code, not a new generic caching module.

Build and validate the entire replacement away from the live snapshot. All name/ISO2/ISO3 aliases for a country point to the same canonical record. Publish the replacement through one atomic reference swap. Each request captures the published snapshot once; published records and dictionaries are not mutated. Conflicting identifiers fail validation rather than returning arbitrary countries.

A failed refresh changes neither the published data nor its last-success timestamp. Do not clear the live cache before downloading or updating it. Memory use must not accumulate previous snapshots or refresh histories. Use existing REST bounds; avoid a hardcoded expected world-country count.

### DATA-04 — Initial load and hourly schedule

`CountryCatalog` implements the existing `IStartable` and `IHeartbeatSubscriber` contracts. Its initial start awaits the complete refresh/publication operation and records usable data with the existing availability monitor. Failure after the configured REST policy ends fails startup with a clear diagnostic; readiness stays closed.

After initial publication, register the same initialized instance through its real Heartbeat owner. Keep both the subscriber and owner alive for the application lifetime. The registration remains inactive until the existing startup integration permits it.

```csharp
new HeartbeatRegistrationOptions
{
    Name = "Country data refresh",
    InitialDelay = TimeSpan.FromHours(1),
    Interval = TimeSpan.FromHours(1)
}
```

Both values use the configured refresh interval, default one hour. Initial delay begins at readiness for this pre-readiness registration; subsequent intervals begin after full previous execution settlement. This is completion-based periodic work, not an exact clock-hour schedule. Do not add another scheduler or overlapping refresh trigger.

The callback awaits the same refresh operation used at startup and returns true only after a valid publication. An expected failed `RestResult` returns false through the existing Heartbeat result path; it need not throw a synthetic business exception to obtain failure reporting. Retain the existing unexpected-exception handling and log a failure once at the owning boundary.

### DATA-05 — Availability, configuration and shutdown

After the first successful publication, a failed hourly refresh retains usable data and reports the failed refresh through the existing availability/logging/metrics integration. The sample stays able to serve that snapshot. Its default policy has no expiry that automatically clears valid country data. Last-success time remains observable; a new freshness framework or arbitrary stale-data deadline is out of scope.

`CountrySettings` uses the existing Configuration contract. The core settings are source URL, refresh interval (one hour), request timeout (30 seconds), and selection of the existing REST retry policy. Obtain a consistent settings view through the existing configuration reader where necessary. For v1, these operational settings are captured for the catalog lifetime; changes require application restart. Do not add heartbeat unregister/re-register machinery for live rescheduling. Preserve module-level live-configuration behaviours in their existing tests.

Shutdown cancellation must reach an in-flight REST request and its owned body/mapping/retry work. The callback must settle before dependent services or the cache's monitoring resources are disposed. Expected application-stopping cancellation is not reported as an unexpected upstream fault and does not update the last-success timestamp.

The live sample uses the real provider. Controlled responses, failures, shortened intervals and test barriers belong in the test projects and enter through the established configuration/DI/HTTP injection points.

### DATA-06 — Logging and metrics

Demonstrate ordinary CIS logging for initial loading, refresh success/failure and shutdown. Include a safe failure code, elapsed work where already available and country count where useful. Do not log whole responses or credentials. Normal request logging follows the existing HTTP policy.

Reuse automatic inbound HTTP, REST logical/transport, Heartbeat, startup and health metrics. Do not add duplicate request/refresh duration counters merely to produce sample-specific names. The selected optional adapter exposes the real `/_metrics` endpoint; preserve exclusions for operational routes and the central `application`/`environment` labels.

Add only these small application-state observations, through the implemented CIS metrics API:

| Logical definition | Intended exported name | Kind / unit | Boundary and query |
|---|---|---|---|
| `app.country_cache.entries` | `app_country_cache_entries` | Observable gauge; count | Distinct records in the current published snapshot, not alias count. Shows cache size. Zero before publication is a real empty-cache state. |
| `app.country_cache.last_success_timestamp` | `app_country_cache_last_success_timestamp_seconds` | Observable gauge; Unix seconds | Publication time of the current snapshot. Omit before any success; failures leave it unchanged. Cache age can be derived as `time() - ...`. |

These are new sample metric definitions, not claims about existing catalog entries. Confirm actual exporter naming and record the final mapping in the sample guide. Reuse handles for the application lifetime; observations read only the existing snapshot and perform no I/O. No country name/code, raw identifier, raw URL, request ID or arbitrary error text becomes a dimension. Callback ownership/drain follows the existing Metrics contract. Disabled metrics must not change application behaviour.

## 6. Mandatory example consolidation and removal

### CLEAN-01 — Required disposition

The maintained examples tree must end with one canonical runnable tutorial application: CountryData. The following paths are relative to `implementations/csharp/` and must be removed after their useful behaviour and active consumers have been addressed [S8].

| Existing path | Disposition |
|---|---|
| `examples/startup/CountryList` | Evolve applicable code into CountryData; remove the old project and path. |
| `examples/healthcheck/CountryList` | Consolidate normal availability/readiness behaviour; remove the duplicate application. Retain distinct health/pipe/failure checks in focused tests. |
| `examples/heartbeat/Polling` | Demonstrate ordinary polling in CountryData; remove the old application. Retain meaningful negative-result, recovery and drain coverage. |
| `examples/logging/ConsoleHuman` | Demonstrate logging through CountryData; remove the unrelated order example. Preserve useful format/context/flush tests and short documentation. |
| `examples/metrics/MetricApi` | Demonstrate automatic/custom metrics through CountryData; remove the separate order example. Preserve optional-package compatibility checks. |
| `examples/type-manager/Console` | Remove the separate tutorial host. Retain only necessary process/discovery/package-consumer fixtures. |
| `examples/type-manager/Web` | Remove the separate tutorial host. Reuse CountryData or minimal fixtures for genuinely distinct scope/discovery/package cases. |
| `examples/type-manager/Contracts`, `Unused`, `Shared` | These are support code, not three more runnable examples. Retain only the smallest pieces required by a named surviving test; move those pieces under the existing test structure and remove redundant helpers. |

Remove each obsolete application's project file, source, exclusive helpers, lock/launch settings and obsolete application-only instructions. Update shared package references carefully; do not remove dependencies still used by current code.

**Do not copy whole old applications into `tests/fixtures`, an archive/legacy directory, excluded projects or compatibility modes to avoid changing the tests.** A surviving separate fixture needs a specific process, assembly, ownership or packaging requirement that the canonical sample cannot adequately verify. Reduce it to that purpose. Git history retains removed code.

This explicit cleanup direction supersedes earlier instructions that preserved these particular tutorial sources. It does not authorize removal of public library contracts, historical verification, completed-work accounting, or unrelated user work.

### CLEAN-02 — Keep the canonical application instructional

Remove embedded `--self-test`/controlled-failure modes, forced holds, test-only counters, assertions, observation reports and artificial recovery sequences from the delivered sample. Tests own their own controls and assertions and use the sample's real injection/configuration boundaries. Do not maintain per-module copies of CountryData.

Keep code understandable to a human reading the example. Suggested responsibilities are `Program`, country record, catalog/refresh logic, provider mapping, settings and endpoint mapping. Combine small related types where appropriate. Do not turn a suggested file outline into a mandatory layered architecture or add repository/service/factory abstractions without a concrete need.

### CLEAN-03 — Update current references in the same change

Search with `rg` for old project paths, assembly names, launch commands and fixture outputs. At minimum inspect:

- `implementations/csharp/CisLib.slnx` and active project/package files.
- `tests/CIS.Core.BusinessCases/CIS.Core.BusinessCases.csproj`, including CountryList/MetricApi references and copy targets.
- Business cases for Startup CountryList and Healthcheck CountryList and their shared expectations.
- `tests/CIS.Core.IntegrationTests/CountryListPackageFixture.cs` and Healthcheck `CountryListPackageTests.cs`.
- Heartbeat `HeartbeatPackageTests.cs`.
- `ConsoleHumanPackageFixture.cs` and `ConsoleHumanPackageTests.cs`.
- Metrics `MetricsPackageConsumerTests.cs`.
- Type Manager package-consumer fixtures/tests and their separate discovery assemblies.
- Root/module READMEs, current example guides, executable scripts, active test profiles and repository skills/instructions that route agents to old examples.

Update retained tests to CountryData or their minimal necessary fixture. Remove obsolete tests/helpers when meaningful replacement coverage already exists. Do not preserve old self-test JSON schemas merely to keep assertions unchanged.

Preserve historical records and supplied historical requirements as history. Do not rewrite a past pass as evidence for the new application or reopen a completed owner simply to update example paths. Clearly historical references may remain; current runnable/build/documentation references must resolve.

Provide one concise sample README with the real `dotnet run` command, framework/package requirements, the three Malta requests, startup/refresh/failure behaviour, settings and the operational endpoints. Link module guides to the canonical sample; retain short module-specific snippets where they explain a distinct API.

## 7. Acceptance and proportionate verification

Use existing C# MSTest projects and ordinary native execution. The rows below are behaviours to verify, not a requirement for one test per row or another testing framework. Combine related observations and reuse already meaningful module tests. A local HTTP fixture should exercise the actual REST mapping without relying on the public provider in automated tests. Use existing controllable timing or a short test-only configured interval; never wait an hour.

| ID | Acceptance evidence |
|---|---|
| A01 | A clean consumer starts with the constructor form and only application registrations; the web sample uses the explicit web entry, one route and `RunAsync`. All normal CIS wiring is inside the library. |
| A02 | One host/provider exists; the prepared Type Manager and retained configuration objects are the actual objects seen by consumers. Initialized configuration is available in constructors. Defaults-only startup needs no external store. |
| A03 | All scheduled root constructors finish before participant starts; participant starts remain sequential and same-object aliases start once. Startup failure/cancellation cannot produce positive readiness. |
| A04 | The real web host preserves early operational access, ready 500/200 and business 503 gating under the existing contract. No country request or scrape causes bootstrap/provider work. |
| A05 | Supported custom options/replacements are respected; the same CountryCatalog is used for lookup/startup/refresh. Core builds without the optional exporter; the sample with the adapter exposes actual metrics without manual registration. |
| A06 | `/country/MT`, `/country/MLT`, `/country/Malta` and case variants return equivalent records. Unknown/empty identifiers use the defined statuses. These requests make zero extra provider calls. |
| A07 | Initial load precedes readiness. Provider business failure, HTTP failure and invalid/empty data cannot publish a ready empty application. |
| A08 | Successful refresh atomically replaces all aliases. Failed or invalid refresh retains the previous records and last-success time. Readers never observe partially rebuilt indexes. |
| A09 | The sample sets both initial delay and interval to one hour by default; the actual Heartbeat path refreshes without overlap or an immediate duplicate prime. Tests use bounded controlled timing. |
| A10 | Shutdown reaches an in-flight REST operation and waits for actual callback/drain completion before dependency disposal. Partial startup and build failures clean up acquired resources safely. |
| A11 | A real scrape contains the required identity labels, automatic observations and correctly named cache gauges. Gauges reflect publication and retain values after refresh failure; collection performs no remote work. |
| A12 | The canonical sample has no embedded test mode. All seven superseded runnable example paths are removed; no whole old application has been preserved elsewhere as a disguised duplicate. |
| A13 | Active solution/project/test/profile/script/documentation/skill references resolve. Remaining historical references are identified as historical. Retained fixtures have a stated distinct purpose. |
| A14 | Affected builds and focused native tests pass through the current CIS workflow. Relevant package-consumer checks still exercise real packages where that boundary changed. Report the actual platform/scope and any unexecuted checks accurately. |

Use the REST module's existing retry/timeout/cancellation tests rather than retesting its entire transport in this sample. Preserve useful Configuration identity/reload, Type Manager discovery/deployment, Healthcheck reporting, Heartbeat ownership, Logging flush and Metrics compatibility coverage through the current test structure.

One bounded live-provider smoke is useful for confirming the documented live command when network access permits. It is supplementary, not a flaky automated acceptance dependency. Record inability to reach the provider separately from deterministic native results. Do not manufacture a pass, force saved-result reuse or broaden into unrelated qualification to remove a status warning.

## 8. Implementation workflow and completion

Import/preserve this document as a new feature input through the installed CIS workflow. Do not replace the product-wide BRD, recreate the workspace, upgrade CIS as a side task or copy a historical work allocation. Inspect command help and current repository instructions rather than invent commands.

Create a concise dependency-aware plan with tasks no larger than medium. Suggested outcomes are: (1) facade/lifecycle and automatic core composition; (2) optional integration/defaults completion; (3) CountryData application; (4) consolidation and active-reference cleanup. Split only when the actual implementation warrants it. Tests, documentation and independent review belong with their implementation outcomes rather than separate approval-only tasks.

Parallelize independent work after the shared public API is established. Assign nonoverlapping file ownership; one owner coordinates shared projects, package files, solution changes and builds using shared outputs. Cleanup inventory and documentation preparation can run alongside the facade/sample; final deletion follows replacement and reference migration. All cleanup remains in this delivery.

Read the current root `AGENTS.md`, dependency policy, startup/configuration/heartbeat/health documentation and the repository Metrics skill/contract before editing. Update existing instructions and skill links to the new canonical example. Do not create a personal skill or an additional generic workflow skill for this feature.

Use current eligible CIS work ownership and bounded native verification. Reuse existing session authorization; do not ask again about settled design or the explicitly requested cleanup. If a genuine tool/workflow authority requirement is still unsatisfied, complete the authorized preparation, identify the exact blocking requirement and present the concrete next action. Do not fabricate approvals or alter protected accounting.

### Required completion report

Report, concisely:

1. Implemented facade/API and how automatic registration preserves the existing lifecycle.
2. The canonical sample path and exact run command, including actual package requirements.
3. Removed example applications and active references updated.
4. Remaining dedicated fixtures, each with its distinct requirement; identify any unresolved active old references.
5. Actual focused build/test/package observations and platform scope, plus any live-provider smoke outcome.
6. Remaining concrete dependency or implementation gaps. Do not claim completion while the requested cleanup or real REST integration is incomplete.

Keep documentation and evidence small and useful. Do not retain replacement archives, large run bundles, duplicate plans or an unrelated tooling project. Publication, main-branch merge and full release qualification are separate actions.

## 9. Sources and implementation references

These links ground the inspected baseline; later accepted repository contracts take precedence over an outdated source snapshot.

- **[S1] Repository instructions:** [AGENTS.md](https://github.com/andrewspiteri/CISLIB/blob/7b5923c031027b932f730e1cf45b2d51270b17cc/AGENTS.md).
- **[S2] Startup lifecycle and current web gating:** [Startup README](https://github.com/andrewspiteri/CISLIB/blob/7b5923c031027b932f730e1cf45b2d51270b17cc/docs/modules/startup/README.md).
- **[S3] Configuration:** [Hosting](https://github.com/andrewspiteri/CISLIB/blob/7b5923c031027b932f730e1cf45b2d51270b17cc/docs/modules/configuration/hosting.md), [retained objects and identity](https://github.com/andrewspiteri/CISLIB/blob/7b5923c031027b932f730e1cf45b2d51270b17cc/docs/modules/configuration/core.md).
- **[S4] Heartbeat:** [Lifecycle and cancellation](https://github.com/andrewspiteri/CISLIB/blob/7b5923c031027b932f730e1cf45b2d51270b17cc/docs/modules/heartbeat/README.md), [registration options](https://github.com/andrewspiteri/CISLIB/blob/7b5923c031027b932f730e1cf45b2d51270b17cc/implementations/csharp/src/CIS.Core/Heartbeat/Contracts/HeartbeatRegistrationOptions.cs).
- **[S5] Metrics:** [Hosting and callback ownership](https://github.com/andrewspiteri/CISLIB/blob/7b5923c031027b932f730e1cf45b2d51270b17cc/docs/modules/metrics/hosting.md); read the current `.github/skills/cislib-metrics/SKILL.md`, portable contract and metric catalog in the implementation checkout.
- **[S6] Existing starting point:** [Startup CountryList Program](https://github.com/andrewspiteri/CISLIB/blob/7b5923c031027b932f730e1cf45b2d51270b17cc/implementations/csharp/examples/startup/CountryList/Program.cs), [CountryCatalog](https://github.com/andrewspiteri/CISLIB/blob/7b5923c031027b932f730e1cf45b2d51270b17cc/implementations/csharp/examples/startup/CountryList/CountryCatalog.cs).
- **[S7] Native .NET foundations:** [Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host), [DI guidelines](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines), [registration semantics](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/service-registration).
- **[S8] Cleanup inventory:** [Examples tree](https://github.com/andrewspiteri/CISLIB/tree/7b5923c031027b932f730e1cf45b2d51270b17cc/implementations/csharp/examples), [business-case project references](https://github.com/andrewspiteri/CISLIB/blob/7b5923c031027b932f730e1cf45b2d51270b17cc/implementations/csharp/tests/CIS.Core.BusinessCases/CIS.Core.BusinessCases.csproj), [Type Manager examples and package fixtures](https://github.com/andrewspiteri/CISLIB/blob/7b5923c031027b932f730e1cf45b2d51270b17cc/implementations/csharp/examples/type-manager/README.md).
- **[S9] Provider response:** [CountriesNow country ISO endpoint](https://countriesnow.space/api/v0.1/countries/iso), verified 17 November 2026. Provider availability/data is external; the application contract and deterministic tests remain locally controlled.
- **REST dependency:** the accepted `CISLIB_RestClient_Implementation_Specification_v1.0.md`, its agent guide and the actual implemented REST API in the target checkout. Resolve these existing feature inputs; do not substitute an invented API from this document.
