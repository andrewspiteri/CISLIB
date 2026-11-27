---
title: "cislib High-Level UI Direction"
type: ui-direction
status: Active
scope: Workspace
owner: Product owner and design authority
last_reviewed: 2026-11-25
review_cadence: on approved surface, shell, brand, design-system, accessibility, or source-baseline change
cis:
  stable_id: cislib:design:ui-direction
  ui_direction_schema: 1
  solution_design_hash: semantic-v1:0e7bfb36f4adfc4380abccde1d2acb97de9f16016df4cf35cb776d4ebfd65a42
  questionnaire_hash: sha256:0cd4cf05e3276732c73358b1b5d90439d31eea85017b829885f4736ea9527f89
  design_guidelines_hash: sha256:4775b0f52d71a0ddaccd065d2381cc95e7895e65f70d93e3deee32081cab74b5
  ui_framework_profiles_hash: sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
  approved_by: "Andrew Spiteri"
  approved_at: "2026-11-25T22:41:13.6822054\u002B00:00"
  approval_reason: "Transcribe current explicit instruction to correct capabilities/human labels and complete remaining portal workflows. Preserve the already selected R06 desktop shell/palette/two-tab task structure. This scope reconciliation approves no unseen R07 pixels: exact screenshot approval remains pending and gates productionUI. Prior readonly increment and its exact approvals/evidence remain historical."
  approved_content_hash: "sha256:a44e8795a32b2a865b15b521f50bc29f0e0adc910befe4d8ae6f3163d8d31d50"
---

# cislib High-Level UI Direction

This record preserves the original CISLIB no-visual-UI baseline and the subsequent
R02-R06 desktop backoffice decisions as history. The user's current explicit
instruction corrects application Capabilities and human-first labels and requests
completion of the remaining Management portal, including Configuration and
Scheduled Task editing, operational actions and real scoped history. The earlier
read-only increment is therefore a delivered baseline rather than the current
scope limit. Retain R06's dark 200 px CIS/Management rail with Applications, the
Management header, light palette, desktop master/detail presentation and accepted
two-tab Scheduled Task structure. The updated high-level answers guide the new
work; they do not approve its unseen visuals. R07 screenshot review remains
pending for all uncovered or materially changed visual workflows. Preserve the
exact earlier approvals, renderer/screenshots and actual evidence without
claiming that those decisions approve new pixels or prove runtime completion.

<!-- cis:ui-direction-managed:start -->
## Experience intent and principles

Retain the clear, calm, practical and technically precise backoffice presentation shown in approved R06. Give application identity, observed version, report provenance and unavailable information a readable hierarchy. Preserve the restrained visual noise and evidence-oriented language of the selected CIS default guideline; do not introduce decorative imagery, a rebrand or a new product personality.

Authority: `UI-Q-002`.

## Product surfaces and audience classes

The surface is the existing desktop-browser backoffice Management portal for authorized human operators. The user's current explicit instruction extends the earlier inspection increment to complete the remaining application metadata, Configuration authoring/publication/release/assignment/adoption, Scheduled Task and supported trigger configuration, and real job/task/event/run/attempt history journeys. Capabilities means the application's registered job/task types, exposed engine features and scheduler trigger capabilities, with logical trusted definitions and separately reported instance/version availability; it does not mean the Management HTTP endpoint list. This is current requested product scope, not a claim of implementation or new screenshot approval. Preserve the approved R06 shell and desktop presentation; every uncovered or materially changed visual remains pending R07 review. No public/customer frontend, mobile/native application or unrelated graphical surface is added.

Public, customer, backoffice, and native surfaces keep distinct trust and disclosure behavior while sharing the approved visual language. Authority: `UI-Q-001`.

## Shell and navigation model

