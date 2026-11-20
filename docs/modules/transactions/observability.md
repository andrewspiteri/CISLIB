---
title: Local transaction observations
type: document
status: draft
cis:
  stable_id: cislib.document.modules.transactions.observability
---

# Local transaction observations

`AddCisTransactions` automatically captures safe logging through the existing
`ILoggerFactory` under `CIS.Transactions`. Normal terminal summaries are quiet by
default. To enable one Debug summary after actual owned cleanup:

```csharp
services.AddCisTransactions(new LocalTransactionRuntimeOptions { LogCalls = true });
```

The namespace is `CIS.Transactions.Hosting`. Database alias, concrete isolation,
persistence outcome, end reason, known seconds, commit/rollback flags, cleanup
error count and safe primary/cleanup error codes describe the settled owner. Unknown
commit, rollback-only refusal and cleanup failures remain eligible for Warning
even with normal call logging disabled. Failed native acquisition and its cleanup
use safe warning events without returning a partial owner. Existing logger
filtering/correlation still applies. Expected caller cancellation alone is not an
Error event. Native cancellation after commit invocation still has Unknown
persistence evidence and remains visible.

If an escaping callback leaves the owner rollback-only and `await using` selects
ordinary uncommitted disposal, one safe warning remains eligible. An internal
boolean supplies that diagnostic fact after admitted work settles; it does not
change the public completion's outcome, end reason or errors, disclose the
rollback reason, or infer a failed native invocation. Expected callback
cancellation is likewise never promoted to an Error event by itself.

Automatic logs never attach a native exception, SQL, parameters, DSN, callback
result or application rollback reason. Applications may retain their original
exception for their own controlled handling; these summaries do not serialize it.
An absent or defective logger cannot alter callback propagation, cleanup or the
completion report.

For optional collection, use the existing `AddCisMetrics` hosting integration.
It recognizes final transaction registrations in either registration order and
binds their exact owner. Core acquires no collector dependency. See the
[fixed transaction catalog](../../../contracts/metrics/transactions.md) for names,
dimensions, capacities and exported families, and the existing
[collection policy](../metrics/collection.md) for identity, budgets and lifetime.

Duration starts at successful owner publication, excludes connection acquisition,
and ends after rollback and both resource-disposal attempts actually settle. It
uses the report's exact `DurationSeconds`; an unavailable clock produces no fake
zero. Active remains one during held cleanup, then balances to zero on the same
captured binding. Native failure counts include each failed invocation; application
callback errors and ordinary business results do not create guessed database
failures. These observations make no claim that an external effect participated
in the database transaction.

Transactions remain usable without collection, with Metrics disabled, or with
`CollectCoreModules=false`. A begin accepted before collection attachment retains
its original absent binding through acquisition and cleanup. Nothing is replayed
later. Logging and measurement failures remain independent of persistence
outcome; dispose or rollback failure never changes a confirmed commit, and a
later successful rollback never resolves Unknown.

Stop application admission, finish or cooperatively cancel work, await each owned
transaction and only then dispose sources and host dependencies. Metrics adds no
global transaction registry, drain service or detached cleanup wait. Focused
recorder and actual exporter checks are distinct from real PostgreSQL atomicity,
deployment and release qualification; consult CIS-0017's current evidence rather
than treating authored tests as a passing result.
