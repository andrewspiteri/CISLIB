# Healthcheck shared contract catalog

**ID:** HC-CONTRACT · **Revision:** 0.2 · **Status:** HC00 working contract, unpublished; compilation evidence is in baseline.json.

HC00 reconciles this catalog with the current user direction and source. `CIS.Healthcheck.AspNetCore` is a namespace/folder inside `CIS.Core`, which now explicitly references `Microsoft.AspNetCore.App`; no adapter package is introduced. The [foundation surface](foundation-surface.md) fixes concrete signatures omitted by the original proposal. The actual C# contracts and language-neutral schemas are hashed in [baseline.json](baseline.json). The original v0.1 attachment is preserved under sources/.

This is the shared input for implementation, test authoring, and documentation. Existing source R requirements remain binding. API spellings and new choices below are proposed Technical Intent decisions. HC00 reconciles existing APIs and compiles the agreed surface **before dispatch**; it may not leave each worker to choose a different shape.

Use namespace `CIS.Healthcheck` unless otherwise stated. Standard library types mean the repository's pinned net10.0 target. Do not introduce new runtime NuGets for these contracts. Public interface/enum/DTO declarations are real shipping code in the foundation. Missing service behavior is represented only by the explicitly nonshipping compile-only facade. No throwing service stub enters Core.

## C01. Service, factory, and snapshot source

The public service implements these small responsibilities; consumers receive only what they need.

```csharp
public interface IHealthSnapshotSource
{
    HealthSnapshot CaptureSnapshot();
}

public interface IHealthMonitorFactory
{
    IConditionMonitor CreateCondition(ConditionMonitorOptions options);
    IConsecutiveFailureMonitor CreateConsecutiveFailures(
        ConsecutiveFailureMonitorOptions options);
    INumericThresholdMonitor CreateNumericThreshold(
        NumericThresholdMonitorOptions options);
    IAvailabilityMonitor CreateAvailability(AvailabilityMonitorOptions options);
}
```

`HealthcheckService` implements both interfaces and `IAsyncDisposable`. Its constructor is:

```csharp
HealthcheckService(
    HealthcheckOptions options,
    HealthApplicationIdentity? application = null,
    TimeProvider? timeProvider = null)
```

Null options is an argument error. An omitted application identity is copied from current `CIS.EnvironmentOptions` facts; omitted timeProvider uses `TimeProvider.System`. Explicit identity is a value-only override for embedded hosts and deterministic tests, not another process-global environment store. No business I/O or collectors start from construction.

Service lifecycle members:

| Member | Semantics |
| --- | --- |
| `void MarkStartupCompleted()` | Set success once, idempotently. InvalidOperationException after stopping; never clear a startup-failure monitor. |
| `void BeginStopping()` | Idempotent, terminal. Set IsStopping and close new monitor/binding/collector admission atomically; signal cancellation outside the state gate. Existing active monitors still accept final reports. |
| `ValueTask DisposeAsync()` | BeginStopping, withdraw remaining monitors, and await their actual owned-work barriers. Host must finish final Heartbeat handoffs before disposing the service. |
| `Task WaitForDisposalAsync(CancellationToken waitCancellation = default)` | Wait for shared service disposal; cancel only this wait. Does not initiate disposal. |

Snapshots remain available during BeginStopping/final handoff. After final service disposal, CaptureSnapshot and new service operations throw ObjectDisposedException. Repeated BeginStopping/DisposeAsync remains harmless. No restart method exists.

## C02. Monitor handles and policy APIs

```csharp
public interface IHealthMonitor : IAsyncDisposable
{
    Guid MonitorId { get; }
    string? Name { get; }
    bool IsCritical { get; }
    void ReportMeasurement(HealthMeasurement measurement);
    void Withdraw();
    Task WaitForDisposalAsync(CancellationToken waitCancellation = default);
}

public interface IConditionMonitor : IHealthMonitor
{
    void Flag(HealthDiagnostic? diagnostic = null);
    void Clear();
}

public interface IConsecutiveFailureMonitor : IHealthMonitor
{
    void ReportSuccess();
    void ReportFailure(HealthDiagnostic? diagnostic = null);
    void ReportSkipped();
    void ReportExpectedCancellation();
}

public interface INumericThresholdMonitor : IHealthMonitor
{
    void ReportValue(double value, HealthDiagnostic? diagnostic = null);
}

public interface IAvailabilityMonitor : IHealthMonitor
{
    void ReportPublished();
    void ReportRefreshFailure(HealthDiagnostic? diagnostic = null);
    void Invalidate(HealthDiagnostic? diagnostic = null);
}
```

