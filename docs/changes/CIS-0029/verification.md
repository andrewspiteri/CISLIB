---
title: CIS-0029 verification
type: verification-record
status: Draft
change_id: CIS-0029
---

# Verification

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |

| Intake | Exact source preservation | `source-intake.json` | SHA256 matched | All twenty acceptance scenarios retained; supplied document is requirements, not approval of an unseen UI. |
| Intake | Actual baseline reconciliation | `reconciliation.md` | Source inspected | Current main already supplies RM08; four small prerequisite groups remain for the portal. No RM09 product test pass claimed. |
| Draft | CIS dossier and draft plan | `rm09-change-create.json`, `rm09-draft-plan-build.json` under local configuration-publication-intake | Created/derived | The canonical seed is Draft and has no implementation authority. The ten-leaf proposal is `implementation-plan.md`. |
| Draft | Outcome authorization and legacy feature import | Retained `rm09-authorize.json` and `rm09-draft-feature-import.json` | Blocked, unchanged | Older no-UI direction and lack of accepted impact work remain intact. No approval or executable task import was manufactured. |
| Design | Native wireframe validation | `rm09-wireframe-validate.json` | Passed | Eight backoffice screens, 44 actions, three journeys; exact digest is retained by CIS. |
| Design | Governed scaffold | `rm09-design-scaffold.json` | Generated | Existing shell/component templates; source provenance retained. PNG rendering and human review remain pending at this checkpoint. |
| Design | Documentation validation | `rm09-docs-validate-fixed.json` | Valid; zero errors | The 77 existing documentation warnings remain visible. New catalogue authority/YAML authoring errors were corrected; failed diagnostics remain retained. |
| Design | Frozen renderer syntax and provenance | `rm09-design-validate-frozen.json` and native `node --check` | Passed | Twenty proposed targets, including two actual 390 px layouts. This is static validation, not rendered accessibility or product execution. |
| Design | Render capacity admission | `.cis/local/testing/rm09/design-render-admission.json` | Within existing ceiling | 5,578,540,696 bytes including fixed allowances, reserve, canonical design inputs and 32 MiB maximum PNG growth, under 6 GiB. No cleanup. |

The draft renderer uses ignored Sharp 0.35.5, libvips 8.18.7 and Node 24.15.0;
the tool installation observed npm 11.13.0 without changing product pins.
The embedded Inter v4.1 font has its SIL Open Font License notice retained.
The runtime/cache measured 35,091,009 bytes before rendering, within its 96 MiB
admission. Conservative preparation allowances and exact blocked/successful
metadata timings are retained in `.cis/local/testing/rm09/pre-review-cost.json`.
These observations do not grant production execution or claim exact token costs.

The current phase is directly user-authorized draft preparation. No production
portal, backend changes, live operator identity, management mutations or native
consumer tests have been implemented or executed. Normal task derivation and
execution admission follow the exact UI/scope decision through supported CIS
operations. Current and failed tool observations remain retained rather than
being relabeled as successful import or approval.

## Verified prerequisite: merged CIS installation

Both reviewed branches were merged through CIS PR #4 and CISLIB PR #19. CISLIB
main is `6c70cee807f12caddf40cd1b443e4a20045d35aa`; the source build is from CIS
main `aad03d06be88662538743996d6e67b9f9480afc5`.
The installed candidate is
`0.3.0+worktree.5ea282098634e43c723b6c86696c10d44920f6bc4250c8c47ce07fef858fee47`.
Its build-manifest SHA256 is
`382911d89cfe60fe10afa3579042c4f396581d1db4f75c96b38f58e88635ba40`.
All 258 installed files match; the existing explicit wrapper passed version and
help checks. The installation matched all 988 protected files, retained the old
binary rollback and left graph bytes, completed consumer owners and native
reports unchanged. Installation receipts are in
`.cis/local/testing/main-install-20261125/`; this dated proof precedes RM09 draft
document edits. It does not claim a new consumer native execution or full source
qualification.

## R02 review continuation

Andrew Spiteri requested changes to R01: no mobile, and begin with a filterable
application list followed by selected application instances and their reported
versions. Native `design reject` retained the exact R01 decision/hashes and
removed rejected PNGs; local Git commit `d4ce9478` preserves the earlier draft.
No R02 approval or production resumption is inferred.

