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
  questionnaire_hash: sha256:37d26c93084eff0d47043d110d7b777acb61e8d71a68133543a19f8f77401ab8
  design_guidelines_hash: sha256:4775b0f52d71a0ddaccd065d2381cc95e7895e65f70d93e3deee32081cab74b5
  ui_framework_profiles_hash: sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
  approved_by: "Andrew Spiteri"
  approved_at: "2026-11-25T17:29:29.9955515\u002B00:00"
  approval_reason: "Record the desktop backoffice direction already selected in the user\u0027s R02-R06 screenshot decisions, culminating in Approve connected design. This explicitly supersedes the earlier global no-UI applicability only for the RM09 read-only Management portal. It carries no new visual choice, mobile scope, write workflow, scheduler, history service, or independently reviewed numerical allocation."
  approved_content_hash: "sha256:1540f446d07643e84889996e1e8170ee0f9462edf8092cdd2010bbf227db5135"
---

# cislib High-Level UI Direction

This record retains the original CISLIB no-visual-UI baseline and records its explicit narrow exception for the RM09 desktop backoffice Management portal, selected through the user's subsequent request and approved R02-R06 screenshot decisions. All other original exclusions remain. The managed direction below transcribes that already reviewed design; it grants no new visual scope. Retain the exact R06 dark 200 px left rail with CIS/Management and the sole Applications link, together with the Management header. References to no multi-area rail exclude additional destinations; the existing approved rail remains. This records the current approved implementation scope and visual design; neither screenshot approval nor wording about a delivered step claims implemented portal code, working accessibility or verified runtime behavior.

<!-- cis:ui-direction-managed:start -->
## Experience intent and principles

Retain the clear, calm, practical and technically precise backoffice presentation shown in approved R06. Give application identity, observed version, report provenance and unavailable information a readable hierarchy. Preserve the restrained visual noise and evidence-oriented language of the selected CIS default guideline; do not introduce decorative imagery, a rebrand or a new product personality.

Authority: `UI-Q-002`.

## Product surfaces and audience classes

Add only the RM09 desktop-browser backoffice Management portal for authorized operators. This is the explicit, narrow exception to the 10 November 2026 global No user-facing UI record, selected by the user's subsequent RM09 request and R02-R06 screenshot decisions. The current delivered step is read-only Applications to application detail: Instances, Configuration list, Capabilities and Scheduled jobs, with item inspection and Job details. Approved Run history, Scheduled Task and immutable Event designs retain their named future backend prerequisites and cannot imply a delivered scheduler/history service. No public/customer UI, mobile/native application, management write workflow or unrelated graphical surface is selected.

Public, customer, backoffice, and native surfaces keep distinct trust and disclosure behavior while sharing the approved visual language. Authority: `UI-Q-001`.

## Shell and navigation model

Use the simple desktop header shown in R06, with Applications as the only top-level destination and no multi-area navigation rail. Applications supports name/identity and authorized-environment filtering. Opening an exact application defaults to Instances and retains its application/environment context across Instances, Configuration, Capabilities and Scheduled jobs tabs. A selected item opens the shared right-side modal inspection panel while its list stays visible. Job View full details opens dedicated detail navigation. Preserve Back, query/cursor, report, scroll and focus context. The approved future Job/Task/Event/history links remain explicit unavailable or prerequisite-dependent reads until their contracts exist.

The application shell is reusable product infrastructure. Feature designs may add destinations within it but may not silently replace its navigation model. Authority: `UI-Q-003`.

## Visual language and tokens

Retain the approved light palette: white surfaces on #F8FAFC, navy #0F172A, secondary text #334155, blue #2563EB and borders #CBD5E1. Follow the selected default guideline for semantic status colors and visible focus. Status always includes text, so color carries no unique meaning. No alternate theme, theme switcher, brand replacement or new palette is selected.

The canonical design guidelines supply the starting color, spacing, elevation, radius, status, and contrast tokens. Product-specific replacements must be named, reusable, and reviewed here. Authority: `UI-Q-005`.

## Layout, spacing, and density

Retain the R06 desktop master/detail layout, bounded readable tables, aligned filters and the 8 px spacing rhythm. Preserve white content surfaces, clear headings, compact status/context fields and readable long application identities, versions and API paths. The approved reference viewport is 1600 by 1000. The right-side modal panel has an independently scrolling body and keeps the underlying list context visible. Do not add cards, multi-area navigation, columns or controls that change the approved information hierarchy.

Feature designs must preserve information hierarchy and task order at every supported viewport rather than merely scaling a desktop image. Authority: `UI-Q-004`.

## Typography, content, iconography, and imagery

Retain Inter, the R06 14-16 px body text and 20-24 px titles, sentence-case labels and direct evidence-oriented copy. Keep canonical identities, raw/open status codes and timestamps readable; distinguish Not reported, Not evaluated and unavailable data. Use icons only where the approved design uses them to support an already named control; no new photography, illustration or decorative image family is selected. Synthetic previews remain identified as synthetic and never substitute for runtime facts.

Text remains understandable without decorative imagery; icon-only actions require accessible names and an unambiguous familiar symbol. Authority: `UI-Q-006`.

## Reusable component system

