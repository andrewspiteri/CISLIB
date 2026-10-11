---
title: "AI Routing Profile"
type: ai-routing-profile
status: Draft
owner: "Repository maintainers"
template_last_reviewed: "2026-08-27"
last_reviewed: "2026-10-11"
review_scope: initialization metadata only; template policy remains unadopted
review_cadence: "on provider, model, privacy, or cost-policy change"
---

# AI routing profile

Imported CIS template, retained as a Draft for future review. Its provider permissions, routing, evaluation or retention settings are proposals, not adopted CISLIB policy. Existing AGENTS.md guidance remains authoritative; Claude review in this repository is read-only. The template review date above records its source metadata, not a CISLIB approval.

Local Ollama is the default. Remote transmission requires both a reviewed route
and explicit `--allow-remote` authorization for the exact submitted content.

| Capability | Provider | Model | Allow remote | Cache |
|---|---|---|---|---|
| index-card | ollama | repository-smallest-local | no | yes |
| brd-question-suggestion | ollama | repository-smallest-local | no | no |
| context-summary | ollama | repository-smallest-local | no | yes |
| learning-summary | ollama | repository-smallest-local | no | yes |