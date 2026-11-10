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

- Resolved: 1 of 12
- State: No visual UI — visual-design questions do not apply

## Questions
### UI-Q-001 — Surfaces and audiences

Which user-facing surfaces and audience classifications need one coherent visual direction?

- Why this is needed: Public, customer, backoffice, mobile, desktop, and embedded surfaces have different trust, navigation, and density needs.
- Common options: Public web; Customer web; Backoffice web; iOS; Android; Desktop; Embedded; No user-facing UI
- Advisory starting direction: Include only surfaces established by the Active BRD and technical intent. Keep public, customer, and backoffice experiences explicitly classified.
- Status: Answered
- Answered by: Andrew Spiteri (approved technical intent; transcribed by Codex)
- Answered at: 2026-11-10T13:59:42.4444631+00:00
- Resolution source: human
- Confidence: confirmed
- Evidence: Not recorded

#### Recorded answer

No user-facing UI

### UI-Q-002 — Product character

What should the product feel like to its users?

- Why this is needed: A small set of experience qualities gives later screen designs a stable basis for visual judgment.
- Common options: Clear and calm; Practical and efficient; Premium and restrained; Friendly and approachable; Technical and precise; Playful; Institutional
- Advisory starting direction: Use a clear, credible, modern, confident, and practical direction with restrained visual noise unless product evidence requires another character.
- Status: Unanswered
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: unresolved
- Confidence: Not assessed
- Evidence: Not recorded

#### Recorded answer

Not answered.

### UI-Q-003 — Shell and navigation

What application shell and primary navigation model should each surface use?

- Why this is needed: Shell and navigation are shared product structures; deciding them per feature creates inconsistency and unnecessary design work.
- Common options: Header only; Header and side navigation; Bottom navigation; Tabs; Master/detail; Document flow; Platform-native shell
- Advisory starting direction: Choose the smallest persistent shell that supports the product's information architecture. Do not add side or bottom navigation to a simple product without a demonstrated need.
- Status: Unanswered
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: unresolved
- Confidence: Not assessed
- Evidence: Not recorded

#### Recorded answer

Not answered.

### UI-Q-004 — Layout and density

What layout rhythm, content width, and information density should guide screens?

- Why this is needed: Density and hierarchy affect scanability, task speed, responsive behavior, and component selection.
- Common options: Spacious; Balanced; Compact; Data-dense; Single-column; Responsive grid; Master/detail
- Advisory starting direction: Use an 8 px spacing rhythm, generous outer margins, one dominant action per region, and the lowest density that still supports efficient completion.
- Status: Unanswered
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: unresolved
- Confidence: Not assessed
- Evidence: Not recorded

#### Recorded answer

Not answered.

### UI-Q-005 — Color and theme

What color, theme, branding, and contrast direction should apply?

- Why this is needed: Color is both identity and interaction state; it must remain accessible and reusable across all surfaces.
- Common options: Light; Dark; System theme; Brand-led; Neutral with restrained accent; High-contrast option
- Advisory starting direction: Start from the Active design guidelines: neutral surfaces, deep anchors, restrained accents, and WCAG-compliant contrast. Record any brand-specific replacement tokens.
- Status: Unanswered
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: unresolved
- Confidence: Not assessed
- Evidence: Not recorded

#### Recorded answer

Not answered.

### UI-Q-006 — Typography and content

What typography, iconography, imagery, and writing tone should the interface use?

- Why this is needed: These choices establish hierarchy and voice while preventing each feature from inventing its own presentation language.
- Common options: Sans serif; System typography; Product typeface; Text-first; Icon-supported; Illustration-led; Photography-led
- Advisory starting direction: Use one highly readable sans-serif family, sentence-case outcome headings, direct evidence-oriented copy, and icons only where they improve recognition.
- Status: Unanswered
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: unresolved
- Confidence: Not assessed
- Evidence: Not recorded

#### Recorded answer

Not answered.

### UI-Q-007 — Reusable component system

