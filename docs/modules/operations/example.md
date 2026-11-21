---
title: Operation consumer and recovery example
type: guide
status: Draft
---

# One transaction, one original result

The compiling native consumer is the existing
[PostgresOperationFixture](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Operations/PostgresOperationFixture.cs),
with its [acceptance tests](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Operations/PostgresOperationTests.cs).
It uses actual public Core APIs, an application-owned Npgsql data source in the
test project, and four ordinary logged tables: schema metadata, journal, business
record and outgoing record. This is a small protected receipt-recording consumer,
not a complete account ledger or debit implementation. The outgoing row is only a protected database write;
this module does not dispatch it. CountryData remains the maintained tutorial
application with its database-free default.

The fixture deliberately installs
`PostgreSqlOperationSchema.GetInstallationSql(schema)` in its fresh owned schema.
Production composition registers the application's source through existing
`AddCisAdoNetDatabase`, adds `PostgreSqlOperationJournal` as `IOperationJournal`,
then selects `AddCisOperations` and a typed `AddCisOperation` definition. These
registrations open no connection and install no schema. Deployment credentials
and installation remain application-controlled.

The typed definition names `Debit` and database `Ledger`, captures stable input
and result contracts, and requires explicit submission/disclosure capabilities.
The application's resolver maps requested scope to the actual account resource;
the existing authorizer evaluates the complete target using server identity.
The normalizer owns its input snapshot and sets its account from the resolved
`OperationScope.ResourceIdentifier`, then binds meaningful fields to the trusted
scope. Keep each logical scope bound to the same protected resource across retries.
If an input alias is reassigned, resolve it without reusing an old scope for the
new resource; otherwise a newly authorized resource could disclose an old resource's
retained result. The result codec persists original generated receipt data using a stable
contract, with no CLR type identity. Tests' explicit authorizer is a fixture
seam, not a production authorization policy.

The consumer begins one ReadCommitted owner and awaits the complete
`ExecuteAsync<DebitInput,DebitReceipt>`. Its handler writes business and outgoing
rows through separate awaited `UseAdoNetAsync` callbacks on that **same original
context**. The coordinator's claim and terminal result use the same pair; no
second connection participates in those protected writes. An independent
observer sees none of the staged rows before explicit commit.

After Prepared, the application calls `CommitAsync` and inspects the actual
completion. Committed confirms persistence even with later release diagnostics.
NotCommitted says commit was never invoked. Unknown says commit was invoked
without confirmation; the application must not retry commit, replace the key or
infer rollback from a missing lookup. The owner and every native operation must
settle before the application-owned data source is disposed.

Recovery uses `GetResultAsync<DebitReceipt>` without the original write context.
It authorizes disclosure, reads through a fresh authoritative ReadCommitted
owner, and settles that read scope. Found returns the original generated receipt.
Its optional ReadScopeCompletion describes the new read owner, whose expected
NotCommitted does not change the original result's durability. NotFound alone
does not resolve an uncertain original commit.

Alternatively, explicitly submit the same identity and business input in a fresh
owner. Database uniqueness arbitrates again: a committed matching original is
Replayed without another handler, changed comparable input is KeyConflict, an
expired protected result is ResultExpired, and unsupported retained contracts
remain protected and Incompatible. A handler may execute again after rollback;
the guarantee concerns committed protected effects while the scoped row remains.
Await Execute before completing or disposing an owner, including cancellation
paths. HTTP, files, memory and broker delivery are outside this local guarantee.

For database-free application tests, the
[outside-friend package consumer](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/CommonTesting/CommonTestingPackageTests.cs)
substitutes `IDurableOperationService` using CommonTesting and demonstrates
Prepared, Unknown and Found as independent logical inputs. See
[testing](testing.md), [Core ownership](core.md), the
[PostgreSQL profile](postgresql.md) and the current
[verification record](../../changes/CIS-0018/verification.md). These links separate
compiling examples, scripted observations and actual native qualification.