Every successful factory call has registered a unique monitor. Structural errors/capacity/stopping reject atomically. `ReportMeasurement` adds optional diagnostic metrics; it never implicitly becomes a numeric-threshold policy or changes health. A required numeric condition uses its explicit numeric monitor or a required collector result.

Identity getters remain readable after withdrawal. Mutating/report methods then throw ObjectDisposedException. Withdrawal is not success and does not increment outcome counts. Expected internal late publication is discarded, not routed through a public throwing report.

### Options and validation

Use named init properties, not large positional constructors. Validate and copy options before publishing state. No caller-owned mutable dictionary is retained.

| Options type | Required fields | Optional/default fields |
| --- | --- | --- |
| Every policy options type | `bool IsCritical` explicitly provided | `string? Name = null` |
| `ConditionMonitorOptions` | `bool InitiallyFlagged` explicitly provided | `HealthDiagnostic? InitialDiagnostic = null` |
| `ConsecutiveFailureMonitorOptions` | `int FailureThreshold` in 1..Int32.MaxValue | No implicit threshold |
| `NumericThresholdMonitorOptions` | `NumericComparison Comparison`, finite `double Threshold`, nonblank `string Unit` | None |
| `AvailabilityMonitorOptions` | Common fields | Begins Pending; no inherited usable-data state |

Use `required` for the selected required init properties where it expresses the contract. Runtime validation still applies; compiler-only required-member checks do not prove valid configuration. Unsupported enum values fail.

`NumericComparison` values are `GreaterThan=0`, `GreaterThanOrEqual=1`, `LessThan=2`, `LessThanOrEqual=3`. No implicit hysteresis/window/expiry/unit conversion. A direct finite value is compared as supplied using double comparison; equality means representational equality of those finite double inputs. No automatic decimal tolerance is invented.

`HealthStatus`: `Healthy=0`, `Unhealthy=1`.  
`MonitorState`: `Pending=0`, `Healthy=1`, `Unhealthy=2`.  
`HealthPolicyKind`: `Condition=0`, `ConsecutiveFailures=1`, `NumericThreshold=2`, `FirstSuccessAvailability=3`.  
`HealthExecutionOutcome`: `Succeeded=0`, `Failed=1`, `Skipped=2`, `ExpectedCancellation=3`.

These numeric assignments are draft API choices to freeze before publication. Wire enum spelling uses the named strings, not numeric values.

### Policy transition fixtures

| Policy/input | Required transition |
| --- | --- |
| Threshold=3; Failure, Failure, Failure, Success | Streak 1,2,3,0; state Healthy,Healthy,Unhealthy,Healthy; failed total 3; succeeded total 1. |
| Threshold=3; Failure, Skip, ExpectedCancellation, Failure | Streak 1,1,1,2; skips and cancellation counted separately. |
| Condition initially clear; Flag; Clear | Healthy, Unhealthy, Healthy. Clearing one monitor does not clear another. |
| Numeric `GreaterThan`, threshold 10; input 10 then 11 | Healthy then Unhealthy. With GreaterThanOrEqual, input 10 is Unhealthy. |
| Direct numeric NaN/infinity | Reject with no revision/value/state change. |
| Availability: initial refresh failure; published; repeated refresh failure | Unhealthy; Healthy; stays Healthy with failed refresh visible. |
| Availability: published; invalidate; published | Healthy; Unhealthy; Healthy. |

## C03. Diagnostics, measurements, counters, snapshots

`HealthDiagnostic` is a detached immutable value with optional `Code` (at most 64 characters), optional `Message` (at most 1,024 characters), optional `ExceptionType` (at most 256 characters), and `IsTruncated`. Direct structural codes/type identifiers exceeding their limits are invalid; projected runtime exception type names and free text are truncated on Unicode scalar boundaries with IsTruncated=true. Invalid Unicode in direct caller-supplied values is rejected without state change. When projecting a caught runtime exception into a diagnostic, replace unmatched UTF-16 surrogates with U+FFFD, then truncate on scalar boundaries and set IsTruncated for replacement or truncation; never publish malformed UTF-8. No automatic stack or Exception.Data serialization.

`HealthMeasurement` has `Key`, `ValueKind`, typed scalar value, `Unit`, `ObservedAtUtc`, and `IsDiagnostic`. Key is nonblank and at most 128 characters; unit at most 32; string value at most 1,024; at most 32 keys per monitor. Value kinds are `Unavailable`, `String`, `Boolean`, `Integer`, `Number`. Integer uses Int64 in memory and a decimal string on the wire; Number is finite double. Unavailable has no scalar value. Exactly one scalar representation matches a non-unavailable kind. Values are diagnostic in this initial common report method; IsDiagnostic defaults to true, and ReportMeasurement rejects false without state change. Required policies use their explicit result paths.

