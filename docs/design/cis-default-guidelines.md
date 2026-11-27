---
title: "Default Design Guidelines"
type: design-guidelines
status: Active
version: "1.0"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-23"
review_cadence: "on visual-language change"
cis:
  stable_id: change-impact-studio:design-guidelines:default
---

# Default design guidelines

These are the populated CIS defaults. A repository-specific approved guideline may
override them. Without an override, the design task records this document's path and
digest and renders against these rules.

## 1. Brand intent

Materials should feel:

- **Credible**: senior, experienced, and technically grounded.
- **Clear**: structured, readable, and low-noise.
- **Modern**: current and digital-first without being corporate-heavy.
- **Confident**: clear, evidence-backed language that preserves uncertainty and limits.
- **Practical**: focused on outcomes, risk reduction, and execution quality.

The system keeps slide decks, proposals, PDFs, one-pagers, landing pages, diagrams,
case studies, audit material, assessment material, and product mockups aligned.

## 2. Core visual direction

The visual language sits between technical consultancy and executive advisory.

Avoid:

- bright startup color chaos;
- generic corporate-blue templates;
- overly playful visuals;
- dense compliance-style bureaucracy;
- excessive gradients, shadows, or decorative shapes; and
- noisy, sales-heavy, generic, or overdesigned screens.

Aim for dark or deep neutral anchors, restrained accents, high whitespace, crisp
typography, structured layouts, subtle hierarchy, and engineered-looking diagrams.

## 3. Color system

| Token | Value | Usage |
| --- | --- | --- |
| `color.bg.primary` | `#FFFFFF` | Primary content surfaces |
| `color.bg.soft` | `#F8FAFC` | Page backgrounds, cards, and content sections |
| `color.bg.dark` | `#0F172A` | Dark anchors, headers, and section dividers |
| `color.text.primary` | `#0F172A` | Primary text |
| `color.text.secondary` | `#334155` | Emphasis, secondary titles, labels, and icon strokes |
| `color.text.muted` | `#64748B` | Supporting and metadata text |
| `color.border` | `#CBD5E1` | Muted borders and separators |
| `color.accent.primary` | `#2563EB` | Links, key highlights, and important labels |
| `color.accent.secondary` | `#0F766E` | Trust, resilience, maturity, and supporting emphasis |
| `color.status.success` | `#15803D` | Positive state |
| `color.status.warning` | `#B45309` | Warning state |
| `color.status.critical` | `#B91C1C` | Risk and critical state |

Use approximately 70% neutral base, 20% dark anchors, and 10% accent. Accent colors
guide attention rather than decorate. Never communicate state through color alone.

## 4. Typography

Use one modern, highly readable sans-serif family per asset.

- Primary: Inter.
- Fallbacks: Aptos, Segoe UI, Helvetica Neue, then Arial.
- Headings: medium or semibold.
- Body: regular.
- Avoid thin weights, full justification, and multiple type families.
- Headings use sentence case, remain short, and state an outcome or conclusion.

For the self-contained renderer, embed the selected font bytes in the JavaScript
source under a compatible license. Do not rely on a machine-installed font.

### Default web and product scale

| Role | Size | Weight |
| --- | ---: | --- |
| Hero/title | 44-60 px | Semibold |
| Section heading | 28-36 px | Semibold |
| Card heading | 18-22 px | Semibold |
| Body | 16-18 px | Regular |
| Meta text | 13-14 px | Regular |

### Default document scale

| Role | Size | Weight |
| --- | ---: | --- |
| H1 | 26-30 pt | Semibold |
| H2 | 18-22 pt | Semibold |
| H3 | 14-16 pt | Semibold |
| Body | 10.5-12 pt | Regular |
| Small note | 9-10 pt | Regular |

## 5. Tone expressed visually

The design communicates technical experience, evidence-based structure, commercial
awareness, and premium quality without inflation.

- Generous spacing communicates confidence.
- A limited palette communicates discipline.
- Sharp headlines communicate decisiveness.
- Clear structure communicates operational maturity.
- Simple diagrams communicate practical clarity.

## 6. Layout system

Use a modular grid and an 8 px spacing rhythm: `8, 16, 24, 32, 48, 64`.

Prefer hero plus proof, problem/risk/implication/action, three-column comparison,
two-column narrative plus evidence, or a section heading with three to five concise
points. Each screen or major region has one main message and one clear primary action.

- Use generous outer margins.
- Keep large vertical separation between content sections.
- Do not combine dense narrative, detailed diagrams, long bullet lists, and several
  competing highlights in the same viewport.

## 7. Content density

Prefer short paragraphs, grouped points, callouts, comparison cards, visual
summaries, and diagrams with minimal labels. Avoid walls of text, long bullet stacks,
crowded tables, and multiple competing charts.

A strong summary view usually contains one core headline, one supporting statement,
and three to five proof points or one principal visual.

## 8. Heading and copy style

Headings state conclusions rather than topics. Body copy is direct, calm,
evidence-oriented, technically literate, and commercially aware.

Avoid filler such as "in today's fast-paced world", "leveraging synergies",
"cutting-edge solutions". Use concrete verbs when evidence supports the claim.
Keep qualifications such as "may" or "might" when the outcome is uncertain;
never turn an expectation into a guarantee to sound decisive. Apply the
[shared content standard](../standards/human-readable-content-standard.md),
preserving required sections, conditions, evidence and approval boundaries.

## 9. Visual components

### Cards

- White or Cloud background.
- 1 px muted border.
- 12-16 px radius.
- Very subtle or no shadow.
- Concise heading and short explanation.

### Callouts

