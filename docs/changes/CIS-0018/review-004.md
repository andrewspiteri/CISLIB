---
title: WORK-004 independent retention and recovery review
type: review
status: Complete
---

# WORK-004 independent review

The assigned reviewer inspected the frozen maintenance contracts, PostgreSQL
maintenance and passive registration, reused journal settings/command/version
helpers, changed retention timestamp SQL, native commit-boundary decorator,
PostgreSQL fixture and retention tests, three controlled factory-guard cases and
the retention guide. The review used the actual repository guidance and the
approved WORK-004/P11-P13/P15 ownership and retention requirements. This reviewer
is independent of the source author and performed no native execution.

No material source correction remains in the assigned frozen candidate. The
earlier fresh-factory metadata concern is resolved: null owners and returned
database/isolation mismatches fail before native access, with any returned owner
actually disposed. Three focused controlled cases cover those boundaries.

## Assessed behavior

- Maintenance selects one registered immutable operation. Empty captured
  maintenance capabilities return Disabled before resolution, authorization or
  transaction acquisition. Otherwise the application resolves the actual trusted
  scope/resource, and the complete existing capability authorizer receives the
  captured nonempty capabilities plus server application/environment and actual
  resource target. Denial returns no counts or completion before owner/SQL work.
  Resolver, authorization and audit faults propagate normally.
- Explicit passive registration requires the exact selected PostgreSQL journal
  instance. Runtime construction checks that instance and the provider-held
  operation/composition snapshot. Maintenance reuses the journal's captured
  quoted schema, command timeout and schema-version check; it selects no second
  source, schema or provider and performs no registration-time I/O.
- Each authorized call owns a fresh explicit ReadCommitted transaction for the
  definition's authoritative database and validates the returned context. One
  awaited native callback checks schema version and executes one bounded CTE
  batch. The full application/environment/trusted-scope/operation filter and
  ordinal key ordering precede LIMIT and row locking. SKIP LOCKED appears only
  in maintenance. No claim, lookup or handler acquires that behavior.
- The single materialized database-time observation supplies eligibility. At
  most the requested 1..256 finalized rows are locked. Disjoint CTE branches
  either clear expired payload/error fields or delete a whole row whose
  persisted protection expiry permits removal. Only two bounded counts return;
  no result payload is materialized by maintenance. There is no batching loop,
  retry, scheduler, background worker or implicit tenant sweep.
- Result and protection expiry remain per-row recording facts with indefinite
  defaults. New policy does not rewrite old expiry. Positive ticks round up to
  native microseconds. Logical expiry already applies before maintenance;
  unsupported fingerprint comparison precedes mismatch, and comparable mismatch
  precedes matching-result expiry. Payload clearing retains full identity,
  fingerprint and result metadata. Unsupported decoding never triggers deletion
  or protected reexecution. Protection eligibility alone does not release a key;
  only actual eligible full-row deletion ends that retained protection.
- Candidate counts are explicitly separate from persistence. Executed requires
  bounded counts and the owned transaction's actual completion; Denied/Disabled
  reject all counts/completion. Confirmed properties are non-null only for
  Committed, remaining null after Unknown. The service awaits commit and owned
  disposal without completing a caller owner or disposing its data source.
  Unexpected work faults retain priority over secondary disposal faults. The
  existing RM-02 completion is published after native cleanup; later release
  diagnostics do not redefine that persistence fact.

## Native seam and test responsibility

NativeCommitBoundarySource is confined to IntegrationTests. It delegates native
connections, commands, parameters, readers and transactions to the real selected
provider. Its commit seam first awaits a successful real native commit, records
that fact, then throws a controlled IOException through the actual RM-02 commit
boundary. RM-02 derives Unknown itself; the seam constructs no completion. The
recovery test checks independent committed business/journal/outgoing rows, fresh
authorized lookup of the original result, same-key replay without another
handler, and no terminal commit/rollback retry. The separate disposal seam
throws only after actual native transaction disposal and checks that Committed
remains Committed with its real cleanup diagnostic and connection release.
These are simulated acknowledgement/disposal failures, not real network faults.

