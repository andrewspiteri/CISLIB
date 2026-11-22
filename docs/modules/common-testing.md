---
title: Public consumer testing helpers
type: document
status: Draft
---

# Public consumer testing helpers

`CIS.CommonTesting` is an optional test-consumer package. It references Core and
the approved ASP.NET shared framework, with no test framework, mocking library,
collector SDK or retry dependency. Runtime projects do not reference it. See its
[packaged usage guide](../../implementations/csharp/src/CIS.CommonTesting/README.md)
for the finite manual Heartbeat, recording health/logger, scripted REST,
configured snapshot and single promoted clock APIs. Repository process runners
and internal probes remain in nonpackable TestSupport.

For the compiled CountryData example, supply a test-owned `IConfigurationReader`
and `IHostApplicationLifetime` through their existing public contracts. The
reader must respect its contract when shared with concurrent mutation. A simple
synchronous reader is sufficient for this test's initialized, unchanged settings.
The example assembly is a separate reference; it is not shipped in CommonTesting.

```csharp
var rest = new ScriptedRestClient();
var health = new RecordingHealthMonitorFactory();
await using var heartbeat = new ManualHeartbeat();
var logs = new RecordingLogger<CountryCatalog>();
var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
await using var catalog = new CountryCatalog(rest,
    new CountrySettings { RetryPolicyName = null }, reader, heartbeat, health,
    DisabledMetrics.Instance, lifetime, logs, clock, []);

rest.EnqueueJsonValue("""{"error":false,"data":[{"name":"Malta","Iso2":"MT","Iso3":"MLT"}]}""");
await catalog.StartAsync(CancellationToken.None);
var schedule = heartbeat.Registrations.Single().Options; // InitialDelay and Interval: one hour.

rest.EnqueueFailure(RestFailureKind.Http, "provider.unavailable", "Temporary", 503);
var refresh = await heartbeat.TriggerAsync(catalog);
bool retained = refresh.CallbackResult == false && catalog.TryGet("MLT", out _);
```

The ordinary monitored-registration extension dispatches through the same public
capability for Core and the manual substitute. A manual owner closes admission
and joins the work it actually accepted; canceling a close wait does not complete
that work. Explicit triggers do not run a scheduler, HTTP, retry, response mapper
or health-policy engine. Keep real production scheduling, atomic Core binding,
native cancellation and drain tests alongside consumer tests.

The outside-friend package test references actual Core/CommonTesting packages
and the hashed compiled CountryData DLL, and directly constructs the catalog.
It copies no catalog source and uses no internal or reflection bypass. This is
component/package evidence; it does not execute the unchanged application Main.

## Explicit local transaction substitution

Use `ScriptedLocalTransactionFactory` as the application's public
`ILocalTransactionFactory`. `Enqueue()` supplies a clean committed result;
`Enqueue(completion)` supplies immutable commit-request facts, including Unknown
and ordered cleanup errors. `EnqueueBeginFailure(exception)` preserves the
original begin fault without creating an owner. Queue and retained-owner
capacities default to16 each; exhaustion fails before consuming another script,
without eviction. Create a fresh factory after retained-owner exhaustion.

Pass the returned `Context` to fake stores and compare those received references.
The separately recorded owner exposes status/completion and whether commit,
explicit rollback or disposal was requested. MarkRollbackOnly keeps its first
reason and prevents scripted committed success. Pre-canceled commit returns a
NotCommitted cancellation snapshot; explicit rollback/disposal select normalized
noncommit facts. No native operation, callback engine, timer or SQL executes;
UseAdoNetAsync rejects the portable-only context before invoking its delegate.

See the [main transaction guide](transactions/README.md#portable-consumer-tests)
and [package consumer](../../implementations/csharp/tests/CIS.Core.IntegrationTests/CommonTesting/CommonTestingPackageTests.cs).
The latter uses the existing isolated actual-package fixture, public APIs and
fake-store context propagation; its authored source alone is not a passing claim.
Real database atomicity belongs to the separate PostgreSQL fixture.

## Durable operation substitution

Use `ScriptedDurableOperationService` through the public `IDurableOperationService`
port. `EnqueueExecution<TRequest,TResult>`, `EnqueueLookup<TResult>` and their
failure variants supply explicit logical outcomes. Scripts and call recordings
default to sixteen each; exhaustion refuses another call before consuming its
script. Recordings keep the exact borrowed context and bounded request metadata,
with no arbitrary input/result payload history. The helper runs no handler,
authorizer or SQL and never completes a transaction owner.

See the [operation testing guide](operations/testing.md) and existing outside-friend
package consumer for Prepared, Unknown and Found as separate facts. Real journal
atomicity and recovery use the [PostgreSQL consumer](operations/example.md).

## Messaging substitution

Use `ScriptedMessagingClient`, `ScriptedMessagePublisher` and
`ScriptedMessageDelivery` for bounded public application tests. Their explicit
facts and owned-work barriers do not establish SQL atomicity, authentication or
broker durability. See [Messaging testing](messaging/testing.md) for compiling
unit/package consumers and the separate native evidence boundaries.
Use [typed requests and recovery](messaging/requests.md) for actual runtime
submission, provisional-ticket ownership and fresh authorized recovery.
