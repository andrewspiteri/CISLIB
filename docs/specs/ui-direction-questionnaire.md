---
title: "cislib UI Direction Questionnaire"
type: specification
status: Complete
scope: Workspace
owner: Product owner and design authority
last_reviewed: null
review_cadence: on approved solution architecture, surface, brand, or design-system change
cis:
  stable_id: cislib:spec:ui-direction-questionnaire
  ui_direction_questionnaire_schema: 1
  solution_design_hash: semantic-v1:0e7bfb36f4adfc4380abccde1d2acb97de9f16016df4cf35cb776d4ebfd65a42
---

# cislib UI Direction Questionnaire

This governed pre-design record captures the workspace-level look, feel, shell, interaction, accessibility, and reusable-component direction. Existing implementation facts are derived when evidence is strong; subjective or product-specific choices remain human decisions.

## Decision progress

- Resolved: 12 of 12
- State: Complete — ready to generate high-level UI direction

## Questions
### UI-Q-001 — Surfaces and audiences

Which user-facing surfaces and audience classifications need one coherent visual direction?

- Why this is needed: Public, customer, backoffice, mobile, desktop, and embedded surfaces have different trust, navigation, and density needs.
- Common options: Public web; Customer web; Backoffice web; iOS; Android; Desktop; Embedded; No user-facing UI
- Advisory starting direction: Include only surfaces established by the Active BRD and technical intent. Keep public, customer, and backoffice experiences explicitly classified.
- Status: Answered
- Answered by: Andrew Spiteri (current explicit completion request transcribed by Codex; R07 visual approval pending)
- Answered at: 2026-11-25T22:34:39.8785914+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

The surface is the existing desktop-browser backoffice Management portal for authorized human operators. The user's current explicit instruction extends the earlier inspection increment to complete the remaining application metadata, Configuration authoring/publication/release/assignment/adoption, Scheduled Task and supported trigger configuration, and real job/task/event/run/attempt history journeys. Capabilities means the application's registered job/task types, exposed engine features and scheduler trigger capabilities, with logical trusted definitions and separately reported instance/version availability; it does not mean the Management HTTP endpoint list. This is current requested product scope, not a claim of implementation or new screenshot approval. Preserve the approved R06 shell and desktop presentation; every uncovered or materially changed visual remains pending R07 review. No public/customer frontend, mobile/native application or unrelated graphical surface is added.

### UI-Q-002 — Product character

What should the product feel like to its users?

- Why this is needed: A small set of experience qualities gives later screen designs a stable basis for visual judgment.
- Common options: Clear and calm; Practical and efficient; Premium and restrained; Friendly and approachable; Technical and precise; Playful; Institutional
- Advisory starting direction: Use a clear, credible, modern, confident, and practical direction with restrained visual noise unless product evidence requires another character.
- Status: Answered
- Answered by: Andrew Spiteri (R02-R06 screenshot decisions; transcribed by Codex)
- Answered at: 2026-11-25T17:15:21.4166765+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

Retain the clear, calm, practical and technically precise backoffice presentation shown in approved R06. Give application identity, observed version, report provenance and unavailable information a readable hierarchy. Preserve the restrained visual noise and evidence-oriented language of the selected CIS default guideline; do not introduce decorative imagery, a rebrand or a new product personality.

### UI-Q-003 — Shell and navigation

What application shell and primary navigation model should each surface use?

- Why this is needed: Shell and navigation are shared product structures; deciding them per feature creates inconsistency and unnecessary design work.
- Common options: Header only; Header and side navigation; Bottom navigation; Tabs; Master/detail; Document flow; Platform-native shell
- Advisory starting direction: Choose the smallest persistent shell that supports the product's information architecture. Do not add side or bottom navigation to a simple product without a demonstrated need.
- Status: Answered
- Answered by: Andrew Spiteri (current explicit completion request transcribed by Codex; R07 visual approval pending)
- Answered at: 2026-11-25T22:35:09.8547325+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

