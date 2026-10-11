---
title: "Local Artifact Retention"
type: local-artifact-retention
status: Draft
owner: "Repository maintainers"
template_last_reviewed: "2026-08-27"
last_reviewed: "2026-10-11"
review_scope: initialization metadata only; template policy remains unadopted
review_cadence: "on evidence, storage, privacy, or recovery-policy change"
---

# Local artifact retention

Imported CIS template, retained as a Draft for future review. Its provider permissions, routing, evaluation or retention settings are proposals, not adopted CISLIB policy. Existing AGENTS.md guidance remains authoritative; Claude review in this repository is read-only. The template review date above records its source metadata, not a CISLIB approval.

Rules apply only to immediate entries beneath the declared `.cis/local/` path.
Canonical Markdown and user-managed repository files are never eligible.

| Family | Path | Retain days | Keep latest | Action |
|---|---|---:|---:|---|
| graph | .cis/local/graph | 0 | 0 | preserve |
| api | .cis/local/api | 0 | 0 | preserve |
| references | .cis/local/references | 0 | 0 | preserve |
| frontend | .cis/local/frontend | 0 | 0 | preserve |
| policy-impact | .cis/local/impact | 0 | 0 | preserve |
| index-cards | .cis/local/index-cards | 0 | 0 | preserve |
| brd-question-suggestions | .cis/local/brd/questions | 0 | 0 | preserve |
| context | .cis/local/context | 30 | 20 | archive |
| workflows | .cis/local/workflows | 30 | 20 | archive |
| testing | .cis/local/testing/runs | 30 | 10 | archive |
| security | .cis/local/security/runs | 30 | 10 | archive |
| ci | .cis/local/ci | 14 | 20 | archive |
| diagnostics | .cis/local/diagnostics | 30 | 20 | archive |
| agents | .cis/local/agents | 30 | 20 | archive |
| generation | .cis/local/generate | 30 | 20 | archive |
| ai-cache | .cis/local/ai/cache | 7 | 50 | delete |
| ai-qualification | .cis/local/ai/qualification | 90 | 20 | archive |