Which UI framework, design tokens, and reusable component families are authoritative?

- Why this is needed: Buttons, fields, menus, tables, feedback, and navigation should be reused rather than redrawn in every feature.
- Common options: Existing component system; Classification-selected CIS default; Platform-native components; Custom governed components
- Advisory starting direction: Preserve an evidenced existing UI system. Otherwise use the platform-specific framework recorded in the UI framework profile and govern shared shell, controls, data display, feedback, overlays, and navigation components.
- Status: Unanswered
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: unresolved
- Confidence: Not assessed
- Evidence: Not recorded

#### Recorded answer

Not answered.

### UI-Q-008 — Responsive and platform adaptation

How should layout and interaction adapt across supported viewports and platforms?

- Why this is needed: Responsive behavior is part of the interaction contract, not an afterthought added during implementation.
- Common options: Desktop first; Mobile first; Desktop and compact web; Tablet; Native adaptive layouts; Keyboard-heavy desktop
- Advisory starting direction: Define at least desktop and compact behavior for web surfaces; preserve task order and meaning while adapting navigation, density, and touch targets.
- Status: Unanswered
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: unresolved
- Confidence: Not assessed
- Evidence: Not recorded

#### Recorded answer

Not answered.

### UI-Q-009 — Accessibility and input

Which accessibility baseline and input modes must every feature support?

- Why this is needed: Keyboard, screen reader, focus, contrast, touch, motion, and error behavior must be designed before screenshots are approved.
- Common options: WCAG 2.2 AA; Keyboard; Screen reader; Touch; Pointer; Voice control; Reduced motion; High contrast
- Advisory starting direction: Use WCAG 2.2 AA as the baseline with semantic structure, complete keyboard operation, visible focus, non-color state cues, accessible names, and reduced-motion support.
- Status: Unanswered
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: unresolved
- Confidence: Not assessed
- Evidence: Not recorded

#### Recorded answer

Not answered.

### UI-Q-010 — State, feedback, and motion

How should loading, empty, success, warning, error, unavailable, destructive, and transition states behave?

- Why this is needed: A polished happy path is insufficient when users cannot understand pending, failed, denied, or recoverable states.
- Common options: Inline feedback; Status banners; Toast notifications; Skeletons; Progress indicators; Confirmation dialogs; Optimistic updates
- Advisory starting direction: Every operation needs truthful pending, success, failure, and recovery feedback. Prefer inline feedback; reserve motion for continuity and honor reduced-motion preferences.
- Status: Unanswered
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: unresolved
- Confidence: Not assessed
- Evidence: Not recorded

#### Recorded answer

Not answered.

### UI-Q-011 — Data and action patterns

Which recurring presentation and action patterns should be standardized?

- Why this is needed: Lists, tables, forms, filters, editors, and destructive actions are common sources of avoidable inconsistency.
- Common options: Flat lists; Tables/grids; Cards; Forms; Inline editing; Search/filter; Bulk actions; Destructive confirmation
- Advisory starting direction: Standardize only patterns supported by the product's journeys. Keep primary actions obvious, secondary actions compact, and destructive actions explicitly confirmed.
- Status: Unanswered
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: unresolved
- Confidence: Not assessed
- Evidence: Not recorded

#### Recorded answer

Not answered.

### UI-Q-012 — Constraints and exclusions

Which brand assets, mandated conventions, legacy constraints, or explicit visual exclusions must be preserved?

- Why this is needed: Explicit constraints prevent feature design from silently rebranding, replacing frameworks, or expanding platform scope.
- Common options: Existing brand; Existing shell; Existing design system; White-label; Localization; Offline; No imagery; No animation
- Advisory starting direction: Record only evidenced constraints. Treat framework replacement, rebranding, new surfaces, and major shell changes as explicit decisions rather than feature-level interpretation.
- Status: Unanswered
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: unresolved
- Confidence: Not assessed
- Evidence: Not recorded

#### Recorded answer

Not answered.