Observation times are UTC. For direct policy calls without an externally supplied business time, the service's clock marks acceptance. Metrics carry their actual observation time; no capture request refreshes it. The component carries business freshness conditions explicitly rather than asking the scheduler to infer them.

### In-memory snapshot surface

All returned snapshot DTOs are sealed and expose getter-only properties. The input-only HealthApplicationIdentity is the named-init exception identified below. Internal constructors copy collections into read-only owned storage; no mutable arrays/dictionaries escape. Reference-only shapes may omit internal constructors. Public consumers read the properties below; no public DTO builder framework is required. Formatter tests can obtain real snapshots from a controlled service instead of inventing a second mutable model.

| DTO | Getter properties and types |
| --- | --- |
| `HealthApplicationIdentity` | `string Name`, `string? BuildVersion`, `string? ServiceVersion`, `Guid InstanceId`, `string? HostName`, `string? OsDescription`; named-init input class copied by the service. |
| `HealthSnapshot` | `int SchemaVersion`, `BigInteger Revision`, `DateTimeOffset CapturedAtUtc`, `HealthApplicationSnapshot Application`, `HealthStatus HealthStatus`, `bool Ready`, `HealthMonitorCounts MonitorCounts`, `IReadOnlyList<HealthMonitorSnapshot> Monitors`. |
| `HealthApplicationSnapshot` | Identity fields above plus `bool StartupCompleted`, `bool IsStopping`. |
| `HealthMonitorCounts` | `int Active`, `int Critical`, `int Failing`, `int CriticalFailing`, `int Pending`. |
| `HealthMonitorSnapshot` | `Guid MonitorId`, `string? Name`, `HealthPolicyKind Policy`, `bool IsCritical`, `MonitorState State`, `DateTimeOffset RegisteredAtUtc`, nullable `LastObservationAtUtc`, nullable `HealthDiagnostic Diagnostic`, nullable `HealthOutcomeSnapshot Outcomes`, nullable `HealthCollectionSnapshot Collection`, nullable `HealthNumericSnapshot Numeric`, `IReadOnlyList<HealthMeasurement> Measurements`. |
| `HealthOutcomeSnapshot` | `long Successful`, `long Failed`, `long Skipped`, `long ExpectedCanceled`, `long ConsecutiveFailures`, `bool CountersSaturated`, nullable `HealthExecutionOutcome LastOutcome`, nullable UTC `LastSuccessAtUtc`, `LastFailureAtUtc`, nullable `HealthDiagnostic LastFailureDiagnostic`, nullable `HealthExecutionSnapshot Execution`. |
| `HealthNumericSnapshot` | `NumericComparison Comparison`, finite `double Threshold`, `string Unit`, nullable finite `double LastValue`, nullable UTC `LastValueAtUtc`; last value/time remain unchanged on a required evaluation error. |
| `HealthCollectionSnapshot` | `bool InProgress`, nullable UTC `LastCompletedAtUtc`, nullable `HealthDiagnostic LastError`, nullable UTC `LastErrorAtUtc`. |
| `HealthExecutionSnapshot` | `Guid RegistrationId`, `long ExecutionId`, phase and nullable eligibility/admission/start/work-completion/settlement UTC times, nullable monotonic durations expressed in milliseconds, handler/cleanup status facts and detached error diagnostics. |

HealthSnapshot is a complete in-memory snapshot even when the selected transport view is compact. All aggregates and selected outcomes come from it, not from recounting a truncated list. Outcome counts are present for policies/facts that actually have outcomes; they are not fabricated for condition toggles. Collection fields are present when collection is registered or retained applicable collection diagnostics exist.

Numeric is present only for numeric policies; its policy value is not inserted into the optional metrics dictionary under an invented reserved key. Diagnostic contains the latest applicable observation reason and clears on a successful/clear observation; LastFailureDiagnostic retains the latest detached failed-outcome diagnostic independently. Availability ReportPublished/ReportRefreshFailure record successful/failed outcomes; Invalidate is a condition change, not another execution outcome.

The API input identity is a small named-value object. Service constructors validate/copy it and preserve the established shared metadata semantics. Snapshot DTO constructors remain implementation details, avoiding a large permanent public construction API merely to facilitate concurrent development.

## C04. Collector ownership, activation, and result contract

