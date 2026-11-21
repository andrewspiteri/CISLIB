---
name: cislib-operations
description: Use, implement or review CISLIB durable operation execution, PostgreSQL idempotency journals, retained-result recovery and public consumer testing. Apply to CIS.Operations; delivery workers and ambient transaction orchestration remain separate modules.
---

# CISLIB durable operations

Read [root guidance](../../../AGENTS.md), the
[portable contract](../../../contracts/operations/README.md) and
[Core coordinator guide](../../../docs/modules/operations/core.md).
For the journal and explicit installation use the
[PostgreSQL guide](../../../docs/modules/operations/postgresql.md).
For privileged retention or uncertain-commit recovery read [retention](../../../docs/modules/operations/retention.md).
For diagnostics read [observability](../../../docs/modules/operations/observability.md).
For application substitution and actual package proof read
[testing](../../../docs/modules/operations/testing.md); the
[consumer guide](../../../docs/modules/operations/example.md) links compiling code.

Keep the complete ordinal identity: captured server application/environment,
application-resolved trusted scope, registered operation name and opaque key.
Caller projections and request scope text grant no authority. Resolve the actual
resource and await the existing capability authorizer before first execution,
replay or lookup disclosure. Execute permissions cover returning an original
result as well as submission. Preserve stable scope-to-resource binding.

Use the original explicit RM-02 context. One module-wide weak admission marker
spends that context once across services, definitions and generic types; retries
need a fresh owner. Await claim, run the handler outside native callbacks on an
owned normalized input, then encode/validate/decode stored bytes and separately
finalize. Never wrap the handler in a native callback or retry its statements.
Await the whole Execute before owner completion/disposal; RM-02 protects native
callbacks, not the entire application-handler interval.

Prepared is uncommitted staging; Succeeded/Rejected are business facts; actual
Committed/NotCommitted/Unknown comes only from the application-owned transaction.
Poison admitted failures while preserving the original fault and first safe
rollback reason. Never retry commit after Unknown. Fresh authorized lookup owns
only its read scope; Found survives that scope's cleanup diagnostics, and absence
does not prove rollback. Unsupported/corrupt retained data never permits work.

Canonicalize meaningful application input with explicit versioned fields and
stable result readers. Preserve original encoded result fidelity. Matching
logical result expiry is distinct from absence; a protection timestamp alone
does not remove uniqueness. Explicit privileged bounded maintenance is separate
from execution; no worker or polling interval is implied by retention policy.

Core uses BCL ADO.NET, with application-owned data sources and explicit one-time
versioned installation. Keep PostgreSQL v1 ReadCommitted-only and claim conflict
reads on a subsequent statement snapshot. Public CommonTesting scripts execute
no SQL, authorization or handlers and prove no native atomicity. Keep actual
provider/package evidence and unverified infrastructure lanes distinct. Preserve
CountryData as the one maintained tutorial and its database-free default.
