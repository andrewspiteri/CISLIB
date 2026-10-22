# Collector execution boundary detectors

The retained u2 report (`9cf5bd3e627e4daacc5fd86ca697092b975d8146c376557313a95a6fdf1771db`)
includes the earlier 14 collection-state and 25 runner qualification cases. This
new batch adds public receiving-boundary observations rather than repeating those
cases or manufacturing private execution scopes.

`HealthCollectorExecutionBoundaryTests` has three methods and 17 expanded cases:

- Seven real evaluation callbacks await disposal/wait barriers for a different
  monitor, different registration, different service or different service's
  runner. These barriers contain no part of the current operation and must be
  allowed without turning a successful evaluation into an integration failure.
- Six callbacks exercise self-containing monitor/registration/service waits from
  actual lease acquisition and actual lease cleanup. Each requires synchronous
  `InvalidOperationException`, rather than an outstanding task handed back to its
  owner, and independently verifies the real successful evaluation and cleanup.
- Four actual owner-created child tasks capture the execution context, remain
  gated until the manual collection and its cleanup have settled, then dispose
  the registration, monitor, runner or service. A completed operation must no
  longer prohibit that public disposal. Child gates are released in finally and
  all waits/cleanup are bounded; no fixed sleeps or native resources are used.

The relevant raw review locations include execution-scope entry and identity
predicates (1552, 1556–1569) and cleanup/deactivation paths. These are detector
intent, not a claim of individual mutant outcomes or equivalence. The original
report's scores, timeouts and omitted cases remain unchanged. This batch makes no
production change, supplies no Heartbeat and creates no qualification credit.

Focused native filter:
`FullyQualifiedName~CIS.Tests.UnitTests.Healthcheck.HealthCollectorExecutionBoundaryTests`
(17 authored cases; native execution remains coordinator-owned).
