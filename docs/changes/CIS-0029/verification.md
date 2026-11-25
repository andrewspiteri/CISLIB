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
