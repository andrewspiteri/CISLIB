# CIS.CommonTesting

Optional net10.0 helpers for consumer tests, using public Core contracts only.
There is no MSTest/xUnit/NUnit, mocking, collector or retry dependency. Runtime
projects must never reference this package. These are explicit test controls,
not proof of Core scheduling, health-policy evaluation, HTTP or hosted shutdown.

- ManualHeartbeat creates public owners and records active registrations by
  subscriber reference. TriggerAsync explicitly awaits condition, callback,
  OnError and public monitor reporting. No background timer or dispatch queue runs.
  Requested exact execution-token cancellation follows Core Heartbeat's public
  contract; unrelated cancellation faults reach OnError. Overlapping triggers
  for one registration are rejected. Close/unregister stop
  admission and join accepted work; canceling a wait does not stop actual work.
  A supplied monitor stays caller-owned; a policy-created monitor is retired by
  the registration. Scope closure never disposes the subscriber. Calling one's
  own drain from an invocation is rejected. Active owner/registration capacity
  defaults to128; completed records are removed, not archived.
- RecordingHealthMonitorFactory records public operations without evaluating
  health policy. Creation history defaults to64, each monitor report tail to256;
  fixed saturating per-kind counters include evicted reports. Withdraw/dispose
  closes reporting once. No collectors run, so its disposal barrier is immediate.
  HealthSnapshotStub returns a caller-configured genuine immutable snapshot.
- ScriptedRestClient consumes a finite ordered script, default capacity256. Use
  EnqueueResult for known public T, EnqueueJsonValue for the actual requested T
  (including an internal application DTO), EnqueueFailure/Empty/Exception for
  explicit logical results, and EnqueuePendingJsonValue for controlled work.
  Unconfigured method/type calls fault clearly. It does no HTTP, retry, response
  mapper or codec work. Generated contexts claim zero native dispatches.
  Pending work normally observes caller cancellation; an explicitly ignoring
  hold still requires Release. Clear does not cancel tasks already started.
- RecordingLogger<T> retains the last256 enabled calls by default, including
  structured event identity/fields and original exception references. Clear
  releases records/counters. Values are shallow test-owned references; scopes
  and Activity are not simulated. Capacity bounds event count, not arbitrary
  caller payload size. Disabled levels never run the formatter.
- ManualTimeProvider is the single promoted repository clock. Advance drives
  real TimeProvider timers synchronously; SetUtcNow does not move monotonic time.
  Tests must release held callbacks before cleanup. Advancing time does not join
  arbitrary asynchronous work; await that work's actual completion separately.
- ScriptedLocalTransactionFactory substitutes the portable transaction factory.
  Pending scripts and retained owners default to16 each and fail clearly on
  exhaustion without consuming the next script or evicting recordings. Enqueue()
  scripts a clean commit; Enqueue(completion, defaultIsolation) supplies immutable
  commit facts, and EnqueueBeginFailure preserves the original exception. The
  separate context exposes metadata and first-reason rollback-only marking,
  without native ADO.NET capability. Marking or pre-commit cancellation refuses a
  committed script; explicit rollback/disposal normalize noncommit facts and
  cleanup flags. CreatedTransactions is a detached collection of inspectable
  owners with CommitRequested, RollbackRequested and DisposeRequested flags.
  These logical facts execute no SQL, native completion, timers or atomicity.
  Use a new factory after retained-owner exhaustion. See the
  [transaction guide](../../../../docs/modules/transactions/README.md#portable-consumer-tests).

A short logical-result test uses only public APIs:

```csharp
using System.Text.Json;
using CIS.CommonTesting;

var rest = new ScriptedRestClient(capacity: 8);
rest.EnqueueJsonValue("{\"count\":3}");
var result = await rest.Get<JsonElement>("https://test.invalid/countries");
if (!result.Success || result.Value.GetProperty("count").GetInt32() != 3)
{
    throw new InvalidOperationException("Unexpected scripted result.");
}
```

An outsider can supply CountryCatalog's IHeartbeatOwnerFactory,
IHealthMonitorFactory, IRestClient and ILogger<CountryCatalog> without a native
host, HTTP, reflection or friend-assembly access. For example, prepare a
ScriptedRestClient with EnqueueJsonValue containing the provider's known envelope,
use RecordingHealthMonitorFactory, ManualHeartbeat and RecordingLogger, and pass
Core's public DisabledMetrics.Instance. Keep the application stopping token an
explicit test-owned IHostApplicationLifetime. Await StartAsync, inspect the one-
hour registration, explicitly trigger refresh, inspect aliases and reports, then
await catalog disposal/owner closure. The actual outsider package consumer is
owned by the existing integration test infrastructure; this README is not a
passing test claim. Keep real Core scheduling/binding/drain tests as well.

Publish/build uses the repository's normal exact Core package version and approved
framework closure. This optional test package does not enter Core or either
optional production adapter. Publication and broader qualification are separate.

For a test referencing the compiled CountryData example, supply initialized,
unchanged settings through a test-owned public IConfigurationReader and supply
IHostApplicationLifetime through its existing contract. No native host is needed:

```csharp
var rest = new ScriptedRestClient();
var health = new RecordingHealthMonitorFactory();
await using var heartbeat = new ManualHeartbeat();
await using var catalog = new CountryCatalog(rest,
    new CountrySettings { RetryPolicyName = null }, reader, heartbeat, health,
    DisabledMetrics.Instance, lifetime, new RecordingLogger<CountryCatalog>(),
    new ManualTimeProvider(DateTimeOffset.UnixEpoch), []);

rest.EnqueueJsonValue("""{"error":false,"data":[{"name":"Malta","Iso2":"MT","Iso3":"MLT"}]}""");
await catalog.StartAsync(CancellationToken.None);
var schedule = heartbeat.Registrations.Single().Options; // One-hour initial delay and interval.
rest.EnqueueFailure(RestFailureKind.Http, "provider.unavailable", "Temporary", 503);
var refresh = await heartbeat.TriggerAsync(catalog);
bool retained = refresh.CallbackResult == false && catalog.TryGet("MLT", out _);
```

CountryCatalog belongs to the example assembly, not this package. The outsider
integration consumer tests actual packaged helpers plus that compiled assembly;
it does not execute the unchanged application entry point.

For ordinary MSTest unit examples, start with the real application's
[catalog tests](../../examples/CountryData.UnitTests/CountryCatalogUnitExamples.cs),
[lookup-step tests](../../examples/CountryData.UnitTests/CountryLookupStepUnitExamples.cs)
and their [small setup](../../examples/CountryData.UnitTests/CountryCatalogExample.cs).
The dedicated `CountryData.UnitTests` project is under `examples` in the solution
and references the compiled CountryData application; no application
source is copied and no friend access is added. The setup constructs the public
catalog with these helpers and `DisabledMetrics.Instance`, without a native host,
HTTP server, service provider or collector. The
[application testing guide](../../examples/CountryData/README.md#testing-examples)
lists expected results, the test command and the integration boundaries.