`HealthCollectorRunner(HealthcheckService service)` is application-owned and optional. It uses that service's frozen limits/time source. A service accepts at most one runner in this draft. Constructor performs no collection. Members:

| Member | Behavior |
| --- | --- |
| `Task StartAsync(CancellationToken waitCancellation = default)` | Enable dispatch once. Repeated start while running shares success; after stop, reject. A pre-canceled start does not start it. |
| `IHealthCollectionRegistration Register(IHealthMonitor monitor, IHealthCollector collector, HealthCollectorOptions options)` | Register one collector per monitor, atomically; monitor must be active and belong to this service. No invocation or periodic activation. |
| `Task StopAsync(CancellationToken waitCancellation = default)` | Stop new runner admission, cancel lifetime work cooperatively, and await actual wrappers/leases. Does not dispose externally owned monitors or stop final direct reports. |
| `ValueTask DisposeAsync()` | Idempotent terminal Stop plus disposal of runner resources. |

`HealthCollectorOptions`: `TimeSpan? Interval = null`, `bool IsRequired = true`. A nonnull interval must be positive and representable by the timing implementation; checked arithmetic must fail rather than wrap. No default period. Invalid options leave no partial registration. Required/optional cannot change after registration.

```csharp
public interface IHealthCollector
{
    bool TryAcquire(out IHealthCollectionLease? lease);
}

public interface IHealthCollectionLease : IAsyncDisposable
{
    Task<HealthCollectionResult> CollectAsync(
        CancellationToken lifetimeCancellation);
}

public interface IHealthCollectionRegistration : IAsyncDisposable
{
    Guid MonitorId { get; }
    void ActivatePeriodic();
    Task<HealthCollectionAttempt> CollectAsync(
        CancellationToken waitCancellation = default);
    void Withdraw();
    Task WaitForDisposalAsync(CancellationToken waitCancellation = default);
}
```

TryAcquire is nonblocking owner-controlled admission: false requires a null lease, true requires a nonnull lease, and a successful lease prevents owner dependency disposal until its DisposeAsync settles. It is not just a GC pin. The owner closes lease acquisition before beginning its own dependency disposal. No callback or lease may resolve its dependencies from an unrelated root scope.

`ActivatePeriodic()` requires a nonnull interval and a running runner; it is idempotent. Registration withdrawal stops future collection, not the caller's monitor. Monitor withdrawal closes both. Successful lease acquisition after a concurrent withdrawal is still disposed and tracked, but owner callback code is suppressed if invocation admission has not occurred. No registration operation retries a failed mandatory startup.

CollectAsync joins one shared operation. A pre-canceled caller token creates no new operation; canceling a joined wait affects only that caller. `HealthCollectionAttempt` contains `Disposition` (`Committed`, `Obsolete`, `Retired`, `OwnerUnavailable`), `EvaluationFailed`, and a safe nullable diagnostic. The attempt completing is not a declaration that health is Healthy.

`HealthCollectionResult` is a bounded immutable discriminated value. Its public static factories are `Condition(bool isFlagged, HealthDiagnostic? diagnostic = null)`, `Execution(HealthExecutionOutcome outcome, HealthDiagnostic? diagnostic = null)`, `Number(double value, HealthDiagnostic? diagnostic = null)`, `AvailabilityPublished()`, `AvailabilityRefreshFailed(HealthDiagnostic? diagnostic = null)`, and `Diagnostics(IReadOnlyList<HealthMeasurement> measurements)`. `WithMeasurements(IReadOnlyList<HealthMeasurement> measurements)` returns a new copied result for attaching optional diagnostics to a health-bearing result. Duplicate keys/invalid scalars are rejected; results do not carry owner references.

Required results must match the monitor policy. A mismatched/null result is one failed required evaluation. Optional-only collection must return Diagnostics; a health-bearing result is invalid and recorded as an optional collection error without changing health. Failure mapping follows source HC-POL-06. A returned availability refresh failure does not invalidate established availability. Explicit invalidation remains an owner report.

### Internal state/runner seam

Freeze these logical operations into `IHealthCollectionState` and immutable internal token/result types at HC00. They are not a public extension framework.

| Operation | Inputs/result | Coordination guarantee |
| --- | --- | --- |
| `TryReserve` | Monitor ID and collection registration ID → reservation or retired | Under state gate: verify live active generation, reserve one wrapper, record baseline health/per-key metric revisions; duplicates identify the current operation. No owner callback. |
| `TryEnter` | Reservation → bool | Atomic invocation boundary against monitor/collection withdrawal and application stopping. |
| `CommitResult` | Reservation and detached result → commit disposition | Recheck identity/generation/revisions; evaluate policy and applicable metrics atomically. Stale facts never recreate state. |
| `CommitEvaluationFailure` | Reservation and detached error → disposition | Same freshness guards and one policy failure, or optional diagnostic-only error. |
| `Complete` | Reservation and actual completion/cleanup facts | Exactly-once release of wrapper bookkeeping; complete disposal barriers only after acquired owner lease is actually released. |
| `WithdrawCollection` | Collection registration ID | Close admission and arrange cancellation without disposing the caller's monitor. |

