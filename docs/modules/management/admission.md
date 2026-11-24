# Managed admission and settlement

Selecting `AddCisManagementRegistration` selects managed execution. Its gate starts
closed, and sequential Startup must finish live registration, authoritative
control and required local module reconciliation before work-producing owners
run. A passive `IManagementClient` does not select this lifecycle. See
[registration](registration.md) for timing, recovery and ownership.

`IManagedWorkAdmission.TryAdmit()` checks the current finite admission point. A
snapshot or change notification is observational: retaining one never authorizes
later work. Gate closure has no revocation token and does not cancel a callback
already admitted with its immutable settings, valid credentials, lease and
deadline. Owning modules retain configuration adoption and transaction cleanup.

Starting a control renewal also checks the previous confirmed authority window
before replacing its state with `Reconciling`. A scheduling pause therefore
detects expiry or an untrusted clock gap even when no admission, status or health
query ran first, including a gate closed only by reporting refusal. That gap is
published before the new control-attempt observation and requires Configuration's
existing authoritative reconciliation epoch. Successful control cannot reopen
work while that read is incomplete. Repeated attempts share the same old-window
gap; only a newly completed ready confirmation resets that recognition. Healthy
renewals do not create epochs or force another configuration sweep. Reporting
refusal retains the clock sample needed for this check; explicit host
`Invalidate()` keeps its existing gap and revalidation behavior.
When another prerequisite also refuses, an observed untrusted clock or expired
window takes precedence so that consuming the clock sample cannot erase the
required Configuration gap.

The existing [Jobs adapter](../coordination/managed-admission.md) checks new duty
acquisition, scheduled occurrences and each continuous iteration. Existing lease
renewals and fencing keep their original rules. A lost duty cannot be reacquired
until managed admission reopens.

Messaging checks application delivery after native authority/policy preparation,
before an inbox claim and again before the application callback. This includes
callbacks reached after waiting for an RM-03 original-operation claim. Closed
admission rolls back the unused original owner and uses the native broker requeue
path after actual scope/transaction cleanup. It does not acknowledge/discard the
item, produce a terminal reply or create a durable retry. Unsettled native
completion stops that consumer rather than claiming a safe disposition.
The native reply-ingress path remains available to finish an admitted RPC.
Committed outbox publication, original-result reads and an admitted handler's
original Transactions/Operations/Audit/outbox settlement keep their existing
owners; the gate is not installed on global transaction completion.

Effect-producing minimal API endpoints select the explicit native filter:

```csharp
using CIS.Management.Hosting;
using CIS.Security.Hosting;

web.MapPost("/orders", CreateOrderAsync)
    .RequireCisCapabilities("business", orderTarget, "orders.write")
    .RequireCisManagedWork();
```

Native authentication and capability authorization run before this filter. It
returns HTTP 503 with `Cache-Control: no-store` while the selected gate is closed.
An unmanaged provider with no gate retains ordinary behavior. Keep this filter at
the actual finite effect boundary; an endpoint that admits independent later
batches must check each batch separately. An admitted request retains the native
request token and its existing cleanup lifetime. Mark every effect-producing
route explicitly; a route is not exempt because it uses HTTP instead of a job.
Permitted read/status/recovery endpoints retain their existing authorization and
data dependency policies and omit the effect filter.

`IHealthReadinessSource` supplies a bounded retained local prerequisite without
I/O, waits or collector execution. The facade captures at most 32 sources once;
direct `HealthcheckService` consumers can pass the same sources through its
four-argument constructor. Readiness and its existing metric include these facts;
liveness still reflects existing critical health monitors. The facade's ordinary
request gate preserves startup/stopping/critical-health policy, while the explicit
effect filter handles managed admission. This keeps status and recovery reachable
in ContinuingOnly. Existing Healthcheck readiness HTTP responses remain 200/500;
503 belongs to rejected effect admission. Constructors start no background work.

Use the current Health reader/probe when selecting readiness-only prerequisites.
Older strict-derived-readiness readers can classify Healthy/NotReady as an
unavailable report. The normal unselected profile keeps its existing output.

Use the public `ManualManagedWorkAdmission` for bounded logical consumer tests.
It selects one current observation and publishes changes outside its lock; it
implements no freshness or remote authority policy. Scripted facts do not prove
native authentication, broker durability or PostgreSQL atomicity.
