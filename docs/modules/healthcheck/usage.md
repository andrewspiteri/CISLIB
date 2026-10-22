# Usage and configuration

Create one application-owned service and retain each monitor for its actual owner lifetime. Construction starts no endpoint, collector, polling, or business initialization.

```csharp
using CIS.Healthcheck;

HealthcheckOptions options = HealthcheckOptions.Resolve(new HealthcheckOptions
{
    MaxConcurrentCollections = 4,
    DetailsEnabled = false
});
await using var health = new HealthcheckService(options, new HealthApplicationIdentity
{
    Name = "CountryService",
    InstanceId = Guid.NewGuid()
});
await using IAvailabilityMonitor usableData = health.CreateAvailability(new AvailabilityMonitorOptions
{
    Name = "Usable country data",
    IsCritical = true
});
await using IConsecutiveFailureMonitor refresh = health.CreateConsecutiveFailures(new ConsecutiveFailureMonitorOptions
{
    Name = "Country refresh",
    IsCritical = false,
    FailureThreshold = 3
});

// After the application has actually fetched, validated and published data:
usableData.ReportPublished();
// After every required application startup operation has actually succeeded:
health.MarkStartupCompleted();

HealthSnapshot snapshot = health.CaptureSnapshot();
```

`IsCritical` is an explicit owner decision on every policy. Aggregate health is Unhealthy exactly when an active critical monitor is Unhealthy. Noncritical failures remain visible in details and counts. Pending is distinct from failure; a fresh availability, numeric, or consecutive-failure monitor is Pending. A healthy aggregate alone does not prove initialization completed.

`Ready = StartupCompleted && !IsStopping && HealthStatus == Healthy` is evaluated in the same snapshot. `MarkStartupCompleted()` is explicit and idempotent. `BeginStopping()` is terminal: readiness becomes false, new admission closes, and existing live monitors may still accept final direct observations. Startup cannot be reopened after stopping.

| Policy | Reporting and evaluation |
| --- | --- |
| Condition | Select `InitiallyFlagged` explicitly. `Flag(reason)` fails the monitor; `Clear()` recovers it. |
| Consecutive failures | Select `FailureThreshold >= 1`. The Nth consecutive failure flags. Success resets the streak and recovers; skipped and expected cancellation do not reset or increment the streak. Lifetime counters remain separate. |
| Numeric threshold | Select finite `Threshold`, explicit `Comparison`, and nonblank `Unit`. `ReportValue` accepts only finite values. Equality depends on the chosen strict/non-strict comparison. |
| First-success availability | `ReportPublished()` establishes usable data. Initial `ReportRefreshFailure()` fails availability. Later refresh failures retain established availability; call `Invalidate()` explicitly when the data is unusable. |

`ReportMeasurement` accepts bounded optional diagnostic scalars. Measurements do not change health or the policy observation time. Numeric policy values and optional diagnostics have separate meanings; failed optional collection retains previous values and timestamps. Never use a missing measurement as a numeric zero.

Snapshots are detached immutable observations with one revision, application facts, counts, and ordered monitor details. An earlier snapshot can still include a subsequently withdrawn monitor. Revisions are arbitrary-precision identifiers; wire revisions and integer counters use canonical decimal strings to avoid precision loss. Counters saturate rather than wrap. Details retain the most recent failed diagnostic through later success while the active diagnostic clears.

`HealthMonitorSnapshot.RegistrationDiagnostic` separately retains the last accepted registration-scoped `IntegrationFault` or `Retired.IntegrationError`. It does not flag the monitor, change an outcome counter, fabricate an execution, or replace the policy, execution or collection error. Multiple bindings to the same monitor share one bounded slot, replaced in state commit order even if the reported event clock moves backward. Direct success, retirement without a reason and external unbinding retain it. A repeat of a binding's last normalized diagnostic event (same UTC instant, optional source phase and detached diagnostic fields) returns `Duplicate` without another revision. A distinct accepted event advances revision. Monitor withdrawal removes its current detail; earlier snapshots remain immutable. Supply safe bounded diagnostics; this observation path does not start or supply a Heartbeat scheduler.

Call `HealthReportFormatter.FormatSnapshot(snapshot, view, options)` for a bounded owned UTF-8 buffer, or `FormatPipeResponse` for the matching envelope. `SelectedHealthy` describes the selected health/readiness predicate. Always check `IsError`: a formatting error cannot be treated as healthy. Limits include the complete encoded payload and pipe envelope. See [transport errors](transports.md).

## Options

`HealthcheckOptions.Resolve(setup)` reads each module environment value once, overlays nonblank environment values on explicit setup and defaults, validates, and returns copied settings. Precedence is environment, explicit setup, then default. Whitespace-only environment values are absent; invalid selected values fail without fallback. Direct service/adapter construction validates and copies supplied options without reading environment overrides again. There is no dynamic reconfiguration.

| Property | Environment key | Default | Allowed value |
| --- | --- | --- | --- |
| `MaxConcurrentCollections` | `CIS_HEALTHCHECK_MAX_CONCURRENT_COLLECTIONS` | 4 | 1–64 |
| `MaxMonitors` | `CIS_HEALTHCHECK_MAX_MONITORS` | 1024 | 1–65536 |
| `DetailsEnabled` | `CIS_HEALTHCHECK_DETAILS_ENABLED` | false | true/false, case-insensitive |
| `PipeEnabled` | `CIS_HEALTHCHECK_PIPE_ENABLED` | false | true/false, case-insensitive |
| `PipeName` | `CIS_HEALTHCHECK_PIPE_NAME` | absent | Required when enabled; 1–64 ASCII characters; first alphanumeric, then alphanumeric/dot/underscore/hyphen; `anonymous` prohibited |
| `PipeMaxClients` | `CIS_HEALTHCHECK_PIPE_MAX_CLIENTS` | 8 | 1–64 |
| `PipeRequestTimeoutMs` | `CIS_HEALTHCHECK_PIPE_TIMEOUT_MS` | 1000 | 50–60000 ms total per connection |
| `MaxReportBytes` | `CIS_HEALTHCHECK_MAX_REPORT_BYTES` | 4194304 | 1024–16777216 bytes |

Disabled transports still validate supplied values. Only the requirement to supply `PipeName` depends on enablement. Thresholds, criticality, retry policy and collector intervals are application choices, with no global environment defaults. Identity is copied from explicitly supplied `HealthApplicationIdentity` or current repository `EnvironmentOptions` facts; Healthcheck adds no global startup/configuration framework.

| Error | Meaning |
| --- | --- |
| `ArgumentNullException` | A required public argument is null. |
| `ArgumentException` / `ArgumentOutOfRangeException` | Direct scalar/enum/policy/binding input is invalid or incompatible. |
| `CoreConfigurationException` | Invalid selected configuration, with immutable issues keyed to the setting and codes `Healthcheck.InvalidValue`, `Healthcheck.OutOfRange`, `Healthcheck.RequiredValue`, or `Healthcheck.UnsupportedCombination`. |
| `HealthcheckCapacityException` | Monitor admission exceeds the configured `Limit`; code `Healthcheck.MonitorCapacity`. Existing contributions are unchanged. |
| `ObjectDisposedException` | A withdrawn handle is used for reporting or activation. |
| `InvalidOperationException` | Admission during stopping, incompatible attachment, contradictory retained execution facts, or an established self-wait. |

Collectors and transports project bounded detached diagnostics instead of retaining an exception/owner graph. Diagnostics contain no automatically copied stack trace or `Exception.Data`; application-supplied messages can still contain sensitive text, so select safe reasons before reporting.
