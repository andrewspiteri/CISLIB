---
title: "RM09 R06 full details and history review"
type: design-review-package
status: Approved
change_id: CIS-0029
---

# RM09 R06: from quick details to full context

The user requested a link from the side panel to full event/job/task details,
including run history and configuration. R06 retains the approved application
and four-tab base, keeps quick side panels, and adds **View full details** to the
job panel. R05 was revised before approval; its exact review is preserved in
local Git commit `f947e78cbae9614fa6c729f682863a49999262c5`. R04's earlier native
approval remains preserved in `ed65c3f1`; it does not approve these new views.

## Connected journey

1. Select a reported job, inspect its quick panel, then open View full details.
2. The dedicated Job page has Overview, Run history and Configuration. History
   opens the shared history view scoped to that logical job, independently of
   the currently selected registration report.
3. History links to the originating Scheduled Task and triggering Event. Retries
   and recovery remain attempts under one logical run and original operation.
4. Scheduled Task remains the main association/settings record, with exactly
   **Trigger** and **Job** contextual configuration tabs. **View run history**
   is separate and includes that task's history even if its selected job changes.
5. Event details retain immutable event/produced-run context. Their Configuration
   view shows captured trigger settings, distinct from current editable settings.

This follows the earlier **RM-10 Scheduler Brainstorm** discussion read directly
through the connected chat. Its user decisions, turn references and current API
mapping are documented in [reconciliation](reconciliation.md). Prior assistant
text is requirement evidence referenced by the current user, not proof that the
runtime contracts are delivered.

## Delivered facts and implementation prerequisites

Current job Overview can use the existing reported definition/resource snapshot.
Current Job Configuration supplies only optional reported local revision and
effective enablement. The preview keeps those distinct from central desired
configuration, task revision, trigger revision and job execution-contract version.
No CountrySettings/job binding or unreported job defaults is inferred.

Run chronology and typed event/task records are not supplied by today's
Management API. The populated history/task/event previews are **synthetic desired
contract examples**. Production needs P6 bounded authoritative run/attempt
capture/retention/authorized query, P7 typed task/event identity/detail/relationship
reads, and P8 explicit richer job policy/configuration-source mappings. Current
inventory, Operations lookup, logs or current worker registration cannot stand
in for these records. Missing history support is shown as unavailable, not an
empty successful run list.

The history example keeps business execution by worker A separate from original
result recovery by worker B. Their captured versions/builds remain per attempt;
a current application version cannot overwrite them. An unknown outcome is not
presented as rollback or confirmed failure. Captured task/trigger/configuration/
contract references remain distinct, and history views share the same records
with authorized job/task/event scopes.

This review adds no scheduler, event bus, general task engine, telemetry store,
Audit archive, editing, execution/cancellation control, package or deployment.
RM-10 runtime work needs its own imported specification and bounded plan. Existing
application search, scoped definition query and real human/native authorization
prerequisites are unchanged. Mobile remains excluded.

## Review boundary

The new review is one complete full-details journey, not approval of live run
history or a completed RM-10 module. The [wireframes](wireframes.md) and
[design brief](design-brief.md) define its routes/actions/states; the
[design record](design.md) binds exact current renderer/PNG hashes and preserves
prior decisions. All content is synthetic. Normal current-scope/direction/plan/
owner reconciliation still precedes production work.

## Rendered review index