Retain R06's CIS/Management rail, Management header and Applications entry, filterable application master/detail flow, visible selected application/environment, and Instances, Configuration, Capabilities and Scheduled jobs context. Lists retain the shared right-side quick panel and explicit View full details navigation where supported. Keep exact-record Back, filter/cursor, report, scroll and focus context. Extend the connected native-data journeys to Configuration review/publication/rollout/adoption, supported configured triggers, Scheduled Tasks and real scoped event/run/attempt history without replacing the shell. Scheduled Task remains the main editor with selectors at task level and exactly Trigger and Job contextual configuration tabs; history is separate. Job details retain Overview, Run history and Configuration. New destinations, mutation navigation or materially changed interactions are proposals until the exact R07 screenshots are approved; existing R06 approval is not reused for unseen screens.

### UI-Q-004 — Layout and density

What layout rhythm, content width, and information density should guide screens?

- Why this is needed: Density and hierarchy affect scanability, task speed, responsive behavior, and component selection.
- Common options: Spacious; Balanced; Compact; Data-dense; Single-column; Responsive grid; Master/detail
- Advisory starting direction: Use an 8 px spacing rhythm, generous outer margins, one dominant action per region, and the lowest density that still supports efficient completion.
- Status: Answered
- Answered by: Andrew Spiteri (current explicit completion request transcribed by Codex; R07 visual approval pending)
- Answered at: 2026-11-25T22:35:40.6139502+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

Preserve R06's desktop master/detail hierarchy, white readable surfaces, bounded tables, aligned filters, 8 px rhythm and 1600 by 1000 reference. Human names lead each row, heading and selector; owner/environment/version disambiguation remains nearby, while opaque IDs and raw diagnostics move into copyable Advanced details. Use grouped ordinary forms and review sections for the now-required Configuration and scheduling workflows, with one clear primary action per stage and visible target/scope. Keep task-level settings and mapping above the two contextual Trigger and Job tabs, with history separate. Review must make shared-object impact, exact configuration references, partial completed steps and pending adoption/activation readable. The quick panel keeps its independently scrolling body and visible list context. These semantic requirements guide R07; they do not approve unseen columns, form layouts, navigation or action placement. Preserve desktop keyboard/zoom usability without introducing a mobile workflow.

### UI-Q-005 — Color and theme

What color, theme, branding, and contrast direction should apply?

- Why this is needed: Color is both identity and interaction state; it must remain accessible and reusable across all surfaces.
- Common options: Light; Dark; System theme; Brand-led; Neutral with restrained accent; High-contrast option
- Advisory starting direction: Start from the Active design guidelines: neutral surfaces, deep anchors, restrained accents, and WCAG-compliant contrast. Record any brand-specific replacement tokens.
- Status: Answered
- Answered by: Andrew Spiteri (R02-R06 screenshot decisions; transcribed by Codex)
- Answered at: 2026-11-25T17:18:26.9825067+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

Retain the approved light palette: white surfaces on #F8FAFC, navy #0F172A, secondary text #334155, blue #2563EB and borders #CBD5E1. Follow the selected default guideline for semantic status colors and visible focus. Status always includes text, so color carries no unique meaning. No alternate theme, theme switcher, brand replacement or new palette is selected.

### UI-Q-006 — Typography and content

What typography, iconography, imagery, and writing tone should the interface use?

- Why this is needed: These choices establish hierarchy and voice while preventing each feature from inventing its own presentation language.
- Common options: Sans serif; System typography; Product typeface; Text-first; Icon-supported; Illustration-led; Photography-led
- Advisory starting direction: Use one highly readable sans-serif family, sentence-case outcome headings, direct evidence-oriented copy, and icons only where they improve recognition.
- Status: Answered
- Answered by: Andrew Spiteri (current explicit completion request transcribed by Codex; R07 visual approval pending)
- Answered at: 2026-11-25T22:36:07.8740205+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

Retain Inter, the R06 type scale, sentence-case controls and direct evidence-oriented copy. Prefer permitted application display names, reported instance labels, trusted capability/trigger/job names and actual configured task names over GUID headings. Show owner, environment and relevant version beside names when needed; keep stable IDs, raw ETags, digests and exact timestamps available under Advanced/copy actions rather than hiding diagnostic identity. Do not invent names or derive authority from labels. Distinguish descriptor/schema/execution-contract version, application release, exact attempt build/handler identity, desired release/generation and local applied revision. Use Not reported, Unknown, Not evaluated, Unavailable and No eligible instance precisely; missing history is not zero runs. Synthetic R07 previews remain visibly synthetic. Preserve the restrained icon language and introduce no decorative image family, new palette or rebrand.

