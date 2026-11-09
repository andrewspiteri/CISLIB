---
applyTo: "**"
---

# CIS import essentials

Scope: CIS commands and newly created CIS artifacts. Existing project directives, authoritative documents, frameworks and approval gates remain in force. This file does not replace repository-wide routing or resolve conflicts.

- CIS configuration is `.cis/repository.yml`; the selected CIS documentation root is `docs`. Existing documents elsewhere remain authoritative until explicitly reconciled.
- Use `cis repo doctor` to check registration and `cis graph build --workspace <workspace>` to refresh derived workspace context. Use `cis --help` and command help to discover capabilities; use the listed CIS runtime profiles and review inferred test bindings before execution. Security scanner bindings and other capability setup remain explicit follow-up work.
- Read source documents before relying on generated context. Imported evidence and generated drafts do not establish currency or approval. Preserve project-specific requirements and report contradictions for a focused decision.
- Keep automatic model selection local. Remote model use requires explicit authorization for the selected content; never send secrets or credentials.
- Review planned file changes before applying them. Record actual validation results and unresolved decisions; CIS does not grant deployment or policy approval.

## Proposed requirement baselines

Existing project policy takes precedence. These additions apply only where that policy is silent; text matching does not prove full coverage.


The local import assessment is available in `.cis/local/import/report.json`. Overlap, routing and unverified items remain follow-up work.
