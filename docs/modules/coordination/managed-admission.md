# Managed execution admission for Jobs

Jobs can consume the selected `CIS.Management.IManagedWorkAdmission` port without
owning registration, control checks, reconciliation or another worker loop.
`AddCisJobs` captures that optional port from the application's existing provider
when it constructs the runtime. Its absence preserves ordinary unmanaged Jobs
behavior. The original six-argument `JobRuntime` constructor remains available;
direct compositions can select the additional explicit overload:

```csharp
var runtime = new JobRuntime(
    catalog, executionScopes, initialSettings, runSource,
    scheduleEvaluator, timeProvider, managedAdmission);
```

A supplied runtime instance or custom factory must capture the same optional
port registered in its host. Existing Jobs activation rejects a mismatch; it
does not silently use an ungated borrowed runtime or retrofit an active owner.

Select managed execution and arrange its authoritative bootstrap before
work-producing startup participants. The selected port starts closed. Jobs
construction and catalog inspection remain passive and do not bootstrap or
create permission. A retained Ready snapshot or notification is observational:
every finite admission calls `TryAdmit`, which owns freshness and required local
prerequisite checks.

The runtime checks new continuous duties, scheduled pending/actual admission,
each physical acquisition attempt, each new continuous iteration and actual
callback entry after awaited scope preparation. A management refusal defers the
work. A scheduled occurrence remains with its authoritative source for later
explicit dispatch, preserving its input/settings/operation identity. Jobs adds
no acknowledgement, retry scheduler or replacement occurrence.

An already entered callback retains its captured immutable settings and original
completion facts. Closing management admission does not cancel its token,
replace valid configuration, mark the runtime stopping or release its lease.
Independent credential, deadline, lease loss and original-transaction fencing
rules still apply. A valid existing continuous grant can keep renewing through
the existing Coordination owner, but cannot start another iteration while the
gate is closed. A new acquisition or retry waits for current admission.

One passive subscription to admission changes pulses the existing bounded Jobs
condition signal. Paused iterations, acquisition attempts and configured-target
activation therefore wake after reconciliation without polling, another
configuration revision or a second scheduler. Publications never authorize an
invocation by themselves. The subscription is removed after actual Jobs stop
settles during disposal. The existing shutdown path still joins callbacks,
scopes and lease cleanup before dependencies are disposed.

The focused `ManagedJobsAdmissionTests` use the existing continuous/scheduled
fixtures and public CommonTesting admission/lease controls. They distinguish
logical deferral and captured completion from native authentication, bootstrap
freshness, PostgreSQL fencing and real provider renewal evidence. Actual executed
results belong in the current change's verification record.