### UI-Q-007 — Reusable component system

Which UI framework, design tokens, and reusable component families are authoritative?

- Why this is needed: Buttons, fields, menus, tables, feedback, and navigation should be reused rather than redrawn in every feature.
- Common options: Existing component system; Classification-selected CIS default; Platform-native components; Custom governed components
- Advisory starting direction: Preserve an evidenced existing UI system. Otherwise use the platform-specific framework recorded in the UI framework profile and govern shared shell, controls, data display, feedback, overlays, and navigation components.
- Status: Answered
- Answered by: Andrew Spiteri (current explicit completion request transcribed by Codex; R07 visual approval pending)
- Answered at: 2026-11-25T22:36:34.1001493+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

Reuse the existing governed R06 tokens, pinned portal component approach and shared headers/buttons/inputs/selects/tabs/tables/pagers/dialogs/side panels/alerts/badges/cards/empty states. Add the finite controls required by RM10: text, exact numeric, Boolean, enumeration, duration, grouped object fields and the calendar controls supported by accepted descriptors. Form hints must come from trusted provisioned owner descriptions, refer to declared fields and known editor components, and preserve schema/version-specific drafts. They cannot carry arbitrary UI code, React/CLR selectors, scripts, expressions or service URLs. Named lookups resolve authorized registered targets through Management. Configuration uses existing RM08 publication/distribution; a form is not a second store or authority. Reuse bounded exact-payload editing, reviewed command confirmations, whole-map reviews, original-operation receipts, honest adoption and history detail patterns. No new generic UI/schema/rule/auth/DI/test framework or dependency campaign is implied. New visual components and material workflows remain pending R07 screenshots; routine implementation within an accepted design does not require another approval.

### UI-Q-008 — Responsive and platform adaptation

How should layout and interaction adapt across supported viewports and platforms?

- Why this is needed: Responsive behavior is part of the interaction contract, not an afterthought added during implementation.
- Common options: Desktop first; Mobile first; Desktop and compact web; Tablet; Native adaptive layouts; Keyboard-heavy desktop
- Advisory starting direction: Define at least desktop and compact behavior for web surfaces; preserve task order and meaning while adapting navigation, density, and touch targets.
- Status: Answered
- Answered by: Andrew Spiteri (R02-R06 screenshot decisions; transcribed by Codex)
- Answered at: 2026-11-25T17:21:05.6483132+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

Desktop browser only, as explicitly requested by the user. Retain the desktop layout and 1600 by 1000 approved reference frames; no mobile or narrow-screen preview/delivery is required. Preserve readable content, desktop keyboard access and zoom without changing task order or semantics. Do not introduce a mobile shell, native platform, bottom navigation or responsive workflow redesign under this decision.

### UI-Q-009 — Accessibility and input

Which accessibility baseline and input modes must every feature support?

- Why this is needed: Keyboard, screen reader, focus, contrast, touch, motion, and error behavior must be designed before screenshots are approved.
- Common options: WCAG 2.2 AA; Keyboard; Screen reader; Touch; Pointer; Voice control; Reduced motion; High contrast
- Advisory starting direction: Use WCAG 2.2 AA as the baseline with semantic structure, complete keyboard operation, visible focus, non-color state cues, accessible names, and reduced-motion support.
- Status: Answered
- Answered by: Andrew Spiteri (R02-R06 screenshot decisions; transcribed by Codex)
- Answered at: 2026-11-25T17:21:53.1381287+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

Retain the R06 implementation requirements for semantic headings/tables, explicit labels, keyboard activation, visible focus and return, zoom, sufficient contrast, non-color status cues and announced persistent feedback. Opening a named modal panel moves and contains focus, makes background controls inert, and supports Close, Escape and backdrop dismissal. Closing restores originating focus or the list heading with feedback if the item disappeared. Use reduced-motion-compatible ordinary transitions. Screenshot approval does not prove working accessibility; implementation validation must check these behaviors.

### UI-Q-010 — State, feedback, and motion

How should loading, empty, success, warning, error, unavailable, destructive, and transition states behave?

