# Coordination consumer tests

`CIS.CommonTesting` supplies public, bounded substitutes for tests that consume
`ICoordinationService` and `ICoordinationLease`. These helpers do no provider I/O,
authentication, capability authorization, ownership arbitration or renewal.
Scripted success is a test input; it establishes no PostgreSQL locking,
transaction atomicity, durable recovery or broker guarantee.

`ScriptedCoordinationService.Enqueue` supplies one exact ordered
`ManagedCoordinationResult`; `EnqueueFailure` supplies one original exception.
The script queue and request recording have separate finite capacities, initially
16 each, configurable from 1 through 65,536. Missing scripts and exceeded bounds
fail explicitly. Recording pressure preserves the next script. Pre-cancellation
consumes neither a script nor a recording. A scripted `OperationCanceledException`
is an original faulted task, distinct from a pre-cancelled invocation.

`Calls` returns a detached read-only snapshot containing only the requested scope,
duty and resource. These requested strings are never treated as trusted identity
or capability grants. Recordings retain no application callback or native handle.
The test owns any lease supplied in a scripted result. The count limits do not
bound arbitrary state retained inside a test-supplied lease implementation or
exception.

```csharp
var acquisition = new CoordinationAcquisition(
    new CoordinationKey("test", "wallet", "compaction", "partition-1"),
    "wallet-service", "test-instance", Guid.NewGuid());
var grant = new CoordinationGrant(Guid.NewGuid(), acquisition, 1,
    DateTimeOffset.UtcNow.AddSeconds(30));
await using var lease = new ManualCoordinationLease(grant);
var scripts = new ScriptedCoordinationService();
scripts.Enqueue(new ManagedCoordinationResult(
    CoordinationAcquireOutcome.Acquired, lease));
ICoordinationService service = scripts;

ManagedCoordinationResult acquired = await service.AcquireAsync(
    new CoordinationRequest("wallet", "compaction", "partition-1"));
await lease.LoseAsync(CoordinationLossReason.Superseded);
// Assert the consumer stops admitting work, then await its actual business task.
```

`ManualCoordinationLease` starts Owned with the supplied grant. `ConfirmGrant`
changes only the expiry observation of the exact authority, acquisition and
generation. `BeginDraining` records the first positive finite budget and closes
the manual local state; it starts no timer and does not invent authority expiry.
The test explicitly calls `LoseAsync` when its scenario requires loss. Existing
`ManualTimeProvider` remains available for the consumer's own timer/deadline
tests; advancing it does not automatically change this manually controlled lease.

Lost and Released are permanent, and the first loss reason remains stable.
`ReleaseAsync` uses the constructor's fixed release result, successful by default;
even an Indeterminate release leaves the handle locally Released. The ownership
token is cancelled once outside the state lock. `Termination` reports the local
terminal fact. `CancellationCompletion` separately reports actual cancellation
callback settlement, with any callback fault retained as `CancellationException`
without replacing the scripted release result. Neither task proves that separately
spawned business work has stopped. Drain that actual work and its scope before
release or disposal.

The public helper tests use only published types and interfaces. Managed engine
tests exercise conservative timing and unresolved-operation accounting with
controlled providers; real authority concurrency and guards require the selected
owned PostgreSQL integration tests. See the [portable contract](../../../contracts/coordination/README.md)
for these distinct boundaries and [native testing guide](../../../implementations/csharp/tests/README.md)
for actual service prerequisites and resource ownership.
