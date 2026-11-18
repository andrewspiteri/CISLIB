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
