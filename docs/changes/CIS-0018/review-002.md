---
title: RM-03 coordinator independent review
type: review
status: Complete
---

# WORK-002 independent review

The assigned reviewer inspected the frozen coordinator, global admission marker,
result reader, hosting registration, five runtime test files and the portable
contract/coordinator guide. This review is independent of the source author.
No product-native execution was performed by the reviewer.

No material source finding remains. The earlier provider-snapshot concern is
resolved by a provider-held passive definition/composition snapshot and a
fail-closed comparison before runtime construction. Tests cover both appended
and replaced definitions after provider creation, as well as rejection of an
opaque factory before it is invoked. The documented contract requires building
a new provider after changing operation definitions.

## Assessed boundaries

- Scope resolution and the existing complete capability authorizer precede
  admission, journal access and retained-result disclosure. Copied caller data
  remains a projection. Authorization/audit/resolver faults are not treated as
  ordinary diagnostic failures.
- One nongeneric static ConditionalWeakTable holds a tiny atomic marker without
  a context back-reference. Repeated, nested and concurrent attempts across
  service instances, definitions and generic types cannot acquire a second
  admission. Pre-admission faults leave admission available; admitted paths
  never reset it.
- The claim, handler and finalization are sequential. The handler receives the
  original context and the normalized owned input outside journal callbacks.
  Controlled tests use the actual RM-02 ADO owner to exercise separate native
  callbacks, explicit commit and rollback-only refusal. Handler/codec/write/
  cancellation faults poison the context while preserving the original fault
  and first rollback reason. Prepared is never a commit fact.
- Original persisted bytes are copied and decoded before Prepared and on replay.
  Unsupported fingerprint comparison precedes mismatch, comparable mismatch
  precedes expiry, and matching logical expiry precedes result decoding.
  Incomplete, corrupt, mismatched-identity or unsupported rows never run the
  handler. Business rejection remains a separately committable final decision.
- Lookup creates its own authoritative ReadCommitted owner, reads and actually
  attempts rollback followed by disposal. It neither completes an original
  write owner nor owns the application data source. Native transient mapping is
  restricted to Begin/Read phases; query programming faults, unexpected codec
  faults and authorization faults retain their identity. Secondary cleanup
  cannot replace a primary failure or downgrade a known Found result. Optional
  read completion contains only facts the owner actually publishes.

## Necessity and scope

The direct coordinator, one weak admission marker, bounded result reader and
passive registration are necessary to implement the approved protocol. The
controlled journal/read-owner seams complement the existing transaction test
support; they do not implement a SQL engine, alternate runtime, DI framework,
test harness or scheduler. A simpler whole-handler native callback would violate
the existing non-reentrant RM-02 ownership boundary. No responsibility or
framework expansion was identified.

## Evidence boundary

The reviewer independently aligned the runtime receipt with the current 22 C#
source hashes, source/output and executed-copy Core hashes, unit assembly,
portable vectors and both retained TRX hashes. Both reports contain exactly
84 passed, zero failed and zero not-executed Operations cases: 28 foundation
plus 56 runtime cases, in the seven expected classes. The actual lanes are
Windows and Ubuntu24.04. Core SHA-256 is
`f0ade871822e81f22554c256d0cd230eb9e507fab961351347c1b8f6ef82dbd3`.
The receipt records SDK10.0.401, runtime10.0.12 with Disable roll-forward,
successful scoped formatting/verification and a zero-warning/error affected
Release build. The initial three test-only generic-inference build errors and
their costs remain preserved separately; explicit generic arguments corrected
them without a runtime change.

Evidence is retained in
`.artifacts/operations/CIS-0018/WORK-002/runtime-receipt.json` and its exact
Windows/Ubuntu TRX paths. Controlled journal facts do not establish PostgreSQL
uniqueness, durability, isolation, arbitration or package consumption; those
remain in their separately approved delivery tasks. No required review finding
remains for this coordinator checkpoint.

Reviewer accounting: four active source minutes for read-only preparation, nine
for early review, and twelve for the final frozen source/test/document review.
An additional three source/evidence minutes aligned the actual receipts and
current bytes. No native product work or new logical work attempt was performed
by this reviewer.
