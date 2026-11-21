---
title: Durable operation coordinator
type: guide
status: Draft
---

# Explicit durable attempts and recovery

`CIS.Operations.IDurableOperationService` coordinates a short operation through
an application-owned RM-02 transaction. Register immutable typed definitions with
`AddCisOperation` and select the scoped coordinator with `AddCisOperations` from
`CIS.Operations.Hosting`. Both calls are passive. Supply the selected
`IOperationJournal`, existing `ILocalTransactionFactory`, established
`ICapabilityAuthorizer` and `ICallerContextAccessor` in the same provider. No
allow-all authorization, database, migration or second provider is selected.

Each definition captures the authoritative database alias, exact capability
requirements, stable fingerprint/result contracts, byte limits, normalizer,
codec, scoped scope-resolver factory and new-row retention policy. Setup objects
and codec metadata are copied. Adding or replacing definitions after building
the provider requires a new provider: runtime and host metadata must share the
same passive snapshot. Application normalizers/codecs are trusted thread-safe
components; the resolver may use ordinary scoped services.

The request's scope is untrusted text. Resolve it to a stable logical scope and
the actual protected resource. The captured server application/environment plus
that resource form the capability target; a caller projection does not grant
authority. Execution capabilities cover submission **and disclosure of an
original result**. Lookup capabilities separately cover disclosure. Authorization
and audit faults propagate before journal access.

## Execute and complete explicitly

`ExecuteAsync` validates the registered request/result types and exact database
and ReadCommitted isolation, resolves and authorizes the scope, then normalizes
an owned typed input. Its canonical comparisons must include the current
registered fingerprint contract and only explicitly registered versions. The
normalizer receives the resolved scope and must bind business input to that
actual resource. The handler receives its owned snapshot, not raw mutable input.

One original context admits one durable attempt across service instances,
definitions and generic types. Admission uses a module-wide weak marker with no
context back-reference; it is never reset after a replay, conflict or failure.
Validation/authorization/normalization failures before admission do not spend it.
Use a fresh transaction owner for another attempt.

The coordinator sequentially awaits journal arbitration, invokes the handler
outside native callbacks using the original context, copies and validates the
encoded result, decodes that stored representation, and finalizes the claim in
a separate awaited journal call. A handler may use the same context's native
capability through ordinary sequential `UseAdoNetAsync` callbacks. The module
never wraps the whole handler in a native callback and never retries a statement,
handler or commit.

`Prepared` carries the decoded stored business decision and means staged,
uncommitted work. A final `Rejected` decision can also be Prepared and committed
with its valid audit/business effects. Rollback-only prevents finalization or
Prepared, and the first existing reason remains authoritative. Escaping admitted
faults and cancellation mark rollback-only with a bounded safe `operations.*`
reason while preserving the original exception. No raw input, key, scope or
provider message becomes a rollback reason.

Await the **entire** Execute call before completing or disposing its transaction
owner. RM-02 guards native callbacks; it does not guard concurrent owner
completion throughout an application handler. Only the application commits and
inspects `TransactionCompletion`. Prepared is not Committed, and a replayed
original result does not depend on committing this new arbitration transaction.

An existing row must match the complete identity and be finalized/noncorrupt.
An unsupported fingerprint comparison returns Incompatible; comparable changed
input returns KeyConflict even if the result expired; matching logical expiry
returns ResultExpired. Supported readers decode the original bytes. Unsupported
or undecodable retained data never invokes the handler. Unexpected codec faults
propagate; they do not authorize reexecution.

## Independent committed-result lookup

`GetResultAsync<TResult>` accepts a named lookup without a caller-owned write
context. After scope resolution and authorization it begins a fresh authoritative
ReadCommitted owner, reads the journal, then awaits rollback and disposal. It
never touches an active original claim or the application's data-source lifetime.
`NotFound` observes no committed row in that read; it proves no rollback after an
Unknown original outcome. Incomplete/corrupt or identity-mismatched rows return
Unavailable, preserving their protection; unsupported results return Incompatible.

Only transient `DbException` failures in native Begin/Read phases map to
Unavailable. Nontransient query faults, scope/authorization/audit faults,
unexpected decoder faults and cancellation propagate. Cleanup always settles;
secondary cleanup faults cannot replace an original failure. Unexpected cleanup
faults without an earlier failure propagate unless a committed Found result is
already known. Found remains Found through read cleanup faults.

`ReadScopeCompletion`, when the owner actually publishes it, describes only that
fresh read transaction. Its expected NotCommitted and cleanup diagnostics do not
downgrade the original committed result. No completion is fabricated when an
owner publishes none. After Unknown, use this lookup or explicitly resubmit the
same scoped identity/input in a fresh transaction. Never retry commit or infer
rollback from a missing read.

See the [portable contract](../../../contracts/operations/README.md) for exact
canonical bytes, retained-version rules and finite limits. Controlled Core tests
qualify coordinator sequencing and ownership; they do not prove SQL uniqueness,
atomicity or PostgreSQL isolation. Those need the separately owned real-provider
checks in [CIS-0018](../../changes/CIS-0018/implementation-plan.md).
