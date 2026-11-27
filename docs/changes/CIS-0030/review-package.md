# R07 desktop completion preview

Status: **Approved by Andrew Spiteri on26November2026.** Seventeen new 1600 × 1000 desktop previews and seven exact approved R06 reference states passed native design validation. All examples are synthetic. This is design evidence; production implementation and actual provider/browser acceptance remain pending.

R07 retains the approved R06 200 px Applications rail, Management header,
application context and right-side details pattern. Names, environment and useful
versions lead; exact identifiers are available under Technical details. Scheduled
Task settings and input mapping remain above exactly Trigger and Job tabs, with
History separate. All preview data is visibly labelled synthetic.

The new flows cover application-feature Capabilities, display metadata,
Configuration editing/publication/release/assignment/adoption and recovery,
connected Task/Job/Event configuration, and durable logical runs with distinct
execution/recovery attempts and worker versions. Registered RuleEngine instances
are inspected without adding rule authoring or another evaluator.

## Primary desktop frames

| Frame | Preview | Review focus |
| --- | --- | --- |
| R07-F01 | [Capabilities catalog](assets/r07-capabilities--catalog--desktop.png) | Schedulable jobs, registered RuleEngine instances and trigger providers; API diagnostics secondary. |
| R07-F02 | [Configuration editor](assets/r07-configuration-editor--edit--desktop.png) | Known typed controls, inheritance and JSON fallback. |
| R07-F03 | [Publication and release review](assets/r07-configuration-review--review--desktop.png) | Exact reviewed changes and separate publication, pinned release and later assignment. |
| R07-F04 | [Assignment promotion](assets/r07-assignment--promotion--desktop.png) | Complete map, explicit canary removal and unrelated override retention; canary/rollback share this review pattern. |
| R07-F05 | [Adoption](assets/r07-adoption--mixed--desktop.png) | Desired/accepted provenance, local revision and distinct restart/rejection/fault/freshness states. |
| R07-F06 | [Scheduled tasks](assets/r07-scheduled-jobs--tasks--desktop.png) | Configured tasks and separate reported jobs under the existing Scheduled jobs tab. |
| R07-F07 | [Task — Trigger](assets/r07-task-full--trigger--desktop.png) | Main association/resource/input mapping and shared calendar settings. |
| R07-F08 | [Task — Job](assets/r07-task-full--job--desktop.png) | Business defaults versus invocation input; execution/recovery compatibility. |
| R07-F09 | [Whole-task preview](assets/r07-task-preview--review--desktop.png) | Complete draft/shared impact and separate apply/acceptance/activation stages. |
| R07-F10 | [Run history and attempts](assets/r07-run-history--attempts--desktop.png) | One logical run, original outcome, build change and distinct execution/recovery roles. |
| R07-F11 | [Job Configuration](assets/r07-job-full--configuration--desktop.png) | Shared job settings, provenance and related Task input/overrides. |
| R07-F12 | [Event captured Configuration](assets/r07-event-full--configuration--desktop.png) | Immutable captured settings and produced original runs. |
| R07-F13 | [Display metadata](assets/r07-metadata--edit--desktop.png) | Display name/description only; canonical identity remains read-only. |

## Critical supporting frames

| Frame | Preview | Review focus |
| --- | --- | --- |
| R07-S01 | [Unknown original operation](assets/r07-operation--unknown--desktop.png) | Completed versus unresolved stages, original request lookup and no unsafe replacement. |
| R07-S02 | [Concurrent edit](assets/r07-conflict--conflict--desktop.png) | Retained draft, current version and explicit renewed review. |
| R07-S03 | [Blocked Task draft](assets/r07-task-full--blocked--desktop.png) | Mapping/profile/worker incompatibility prevents activation. |
| R07-S04 | [Source publication history](assets/r07-configuration-list--history--desktop.png) | Names-first list, scoped paged Common/Application versions, immutable authorized content and exact release-reference selection. |

Task/Event/history views distinguish Configured by on the captured Task revision,
the authenticated Trigger producer, Requested by only when actually present,
and each attempt's actual Executed as/profile/effective policy plus separately
reported execution instance. Automatic calendar events have no invented human
requester. Full stable subjects and exact build identities are technical details.

## Unchanged accepted references

The native design reuse records link the exact approved R06
[Applications](../CIS-0029/assets/rm09-applications--primary--desktop.png) and
[Instances](../CIS-0029/assets/rm09-instances--primary--desktop.png) PNGs. Their
bytes and prior approval are preserved as unchanged layout references; they are
not newly rendered R07 pixels or proof of new code. The material Configuration
list change receives S04. Existing maintained human sign-in is an external entry,
without another auth design or repeated approval.

The [wireframes](wireframes.md) define actions, permissions, conflicts, privacy,
lifetime and backend prerequisites. The self-contained
[renderer](assets/render-rm09-completion-r07-screens.mjs) retains generated CIS
provenance and the actual R06 visual primitives. New screenshots require their
exact review decision after native rendering and visual inspection. Production
completion still requires real contracts, authorization, provider durability,
owned cancellation/drain and focused service/browser evidence. This package does
not claim deployment, live Entra qualification or release readiness.

## Exact review identity

Renderer SHA-256: `84167b628b10729927e5d0643035721befe53d22a39cf887e3c1d66851d5fc37`. Native validation: `.cis/local/configuration-publication-intake/rm09c-design-validate-review-package.json` (valid, PausedForReview, 24 artifacts). The exact native renderer/PNG/wireframe approval is recorded in rm09c-r07-user-approve.json. Existing R06 approvals were preserved; no new approval is claimed by this review package.

All 17 frames received visual inspection. Review corrections completed the editable Job tab, fixed the cropped timestamp, made capability actions visible, added the full Job-default difference and named affected tasks to whole-task preview, and qualified adoption counts by page. The screenshot review pause has been released by the explicit approval; implementation proceeds under the existing authority.