R02 wireframe validation found two heading-level errors, retained the failed
result, then passed after correction. A separate source-aware review caught the
actual registration identity (`registrationId`, not `instanceId`) and optional
last-seen field. Those corrections were applied before regenerating the native
scaffold. Version reporting already exists; no new version API is required.
The corrected native wireframes contain two backoffice screens and twelve read
or navigation actions. Their digest is
`sha256:f423833ca481701d21886592d67471c57b87b780ed980d58fa32bc2dee1fd09a`.
Receipts are retained under local configuration-publication-intake with
`rm09-r02-*` names. Only review documents and synthetic design assets changed.
These checks are not browser, keyboard, accessibility or product test passes.

R02 rendering completed successfully: six desktop PNGs at 1600 × 1000, using
Sharp 0.35.5/libvips 8.18.7. The native gate is **PausedForReview** and approval
state is **ReadyForReview**, with no human R02 decision. The pre-render design
validation correctly reported missing new PNGs after rejection; the normal
render command generated them, and post-render native validation passed. Both
results and costs remain retained.

Root visually inspected all six PNGs. A separate context-limited model reader
inspected only the two primary screenshots and correctly identified filtering,
selection/back navigation, per-instance registration versions, presence limits
and synthetic data. No material clipping, overlap or task confusion was found;
the reader noted the minor ambiguity of a Current page label without a page
number. These are model-assisted/source and visual reviews, not real-user
usability, browser, accessibility or native product test evidence.

The exact renderer/result hashes and all six links are in review-package.md.
R02's additive conservative preparation accounting and fresh storage admission
are retained under `.cis/local/testing/rm09/r02-*`. The measured admission is
5,578,552,596 bytes under the unchanged 6 GiB ceiling with 32 MiB image-growth
headroom, fixed allowances and reserves included. Earlier costs and failures
are retained. No cleanup, resource reset or new production authority is claimed.

## R03 application detail tabs review

Andrew Spiteri accepted the R02 application/instance direction and requested
Instances, Configuration and Capabilities tabs. R02 is preserved in local Git
commit `6176b895d423ddfbf76936733432191bd2287782`. The native requested-revision
receipt records that partial acceptance and the material navigation change;
it does not manufacture blanket approval of the unmodified full R02 manifest.
Only review work continues; production remains paused and mobile excluded.

The R03 wireframes contain four screen definitions and twenty-one read/navigation
actions. An independent source-aware check verified the ConfigurationDefinition
and capability/caller-hint boundaries. It caught a documentation regression in
already accepted instance fields; registrationId, optional lastSeenAtUtc with
Not reported, lowercase presence mapping and defaultsJson spelling were restored
before renderer work. Both initial and corrected native scaffold receipts/costs
remain retained. The corrected wireframe digest is
`sha256:06582aab7edb438721e3d82c72c08761839474932dbc02cc6fc6e3271e6b155a`.
Documentation validation passed with zero errors and the same 77 existing
warnings. Scoped definition completeness remains an explicit prerequisite;
current global discovery is not presented as complete selected-app filtering.

Fresh R03 storage admission is 5,580,965,481 bytes under the unchanged 6 GiB
ceiling, including fixed allowances/reserve and 32 MiB renderer/image growth.
The proposed 48-hour shared envelope and historical costs are unchanged; additive
R03 conservative estimates are recorded under `.cis/local/testing/rm09/`.
No production package install, service access, native consumer test, build,
push or merge is part of this review revision.

R03's eight desktop frames rendered and post-render native validation passed.
The final renderer is
`071d759ed388a2852f15ce82ff9a07ceb3e072991df4e1c50774be8c33ae24b4`;
the final native render-result hash is
`8450342bbf6487b7bf961ac8b469f8374d4c3ad92f52714d317b21f29a00dab4`.
The gate is PausedForReview/ReadyForReview; R03 has no human decision yet.

Root inspected all five detail-state PNGs and corrected one orphaned letter
wrap in the capabilities column. All three application PNGs match accepted R02
reference hashes byte for byte; comparison evidence is retained locally and is
not presented as native approval reuse. A separate context-limited model reader
of the three primary detail PNGs identified the tabs, consistent target, metadata
only configuration list, advisory Management capabilities and absent-hint
meaning correctly, with no material clipping or confusion. These are visual/model
checks, not real-user usability, accessibility or browser/service tests.

The first column-fix rerender encountered one PNG write error; an exclusive
read-open then succeeded and a bounded retry through the normal native command
completed. The cause was not established. The failed receipt/cost and successful
retry are both retained. No manual PNG edits, cleanup or hidden result
replacement was performed. The final package index binds all eight output hashes.

