---
title: "Agent Provider Profile"
type: agent-provider-profile
status: Draft
owner: "Repository maintainers"
template_last_reviewed: "2026-08-28"
last_reviewed: "2026-10-11"
review_scope: initialization metadata only; template policy remains unadopted
review_cadence: "on provider, permission, isolation, or retention-policy change"
---

# Agent provider profile

Imported CIS template, retained as a Draft for future review. Its provider permissions, routing, evaluation or retention settings are proposals, not adopted CISLIB policy. Existing AGENTS.md guidance remains authoritative; Claude review in this repository is read-only. The template review date above records its source metadata, not a CISLIB approval.

No direct provider is selected by default. A controller must select Codex, Claude,
or another discovered provider until this profile is reviewed. Credentials remain
provider-native and must never be copied into this document.

| Setting | Value | Rationale |
|---|---|---|
| default-provider | none | Avoid silent vendor selection. |
| default-transport | provider-preferred | Capability negotiation remains explicit. |
| implementation-isolation | git-worktree | Preserve the user's current working tree while seeding the isolated worktree from its exact tracked and non-ignored untracked baseline. |
| direct-dirty-working-tree | denied | Requires an explicit reviewed policy change. |
| maximum-run-seconds | 3600 | Bound local foreground execution. |
| maximum-startup-seconds | 30 | Fail closed when a provider produces no startup evidence. |
| maximum-idle-seconds | 300 | Classify a silent stalled provider separately from total expiry. |
| approve-within-ceiling | denied | Permission requests require explicit controller authorization per run. |

## Providers

| Provider | Enabled | Preferred transport | Allowed modes | Maximum permission | Notes |
|---|---|---|---|---|---|
| codex | yes | app-server | plan, implement, review | workspace-write | `exec-json` is an explicit non-interactive fallback. Resolution checks `CIS_CODEX_EXECUTABLE`, process PATH, then the current Windows Codex Desktop installation. Inconclusive login status is advisory because ambient Desktop/App Server authentication can still execute. Explicit setup uses provider-native browser or device authentication through `cis agent provider authenticate codex`; CIS never receives credentials. |
| claude | yes | stream-json | plan, implement, review | workspace-write | Headless execution does not relay interactive approvals. Implementation-only `--allow-command` adds literal native rules per attempt; denied commands fail. Native rules are not an OS sandbox. |
| portable | yes | envelope | plan, implement, review | read-only | Preparation only; no direct execution. |