The fourteen authored real-provider cases cover logical expiry and persisted
policy, comparison/version precedence, unsupported decode, tombstones, actual
deletion, the 257-row boundary, skipped row locks, uncommitted deletion versus
lookup/arbitration, real claim deadlock followed by an explicit fresh attempt,
the two native seams, uncertain candidate counts, disabled/denied nondisclosure
and one-tick rounding. Race assertions consult actual server blocker relations
and deadlock counters. Observer loops are finite test controls. In-flight race
tasks are canceled/joined and held locks released before owned scopes, source
and fresh schema are released. Fixture initialization requires the explicitly
selected private loopback database and normal durable PostgreSQL settings.

The direct public maintenance port/result, one scoped implementation and one
SQL batch are necessary to provide the approved privileged operation. Reusing
three small internal journal helpers preserves the same concrete configuration
and protocol rather than introducing another backend. The native decorator
addresses the distinct post-real-commit reporting requirement. No Core test
hook, fabricated completion, SQL simulator, network proxy, transport framework,
transaction framework, new runtime package or test harness was introduced.
Tests remain ordinary MSTest cases in the existing projects.

## Evidence limits and accounting

The initial checkpoint reviewed source/test/document assertions without treating
authored tests as passing evidence. Subsequently the reviewer independently
aligned `.artifacts/operations/CIS-0018/integrated-receipt.json` and
`WORK-004/retention-receipt.json`: all 46 current source/vector hashes, the three
Core source-output/Unit/Integration copies, CommonTesting and collector output
and test-directory copies, both current test assemblies, runtime configurations
and all nine exact final TRX hashes match. The receipts agree on source and
report records. No mismatch was found. Current Core SHA-256 is
`76df3a0cada0f502bd063a56509b74d684dd1e2503fca83a1990e6deb11c6340`.

Independent TRX result-to-definition checks confirm every reported class count.
Each actual Windows/Ubuntu24.04 lane has 119 passing Operations Unit cases
(including the three new factory guards), 25 passing public-helper cases,
33 passing real PostgreSQL cases (14 retention/recovery plus 19 journal) and
42 passing SDK/affected-catalog cases: 219 per runtime, zero failed/not-executed.
The ninth report contains one passing actual Windows public-package case. Both
current runtime configurations retain .NET/ASP.NET10.0.12 and Disable roll-forward;
global.json and the receipt retain SDK10.0.401. This is focused runtime/package
evidence, not full-suite or release qualification.

The retained first 33-case PostgreSQL run contains 32 passes and one genuine
failed race assertion: the independent observer did not confirm the expected
blocking relationship. The original BEFORE DELETE test hold preceded the actual
uncommitted deletion marker, so that arrangement did not guarantee the contender
would block. The corrected fixture holds AFTER DELETE, retaining the actual
server blocker assertion, no-early-handler check, expired lookup, post-release
Prepared/one-handler/count assertions and cancellation/join cleanup. The fix
changes test synchronization, not production protocol. The retained focused
one-case report passes, followed by the final complete 33-case passes on both
OS lanes. The failure is preserved rather than discarded or counted as a pass.

The real-commit seam and its assertions were rechecked against the passing
retention cases: native commit is awaited before injected failure, RM-02 itself
publishes Unknown, independent lookup/counts recover the actual committed result
without another handler, and native disposal diagnostics preserve Committed.
Uncertain maintenance counts remain candidates. No completion was fabricated,
and this remains simulated acknowledgement loss rather than a real network fault.

The reviewer read `.cis/local/operations-intake/postgres-restoration.json` and
the receipt's restoration summary. Those coordinator-performed Docker/database
checks report zero live clients/case schemas, 68,026,368 final data bytes,
removal of the exact fresh owned container/volume/unshared image and secret
files, and 2.864 seconds native restoration. The reviewer did not repeat Docker
or database checks. Earlier WORK-003/005 records retain their historical exact
candidates. Full Linux SDK-backed package execution remains unverified; no EF,
real network fault, external deployment, platform-wide, coverage/mutation,
performance or release qualification is inferred.

Reviewer accounting: five estimated active source minutes for the earlier
read-only review plus ten for this frozen source/test/document assessment and
record. The final review stays within its separately assigned fifteen-minute
cap and shares the existing WORK-004/global allocation. This reviewer performed
no native/DB execution, Git/CIS mutation or new logical work attempt. Only this
review document was written.

Final receipt/current-byte/TRX alignment, synchronization correction review and
this update used an additional estimated five active source/evidence minutes,
within the assigned five-minute cap and the same WORK-004/global allocation.
No native work was repeated. No material review finding remains.