Reservation includes opaque operation identity, monitor/collection identities, generation, starting observation revisions, and lifetime cancellation token. It contains no retained owner/delegate/Exception. Runner owns its global capacity reservation separately and releases it only after Complete. No nested state/scheduling locks. The state owner supplies implementations; the runner owner does not create parallel health state.

## C05. Execution binding

`HealthExecutionBinder` exposes these static overloads, all returning `IHealthExecutionBinding`:

```csharp
Bind(IHealthMonitor monitor, HealthExecutionBindingOptions options)
Bind(IHealthMonitorFactory factory,
     ConsecutiveFailureMonitorOptions policy,
     HealthExecutionBindingOptions options)
Bind(IHealthMonitorFactory factory,
     AvailabilityMonitorOptions policy,
     HealthExecutionBindingOptions options)
```

`HealthExecutionBindingOptions`: required nonempty `Guid RegistrationId`; `bool SuccessPublishesUsableData = false`; optional detached schedule metadata (`Interval`, `InitialDelay`, display name). An existing monitor must belong to this implementation and be suitable for the supplied fact stream. Availability requires the explicit success declaration. Unknown policies and stopping/withdrawn monitors fail before activation. The absent-policy case is handled by the Heartbeat adapter not creating a binding; it is not an implicit default threshold.

```csharp
public interface IHealthExecutionBinding : IAsyncDisposable
{
    Guid RegistrationId { get; }
    Guid MonitorId { get; }
    HealthFactDisposition Report(HealthExecutionFact fact);
}
```

HealthFactDisposition is `Recorded`, `Duplicate`, `Obsolete`, or `Retired`. Invalid/contradictory retained facts raise a bounded documented integration error; the caller isolates it from callback execution. Disposing an owned binding withdraws/drains its monitor; disposing an external binding does not dispose the monitor. The factory overloads own the created monitor. No caller-controlled boolean can accidentally transfer ownership of an external monitor.

`HealthExecutionFact` has required `RegistrationId`, `Kind`, UTC `OccurredAtUtc`; a positive Int64 `ExecutionId` for execution-scoped facts; relevant optional phase/timing/outcome/diagnostic fields. IDs never wrap: retire/reject before exhaustion. Kinds are Registered, Eligible, Admitted, ConditionStarted, CallbackStarted, Outcome, ErrorHandlingStarted, ErrorHandlingCompleted, CleanupCompleted, Settled, Retired, IntegrationFault. Execution-related durations use monotonic elapsed milliseconds; optional schedule values use explicit TimeSpan internally and milliseconds in wire diagnostics.

The [execution validation matrix](execution-facts.md) fixes per-kind required/forbidden values, duplicate Bind behavior, terminal retirement and secondary step statuses. These rules are frozen with HC00 rather than left to separate state/binder/test owners.

An Outcome carries exactly one HealthExecutionOutcome plus source phase, work-completed time, detached original error and any already-observed secondary handler/cleanup diagnostics. Settled carries its actual settlement time; it never increments outcomes. Outcome does not claim a settlement timestamp that did not yet exist. A false callback is Failed without invoking OnError; a condition false is Skipped; a successful handler does not replace an originating failed outcome.

### Internal atomic binding seam

The service/factory and each Core monitor explicitly implement internal `IHealthStateAccess`, exposing stable service-lifetime `IHealthCollectionState CollectionState` and `IHealthExecutionState ExecutionState` getters. Read these outside the state gate. `Bind(monitor, options)` requires this access or throws `ArgumentException` for monitor before mutation, then uses its ExecutionState with a copied monitor request. Factory overloads require the same internal access or reject factory, and call atomic state Bind directly without a preceding public Create call. The collector runner captures its service's CollectionState and requires the monitor's CollectionState to be reference-equal before passing its ID to RegisterCollection. State still atomically verifies liveness, stopping and attachment. The registry never strongly retains this access object or a caller-owned monitor graph; no global monitor lookup or public adapter interface is added. This connection fixes the shared C01/C04/C05 implementation seam.

