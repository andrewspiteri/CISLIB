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
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2669; failed=357; possibleTokenSavings=591; ledgerDigest=sha256:8fa42a443d476548c4372e05dc16cbd5d4f5fc77026fc5b2e19906fef46506f8 |

## Approved desktop execution boundary

Native R06 approval was recorded at 17:04:52 UTC. Twelve transcribed R02–R06
answers and normal native UI operations reconciled the old NoUI baseline.
Existing-target initialization retained false applicability and failed; the
original and failed records were preserved before normal fresh initialization.
No managed hash was edited. Postgraph status is Active, valid and current.

The graph completed in 438,506 ms: exit 0, partial, 56,670 nodes, 332,601 edges,
zero errors and 80 warnings. It predates later task metadata and is not final
implementation proof. The first authorization check overlapped that build and
was blocked. After drainage and a passing status check, the same request
succeeded. This suggests a transient overlap; it does not prove a source defect.
Both receipts and costs remain. No source-CIS campaign was opened.

Native scaffold/render refreshed only the generated provenance block. All 21
PNGs and manifest ff55f6da remain byte-identical to the actual R06 approval.
Current renderer d1e9348c binds the current direction. The first rerender omitted
the known isolated Sharp runtime and failed; supplying that pinned runtime
passed. The existing screenshot decision was recorded against these unchanged
pixels, without claiming another human review.

The narrow outcome and five medium contracts validate without errors or
warnings. Design validation passes. Docs validation has zero errors and retained
historical warnings; the adopted guideline's missing catalog entry was added.
A descriptive storage reference was rejected. The corrected allocation reuses
`CISLIB-CIS-0001-STORAGE-2026-11-10`, preserving one 48-hour/48-attempt allocation,
one-hour restoration reserve, 6 GiB ceiling, 64 MiB reserve and all prior costs.
Native adoption does not erase external conservative accounting.

Original source 1574ec26 and protected launch settings ea7e7219 remain unchanged.
WORK-001 is Complete for its actual design/intake acceptance and native checks.
Production leaves remain separate. No runtime, consumer test, installation,
cleanup, push or merge is claimed at this boundary. The original full
20-scenario specification is not complete.

Docker showed zero active containers, 1.06 GB images, 144.3 MB volumes and
410.3 MB build cache. Admission counts all retained bytes, an 8 MiB rounding
allowance and operation-specific growth while preserving the 2 GiB Docker
maximum inside 6 GiB. No old images, volumes or cache were deleted. New npm,
.next and browser outputs are counted. Runtime pins stay unchanged. Stable
Auth.js replaces the uninstalled Better Auth proposal for the bounded provider
I/O reason recorded in dependency-decision.md.

## Desktop implementation checks in progress

The frozen handoffs now have source implementations for the native operator
identity profile, bounded discovery/portable clients, the independent Next host
and approved desktop inspection views. Source reviews are retained under
`.cis/local/testing/rm09/`; a source disposition does not substitute for native
execution. WORK-002 through WORK-005 remain separate active owners.

The first integration compilation exposed one missing test namespace import;
that was corrected without changing product behavior. The affected Core unit
project subsequently built with zero warnings/errors. Its first focused native
client/wire selection returned **197 passed, 1 failed, 0 skipped**. The failure
was a real finite-profile regression: adding a new environment observation alias
exceeded the existing 32-pair REST composition budget. The correction groups
environment discovery under the existing `applications` observation family,
while retaining the distinct public API, typed environment response and exact
HTTP URI. The global REST bound and existing profile-budget regression test are
unchanged. Passing execution of this correction remains to be recorded.

The portal's eleven focused profile/input/projection Node tests passed with no
failures, cancellation or skips. Initial strict application checking exposed
dependency-only declaration errors for unselected email/WebAuthn providers;
app-local `skipLibCheck` leaves all application/server source strict and adds no
unused provider dependencies. Including the custom `.mts` entry points then
exposed two genuine v4/v5 adapter-input differences and one environment typing
error. The adapter now constructs explicit closed validated inputs rather than
casting across versions. A production build and actual PostgreSQL/OIDC runtime
proof are still required before accepting the host.

