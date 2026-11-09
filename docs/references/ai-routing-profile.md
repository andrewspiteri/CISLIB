---
title: "AI Routing Profile"
type: ai-routing-profile
status: Active
owner: "Repository maintainers"
last_reviewed: "2026-08-27"
review_cadence: "on provider, model, privacy, or cost-policy change"
---

# AI routing profile

Local Ollama is the default. Remote transmission requires both a reviewed route
and explicit `--allow-remote` authorization for the exact submitted content.

| Capability | Provider | Model | Allow remote | Cache |
|---|---|---|---|---|
| index-card | ollama | repository-smallest-local | no | yes |
| brd-question-suggestion | ollama | repository-smallest-local | no | no |
| context-summary | ollama | repository-smallest-local | no | yes |
| learning-summary | ollama | repository-smallest-local | no | yes |