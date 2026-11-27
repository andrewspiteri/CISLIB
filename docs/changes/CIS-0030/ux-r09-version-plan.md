# R09 — Configuration versions and deployment

The user's latest decision replaces the previous Save/Apply screen proposal:
each application/environment/Configuration has one page listing saved versions,
a clearly marked deployed version and a Deploy action on any retained saved
version. Redeploying an older version is rollback. No normal workflow requires
separate publication, release, canary or assignment screens.

WORK-189 prepared the exact backoffice wireframe/preview and acceptance contract.
The [recorded user approval](r09-user-approval.json) accepts that direction with a
read-only comparison addition. WORK-190 implements the page, native-client
orchestration, editor/navigation/resize corrections, meaningful tests,
documentation and independent review. Supporting WORK-191 applies the later
explicit clarification: compare with the actual currently deployed version,
without a second version picker. This supporting task supersedes only the
earlier arbitrary-selection wording; executable task history is preserved.

A saved deployable version binds the existing immutable coherent release and its
exact Shared/Application references. Save uses the existing setup (when needed),
publication and release effects with distinct originals and truthful partial
recovery. Deploy is a separate deliberate whole-map assignment. Its review names
the application/environment/Configuration and all-instance target; any existing
cohort-specific targeting is explicitly replaced, never silently retained while
claiming one deployed version. Actual instance acceptance remains an independent
observation, available without a required detour through another screen.

Use immutable change notes and saved timestamps for readable version labels,
with exact release identities behind row actions. Actual actor kind/reference,
when present, belongs in Technical details; no human author is invented. Do not invent
stable sequential version numbers from the order of a retained/paged list. This
requires no new counter, migration, package, runtime framework or backend Save
API. Native original-key, ETag, capability, current-session, exact-byte, Audit and
actual owned-cleanup contracts remain intact.

The comparison uses the current fresh exact assignment's uniform default, not
the newest retained row or a guessed source publication. Both saved snapshots
must be independently readable and match the binding/environment. A targeted
map, no deployed version, stale read or protected content keeps comparison
explicitly unavailable. Exact numbers and Unicode survive the bounded read-only
JSON/text comparison; it does not compute effective merged values. A changed
Shared reference remains visible even if the displayed Application JSON is
identical. Deployment/map/session changes invalidate the prior comparison.

The delegated controller transferred seven unused hours/eight attempts from
unstarted WORK-140, retaining it at 2h/2 attempts: design 2h/2 and a shared
implementation pool of 5h/6. The implementation pool is now distributed as
WORK-190 4h/5 attempts and WORK-191 1h/1 attempt. This is a split of the existing
total, not an increase. All agents, already-performed intake, rendering, tests,
docs and review share these totals. Existing shared 120h/128 attempts, 3h/3
restoration reserve, 6 GiB/64 MiB reserve and contained Docker allowance remain
unchanged. Historical costs/results and manual-session closure are retained;
the earlier finite review lease expired and drained. No Scheduler
implementation, qualification campaign, push, merge or release is included.

Implementation source and focused model checkpoints do not establish the whole
workflow's acceptance. Current build, real browser/provider effects, independent
SQL/Audit assertions and owned cleanup remain pending root verification; no
future pass counts are assigned by this plan.