Current R06 gate: **Approved**. Andrew Spiteri selected **Approve connected
design** on 25 November 2026. Native approval at 17:04:52 UTC binds renderer
`6375438db766` and manifest
`sha256:ff55f6dafc4710fa865420567cb8e51acdc8968118dca12461abfb47971fed7a`.
Earlier R04 approval remains preserved. This approves the connected UI design;
the missing RM-10 contracts described above remain implementation prerequisites.
Original human-reviewed renderer SHA256:
`6375438db766391a9c376cf43ed12ec33fe24273e76334092e63e850229f7309`.
Original native render-result SHA256:
`ea900731efdbc892051f88c011228ceab24ac785637a79c30de7228b8067ff5d`.
After normal high-level direction reconciliation, native scaffold/render
refreshed only its generated provenance block. The current renderer is
`d1e9348ce9174b742ad7d3bc94227decc26014b38955d97950bf387b9bd04aac`;
all twenty-one PNGs and the manifest remain byte-identical. The existing R06
decision was recorded against that unchanged visual payload at 17:48:25 UTC.
This was no new human visual decision; original input remains in Git `ad2449d5`.

Start with the Job panel View full details link, then Job history, Scheduled
Task Trigger/Job configuration and immutable Event context/configuration.
All three application images match the approved R04 references byte for byte.

| Screen | State | Desktop size | Preview |
| --- | --- | --- | --- |
| rm09-applications | no-matches | 1600 × 1000 | [Open PNG](assets/rm09-applications--no-matches--desktop.png) |
| rm09-applications | primary | 1600 × 1000 | [Open PNG](assets/rm09-applications--primary--desktop.png) |
| rm09-applications | unavailable | 1600 × 1000 | [Open PNG](assets/rm09-applications--unavailable--desktop.png) |
| rm09-capabilities | details | 1600 × 1000 | [Open PNG](assets/rm09-capabilities--details--desktop.png) |
| rm09-capabilities | primary | 1600 × 1000 | [Open PNG](assets/rm09-capabilities--primary--desktop.png) |
| rm09-configuration-list | details | 1600 × 1000 | [Open PNG](assets/rm09-configuration-list--details--desktop.png) |
| rm09-configuration-list | primary | 1600 × 1000 | [Open PNG](assets/rm09-configuration-list--primary--desktop.png) |
| rm09-event-full | configuration | 1600 × 1000 | [Open PNG](assets/rm09-event-full--configuration--desktop.png) |
| rm09-event-full | primary | 1600 × 1000 | [Open PNG](assets/rm09-event-full--primary--desktop.png) |
| rm09-instances | details | 1600 × 1000 | [Open PNG](assets/rm09-instances--details--desktop.png) |
| rm09-instances | empty | 1600 × 1000 | [Open PNG](assets/rm09-instances--empty--desktop.png) |
| rm09-instances | primary | 1600 × 1000 | [Open PNG](assets/rm09-instances--primary--desktop.png) |
| rm09-instances | stale | 1600 × 1000 | [Open PNG](assets/rm09-instances--stale--desktop.png) |
| rm09-job-full | configuration | 1600 × 1000 | [Open PNG](assets/rm09-job-full--configuration--desktop.png) |
| rm09-job-full | primary | 1600 × 1000 | [Open PNG](assets/rm09-job-full--primary--desktop.png) |
| rm09-run-history | primary | 1600 × 1000 | [Open PNG](assets/rm09-run-history--primary--desktop.png) |
| rm09-run-history | unavailable | 1600 × 1000 | [Open PNG](assets/rm09-run-history--unavailable--desktop.png) |
| rm09-scheduled-jobs | details | 1600 × 1000 | [Open PNG](assets/rm09-scheduled-jobs--details--desktop.png) |
| rm09-scheduled-jobs | primary | 1600 × 1000 | [Open PNG](assets/rm09-scheduled-jobs--primary--desktop.png) |
| rm09-task-full | job | 1600 × 1000 | [Open PNG](assets/rm09-task-full--job--desktop.png) |
| rm09-task-full | primary | 1600 × 1000 | [Open PNG](assets/rm09-task-full--primary--desktop.png) |

Twenty-one synthetic images are design evidence. The eight new full-page states
and updated Job-panel link were visually inspected. Native checks, source-aware
review and an independent screenshot reader found no material issue. No live
history, Task/Event backend, scheduler, browser or accessibility proof is claimed.