The foundation also freezes `IHealthExecutionState` for the state owner and binder. `Bind(BindingRequest request)` atomically checks service stopping/monitor liveness and either associates a suitable existing monitor or creates-and-associates one configured monitor before publishing any registration. BindingRequest contains RegistrationId, the external MonitorId or exactly one typed policy, and the explicit availability-success declaration. Failure leaves no newly created monitor or partial binding. It returns a BindingTarget with detached identities, ownership disposition, and a state-owned reporting handle; the state owner exposes no caller-supplied delegate or mutable registry.

`ApplyOutcome(BindingTarget, HealthExecutionFact)` commits one already-deduplicated outcome/policy update atomically; `UpdateLifecycle(BindingTarget, fact)` records detached phase/timing data without outcomes; `ReleaseBinding(BindingTarget)` releases the binding association and withdraws an owned monitor, never an external one. Reporting to withdrawn state returns Retired. The binder orders and deduplicates facts before these calls. Core state never invokes binder code while holding its gate, so a binder-local ordering gate cannot create lock inversion. The binding target may retain its monitor while the caller owns the binding; the singleton registry must not retain that binding/owner graph. These are internal task seams, not a public generic binding framework.

The binder keeps current and previous execution records plus an accepted outcome high-water mark. Compare identical/contradictory terminal repeats only while the actual bounded payload is retained. Ignore older history. A late settlement matching the previous record is permitted even when the current execution has begun. Direct monitor reports and bindings retain their own stated semantics; do not use the binder as a global deduplication service for arbitrary direct calls.

## C06. JSON schema and report formatting

The release schema has version 1. Field names are camelCase and case-sensitive on the wire. UTF-8 without BOM. Unknown optional fields are ignorable. Required fields cannot be missing, duplicated, wrong-typed, or contradictory. Serialization order is not semantically significant, but canonical fixtures use the table order.

| Snapshot field | JSON type/rule | Views |
| --- | --- | --- |
| schemaVersion | integer 1 | All |
| revision | canonical unsigned decimal string, no leading zeros except "0" | All |
| capturedAtUtc | ISO-8601 UTC ending Z, round-trip fractional precision | All |
| application | object with name, instanceId, startupCompleted, isStopping; optional metadata fields | All |
| healthStatus | "Healthy" or "Unhealthy" | All |
| ready | boolean, must equal recorded readiness predicate | All |
| monitorCounts | object: active, critical, failing, criticalFailing, pending as bounded nonnegative integers | All |
| monitors | complete ordered array of detailed monitor records | Details only |

Optional identity fields are omitted when unavailable. Monitor name/last observation/diagnostic/outcomes/collection are omitted when inapplicable or absent. `measurements` is present as an array in each detailed monitor, including an empty array. Policy-specific subobjects appear only where applicable. No null numeric placeholder means zero. For a present unavailable measurement, valueKind="Unavailable" and value is omitted. The only special null rule in the pipe envelope is its invalid-request view.

Detailed monitor fields mirror C03, including the optional numeric object with comparison, threshold, unit, lastValue and lastValueAtUtc; absent initial lastValue/time are omitted. The outcome object retains lastFailureDiagnostic when applicable. Policy strings use `Condition`, `ConsecutiveFailures`, `NumericThreshold`, `FirstSuccessAvailability`. Outcomes include successful, failed, skipped, expectedCanceled, consecutiveFailures as decimal strings; countersSaturated is boolean. LastOutcome uses the C02 names. Optional timestamps use the same UTC form. A diagnostic object uses code/message/exceptionType where present and always includes isTruncated. Measurements encode key, valueKind, value when available, unit when nonblank, observedAtUtc, isDiagnostic. Scalar Integer values are decimal strings; Number is finite JSON number; String/Boolean remain their JSON types.

Guid values use canonical lowercase D format. Public integer counters remain Int64 despite string wire representation. Revision is arbitrary-precision unsigned decimal. Durations are nonnegative JSON numbers explicitly named `...Ms`; clients do not treat wall-clock subtraction as authoritative monotonic timing.

Formatter API:

```csharp
HealthReportBuffer FormatSnapshot(
    HealthSnapshot snapshot, HealthReportView view, HealthcheckOptions options)
HealthReportBuffer FormatPipeResponse(
    HealthSnapshot snapshot, HealthReportView view, HealthcheckOptions options)
```

These are static methods on `HealthReportFormatter`. HealthReportView is Health, Readiness, Details, mapped to wire `health`, `readiness`, `details`. A buffer exposes owned read-only UTF-8 bytes, IsError, nullable ErrorCode, and SelectedHealthy; it never exposes a mutable state registry. Both formatting methods share one snapshot writer and enforce MaxReportBytes on their complete payloads; pipe envelope overhead counts. A failed format cannot report SelectedHealthy=true.

