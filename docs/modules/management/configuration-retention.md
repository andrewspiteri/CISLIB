# Configuration storage and bounded retention

The applied trusted deployment selects one shared logical `QuotaBytes`, a finite
`RetainedUnreferencedReleases` value **N** (initial profile: ten), and optional
exact `PinnedReleaseIds`. The selected quota, N and sorted pin list contribute to
the loaded deployment digest; apply a changed policy using the authenticated
[catalog workflow](configuration-service.md). Loading another file or restarting
a replica does not silently change persisted policy.

## What retention protects

Cleanup first excludes every release referenced by a current default or cohort
assignment and every explicitly pinned release. For each binding it retains the
newest N remaining eligible releases, ordered by native creation time and UUID
as a stable tie-breaker. Eligible-first selection means protected releases do not
consume that history allowance.

Every publication referenced by any retained release stays protected, including
a common publication shared by multiple bindings. Among the remaining orphan
publications, the same deployment-selected N retains the newest eligible items
per source. This explicit publication allowance protects the normal gap between
publishing content and creating a release. Selecting zero permits the next
cleanup to remove any unreferenced release or publication; operators must account
for that policy when preparing a new reference. A pruned publication cannot be
selected by a new release, and a pruned release cannot be selected for rollback.
The service refuses the exact missing reference instead of substituting newer
content or recreating the old object.

Definitions, immutable schema/default contracts, stable source and binding IDs,
current maps and cohort metadata remain retained. Historical consumer adoption
reports do not pin a release or source. Session and adoption metadata is charged
to the same logical quota and released when its owning registration is removed
by ordinary telemetry retention.

## One existing maintenance owner

The existing Management Heartbeat checks authority every five seconds and admits
one bounded cleanup at most once per minute. It reuses the original RM-02 owner;
there is no new timer, scheduler, store or journal. The configured cleanup row
maximum is one allowance across expired registrations, eligible releases, then
eligible publications, rather than a fresh allowance for each table. Cascaded
session/adoption rows belong to their one removed registration. Exact deleted
logical charges and both existing capacity counters settle in that same native
transaction.

Writers and cleanup preserve namespace authority, then capacity when needed,
catalog, and row/reference lock order. The shared catalog lock serializes quota,
pin, assignment and reference installation across replicas. A competing installer
either protects the exact retained object before cleanup, or receives a missing
reference refusal after cleanup; it never installs a dangling reference. Bounded
row selection also uses native row locks and `SKIP LOCKED`.

Cleanup does not evict protected content to admit a new write. When pinned or
assigned content fills the quota, a new operation returns its original capacity
rejection with no source revision, map generation or quota drift. Operators may
explicitly change retention/pins/assignments, wait for bounded cleanup or apply a
reviewed larger quota. They do not reinterpret a rejected or uncertain original
key as different intent.

## Original results remain independent

Content cleanup never deletes the RM-03 operation journal or required outgoing
Audit. The original key still recovers its payload-free publication/release/map
decision after eligible content is pruned. Matching replay returns that decision
without recreating content or repeating Audit. An old successful decision is not
a promise that its bytes remain available forever.

An unconfirmed native cleanup commit returns `management.transaction_unknown`
without committed deletion counts. It does not guess a byte delta or retry an
original business operation. A subsequent independent maintenance owner observes
actual retained rows, removes only then-eligible content and accounts only its
own deletions. Content and counters remain in the same original commit.

Focused native test source reuses the existing isolated PostgreSQL/RabbitMQ/HTTPS
fixtures for eligible-first history, shared/default/cohort/pin protection, one
row budget, hard quota, reference-installation races, original-result recovery
and acknowledgement loss after a real commit. These authored cases are not a
passing receipt; actual serialized runs belong to the current change's evidence.
Logical byte accounting does not describe PostgreSQL page size, disk compaction
or arbitrary process heap usage.
