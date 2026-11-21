---
title: Testing operation consumers
type: guide
status: Draft
---

# Public substitution and native proof

Applications can substitute `IDurableOperationService` through ordinary DI or
constructors. `CIS.CommonTesting.ScriptedDurableOperationService` supplies bounded
logical outcomes without a database, host, security runtime or friend access.
The test package depends only on Core and its approved framework closure; runtime
projects do not reference it.

Use `EnqueueExecution<TRequest,TResult>` and `EnqueueLookup<TResult>` to queue
exact public result objects. The combined ordered queue and retained invocation
list each default to16, configurable from1 to65536. Exhaustion fails clearly
without consuming the next script or evicting earlier recordings. Kind/type
mismatch consumes nothing. Start a new helper when its recordings fill.

`Calls` is a detached read-only snapshot. An execution call records the exact
supplied context, bounded operation alias/requested scope/key and generic types;
a lookup records no context or request type. It retains no handler or arbitrary
input/result history. Queued values are test-owned references until consumed:
the configured count limit does not bound arbitrary application payload size.

The helper returns declared facts. It does not invoke the handler, normalize or
hash input, authorize scope, enforce one-context runtime admission, access a
journal, poison a transaction or complete an owner. For example, an explicit
RollbackOnly script does not mark the supplied context. Test those runtime
invariants against Core, and SQL atomicity against the actual provider.

```csharp
using CIS.CommonTesting;
using CIS.Operations;
using CIS.Transactions;

var transactions = new ScriptedLocalTransactionFactory();
transactions.Enqueue(new TransactionCompletion(TransactionOutcome.Unknown,
    TransactionEndReason.CommitRequested, true, false, false));
var scripts = new ScriptedDurableOperationService();
IDurableOperationService operations = scripts;
scripts.EnqueueExecution<int, string>(new(OperationExecutionDisposition.Prepared,
    OperationDecision<string>.Succeed("original-receipt")));

await using ILocalTransaction owner = await transactions.BeginAsync("Ledger");
OperationExecution<string> attempt = await operations.ExecuteAsync<int, string>(owner.Context,
    new OperationRequest<int>("Debit", "requested-scope", "same-key", 10),
    (_, _, _) => throw new InvalidOperationException("The substitute never invokes this handler."));
TransactionCompletion completion = await owner.CommitAsync();
// The test supplies independent Prepared and Unknown facts; it proves no native commit.
scripts.EnqueueLookup<string>(new(OperationLookupDisposition.Found,
    OperationDecision<string>.Succeed("original-receipt")));
OperationLookup<string> recovered = await operations.GetResultAsync<string>(
    new("Debit", "requested-scope", "same-key"));
bool sameContext = ReferenceEquals(owner.Context, scripts.Calls[0].Context);
```

Failure scripts preserve their original exception and faulted task state,
including an unrelated `OperationCanceledException`. A call whose supplied token
is already canceled returns a canceled task with that token and consumes no
script or recording. There is no timer, background work or automatic retry.

The actual public package consumer is
[CommonTestingPackageTests](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/CommonTesting/CommonTestingPackageTests.cs).
It restores from the existing fresh local-feed/cache fixture into an assembly
outside Core's friend list, builds and executes against the current packaged
Core/CommonTesting DLLs, and compares loaded-byte hashes. It checks public
substitution, exact context, business/execution/Unknown separation, original
faults, finite limits, no handler invocation and the embedded/packaged SQL source.
Reading installation SQL does not execute it or establish database durability.

Focused [helper units](../../../implementations/csharp/tests/CIS.Core.UnitTests/CommonTesting/ScriptedDurableOperationServiceTests.cs)
cover the logical contract; [Core protocol units](../../../implementations/csharp/tests/CIS.Core.UnitTests/Operations/OperationExecutionTests.cs)
exercise actual coordinator ordering and failure boundaries. The real
[PostgreSQL tests](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Operations/PostgresOperationTests.cs)
use independent native sessions and an explicitly selected isolated fixture for
uniqueness, protected effects, cancellation and actual transaction outcomes.
Public scripts replace none of that evidence. Read the current
[verification record](../../changes/CIS-0018/verification.md) for executed lanes
and remaining limits; a missing Linux SDK-backed package run remains unverified.
