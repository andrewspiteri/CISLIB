# Healthcheck compatibility inputs

The [initial candidate](initial-candidate/README.md) records the unpublished HC00
working contract and exercises it against the actual current `CIS.Core` candidate.
It adds no row to the [publication ledger](../publications/ledger.json).
The ledger currently has no publication history. Passing these tests establishes
the selected current API/configuration/wire obligations; it does not establish
source or unchanged-binary replay of a released Healthcheck consumer.

Keep these initial inputs and the original HC00 schema/fixtures unchanged. A real
publication needs its own exact package, provenance, public API and unchanged
consumer records under the existing permanent compatibility policy.
