---
title: "WORK-156 Implement protected in-memory drafts and late-response session isolation"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-156
parent_id: WORK-050
category: data
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-050"
instance_key: "outcome:065e5731bb2a4e2daf33042ccd18f133"
depends_on: ["WORK-155","WORK-171"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","scheduler","integration"]
authorization_scope_basis: "sha256:c3bfe0a3f2c3dbe4245eaa206b2f2c283c42e4ca80680531b31d422f10d6e192"
---

# WORK-156: Implement protected in-memory drafts and late-response session isolation

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement protected in-memory drafts and late-response session isolation

## Acceptance criteria

- [x] Deliver shared protected in-memory draft/request state used by upcoming metadata/Configuration/task flows. Session expiry/signout/user switch clears affected protected content/drafts/receipts and blocks writes/recovery; permission loss clears protected values and leaves only same-user minimal receipts when appropriate. Track exact user/application/environment/definition/schema request context so cancellation or a late old response cannot refill a different user's or environment's screen. Retain only dated allowed snapshots on transient outage, disable submissions and queue nothing until fresh state. Drafts are memory-only with unsaved navigation protection; no payload persistence. Owns state-lifetime slices of RM09-AC09, RM09-AC10, RM09-AC12, RM09-AC14, RM09-AC17, RM09-AC19 and RM10-AC28, RM10-AC29.

## Targeted validation

- [x] Use existing controlled delayed-request/session tests for switch/signout/expiry/grant loss, same-user receipt restoration, definition changes and exact protected state clearing. Include keyboard/navigation protection where this primitive affects it, documentation and independent review; actual full UI journeys remain with their component owners. Verify this owner consumes only the declared medium prerequisites (WORK-155, WORK-171) and keeps the original parent accounting pool; broad provenance containers are not early completion prerequisites.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| State and authentication boundaries | 52 passed,0failed/0skipped/0cancelled; real context producer, exact session/target/epoch and actual callback lifetime; memory-only drafts | `rm09c-156-state-tests-reviewed`; `work156-evidence.json` |
| Production compatibility | Pinned Next16.4.6 production build and strict TypeScript passed | `rm09c-156-portal-build` |
| Native host/browser | Two existing actual OIDC/PKCE/database-session/HTTPS/desktop-browser cases passed; all owned work drained | `rm09c-156-native-portal-tests`; native TRX and `rm09c-156-native-report-verify` |
| Independent review | All required source findings resolved; final source hashes frozen | `.cis/local/testing/rm09-completion/work156-core-review.md` |
| Documentation and scope | Current producer/BFF/coordinator/paging/dirty-owner behavior documented; full editor navigation remains component-owned | `docs/modules/management/portal-protected-state.md`; no Scheduler runtime or fullportal/release claim |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2964; failed=369; possibleTokenSavings=591; ledgerDigest=sha256:57a68584f6b724d3e6daedd5b4e3459e0682b5fff3cb8f7872f87812f43aed8d |