Retain R06's CIS/Management rail, Management header and Applications entry, filterable application master/detail flow, visible selected application/environment, and Instances, Configuration, Capabilities and Scheduled jobs context. Lists retain the shared right-side quick panel and explicit View full details navigation where supported. Keep exact-record Back, filter/cursor, report, scroll and focus context. Extend the connected native-data journeys to Configuration review/publication/rollout/adoption, supported configured triggers, Scheduled Tasks and real scoped event/run/attempt history without replacing the shell. Scheduled Task remains the main editor with selectors at task level and exactly Trigger and Job contextual configuration tabs; history is separate. Job details retain Overview, Run history and Configuration. New destinations, mutation navigation or materially changed interactions are proposals until the exact R07 screenshots are approved; existing R06 approval is not reused for unseen screens.

The application shell is reusable product infrastructure. Feature designs may add destinations within it but may not silently replace its navigation model. Authority: `UI-Q-003`.

## Visual language and tokens

Retain the approved light palette: white surfaces on #F8FAFC, navy #0F172A, secondary text #334155, blue #2563EB and borders #CBD5E1. Follow the selected default guideline for semantic status colors and visible focus. Status always includes text, so color carries no unique meaning. No alternate theme, theme switcher, brand replacement or new palette is selected.

The canonical design guidelines supply the starting color, spacing, elevation, radius, status, and contrast tokens. Product-specific replacements must be named, reusable, and reviewed here. Authority: `UI-Q-005`.

## Layout, spacing, and density

Preserve R06's desktop master/detail hierarchy, white readable surfaces, bounded tables, aligned filters, 8 px rhythm and 1600 by 1000 reference. Human names lead each row, heading and selector; owner/environment/version disambiguation remains nearby, while opaque IDs and raw diagnostics move into copyable Advanced details. Use grouped ordinary forms and review sections for the now-required Configuration and scheduling workflows, with one clear primary action per stage and visible target/scope. Keep task-level settings and mapping above the two contextual Trigger and Job tabs, with history separate. Review must make shared-object impact, exact configuration references, partial completed steps and pending adoption/activation readable. The quick panel keeps its independently scrolling body and visible list context. These semantic requirements guide R07; they do not approve unseen columns, form layouts, navigation or action placement. Preserve desktop keyboard/zoom usability without introducing a mobile workflow.

Feature designs must preserve information hierarchy and task order at every supported viewport rather than merely scaling a desktop image. Authority: `UI-Q-004`.

## Typography, content, iconography, and imagery

Retain Inter, the R06 type scale, sentence-case controls and direct evidence-oriented copy. Prefer permitted application display names, reported instance labels, trusted capability/trigger/job names and actual configured task names over GUID headings. Show owner, environment and relevant version beside names when needed; keep stable IDs, raw ETags, digests and exact timestamps available under Advanced/copy actions rather than hiding diagnostic identity. Do not invent names or derive authority from labels. Distinguish descriptor/schema/execution-contract version, application release, exact attempt build/handler identity, desired release/generation and local applied revision. Use Not reported, Unknown, Not evaluated, Unavailable and No eligible instance precisely; missing history is not zero runs. Synthetic R07 previews remain visibly synthetic. Preserve the restrained icon language and introduce no decorative image family, new palette or rebrand.

Text remains understandable without decorative imagery; icon-only actions require accessible names and an unambiguous familiar symbol. Authority: `UI-Q-006`.

## Reusable component system

Reuse the existing governed R06 tokens, pinned portal component approach and shared headers/buttons/inputs/selects/tabs/tables/pagers/dialogs/side panels/alerts/badges/cards/empty states. Add the finite controls required by RM10: text, exact numeric, Boolean, enumeration, duration, grouped object fields and the calendar controls supported by accepted descriptors. Form hints must come from trusted provisioned owner descriptions, refer to declared fields and known editor components, and preserve schema/version-specific drafts. They cannot carry arbitrary UI code, React/CLR selectors, scripts, expressions or service URLs. Named lookups resolve authorized registered targets through Management. Configuration uses existing RM08 publication/distribution; a form is not a second store or authority. Reuse bounded exact-payload editing, reviewed command confirmations, whole-map reviews, original-operation receipts, honest adoption and history detail patterns. No new generic UI/schema/rule/auth/DI/test framework or dependency campaign is implied. New visual components and material workflows remain pending R07 screenshots; routine implementation within an accepted design does not require another approval.