Reuse the exact approved R06 visual contracts for the selected CIS shell and component templates: page header, buttons, text input, select, tabs, breadcrumbs, pagination, dialog/right-side inspection panel, alert, table, filter bar, status badge, card, empty state and timeline where applicable. R06's simple header and one Applications destination override the generic shell template's multi-area rail description. Reuse shared production components/tokens for these patterns. The screenshot renderer and its Sharp dependency are design tooling, not a selected production UI framework; this visual answer grants no new package or framework campaign.

Standardize the approved filterable application table, query-bound Previous/Next paging, exact-identity links/row activation, application detail tabs, current-tab Refresh, readable status badges, shared item inspection panel and context-preserving Back navigation. Use retained data for quick inspection, rather than introducing extra panel reads. Job details retain Overview, Run history and Configuration navigation; future Task configuration has exactly Trigger and Job tabs with history separate, and immutable Event configuration is captured historical context. Keep all current production actions read-only and truthfully unavailable where an explicit prerequisite is missing.

Shared controls include their focus, validation, loading, disabled, denied, empty, error, and recovery behavior. Feature renderers consume governed templates instead of redrawing common controls. Authority: `UI-Q-007`, `UI-Q-011`.

## State, feedback, and motion

Keep loading, empty, denied, unavailable, stale dated snapshot and incompatible response states distinct. Initial failure offers Retry; failed refresh may retain only the permitted, clearly dated same-scope snapshot. Access/session loss clears protected content; obsolete responses and changed selection cannot repopulate a panel. Missing occurrence times say Not reported; missing history says Run history unavailable and never becomes a successful zero-run result. Preserve the approved persistent inline feedback; no optimistic mutation, new toast system, decorative animation or destructive workflow is selected.

No transition may conceal completion or failure. Destructive operations remain explicit, and animation never becomes the only carrier of meaning. Authority: `UI-Q-010`.

## Responsive and platform adaptation

Desktop browser only, as explicitly requested by the user. Retain the desktop layout and 1600 by 1000 approved reference frames; no mobile or narrow-screen preview/delivery is required. Preserve readable content, desktop keyboard access and zoom without changing task order or semantics. Do not introduce a mobile shell, native platform, bottom navigation or responsive workflow redesign under this decision.

Platform conventions may adapt presentation, but capabilities, terminology, and safety boundaries remain coherent. Authority: `UI-Q-008`.

## Accessibility and inclusive interaction

Retain the R06 implementation requirements for semantic headings/tables, explicit labels, keyboard activation, visible focus and return, zoom, sufficient contrast, non-color status cues and announced persistent feedback. Opening a named modal panel moves and contains focus, makes background controls inert, and supports Close, Escape and backdrop dismissal. Closing restores originating focus or the list heading with feedback if the item disappeared. Use reduced-motion-compatible ordinary transitions. Screenshot approval does not prove working accessibility; implementation validation must check these behaviors.

Accessibility is an acceptance boundary for textual wireframes, rendered designs, implementation, and verification. Authority: `UI-Q-009`.

## Constraints and exclusions

Limit this direction to the already approved RM09 desktop backoffice inspection step. Preserve exact R06 layouts, field meaning, selection/return context, original imported guideline bytes and accepted screenshot evidence. Earlier no-UI documents remain retained historical authority, superseded only by this explicit narrow surface decision. No mobile, public/customer frontend, new navigation area, API execution, metadata edit, configuration publication/assignment, scheduling controls, scheduler engine, archive/history store, synthetic production result or unrelated module is added. Future Run history/Task/Event and richer configuration contracts remain explicit named dependencies. Material new visual workflows require the user's screenshot review; routine implementation/accessibility work that preserves this design is delegated.

The high-level direction does not authorize new business behavior, architecture, data access, or product surfaces. Authority: `UI-Q-012`.

## Feature-design handoff

Each UI-bearing feature consumes this direction together with its applicable component and contract boundaries. Its textual wireframe defines screen structure, actions, states, and paths; its deterministic Sharp/SVG design pack proves the visual interpretation. A feature-level design may specialize this direction but cannot contradict it without first renewing this artifact.

## Traceability

- Solution architecture baseline: `semantic-v1:0e7bfb36f4adfc4380abccde1d2acb97de9f16016df4cf35cb776d4ebfd65a42`.
- UI questionnaire baseline: `sha256:37d26c93084eff0d47043d110d7b777acb61e8d71a68133543a19f8f77401ab8`.
- Design-guideline baseline: `sha256:4775b0f52d71a0ddaccd065d2381cc95e7895e65f70d93e3deee32081cab74b5`.
- UI-framework-profile baseline: `sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`.
- The questionnaire records the exact authority for `UI-Q-001`, `UI-Q-002`, `UI-Q-003`, `UI-Q-004`, `UI-Q-005`, `UI-Q-006`, `UI-Q-007`, `UI-Q-008`, `UI-Q-009`, `UI-Q-010`, `UI-Q-011`, `UI-Q-012`.
<!-- cis:ui-direction-managed:end -->

## UI-specific decisions and accepted exceptions

On 25 November 2026 the user's RM09 request and R02-R06 screenshot decisions, culminating in Approve connected design, supersede the 10 November global No user-facing UI applicability only for the desktop backoffice read-only Management portal described here. R06's exact renderer, wireframes, original default guideline and PNG manifest remain the visual evidence. No mobile, write workflow, API execution, scheduler/history service or unrelated graphical surface is included. Future Run history/Task/Event and richer configuration remain explicit backend dependencies. Preserve the original baseline and native approval history; these authored narrative updates do not edit approval metadata or manufacture an earlier decision.