# Guarded durable operations and outgoing delivery

Use Coordination to admit one current duty and its original transaction guard.
Use Operations to preserve a business request's durable identity and result.
Use Messaging to retain outgoing responsibility in that same application-owned
transaction. None of these facts substitutes for another.

The compiling consumer tests are
[the original-context SQL consumer](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Coordination/Consumers/CoordinationDurableConsumerTests.cs)
and [the public package/process consumer](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Coordination/Consumers/CoordinationPackageConsumerTests.cs).
They reuse the existing isolated PostgreSQL/RabbitMQ owners and the existing
Messaging process entry. They introduce no additional sample application,
transaction orchestrator or test harness. The runnable tutorial remains
[CountryData](../../../implementations/csharp/examples/CountryData/README.md),
with its database-free default and application-supplied native dependencies.

## One original transaction

The native consumer executes these operations in order:

1. Acquire a committed authority grant in a short coordination transaction.
2. Begin the application's original `Messaging` transaction.
3. Await `IDurableOperationService.ExecuteAsync` on that exact context. Operations
   claims its journal before invoking the application handler.
4. Inside the handler, await the coordination guard on the same context, perform
   the protected business write, then stage the outgoing record through
   `IMessageOutbox` on that context.
5. Await the entire Execute call, inspect its independent `Prepared` business
   result and complete the original owner once.

The guarded handler runs outside ADO.NET callbacks. Each SQL callback borrows the
original participant sequentially and disposes its commands before returning.
The guard neither opens another transaction nor commits the application owner.
With a receiving inbox, retain the established lock order: inbox, Operations
claim, coordination row, application rows, outgoing record.

The two native cases check both a confirmed commit and an unknown acknowledgement.
Before owner completion, an independent observer sees no journal, business or
outgoing rows, and the existing publisher has nothing to publish. After actual
native commit, all three rows are visible. PostgreSQL's `claim_xid`, `native_xid`
and `staged_xid` establish that the journal, effect and outgoing record belonged
to the same native transaction.

The unknown case uses the existing narrow commit-boundary seam: it lets the
selected provider commit successfully, then reports an acknowledgement failure.
It does not fabricate `TransactionCompletion` and is not a network-fault,
database-failover or power-loss simulation. The application preserves Unknown and
performs a fresh authorized result lookup. A missing lookup would not prove
rollback and would not authorize blind replay.

## Ownership and business identity stay separate

After the first owner releases, a fresh acquisition gets a higher generation.
The former grant is rejected by a new original-context guard. A fresh Operations
attempt keeps the original requested business scope, operation alias, key and
normalized input. Its handler throws if invoked: the expected result is
`Replayed`, with the original value and one retained effect/outgoing record.
Generation, instance and acquisition identifiers do not enter that business
fingerprint.

The native consumer releases the successor before invoking the existing outbox
publisher. Actual broker confirmation, native delivery evidence, explicit ACK,
unchanged wire bytes/MessageId/digest and the retained published state establish
that committed intent remains deliverable after ownership ends. Publication is
not conditioned on the old duty still holding its grant.

## Three actual process identities

The package case builds the existing public-only Messaging process entry and
contracts against the current exact Core and RabbitMQ packages, plus the already
selected test-only Npgsql archive. Its assembly name is outside Core's friend
list. It verifies deployed assembly hashes, selected package archives and the
packaged coordination installation SQL. Source files are compiled unchanged.

The existing parent fixture installs one isolated authority and shares only its
bounded descriptor and selected private connection files. Only the parent owns
Testcontainers. No child discovers Docker or chooses an external database.

- Process A captures its genuine `LocalApplicationIdentity.InstanceId`, acquires
  a fifteen-second grant, commits the guarded Operations result/business effect/
  outgoing record, and publishes an acknowledgement marker. It has not started
  the Messaging publisher. Its marker hold is bounded to twenty seconds.
- Process B captures a different genuine instance identity and must observe Busy
  with A as the current diagnostic owner. The parent then kills and joins the
  exact acknowledged A process through the existing process owner.
- Process C uses a third genuine identity. Only known Busy responses permit a
  fresh acquisition attempt inside its twenty-five-second acquisition deadline.
  PostgreSQL authority time decides expiry; no client sleep claims that takeover
  is already safe. An indeterminate acquisition is never replayed blindly.
  C obtains a higher generation, replays the original durable operation without
  another business handler, commits that fresh attempt and releases its exact
  grant. Only then does it start the existing Messaging host and publish A's
  original retained intent.

The parent validates all three process identities and exact Core bytes, one
effect/journal/outgoing record, the original result, preserved MessageId/digest,
actual TLS/native delivery evidence and ACK. Generation diagnostics use invariant
decimal strings, preserving the public grant contract's Int64 fidelity. Existing
owned process/output cleanup and schema/topology cleanup are joined before the
fixture can be removed or reused.

## Evidence boundaries

The existing outside-friend CommonTesting package consumer also compiles and uses
`ScriptedCoordinationService`, `ManualCoordinationLease` and
`ScriptedJobRunSource` from the current package. It checks supplied lease loss,
bounded recorded admission metadata and a lossless maximum Int64 grant while
verifying that no native database, broker or collector provider is loaded. Those
supplied facts remain substitutions and prove no native authorization,
exclusivity, recurrence or persistence.

The SQL/process tests use an explicit bounded **controlled scoped capability
authorizer** for the known test resource. It is a transparent application-policy
substitution and proves no workload authentication. Public result execution and
lookup still pass through that current authorizer. Broker TLS, provider-issued
delivery evidence and acknowledgements use the real existing transport. These
facts do not replace the separate Security projection/authorization tests.

These cases cover actual isolated database transactions, real process termination,
authority expiry, native broker delivery and exact public-package consumption.
They establish no fleet consensus, stale-process physical fencing under a network
partition, production recurring scheduler, cluster failover or release readiness.
Read [the current verification record](../../changes/CIS-0022/verification.md) for
the exact executed candidate, lanes, results and remaining qualification limits;
authored consumer source alone is not passing evidence.