The UI source review corrected child-read-only 401 messaging and restored the
approved exactly-two Trigger/Job configuration tabs with a separate history
action. Exact report/scoped identity, stale request abort and truthful missing
history/task/event data remain explicit. The new desktop browser check uses the
ordinary production host and isolated native fixture for actual data; separately
labelled browser-controlled responses exercise race/paging/expiry UI states.
No browser pass, actual screenshot comparison, live Entra deployment or release
qualification is claimed by this source checkpoint.

### Current focused results

| Owner | Native check | Result |
| --- | --- | --- |
| WORK-002 | Integration project build, including the native service | Passed, zero warnings/errors |
| WORK-002 | Operator identity, metadata cancellation/body ownership and strict discovery boundary selection | 54 passed, 0 failed/skipped |
| WORK-002 | Actual native operator/workload and existing certificate/local/Entra profile selection after fixture-policy correction | 5 passed, 0 failed/skipped |
| WORK-003 | Corrected Core client/wire/profile regression selection | 198 passed, 0 failed/skipped |
| WORK-003 | Canonical Management contract/conformance selection | 94 passed, 0 failed/skipped |
| WORK-003 | Actual PostgreSQL filtering, UTF-8 continuation, 4096-candidate refusal and blocked-query cancellation | 5 passed, 0 failed/skipped |
| WORK-003 | Affected TypeScript client/wire/cancellation/configuration/export selection | 189 passed, 0 failed/cancelled/skipped |
| WORK-003 | Portable browser-import TypeScript check | Passed |
| WORK-004 | Final portal input/profile/projection checks | 11 passed, 0 failed/cancelled/skipped |
| WORK-004 | Production Next build, including strict application/custom-server/migration source types after native-client drainage corrections | Passed |
| WORK-004 | Actual HTTPS response/deadline/body-limit/saturation/stop ownership | 1 MSTest passed, asserting all 6 native transport scenarios and actual closure |
| WORK-004 | Real maintained OIDC callbacks, PostgreSQL sessions, native Management grants and host stop | 1 MSTest passed, asserting all 26 recorded scenarios; browserChecked=false |

Native command arguments, actual elapsed walls and exit results remain in
`.artifacts/management-portal/CIS-0029/native-costs.jsonl`; nonempty command logs
and TRX results are under each named owner/phase. Silent compiler success is
recorded in that journal rather than an invented output file. These independent
native commands are outside CIS's internal measured-run coverage; the shared
external admission/accounting record charges them in addition to conservative
source/agent allowances and retained CIS metadata costs. A native ledger zero
does not mean this work was free.

The first native operator selection had 3 existing-profile passes and 2 new-test
failures before authentication. The new test initializer omitted the owned TLS
fixture's short-lived certificate revocation policy, thereby selecting Online.
It now copies both actual fixture policy values; production validation and
trust roots are unchanged. The passing five-case retry above uses that corrected
initializer. Both native observations and costs are retained.

One provider admission was held before launch because its projected growth did
not fit the unchanged physical ceiling. Exact local inspection verified all
three pinned fixture images were already present. The subsequent admission
reserves 192 MiB for sequential owned provider volumes/writable layers and 16 MiB
for the selected native outputs/scratch; the rationale and actual counted roots
are retained in `provider-storage-admission-refinement.json`. No prior cache,
image, database or volume was deleted, and no ceiling or reserve was enlarged.

The first OIDC/session host invocation reached the real PostgreSQL retirement
helper and failed on its additional remote-PID-disappearance assertion. It had
not yet established an auth-host result. Remote PostgreSQL execution/rollback is
distinct from local native client ownership. The corrected helper independently
observes actual public Client.end: absent while held and disposal is pending with
the real backend connected; present after releasing native end and awaiting the
same disposal. Actual query-timeout eviction remains covered. Installed driver
stream-close/Connection.end/Client.end ordering was independently reviewed.
Production drainage is unchanged, and no remote termination claim replaces it.
The original failed invocation and classification remain retained; the corrected
invocation then reached 14 actual scenarios before failing at its forced-expiry
step and timing out in the fixture's aggregate child-close wait. The recorded
host had exited while aggregate close remained unobserved. The next diagnostic
run retained that exact partial result; no partial run is labelled a host pass.

