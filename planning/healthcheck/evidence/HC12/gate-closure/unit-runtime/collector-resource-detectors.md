# Collector resource boundary detectors

`HealthCollectorResourceBoundaryTests` adds seven expanded cases without changing
shipping code or the existing collection/runner qualification cases.

- Two periodic cases complete an actual gated lease at a timestamp where the
  next interval is exactly Int64.MaxValue or exceeds it. The exact boundary stays
  active with a positive scheduled delay; overflow retires the registration while
  preserving its successful health observation and externally owned monitor. A
  second owner acquisition is observable and refused, bounding accidental wrapped
  recurrence instead of leaving an uncontrolled collector loop.
- Two actual injected ITimer.Change failures require automatic stop, resource
  disposal and failed stop completion, including unexpected ObjectDisposedException.
  The test awaits automatic disposal before requesting stop, preserving the
  distinction between an unexpected resource failure and caller-driven shutdown.
- One Change callback requests actual runner stop and then throws
  ObjectDisposedException. That concurrent disposal is harmless, the resource is
  disposed once, and the caller-owned monitor still accepts final observations.
- Two real owner cancellation callbacks throw during registration withdrawal,
  with or without an injected one-shot UTC-clock failure while projecting the
  diagnostic. The real evaluation and lease cleanup stay separately gated.
  Withdrawal/cancellation must not leak the exception, finish disposal early,
  publish stale errors into the detached monitor, count a health failure or strand
  the lease/runner drain. Cleanup releases both gates before bounded disposal.

The u2 report is unchanged; relevant review paths include recurrence overflow
(1513), timer-change exception handling and cancellation diagnostic/settlement
guards (1985 and adjacent finalization paths). These are causal detector targets,
not native kill, timeout or equivalence classifications. The existing 982-case
u2 snapshot and later other detector batches receive no retroactive credit.

Focused native filter:
`FullyQualifiedName~CIS.Tests.UnitTests.Healthcheck.HealthCollectorResourceBoundaryTests`
(seven authored cases; native execution remains coordinator-owned).
