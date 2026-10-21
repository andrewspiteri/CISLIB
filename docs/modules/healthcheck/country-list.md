# CountryList development example

The [CountryList source](../../../implementations/csharp/examples/healthcheck/CountryList/Program.cs) is an application-owned startup and shutdown composition using the real Core service, binder, collector runner and reporting adapters. It uses a deterministic local data source in place of an application's remote data client. The project references Core source for development. It must not be advertised as an exact-package reference example until compiled and executed against that exact verified package.

Authoring status: source and usage authored; coordinator compilation, self-test execution and business/native evidence are recorded separately in [HC11 evidence](../../../planning/healthcheck/evidence/HC11). An example command is not a claimed execution result.

First change from the repository root into the C# implementation directory so `global.json` selects the pinned SDK:

```text
cd implementations/csharp
dotnet run --project examples/healthcheck/CountryList -- --urls http://127.0.0.1:5080
dotnet run --project examples/healthcheck/CountryList -- --self-test
dotnet run --project examples/healthcheck/CountryList -- --self-test --fail-prime
dotnet run --project examples/healthcheck/CountryList -- --self-test --fail-refresh-registration
dotnet run --project examples/healthcheck/CountryList -- --self-test --fail-later-startup
```

Normal operation remains running until host shutdown. `--fail-prime` makes mandatory initialization fail and retains the reporting host with startup incomplete. It creates no recurring refresh registration. `--fail-refresh` allows priming to succeed but makes subsequent actual refresh fetches fail; the noncritical threshold-three refresh policy flags while the usable cache and critical availability remain intact. The five-minute interval is an example choice. Its first periodic attempt is due at activation rather than after five minutes.

`--fail-refresh-registration` selects an explicit two-monitor capacity: the startup/availability monitors consume it, so the required refresh registration fails after usable priming. `--fail-later-startup` makes the actual required country-response contract setup fail after usable priming and before refresh creation. Both keep readiness false, retain the independent critical startup condition and admit no business request. Select at most one startup-failure demonstration flag. Environment overrides still take precedence over explicit setup; clear `CIS_HEALTHCHECK_MAX_MONITORS` when running the controlled registration-failure demonstration.

The application explicitly:

1. Constructs health objects, retained startup/availability monitors, a prime binding, the owning catalog and the existing HTTP host without fetching data.
2. Installs the reserved health branch before its business request gate/middleware, then starts HTTP/optional pipe reporting and the collector runner.
3. Awaits its actual initial fetch, validates and publishes the replacement list, and synchronously reports the actual priming Outcome/Settled facts. Availability success declares usable-data publication; settlement is a separate fact. It then completes required response-contract setup before declaring startup success.
4. Only after successful priming, creates the noncritical threshold-three refresh monitor and inert registration. It marks startup complete only after required setup succeeds, then activates periodic refresh in normal operation.
5. On failed priming, stops dependent startup and retains its independent critical startup-failure monitor. No startup retry or recurring registration is invented. Reporting remains available for the supervisor's decision.
6. On shutdown, publishes terminal stopping, closes collection, awaits actual callback/lease settlement and owner-dependent handoffs, then disposes monitors/owner dependencies and closes reporting.

`CountryCatalog` retains usable data until a fully validated replacement is ready. Ordinary refresh failure cannot discard it. Its owner-held registration protects a real `IHealthCollectionLease`; component disposal closes lease admission and waits for actual acquired leases before disposing `CountrySource`. The service registry does not retain this owner while idle.

`--self-test` starts real Kestrel on an ephemeral loopback port. It asserts health/readiness and the business gate before and during actual in-progress priming, then explicit startup publication, three actual failed manual refresh attempts, retained usability, an independent critical flag/recovery, reporting bypass of business-side effects, terminal stopping, and actual owner dependency drain. The application-owned source barriers block real work until explicitly released; pending prime cannot count as completed startup, and requested shutdown cancellation cannot certify actual collection completion or release its owner lease. Failure self-tests check retained startup failure, no refresh registration and no business admission; later-step failure keeps the actual usable cache available. The failed-prime path also disposes its child availability monitor before checking the still-live startup monitor. It prints one bounded JSON success record only after its assertions pass; assertion failure returns a nonzero process exit through the runtime. These checks qualify the development example only when actually executed by the coordinator.

The JSON record includes actual detached snapshot observations at each stage, including counts, policy state, decimal refresh-failure count, cached-country count and accepted business-request count. It records actual owner-disposal pending/completed observations and the runtime description. Native process tests should independently inspect those observations; `passed: true` alone is insufficient candidate evidence.

The self-test uses controlled manual collection rather than elapsed periodic timing to make assertions deterministic. Normal operation explicitly activates periodic collection after startup. Set the documented environment variables to enable a local pipe, then invoke the matching probe from the same identity/context; the self-test alone does not invoke or qualify the probe executable.

The prime binder demonstrates real in-process fact commitment for an explicit application operation. It is not Heartbeat integration. The actual Heartbeat scheduler and future `IStartable` coordinator are absent: no scheduler/coordinator substitute is supplied, and their activation, instance-identity/at-most-once, failure/cancellation, OnError and final owner-handoff cases remain pending at their real integration boundaries.