The fixture now joins actual process exit, stdout closure, stderr closure and
IPC disconnect separately, all registered before stopping. Aggregate close is
retained as a diagnostic; it is not substituted by exit alone. The pinned Node
24.15 implementation's explicit-disconnect/aggregate accounting path was
independently inspected. No process-private counters or forced stream destruction
were introduced. The forced-expiry check now parameterizes an explicit date on
the validator's clock, requires exactly one updated row and a returned past
deadline, then asserts 401. The previous expiry cause remains unidentified;
the successful run's numeric clock delta is not evidence about that old run.

`rm09-native-oidc-session-public-joins` passed in 28 seconds. Its retained report
lists 26 completed scenarios: all three real PostgreSQL client-retirement cases,
maintained OIDC/PKCE flow, native target denial, per-registration versions and
inventory identity, real metadata-only definition, two independent users,
opaque cookie, token/account-route denial, no retained refresh/ID token,
account-linking/origin/scope rejection, actual persisted session expiry,
reauthentication, revocation and sign-out, five invalid callback cases, restricted
SQL role, no token logging and awaited host stop. All four public shutdown events
and issuer socket drainage were observed. Exact report path/hash is recorded in
`.cis/local/testing/rm09/host-verified-evidence.json`. Browser execution remains
pending; this does not claim current screenshots, live Entra, another OS lane or
release readiness.

## Verified approved desktop increment

WORK-002, WORK-003 and WORK-004 completed through normal CIS transitions before
the connected desktop selection. The final approved-layout production build
passed strict application/server/migration type checking. The installed Edge
run `rm09-native-desktop-approved-layout` then passed: **1 MSTest, 0 failed,
0 skipped, 41 seconds**. It completed the same 26 real host/session/native-service
scenarios and the browser hook below.

The browser evidence separates **10 scenarios using actual native service data**
from **3 controlled browser-response scenarios**. Actual checks cover filters and
Clear filters, per-registration versions, keyboard/Escape/focus return,
metadata-only Configuration, advisory capabilities, complete inventory and exact
registration/report/scoped-job/resource navigation, local configuration, truthful
unavailable history/Task/Event views and independent browser-session target
isolation. Controlled checks separately prove delayed old-tab response rejection,
child-read session-expiry clearing/reauthentication notice and bounded
Next/Previous/filter restart. They do not become real backend or scheduler proof.

The earlier desktop runs and costs remain. They captured the main approved
views, then failed on an unscoped alert selector that also included Next's own
route announcer. The check now targets the actual page notice inside main;
production alert semantics were preserved. Visual review additionally found a
Job Overview density/grouping difference. The final implementation restores the
approved compact Definition/Selected resource cards, full-width Selected report
and related links, with exact native fields. The old captures remain historical.

The final run retained **14 screenshots**. Root and an independent reader viewed
the fresh Job Overview and Applications toolbar against R06; the earlier panel,
Configuration and Instances comparisons remain compatible. No required visual
finding remains. The ordinary fixture's related links also passed full viewport
visibility at 1,600 by 1,000. No new journey or screenshot approval was invented.
Native source, protected launch settings and approved renderer hashes are
unchanged. `.cis/local/testing/rm09/desktop-verified-evidence.json` binds all
fresh images and the final browser/host reports; the visual assessment is
`.cis/local/testing/rm09/desktop-visual-review.md`.

Only this task's newly generated rebuildable webpack caches were disposed after
completed builds to retain browser headroom: 92,025,181 bytes and 81,430,396 bytes.
Exact targets were verified inside the new portal, with no linked entries;
file hashes and manifests were retained. Compiled production output remained
available and was used by the passing native run. No historical cache, image,
existing database/volume, source, report or recovery input was removed. The
unchanged 6 GiB ceiling, 64 MiB reserve and 2 GiB Docker allowance remain in force.

