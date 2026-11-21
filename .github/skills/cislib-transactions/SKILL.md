---
name: cislib-transactions
description: Use, implement or review CISLIB explicit local transactions, guarded ADO.NET participation, persistence outcomes, cleanup, observations and portable consumer testing. Apply to CIS.Transactions work, not production operation-result storage or outgoing dispatch.
---

# CISLIB local transactions

Read [root agent guidance](../../../AGENTS.md) and the
[approved dependency policy](../../../docs/architecture/csharp-dependency-policy.md)
before changing this module. Then read the [portable contract](../../../contracts/transactions/README.md),
[consumer guide](../../../docs/modules/transactions/README.md) and actual
[factory/owner interfaces](../../../implementations/csharp/src/CIS.Core/Transactions/Contracts/ILocalTransactionFactory.cs).
For provider work use the [compiling PostgreSQL consumer](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Transactions/PostgresAcceptanceConsumer.cs)
and [isolated qualification](../../../docs/modules/transactions/postgresql.md).
For instrumentation read [observations](../../../docs/modules/transactions/observability.md)
and the [bounded catalog](../../../contracts/metrics/transactions.md).

Use passive AddCisTransactions/AddCisAdoNetDatabase from CIS.Transactions.Hosting
in the existing provider. The application owns its host-lived DbDataSource;
Core owns the opened lease and native transaction. Preserve immutable bounded
ordinal aliases and the explicit isolation subset. Registration/metadata work
must not resolve sources or perform database I/O.

Pass ILocalTransactionContext explicitly. Access its public IAdoNetTransactionContext
capability through UseAdoNetAsync from CIS.Transactions.AdoNet. Run callbacks
sequentially, disposing commands/readers and temporary ORM contexts before return.
Keep orchestration outside callbacks that would recursively enter the same owner.
Only the owner completes/disposes native handles. Do not retain handles or infer
participation from another connection, request or DI scope.

Preserve actual settlement: external disposal closes admission and joins work;
same-callback disposal fails promptly. Escaping callback faults poison the unit
and preserve the original exception. Caught failures requiring rollback must
explicitly mark it; the first safe reason wins. Business values are separate from
persistence, so do not infer database failure from a declined result.

Inspect Committed, NotCommitted and Unknown independently of cleanup diagnostics.
Ordinary failure after native commit invocation is Unknown; later rollback cannot
resolve it. Confirmed commit survives cancellation/disposal faults. Never
automatically replay an uncertain unit or retry commit. Begin/work/commit tokens
are operation-specific. Independent rollback-budget expiry requests cancellation
but still awaits real provider settlement and all ordered cleanup. Applications
drain owners before sources and host dependencies; add no ambient transaction
registry or alternative lifecycle engine.

Reuse current Logging/Metrics with its one captured host binding, operation
boundary, identity and bounded dimensions. Never log native exceptions, SQL,
parameters, results or DSNs. Report duration and metric duration are the same fact.

Use ScriptedLocalTransactionFactory from CommonTesting for portable application
tests, with bounded scripts/recordings and fake stores receiving the same context.
It provides no native capability or SQL engine. Keep real native/provider proof
in existing tests, and distinguish actual executions from authored source, EF
recipes, scripted lost acknowledgements and deployment claims. CountryData stays
the sole maintained tutorial. Use [the Operations skill](../cislib-operations/SKILL.md) for durable keys and original-result recovery over these owners. Outgoing delivery, background reconciliation workers and EF adapters remain separate scope.