HTTP reporting error payload:

```json
{"kind":"error","schemaVersion":1,"error":{"code":"ReportTooLarge"}}
```

Use stable codes `ReportTooLarge`, `SerializationFailed`, `ReportingUnavailable`. Error messages are optional and bounded to 128 characters. Do not attach monitor failures to a transport error. A legitimate unhealthy snapshot has no kind:error marker.

## C07. HTTP contract

Adapter extension in `CIS.Healthcheck.AspNetCore`:

```csharp
IApplicationBuilder UseCisHealthcheck(
    this IApplicationBuilder app,
    IHealthSnapshotSource source,
    HealthcheckOptions options,
    Func<HttpContext, ValueTask<bool>>? authorizeDetails = null)
```

This installs middleware only; it does not start the application host. If DetailsEnabled=true, authorizeDetails must be supplied (it can explicitly allow all on a restricted listener). An authorization exception becomes a request-level ReportingUnavailable 500, cancellation terminates that request, and false returns 403. No health mutation. Details disabled returns 404 without invoking the predicate.

| Request | Response |
| --- | --- |
| GET /_health | Compact health snapshot; 200 Healthy, 500 Unhealthy. |
| GET /_health/ready | Compact readiness snapshot; 200 Ready, 500 NotReady. |
| GET /_health/details | Complete details when enabled/authorized, same health status mapping. |
| HEAD of an enabled route | Same captured-version/limit/status decision as GET, no body. |
| Disabled details, any method | 404 inside reserved branch. |
| Unknown descendant, any method | 404 inside reserved branch. |
| Unsupported method on enabled known route | 405, Allow: GET, HEAD. |
| /_healthcare | Outside branch, proceeds through normal application routing. |

Known paths are case-insensitive; exactly one optional trailing slash is accepted. Additional trailing slashes or other descendants are unknown reserved paths. Query parameters are ignored; they cannot enable details or change the selected view. No route performs monitor mutation. No retries or collector calls. Known read routes are repeatable snapshot reads, not idempotency-key APIs.

Use JSON content type and no-store/no-cache headers for normal/error health responses. Error bodies for 403/404/405 use bounded codes `Forbidden`, `NotFound`, `MethodNotAllowed`, except HEAD has no body. Do not leak exception details. Infrastructure/proxy middleware installed before the branch may run; the example must place business-side-effect middleware after it.

## C08. Pipe framing, CLI, and budgets

Requests and responses use a four-byte unsigned little-endian payload length followed by UTF-8 JSON. One request/response per connection. Request length 1..512; response 1..MaxReportBytes. Zero length, oversize, truncation, malformed UTF-8 or duplicate property names are errors.

Valid request:

```json
{"protocolVersion":1,"view":"readiness"}
```

Valid response has protocolVersion=1, schemaVersion=1, matching view, kind="snapshot", and snapshot with the C06 selected view. Exactly one of snapshot/error is present. Error envelope has kind="error", the versions, and error.code; view may be null only when the request is unparseable/invalid. A disabled details request receives `DetailsDisabled`; invalid view/version/schema uses `InvalidView`, `UnsupportedVersion`, or `InvalidRequest`. Bad frame/encoding uses `InvalidFrame`. Report size/format errors use C06 codes. If framing/connection damage prevents an error reply, close the connection; the client must still fail.

Parsing has a maximum JSON nesting depth of 16, including unknown optional fields; length/depth validation does not allocate an unbounded object graph. Revision strings are validated as canonical decimal identifiers without requiring client-side arbitrary-precision arithmetic. Client rejects a response view mismatch, unknown protocol/schema version, malformed/inconsistent counts, ready inconsistent with application/health fields, duplicate properties at any object depth, and wrong scalar types. For details it verifies complete list/count consistency. For compact it checks count bounds/relationships and aggregate relation to criticalFailing; it cannot verify omitted per-monitor details and must not claim otherwise.

Server receives only local clients; no remote server option exists. Use CurrentUserOnly where supported plus verified platform endpoint permissions. Test pinned .NET 10 behavior rather than assuming the changed Unix socket permissions described for later runtimes. Details on the local pipe require DetailsEnabled and the local access boundary; no HTTP authorization callback applies to IPC.

CLI entry point `cis-health-probe` accepts exactly:

```text
--pipe <name>                         required
--view <health|readiness|details>     default health
--timeout-ms <integer>               default 1000, range 50..60000
--max-report-bytes <integer>         default 4194304, range 1024..16777216
```

