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