## R03 human approval

Andrew Spiteri accepted the R03 image and requested Scheduled jobs as an
additive follow-up tab. Normal `design approve` succeeded, binding the exact
R03 wireframe/renderer and the PNG manifest
`sha256:ca0b5b2960776dd6a6d474807c80b54bb2ffc1ee94ef646e0f33847c1d070bc5`.
The native approval receipt is `rm09-r03-user-approval.json`; the checkpoint is
Approved/Approved. The unseen Scheduled jobs design is not pre-approved.

## R04 Scheduled jobs preview

R04 adds the explicitly requested Scheduled jobs tab to the approved R03
presentation. Native wireframe validation passed: five backoffice screens,
twenty-five read/navigation actions, digest
`sha256:38af1540c843cb2abcf3e581d7d309a3694afc7dc32a67819bf9eb450e351b68`.
The new jobs block/brief received a separate source-aware model review with no
material findings. Existing registration/version/optional-time facts were
preserved through a narrow document delta; no new read API was invented.

Nine desktop frames at 1600 × 1000 rendered successfully and post-render native
validation passed. Root inspected the new Jobs view and expanded Instances tab
row. The three application images match the native R03 approval byte for byte;
local comparison evidence is retained. A separate context-limited model reader
correctly distinguished the selected registration report, declared support,
mode/state/outcome, unknown occurrence times and synthetic/non-executable view.
Its minor mode/state-label ambiguity was corrected, then the normal renderer
and native validation passed again. These checks are not human usability,
browser, service integration or accessibility evidence.

The final R04 renderer is
`249016b8fde00257a25347205b69f5764052d8be501120f1dea565e12dbcf29d`;
render-result hash is
`e6be96026ed855f54d8327a2270bc58949198e32f24166c23a4daff6c3e0819a`.
The current gate is PausedForReview/ReadyForReview for the new addition; R03's
prior exact approval remains preserved. No production work resumed. Native
same-change reuse is unsupported, so no such mapping or carried authority is
claimed; source code/drawing preservation and hashes are reported accurately.

Fresh admission was 5,586,791,952 bytes under 6 GiB, including fixed allowances,
reserve and 32 MiB growth. Additive conservative R04 costs and exact metadata
walls remain retained within the same proposed shared 48-hour envelope and
12-hour review redistribution. Earlier costs/failed observations are unchanged.
No cleanup, package installation, native consumer build/test, push or merge.

## R04 human approval

Andrew Spiteri approved R04 (ok it is good) and requested an additive item-detail
interaction, suggesting a side panel. Native `design approve` succeeded with
Approved/Approved; exact hashes remain in its approval row and
`rm09-r04-user-approval.json`. The unseen side panel is not pre-approved.

## R05 item-details review preparation

Andrew Spiteri approved R04 and requested item selection to open details,
suggesting a side panel. The exact native R04 approval is retained in
`rm09-r04-user-approval.json` and local Git commit
`ed65c3f10e1f10a67cb61d8369fbb8af3711c6b4`. R05 is additive and does not reject
or silently extend that approval to the unseen interaction.

A narrow wireframe delta specifies one read-only modal right-side panel across
Instances, Configuration, Capabilities and Scheduled jobs. Five existing screen
IDs remain, with thirty read/navigation actions and four new detail states.
Native validation/scaffold passed with wireframe digest
`sha256:d55d6857e738aa933e4e6c958f6988946c8d76e4a72d8da48dcade8934253ff6`.
The governed dialog component is selected as a right-aligned variant.
A separate source-aware model review found no material field/identity/refresh
issues. Panels use existing returned data, with no new details API or content
fetch. Jobs selection explicitly includes resourceId + mode within the selected
registration/sequence/scoped job. Configuration remains metadata-only.

Documentation validation passed with zero errors and the existing 77 warnings.
Fresh admission is 5,588,224,883 bytes under the existing 6 GiB ceiling, including
fixed allowances/reserve and 32 MiB growth. R05 additive conservative estimates
and exact metadata timings remain retained under the same proposed shared
48-hour envelope, with review work redistributed to 14 hours and no reset,
resource increase or production authority. All earlier costs/approvals remain.
No package installation, service access, native consumer build/test, cleanup,
push or merge is part of this review revision.