Standardize the existing application filters, query-bound paging, context-preserving
lists, shared quick panels and connected full-detail navigation. Correct
Capabilities to the trusted application feature catalogue: registered job/task
types, exposed engine features and scheduler trigger capabilities, with separate
instance/version observations. Preserve the Management HTTP API-capabilities
protocol for compatibility checks rather than presenting its endpoint list as
the application's feature catalogue. Lead with permitted human names and show
opaque IDs and raw diagnostics in copyable Technical details.

Extend these patterns for the now-requested whole portal: display-metadata edits;
Configuration source authoring/validation, immutable publication and coherent
release creation; complete assignment/canary/promotion/rollback review; actual
adoption; and supported Scheduled Task/trigger editing and real event/run/attempt
history. Use the same explicit target/owner/environment/version context throughout.
Scheduled Task remains the main configured item, with selectors at task level,
exactly Trigger and Job contextual configuration tabs, and a separate history
view or action. Trigger shared settings, application job defaults and task inputs
remain distinct. Job full details retain Overview, Run history and Configuration;
Event details show captured historical context.

Known trusted RM10 descriptions drive only finite supported field/calendar
controls and named authorized lookups. Whole-task preview covers the complete
draft and shared-object impact, not just the visible tab. Review, original-key
submission, conflict reconciliation, uncertain-result recovery and partial
completed steps use the authoritative typed APIs. Show publication, assignment,
owner adoption and executable task activation separately; neither previews nor
cancelled waits establish completion. Missing support and unknown outcomes stay
explicit. No arbitrary UI code, generic expression/schema framework, new
configuration authority or synthetic production result is introduced.

Preserve the unchanged R06 shell/palette/desktop, focus and Back-context behavior.
Every uncovered or materially changed visual workflow remains pending exact R07
screenshots; this answer records requested semantic scope and does not repeat or
extend the old approval to unseen screens. Backend contracts, routine styling,
accessibility and focused validation proceed under the current delegated work
authority without another non-UI permission request.

Shared controls include their focus, validation, loading, disabled, denied, empty, error, and recovery behavior. Feature renderers consume governed templates instead of redrawing common controls. Authority: `UI-Q-007`, `UI-Q-011`.

## State, feedback, and motion

Preserve distinct loading, no authorized data, no results, unavailable, denied, dated stale snapshot, validation, conflict and incompatible states. Add persistent review and actual native mutation/recovery feedback for the now-requested complete portal: show business acceptance or rejection, original committed/noncommitted/unknown result, and current cleanup observations separately. Freeze the reviewed payload/preconditions and retain the original operation key before submission; never optimistically announce success, automatically retry an uncertain mutation, or infer rollback from cancellation or NotFound recovery. Show source publication, release creation, assignment, owner adoption and executable task activation as separate completed/pending/refused steps; a combined Publish and apply action is not a distributed transaction. Conflicts preserve only still-permitted drafts and require reconciliation. Grant/session/user loss clears protected data and blocks writes; late results cannot repopulate another scope. Outage queues no changes. Manual run and cancellation require real supported durable commands, and cancellation requested does not mean code stopped. Empty history is shown only after an authoritative scoped read; missing support remains unavailable. New confirmations, history/identity states and action meanings require R07 screenshot review; no new toast or decorative motion system is selected.

No transition may conceal completion or failure. Destructive operations remain explicit, and animation never becomes the only carrier of meaning. Authority: `UI-Q-010`.

## Responsive and platform adaptation

Desktop browser only, as explicitly requested by the user. Retain the desktop layout and 1600 by 1000 approved reference frames; no mobile or narrow-screen preview/delivery is required. Preserve readable content, desktop keyboard access and zoom without changing task order or semantics. Do not introduce a mobile shell, native platform, bottom navigation or responsive workflow redesign under this decision.

Platform conventions may adapt presentation, but capabilities, terminology, and safety boundaries remain coherent. Authority: `UI-Q-008`.

## Accessibility and inclusive interaction

