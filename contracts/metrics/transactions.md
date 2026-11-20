# Local transaction observations — v1

These conditional definitions accompany the [local transaction contract](../transactions/README.md).
They are selected by explicit transaction composition when automatic Core module
collection is enabled. Core-only transactions require no collector or SDK.

| Logical name | Kind / unit | Dimensions | Default capacity |
| --- | --- | --- | --- |
| `cis.transaction.duration` | Timer / Seconds | database, outcome | 48 |
| `cis.transaction.active` | Up/down counter / Count | database | 16 |
| `cis.transaction.failures` | Counter / Count | database, phase | 64 |

Outcomes are `committed`, `not_committed` and `unknown`; phases are `begin`,
`commit`, `rollback` and `dispose`. Database values are the at most sixteen
ordinal aliases admitted by transaction registration, including with collection
disabled. No transaction/correlation/business identifier, SQL, connection string,
exception, rollback reason, result or request value is a dimension.

A begin captures one complete host binding before source resolution or native
acquisition. Active adds one only when an owner is successfully published. Its
matching decrement and duration follow actual callback/native work, cooperative
rollback and both owned resource-disposal attempts. Acquisition failure has no
published owner, active increment or duration. Duration records the exact nullable
`TransactionCompletion.DurationSeconds`; an unavailable measurement is omitted.
The duration histogram's count supplies completed known-duration observations;
there is no additional completion counter. Acquisition duration is excluded.

Failure counts describe failed native invocations, including synchronous failures
and cancellation: open/begin use `begin`, commit uses `commit`, cleanup rollback
uses `rollback`, and each failed transaction/connection disposal uses `dispose`.
Both disposal failures therefore add two. Validation, rollback-only refusal,
pre-invocation cancellation, application callback errors and a swallowed native
error do not imply a failed native invocation observed by the runtime. A cleanup
error's safe diagnostic does not add a second count for the same invocation.

The registration's instrumentation owner can attach to one Metrics runtime only.
Complete bundles are published atomically; an operation accepted before attachment
never acquires a later binding, replays earlier observations or decrements another
host's active state. Ordinary logging/measurement failures cannot change native
work, cleanup, original exceptions or persistence evidence. Stop and disposal
remain application-owned: settle owners before disposing their host resources.

The optional collector reads final passive metadata in either registration order
and verifies its exact owner identity. `CollectCoreModules=false`, `Enabled=false`
and absence of a collector preserve standalone operation. Explicit transaction
composition reserves three streams and 128 normal combinations. The unchanged
default Core/incoming profile grows from 21 streams / 3,328 capacity to 24 / 3,456.
Overrides below 48 / 16 / 64 are rejected before activation; larger overrides
consume the existing shared budget. Standard fourteen finite duration bounds,
mandatory central application/environment labels, SDK special points and all
existing global limits remain unchanged.

Required exported families are `cis_transaction_duration_seconds` with `_bucket`,
`_sum` and `_count` children, `cis_transaction_active`, and
`cis_transaction_failures_total`. Authored contracts/tests do not establish an
executed exporter, platform, SQL atomicity or release result; current native
evidence is recorded under CIS-0017.
