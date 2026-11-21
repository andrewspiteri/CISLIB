---
title: RM-03 PostgreSQL journal independent review
type: review
status: Complete
---

# WORK-003 independent source review

The reviewer independently inspected the production PostgreSQL adapter, copied
options, embedded/packaged installer, real-provider fixture and tests, and the
provider guide against P09/P10/P11 and the portable journal contract. No product
native execution or database access was performed by this reviewer.

No material source finding remains. The coordinator's earlier corrections are
present: the one-time installer uses strict table creation instead of accepting
preexisting owned tables, and positive retention rounds up to PostgreSQL's
microsecond precision. A version marker does not silently approve an existing
unlogged or malformed table. Runtime performs version checks, not migration or
an implied full schema audit.

## Assessed boundaries

- All five complete identity columns participate directly in one nondeferrable
  primary key using deterministic C collation. Copied ASCII schema identifiers
  are validated and quoted; every identity/fingerprint/result value is a native
  parameter. Installer UTF8/block-size requirements and the actual maximum-byte
  uncompressed-index test correspond to the 2,488-data-byte bound. No identity
  hash substitutes for full uniqueness.
- Every command uses the supplied context's actual connection/transaction pair
  through RM-02. ReadCommitted is checked before SQL. One conflict INSERT is
  followed by a separate SELECT statement snapshot; missing winners produce a
  bounded non-owning outcome, without looping or invoking a handler.
- Same-native-transaction claims are excluded from committed-row reads. An
  unexpectedly committed incomplete row remains protected. Finalization checks
  full identity, original fingerprint, incomplete state and exact native xid,
  and requires exactly one row. A new owner cannot finalize another owner's
  claim. Recording and expiry timestamps are database preparation facts.
- SQLSTATE55P03/40001/40P01 conversion is restricted to the claim INSERT and
  occurs outside native access after RM-02 rollback poisoning. Later lock
  failures and unexpected command/provider faults propagate. Commands apply
  finite timeout and supplied cancellation and await native settlement. The
  production adapter issues no session settings, transaction-control SQL,
  statement retry, handler retry or commit retry.
- The fixture selects only an explicit private absolute loopback connection
  file, rejects implicit credentials/passfiles and unapproved pool/timeouts,
  checks PostgreSQL18 and ordinary durability settings, and owns a fresh GUID
  schema. Tests are serialized, use independent actual sessions and server
  blocking relationships, and join pending work before cleanup. The four
  logged-table pattern, native xid equality, visibility, races, rollback,
  authorization, timeout and original-result assertions are meaningful native
  acceptance cases once executed.
- The ordinary end-to-end workload output records actual candidate, server/OS,
  row/payload shape and elapsed observation separately from installer checks.
  It is a single representative workload, not a throughput benchmark or broad
  qualification claim.

## Necessity and scope

The direct BCL ADO adapter and explicit versioned SQL are necessary to provide
the approved production journal without a shipping provider dependency. The
single existing native integration project and small owned PostgreSQL fixture
test actual database semantics; they introduce no SQL emulator, test harness,
database abstraction framework, migration engine, scheduler, public Pending
lifecycle or outgoing dispatcher. A single INSERT statement snapshot cannot
replace the separate committed-winner read under the accepted ReadCommitted
contract. No responsibility or framework expansion was identified.

## Evidence boundary

The reviewer independently matched all 32 current source hashes, including the
packaging project and installation SQL, the Integration assembly, all three
source/Unit/Integration Core copies, and both TRX hashes to
`.artifacts/operations/CIS-0018/WORK-003/provider-receipt.json`. Core SHA-256 is
`f2576bc062d64f95180bd79e644e56865a8f69d8fe12ff97a13ac6a33f988329`.
Actual Windows and Ubuntu24.04 reports each contain nineteen passed, zero failed
and zero not-executed provider cases against PostgreSQL18.7. The receipt records
ordinary durability settings, the pinned native profile and successful affected
Release/scoped format checks.

The resolved-resource correction is present and executed: normalization copies
the trusted resource identifier into the owned business target and canonical
input; the atomicity case supplies a forged raw account, asserts the authorized
target reaches the handler and verifies replay with that same resolved meaning.

Both TRX outputs independently contain exactly one `RM03_WORKLOAD` observation.
The extracted workload receipt agrees with those outputs and candidate hashes:
40 stored identity bytes, 20 result bytes and a 192-byte journal row, one committed
journal/business/outgoing row each, one admitted execution and at most three
simultaneous leases. Observed end-to-end elapsed values are 370.7554ms on Windows
and 301.3922ms on Ubuntu. These are bounded single-workload observations, not
benchmark or installation-only claims.

Maintenance and simulated acknowledgement-loss recovery remain WORK-004
acceptance. A verified idle fresh fixture may be handed to that approved task
within the existing shared outcome; this source/evidence review does not itself
assert the coordinator's later resource-drain or restoration observations.
No required review finding remains for the provider checkpoint.

Reviewer accounting: thirteen active source minutes plus four source/evidence
alignment minutes charged to WORK-003; no native product work, database access
or additional logical work attempt.