- Left accent bar or lightly tinted background.
- Two to four lines maximum.
- Reserved for takeaways, risks, decisions, and recommendations.

### Section dividers

- Dark background.
- One sharp heading.
- Optional single supporting line.

### Tables

- Use only when comparison matters.
- Strong header row, light borders, minimal columns, and restrained shading.
- Move dense matrices to an appendix or detail view.

### Controls

- One visually dominant primary action per region.
- Secondary and destructive actions remain visually distinct.
- Disabled, loading, focused, validation, denied, and destructive-confirmation
  states must be unambiguous.
- Reuse the governed component definitions for buttons and links; text inputs,
  text areas, selects/dropdowns, checkboxes, radio groups, and toggles; tabs,
  breadcrumbs, pagination, and dropdown menus; alerts, dialogs, accordions, avatars,
  badges, cards, forms, empty states, timelines, tables, and filter bars.
- Labels, helper text, required markers, validation messages, keyboard focus,
  disabled behavior, loading behavior, and recovery actions are part of a component's
  contract, not screen-specific decoration.
- Do not redraw a common control inside a feature renderer. Extend or replace its
  governed template through an explicit design-system decision.

## 10. Diagram style

Use rectangular blocks, consistent stroke widths, simple connectors, restrained
labels, one accent color for flow, clear hierarchy, minimal crossing lines, and
grouped zones or layers. Label outcomes as well as components.

Avoid cartoon icons, mixed illustration styles, inconsistent arrows, saturated
shapes, and pseudo-3D effects.

## 11. Icons and imagery

Use a single clean geometric icon family with outline or restrained fill. Because
the renderer is self-contained, icons are SVG paths embedded in the JavaScript.

Use imagery sparingly. Prefer abstract infrastructure, restrained product UI,
technical textures, or professional environmental photography only when necessary.
Avoid handshake stock photography, server-room clichés, and overly futuristic AI art.

## 12. Charts and data visuals

Charts support decisions rather than decoration. Use one message per chart, minimal
gridlines, few colors, and highlight only the important series or value. Prefer bars,
simple lines, maturity/risk heatmaps, and comparison matrices. Avoid 3D charts,
rainbow palettes, complex legends, and nonessential pie charts.

## 13. Accessibility and responsive behavior

- Preserve WCAG contrast for text and interactive elements.
- Show a visible focus state and do not encode status through color alone.
- Maintain logical reading and action order.
- Use clear errors with recovery instructions.
- Allow text wrapping and zoom without clipping or hiding actions.
- Preserve touch target size on mobile/native views.
- Reduce density rather than merely shrinking desktop layouts.
- Show desktop and compact/mobile designs when both are in approved scope.

## 14. Format-specific guidance

### Presentations

Use a narrative flow: opening statement, challenge or decision, reasoning,
recommendation, and implementation/next step. Favor fewer words, stronger headlines,
larger spacing, and more contrast.

### PDFs and one-pagers

Use strong subheadings, concise sections, callout summaries, well-spaced paragraphs,
and clear deliverable blocks.

### Landing pages

Use: hero/value proposition, problem solved, intended audience, engagement process,
deliverables, credibility, and call to action.

### Product screens

Use the validated textual wireframe as the behavioral authority. Preserve its Screen
IDs, routes, actions, states, permissions, and exclusions. Visual design may improve
hierarchy and clarity but may not change behavior.

## 15. Positioning cues

Materials should reinforce experienced hands-on technical leadership, architecture,
security, operations, delivery reality, risk reduction, resilience, and maturity.
They should not feel like a checkbox exercise.

## 16. Consistency rules

Keep one primary font family, one core palette, one heading system, one card style,
one diagram style, one spacing rhythm, sentence-case headings, and restrained accent
usage. Density may change by format; visual language does not.

## 17. Design tokens

```text
color.bg.primary = #FFFFFF
color.bg.soft = #F8FAFC
color.bg.dark = #0F172A
color.text.primary = #0F172A
color.text.secondary = #334155
color.text.muted = #64748B
color.border = #CBD5E1
color.accent.primary = #2563EB
color.accent.secondary = #0F766E
color.status.success = #15803D
color.status.warning = #B45309
color.status.critical = #B91C1C
radius.sm = 8px
radius.md = 12px
radius.lg = 16px
shadow.default = none
space.1 = 8px
space.2 = 16px
space.3 = 24px
space.4 = 32px
space.5 = 48px
space.6 = 64px
```

## 18. Starter patterns

- Title: one assertive headline and one supporting line.
- Comparison: two clearly separated choices and a bottom recommendation with reason.
- Detail section: heading, short introduction, three supporting points, and one
  takeaway callout.
- Product list: purpose/status summary, restrained filters, structured results, one
  primary create action, and explicit empty/loading/error states.
- Product detail: identity/status header, primary content, state-aware actions,
  related evidence, and activity/audit history where applicable.

## 19. Final style summary

On-brand material feels sharp, calm, structured, credible, technically mature, and
commercially relevant. It never feels noisy, salesy, generic, bureaucratic,
inconsistent, or overdesigned.

## 20. Working defaults

- Font: Inter, embedded in the renderer.
- Background: White or Cloud.
- Primary text: Ink.
- Primary accent: Signal Blue.
- Supporting accent: Teal.
- Heading style: sentence case and semibold.
- Body style: short paragraphs and minimal bullets.
- Layout: high whitespace and modular grid.
- Visual style: restrained, architectural, premium, and technical.

## 21. Approval and deviations

This document is the approved CIS fallback guideline. A target repository may adopt
it as-is or approve a repository-specific replacement. A feature deviation records
the rule, reason, approver, date, and affected renderer/PNG manifest.