Duplicate/unknown options, missing values, invalid names/numbers, or contradictory response data exit 2. Connect/request/response share one total monotonic timeout budget, not a fresh timeout per read. No internal retries. Exit 0 only for a complete valid healthy/ready selected view; exit 1 for a complete valid unhealthy/not-ready view; exit 2 otherwise. Details selects aggregate health. Stdout contains the valid selected snapshot; errors use bounded stderr. Valid Unhealthy must never print JSON then fall through to exit 0.

`HealthPipeServer(IHealthSnapshotSource source, HealthcheckOptions options, TimeProvider? timeProvider = null)` supports explicit `StartAsync`, terminal `StopAsync(waitCancellation)`, and `DisposeAsync`. PipeEnabled=false means StartAsync does not open an endpoint. When true, invalid/missing PipeName fails before opening. Repeated Start while running is idempotent; after stopped it rejects. Stop closes admission and awaits actual handlers. Canceled caller waits do not certify handler completion.

## C09. Configuration/default catalog

`HealthcheckOptions` is an ordinary named-init value class with these defaults. Its static `Resolve(HealthcheckOptions? setup = null)` reads each applicable named environment value once, overlays nonblank values, validates, and returns copied values. Constructors use supplied values without automatically rereading the environment. Using Resolve is the explicit path for deployment overrides. Existing EnvironmentOptions identity initialization remains unchanged.

| Property | Environment key | Default | Valid domain |
| --- | --- | --- | --- |
| MaxConcurrentCollections | CIS_HEALTHCHECK_MAX_CONCURRENT_COLLECTIONS | 4 | integer 1..64 |
| MaxMonitors | CIS_HEALTHCHECK_MAX_MONITORS | 1024 | integer 1..65536 |
| DetailsEnabled | CIS_HEALTHCHECK_DETAILS_ENABLED | false | true/false, case-insensitive after trim |
| PipeEnabled | CIS_HEALTHCHECK_PIPE_ENABLED | false | true/false, case-insensitive after trim |
| PipeName | CIS_HEALTHCHECK_PIPE_NAME | null | required when enabled; 1..64 ASCII chars; first alphanumeric; rest alphanumeric, dot, underscore, hyphen; reject reserved `anonymous` case-insensitively |
| PipeMaxClients | CIS_HEALTHCHECK_PIPE_MAX_CLIENTS | 8 | integer 1..64 |
| PipeRequestTimeoutMs | CIS_HEALTHCHECK_PIPE_TIMEOUT_MS | 1000 | integer 50..60000 |
| MaxReportBytes | CIS_HEALTHCHECK_MAX_REPORT_BYTES | 4194304 | integer 1024..16777216 |

Environment > explicit setup > default. Whitespace-only means absent. Invalid higher-priority input fails, with no fallback. Validate each supplied setting even if a transport is disabled; only the requirement to supply PipeName depends on enablement. No dynamic reconfiguration, global threshold override, retry default, polling default, health reset, or startup side effect.

Use current `CoreConfigurationException` and immutable issues after checking their real constructors at HC00. Planned stable module issue codes are `Healthcheck.InvalidValue`, `Healthcheck.OutOfRange`, `Healthcheck.RequiredValue`, and `Healthcheck.UnsupportedCombination`, associated with exact setting keys. These are new draft codes, not claims of existing repository members.

Public null arguments use ArgumentNullException; invalid direct scalar/enum input uses ArgumentException/ArgumentOutOfRangeException; withdrawn handles use ObjectDisposedException; stopping lifecycle uses InvalidOperationException. Capacity uses a proposed `HealthcheckCapacityException` with immutable `Limit` and stable Code `Healthcheck.MonitorCapacity`. Configured collector/binding combinations fail atomically. Internal stale/retired results use dispositions, not new monitor failures.

## C10. Contract freeze receipt

HC00 records one receipt containing source BRD hash, this catalog revision/hash, applicable standards commit, verified source API revision, approved package/namespace identities, compiled reference evidence, schema/fixture hashes, and the concrete ownership manifest. Every active task pins that receipt.

A changed member/default/wire rule is a contract change even if it edits a different file from the consuming worker. The contract owner updates the canonical artifact once, records the changed IDs C01..C09 and acceptance cases, and has affected workers acknowledge the new revision. No silent worker-local fork. No indefinite lock on a contract that an experiment has disproved.

HC00 supplies compiled contract declarations and a nonshipping reference consumer. Only the specific native foundation cases in the receipt are runtime-tested; reference facade bodies are never executed. This is a working development contract, not a published API or complete Healthcheck acceptance.