R05 rendering and post-render native validation passed. All thirteen desktop
artifacts and exactly one details state for each of the four detail screens were
verified in the native manifest. All three application images match R04's native
approval hashes byte for byte. Root inspected all four panel PNGs for identity,
fields, readable bounds and the visible persistent Close control; no material
issue was found. A separate context-limited model reader correctly identified
the selected job/list relationship, overlay, Close control, definition/resource/
report distinction, unknown occurrence times and synthetic/read-only boundary.
No material clipping or confusing labels were found. This is model/visual review,
not human usability, browser/focus interaction, accessibility or service proof.

The exact current renderer/result hashes and all thirteen links are recorded in
review-package.md. The checkpoint is PausedForReview/ReadyForReview, with only
the new item-details interaction awaiting a human decision. R04's prior exact
approval remains retained. No production work resumed.

## R06 connected full-details review

The user requested a full-details link with run history and configuration tabs
for Event/Job/Task details, referring to earlier discussion. Root read the actual
connected chat RM-10 Scheduler Brainstorm and reconciled the relevant user turns:
Scheduled Task is the association record with exactly Trigger/Job contextual
configuration tabs and separate history; events produce logical runs with
attempts; per-attempt worker versions/builds and captured references persist.
Selected provenance is retained locally in r06-prior-discussion-evidence.json;
reconciliation.md distinguishes this evidence from delivered API/approval facts.
R05 was preserved unapproved in Git f947e78c and revised normally; R04's exact
approval remains in ed65c3f1 and native history. No R05/R06 approval was inferred.

Current source supplies snapshots, local effective revision/enabled, and exact
Operations result lookup. It does not supply chronology or typed Task/Event
records. P6/P7/P8 are named prerequisites; populated new records are clearly
synthetic future-contract examples, not live completion claims. A separate
source-aware review found no material contract/scope conflict. Task history is
independent of its currently selected job; captured Event settings and worker
attempt versions are not replaced from current state.

The first native validation saw only five screens because a blank split the
inventory table. The expected-nine check caught it; the two table separators
were removed, then native validation passed nine screens/fifty-four actions,
wireframe digest
`sha256:40d9e22cd90e68221daf2ea4be385ed683e022abdf797ef8f86cc632a2a0645c`.
Earlier receipts/costs remain retained. No CIS source/tooling campaign was opened.

Twenty-one desktop PNGs rendered successfully: thirteen base/panel states plus
eight full-page states over nine screen IDs. Root inspected all eight new pages
and the updated Job drawer, including absent history. Plain-language unavailable
copy was improved. One rerender encountered a PNG write error; exclusive read
opening then succeeded, and a bounded normal retry completed with final native
validation passing. Cause was not established; failed/successful receipts and
costs remain retained. All twenty-one outputs and eight expected new filenames
were checked; application images still match the native R04 approval exactly.

An independent context-limited model reader understood the panel link, one
logical run/two attempts, original execution versus recovery, distinct worker
versions/builds, Task Trigger/Job tabs/separate history and immutable Event
configuration. No material clipping/confusion was found. These are static,
visual/source/model checks, not human usability, browser interaction, runtime
history, backend or accessibility evidence.

Final renderer:
`6375438db766391a9c376cf43ed12ec33fe24273e76334092e63e850229f7309`.
Final native render-result hash:
`ea900731efdbc892051f88c011228ceab24ac785637a79c30de7228b8067ff5d`.
At that rendering checkpoint the gate was PausedForReview/ReadyForReview.
Exact links/hashes and P6/P7/P8 boundaries are in review-package.md.

Fresh admission was 5,588,472,080 bytes under the unchanged 6 GiB ceiling with
fixed allowances/reserve and 32 MiB growth. Additive R06 conservative accounting
covers all agents, prior-discussion intake, docs, rendering and reviews within
the same proposed 48-hour envelope, 48 logical attempts and restoration reserve;
review allocation was redistributed to 18 hours. Earlier costs are unchanged.
No package installation, service access, scheduler/backend implementation,
native consumer build/test, cleanup, push or merge occurred.

## R06 human approval

Andrew Spiteri selected **Approve connected design**. The normal native
`design approve` operation recorded Approved/Approved at 17:04:52 UTC on
25 November 2026. Receipt: `rm09-r06-user-approval.json`. The exact wireframe,
renderer and manifest hashes appear in `design.md`; no screenshot was changed
to record this decision. Approval covers the read-only desktop journey, with
synthetic history/task/event examples still conditional on P6/P7/P8. It neither
delivers nor authorizes an RM-10 runtime. Normal current-scope, outcome, task and
finite-owner reconciliation precedes production implementation under the
standing delegation; another work-allocation question is not required.