- Why this is needed: A polished happy path is insufficient when users cannot understand pending, failed, denied, or recoverable states.
- Common options: Inline feedback; Status banners; Toast notifications; Skeletons; Progress indicators; Confirmation dialogs; Optimistic updates
- Advisory starting direction: Every operation needs truthful pending, success, failure, and recovery feedback. Prefer inline feedback; reserve motion for continuity and honor reduced-motion preferences.
- Status: Answered
- Answered by: Andrew Spiteri (current explicit completion request transcribed by Codex; R07 visual approval pending)
- Answered at: 2026-11-25T22:37:00.5258123+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

Preserve distinct loading, no authorized data, no results, unavailable, denied, dated stale snapshot, validation, conflict and incompatible states. Add persistent review and actual native mutation/recovery feedback for the now-requested complete portal: show business acceptance or rejection, original committed/noncommitted/unknown result, and current cleanup observations separately. Freeze the reviewed payload/preconditions and retain the original operation key before submission; never optimistically announce success, automatically retry an uncertain mutation, or infer rollback from cancellation or NotFound recovery. Show source publication, release creation, assignment, owner adoption and executable task activation as separate completed/pending/refused steps; a combined Publish and apply action is not a distributed transaction. Conflicts preserve only still-permitted drafts and require reconciliation. Grant/session/user loss clears protected data and blocks writes; late results cannot repopulate another scope. Outage queues no changes. Manual run and cancellation require real supported durable commands, and cancellation requested does not mean code stopped. Empty history is shown only after an authoritative scoped read; missing support remains unavailable. New confirmations, history/identity states and action meanings require R07 screenshot review; no new toast or decorative motion system is selected.

### UI-Q-011 — Data and action patterns

Which recurring presentation and action patterns should be standardized?

- Why this is needed: Lists, tables, forms, filters, editors, and destructive actions are common sources of avoidable inconsistency.
- Common options: Flat lists; Tables/grids; Cards; Forms; Inline editing; Search/filter; Bulk actions; Destructive confirmation
- Advisory starting direction: Standardize only patterns supported by the product's journeys. Keep primary actions obvious, secondary actions compact, and destructive actions explicitly confirmed.
- Status: Answered
- Answered by: Andrew Spiteri (current completion request transcribed by Codex; R07 visuals pending)
- Answered at: 2026-11-25T22:38:01.7717839+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

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

### UI-Q-012 — Constraints and exclusions

Which brand assets, mandated conventions, legacy constraints, or explicit visual exclusions must be preserved?

- Why this is needed: Explicit constraints prevent feature design from silently rebranding, replacing frameworks, or expanding platform scope.
- Common options: Existing brand; Existing shell; Existing design system; White-label; Localization; Offline; No imagery; No animation
- Advisory starting direction: Record only evidenced constraints. Treat framework replacement, rebranding, new surfaces, and major shell changes as explicit decisions rather than feature-level interpretation.
- Status: Answered
- Answered by: Andrew Spiteri (current explicit completion request transcribed by Codex; R07 visual approval pending)
- Answered at: 2026-11-25T22:37:25.8511442+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

The current explicit user request supersedes the prior read-only/deferred scope restriction for the remaining Management portal, including application capability semantics, human-first labels, metadata changes, complete RM08 Configuration workflows and RM10 scheduling/task/history integration. Preserve R06's approved shell/palette/desktop and accepted two-tab Scheduled Task structure, original guideline bytes, historical approvals and actual evidence. Preserve native resource authorization, independently validated human identity, exact preconditions, stable original recovery identity, immutable historical context and current cleanup distinctions. Backend work may proceed independently; all uncovered or materially changed visuals remain pending exact R07 screenshots, and no unseen approval is claimed. Do not add a rule engine, expression language, arbitrary UI-code/schema engine, new authentication/DI/testing framework, portal copy of authoritative management state, second Configuration authority, deployment orchestrator, mobile/public surface or unrelated product. Exposed engine/trigger features must be actually registered; ordinary presence cannot install trusted schemas. Missing backend facts are unavailable rather than fabricated or empty. Do not repeat an approval merely to implement the unchanged accepted editor or perform routine styling, accessibility or focused validation.