Retain the R06 implementation requirements for semantic headings/tables, explicit labels, keyboard activation, visible focus and return, zoom, sufficient contrast, non-color status cues and announced persistent feedback. Opening a named modal panel moves and contains focus, makes background controls inert, and supports Close, Escape and backdrop dismissal. Closing restores originating focus or the list heading with feedback if the item disappeared. Use reduced-motion-compatible ordinary transitions. Screenshot approval does not prove working accessibility; implementation validation must check these behaviors.

Accessibility is an acceptance boundary for textual wireframes, rendered designs, implementation, and verification. Authority: `UI-Q-009`.

## Constraints and exclusions

The current explicit user request supersedes the prior read-only/deferred scope restriction for the remaining Management portal, including application capability semantics, human-first labels, metadata changes, complete RM08 Configuration workflows and RM10 scheduling/task/history integration. Preserve R06's approved shell/palette/desktop and accepted two-tab Scheduled Task structure, original guideline bytes, historical approvals and actual evidence. Preserve native resource authorization, independently validated human identity, exact preconditions, stable original recovery identity, immutable historical context and current cleanup distinctions. Backend work may proceed independently; all uncovered or materially changed visuals remain pending exact R07 screenshots, and no unseen approval is claimed. Do not add a rule engine, expression language, arbitrary UI-code/schema engine, new authentication/DI/testing framework, portal copy of authoritative management state, second Configuration authority, deployment orchestrator, mobile/public surface or unrelated product. Exposed engine/trigger features must be actually registered; ordinary presence cannot install trusted schemas. Missing backend facts are unavailable rather than fabricated or empty. Do not repeat an approval merely to implement the unchanged accepted editor or perform routine styling, accessibility or focused validation.

The high-level direction does not authorize new business behavior, architecture, data access, or product surfaces. Authority: `UI-Q-012`.

## Feature-design handoff

Each UI-bearing feature consumes this direction together with its applicable component and contract boundaries. Its textual wireframe defines screen structure, actions, states, and paths; its deterministic Sharp/SVG design pack proves the visual interpretation. A feature-level design may specialize this direction but cannot contradict it without first renewing this artifact.

## Traceability

- Solution architecture baseline: `semantic-v1:0e7bfb36f4adfc4380abccde1d2acb97de9f16016df4cf35cb776d4ebfd65a42`.
- UI questionnaire baseline: `sha256:0cd4cf05e3276732c73358b1b5d90439d31eea85017b829885f4736ea9527f89`.
- Design-guideline baseline: `sha256:4775b0f52d71a0ddaccd065d2381cc95e7895e65f70d93e3deee32081cab74b5`.
- UI-framework-profile baseline: `sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`.
- The questionnaire records the exact authority for `UI-Q-001`, `UI-Q-002`, `UI-Q-003`, `UI-Q-004`, `UI-Q-005`, `UI-Q-006`, `UI-Q-007`, `UI-Q-008`, `UI-Q-009`, `UI-Q-010`, `UI-Q-011`, `UI-Q-012`.
<!-- cis:ui-direction-managed:end -->

## UI-specific decisions and accepted exceptions

On 25 November 2026 the user's R02-R06 decisions approved the connected desktop
inspection design and its stated boundaries. Those decisions and their evidence
remain intact for what was actually shown. The user's later explicit correction
and whole-portal instruction now supersede the former read-only/deferred scope
restriction: deliver the application feature catalogue with human-first labels,
metadata and RM08 Configuration workflows, and RM10 scheduling/task/history
integration. Preserve the accepted R06 visual language and task-level Trigger/Job
structure; do not introduce a mobile/public surface, rule engine, arbitrary UI
code, generic framework or second Configuration authority. Backend and contract
work may progress independently under the current controller authority, while
R07 screenshots for uncovered or materially changed UI await the user. This
narrative records the distinction between requested work and actual visual
approval; it neither rewrites historical approval metadata nor claims approval
or completion of the unseen work.

Apply the answer and regenerate/reconcile current explanatory text through the
supported native workflow. Do not hand-edit old approver/date/content hashes or
carry a stale approved-content hash onto a materially changed direction body.

