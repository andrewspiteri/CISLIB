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
- Answered by: Andrew Spiteri (R02-R06 screenshot decisions; transcribed by Codex)
- Answered at: 2026-11-25T17:14:07.4226297+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

Add only the RM09 desktop-browser backoffice Management portal for authorized operators. This is the explicit, narrow exception to the 10 November 2026 global No user-facing UI record, selected by the user's subsequent RM09 request and R02-R06 screenshot decisions. The current delivered step is read-only Applications to application detail: Instances, Configuration list, Capabilities and Scheduled jobs, with item inspection and Job details. Approved Run history, Scheduled Task and immutable Event designs retain their named future backend prerequisites and cannot imply a delivered scheduler/history service. No public/customer UI, mobile/native application, management write workflow or unrelated graphical surface is selected.

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
- Answered by: Andrew Spiteri (R02-R06 screenshot decisions; transcribed by Codex)
- Answered at: 2026-11-25T17:16:13.8388719+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

Use the simple desktop header shown in R06, with Applications as the only top-level destination and no multi-area navigation rail. Applications supports name/identity and authorized-environment filtering. Opening an exact application defaults to Instances and retains its application/environment context across Instances, Configuration, Capabilities and Scheduled jobs tabs. A selected item opens the shared right-side modal inspection panel while its list stays visible. Job View full details opens dedicated detail navigation. Preserve Back, query/cursor, report, scroll and focus context. The approved future Job/Task/Event/history links remain explicit unavailable or prerequisite-dependent reads until their contracts exist.

### UI-Q-004 — Layout and density

What layout rhythm, content width, and information density should guide screens?

- Why this is needed: Density and hierarchy affect scanability, task speed, responsive behavior, and component selection.
- Common options: Spacious; Balanced; Compact; Data-dense; Single-column; Responsive grid; Master/detail
- Advisory starting direction: Use an 8 px spacing rhythm, generous outer margins, one dominant action per region, and the lowest density that still supports efficient completion.
- Status: Answered
- Answered by: Andrew Spiteri (R02-R06 screenshot decisions; transcribed by Codex)
- Answered at: 2026-11-25T17:17:08.8796638+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

Retain the R06 desktop master/detail layout, bounded readable tables, aligned filters and the 8 px spacing rhythm. Preserve white content surfaces, clear headings, compact status/context fields and readable long application identities, versions and API paths. The approved reference viewport is 1600 by 1000. The right-side modal panel has an independently scrolling body and keeps the underlying list context visible. Do not add cards, multi-area navigation, columns or controls that change the approved information hierarchy.

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
- Answered by: Andrew Spiteri (R02-R06 screenshot decisions; transcribed by Codex)
- Answered at: 2026-11-25T17:19:22.7918665+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

Retain Inter, the R06 14-16 px body text and 20-24 px titles, sentence-case labels and direct evidence-oriented copy. Keep canonical identities, raw/open status codes and timestamps readable; distinguish Not reported, Not evaluated and unavailable data. Use icons only where the approved design uses them to support an already named control; no new photography, illustration or decorative image family is selected. Synthetic previews remain identified as synthetic and never substitute for runtime facts.

### UI-Q-007 — Reusable component system

Which UI framework, design tokens, and reusable component families are authoritative?

- Why this is needed: Buttons, fields, menus, tables, feedback, and navigation should be reused rather than redrawn in every feature.
- Common options: Existing component system; Classification-selected CIS default; Platform-native components; Custom governed components
- Advisory starting direction: Preserve an evidenced existing UI system. Otherwise use the platform-specific framework recorded in the UI framework profile and govern shared shell, controls, data display, feedback, overlays, and navigation components.
- Status: Answered
- Answered by: Andrew Spiteri (R02-R06 screenshot decisions; transcribed by Codex)
- Answered at: 2026-11-25T17:20:13.7688810+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

Reuse the exact approved R06 visual contracts for the selected CIS shell and component templates: page header, buttons, text input, select, tabs, breadcrumbs, pagination, dialog/right-side inspection panel, alert, table, filter bar, status badge, card, empty state and timeline where applicable. R06's simple header and one Applications destination override the generic shell template's multi-area rail description. Reuse shared production components/tokens for these patterns. The screenshot renderer and its Sharp dependency are design tooling, not a selected production UI framework; this visual answer grants no new package or framework campaign.

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
- Answered by: Andrew Spiteri (R02-R06 screenshot decisions; transcribed by Codex)
- Answered at: 2026-11-25T17:22:43.6745770+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

Keep loading, empty, denied, unavailable, stale dated snapshot and incompatible response states distinct. Initial failure offers Retry; failed refresh may retain only the permitted, clearly dated same-scope snapshot. Access/session loss clears protected content; obsolete responses and changed selection cannot repopulate a panel. Missing occurrence times say Not reported; missing history says Run history unavailable and never becomes a successful zero-run result. Preserve the approved persistent inline feedback; no optimistic mutation, new toast system, decorative animation or destructive workflow is selected.

### UI-Q-011 — Data and action patterns

Which recurring presentation and action patterns should be standardized?

- Why this is needed: Lists, tables, forms, filters, editors, and destructive actions are common sources of avoidable inconsistency.
- Common options: Flat lists; Tables/grids; Cards; Forms; Inline editing; Search/filter; Bulk actions; Destructive confirmation
- Advisory starting direction: Standardize only patterns supported by the product's journeys. Keep primary actions obvious, secondary actions compact, and destructive actions explicitly confirmed.
- Status: Answered
- Answered by: Andrew Spiteri (R02-R06 screenshot decisions; transcribed by Codex)
- Answered at: 2026-11-25T17:23:30.5767316+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

Standardize the approved filterable application table, query-bound Previous/Next paging, exact-identity links/row activation, application detail tabs, current-tab Refresh, readable status badges, shared item inspection panel and context-preserving Back navigation. Use retained data for quick inspection, rather than introducing extra panel reads. Job details retain Overview, Run history and Configuration navigation; future Task configuration has exactly Trigger and Job tabs with history separate, and immutable Event configuration is captured historical context. Keep all current production actions read-only and truthfully unavailable where an explicit prerequisite is missing.

### UI-Q-012 — Constraints and exclusions

Which brand assets, mandated conventions, legacy constraints, or explicit visual exclusions must be preserved?

- Why this is needed: Explicit constraints prevent feature design from silently rebranding, replacing frameworks, or expanding platform scope.
- Common options: Existing brand; Existing shell; Existing design system; White-label; Localization; Offline; No imagery; No animation
- Advisory starting direction: Record only evidenced constraints. Treat framework replacement, rebranding, new surfaces, and major shell changes as explicit decisions rather than feature-level interpretation.
- Status: Answered
- Answered by: Andrew Spiteri (R02-R06 screenshot decisions; transcribed by Codex)
- Answered at: 2026-11-25T17:24:08.2336271+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

Limit this direction to the already approved RM09 desktop backoffice inspection step. Preserve exact R06 layouts, field meaning, selection/return context, original imported guideline bytes and accepted screenshot evidence. Earlier no-UI documents remain retained historical authority, superseded only by this explicit narrow surface decision. No mobile, public/customer frontend, new navigation area, API execution, metadata edit, configuration publication/assignment, scheduling controls, scheduler engine, archive/history store, synthetic production result or unrelated module is added. Future Run history/Task/Event and richer configuration contracts remain explicit named dependencies. Material new visual workflows require the user's screenshot review; routine implementation/accessibility work that preserves this design is delegated.
