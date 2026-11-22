# Managed jobs review follow-up

The user supplied a source review of `b1eddad71bd0f75531d6c09d8c6d0b556a1dba97`
identifying two managed-jobs defects and one documentation link. The review is
requirements input; its positive observations and recorded test results do not
constitute a fresh native pass.

WORK-012 owns the grouped correction, regressions, documentation and independent
review under the original delegated RM-05 outcome. A supported task addition and
allocation transfer assign **9,000 active seconds and four logical attempts**
within the unchanged **40 hours / 48 attempts / 6 GiB** containing allowance.
Completed tasks, consumed work and all 5,400 restoration seconds remain intact.
The effective distribution is recorded in
[effective-review-owner-allocation.json](effective-review-owner-allocation.json).

The correction is bounded to:

- Retaining accepted activation transitions when intermediate configuration
  revisions coalesce, without replaying work on ordinary completion or failure.
- Preserving unresolved failures by exact job/resource identity with finite
  storage and recovery semantics; another resource cannot clear them.
- Correcting the PostgreSQL setup link to the existing hosting instructions.

The two code changes can proceed independently; the root controller serializes
builds and tests. Runtime startup remains sequential. No provider, dependency,
scheduler, harness or tooling expansion is included. The branch remains
unmerged. Any service-workflow result must name an actual run against the
corrected remote commit; historical results do not satisfy that condition.

Current results and qualification limits belong in
[verification.md](verification.md).

Accepted configuration records a transition revision per frozen catalog entry.
The supervisor captures those revisions atomically and admission checks its exact
expected revision under the same runtime gate as duty reservation. An admitted
attempt consumes that epoch even if its callback faults synchronously. A stale
request or an older duty can defer without consuming the requested activation.
Actual activation cleanup failure stays distinct from benign configuration drain.

Unresolved failures retain exact scope/job/resource identities. Active and failed
resources share a fixed per-job diagnostic bound derived from the original catalog
limits (40 by default). A retained resource can reuse its slot for explicit
recovery; unrelated jobs have independent diagnostic capacity. Recovery requires
successful callback and actual scope cleanup, preserving original business results
and ownership-loss diagnostics. There is no automatic business replay.