This closes the approved R02–R06 desktop inspection behavior. The original full
RM-09 specification is still broader: writes, publication/assignment/adoption
journeys and mobile remain deferred, and real scheduler/history/Task/Event data
requires RM-10 contracts. Local controlled-issuer proof does not qualify live
Entra, external encryption/replicas, AKS/cert-manager, another OS or a release.
No push, merge, publication or external deployment occurred.
| WORK-002 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2716; failed=357; possibleTokenSavings=591; ledgerDigest=sha256:fd11df4a43f11c9e77a8e339e9d20264fca5af586b745db230f741426eacc629 |
| WORK-003 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2718; failed=357; possibleTokenSavings=591; ledgerDigest=sha256:dc1f0ff0e04010a16b51f75d877968abe0d8389f8902b263dab58c05a7050eee |
| WORK-004 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2724; failed=357; possibleTokenSavings=591; ledgerDigest=sha256:13b3701feb30eba1ccf3b5b52851075c542a3552893a509f3a9ab25cb6f417bd |
| WORK-005 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2734; failed=357; possibleTokenSavings=591; ledgerDigest=sha256:aa03c50c65aec759ebb903f1ccdc9748ac971300e806b9fd7a1642e2c552443a |

## Interactive local session and browser-auth correction (WORK-006/007)

The user requested a locally running portal with test data. One supporting manual
fixture task reuses the existing isolated native fixture, ordinary production host
and genuine controlled-issuer OIDC flow. The source work, all agents, native runs,
docs and one-hour lease share the original 48-hour/48-attempt allocation; no new
physical pool or resource increase was created. Completed owners were preserved.
WORK-006 remains active while its finite manual session owns native resources.

Actual new evidence: affected C# scoped formatting/verification and build passed
with zero warnings/errors; 15 policy/manual-control tests passed; final production
Next build passed; the ordinary native host test passed with 26 scenarios; five
actual browser scenarios passed, including chooser/PKCE, both applications,
per-instance versions, Configuration/Jobs, sign-out/reauthentication and restricted
Operator B. Three genuine code exchanges occurred. The test fixture then stopped
normally: actual process, stdout, stderr, IPC, issuer sockets and private session
owners joined; C# roles/schema/Management owners and assembly containers disposed,
and the parent test exited zero. Exact paths/hashes are retained in
`.cis/local/testing/rm09-manual/verified-evidence.json`.

The initial real browser form failed with403 under the old no-referrer policy.
The correction uses same-origin referrer policy in both header layers, retains
strict exact-Origin/CSRF checks (including rejection of null Origin), and moves
CSP to one runtime owner permitting only self and the independently configured
concrete HTTPS issuer. Wildcard/policy-metacharacter and unsupported literal-IP
origins are rejected. The actual four form submissions reported the exact portal
Origin and same-origin site. No production demo/auth bypass was introduced.

A Windows atomic-status rename lock and a Playwright header-capture omission were
corrected. Status publication is awaited only at explicit lifecycle boundaries,
with finite Windows-only lock retry; native event callbacks cannot throw from
status publication. Every helper cleanup is attempted independently. Further
focused browser runs exposed retained Next page/API promises after responses and
network sockets closed. Fixed temporary phase observations identified the wait;
all failed runs and exact verified-child compensation remain retained. Signalling
Next close earlier did not settle those promises and that speculative ordering
was removed. Next's supported compress:false setting resolved the observed hang
in the same browser journey while preserving every actual shutdown join. Direct
host responses are uncompressed; no library patch, new dependency or static-file
framework was added. Temporary diagnostics were removed. Handler shutdown now
settles all individual handlers even on fault, and observes removal bookkeeping.

Only newly generated regenerable webpack caches from these builds were disposed,
with exact verified paths, no linked entries and retained per-file hashes. Current
C# outputs, compiled Next output and all reports were preserved; no historical
cache/image/database/volume or user launch settings was removed. See the two
manual webpack disposal records, including the explicit corrected aggregate for
the first manifest's null total. The unchanged6GiB ceiling/64MiB reserve and2GiB
Docker allowance continue to apply.

The final human session uses a newly owned isolated visible Edge context with a
context-only local TLS exception; backend validation remains enabled and no trust
store is changed. Operator A can read both synthetic applications; B only managed-b.
managed-a reports1.0.0 and2.4.2 and includes metadata/job snapshots. Snapshot
presence can become stale; RM10 history/typed Task/Event data remains unavailable.
A stop file or at most one-hour lease requests normal joined cleanup. This is local
controlled evidence, not a live Entra/deployment, broader qualification or release.
No push or merge occurred.
| WORK-007 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2779; failed=359; possibleTokenSavings=591; ledgerDigest=sha256:f075dbf3cdcd08c9bc98342bf9af925fdef9dffd965a79b4ec0f601cd2b78af4 |
