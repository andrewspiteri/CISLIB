---
title: "WORK-004 Deliver native certificate authentication and listener trust boundaries"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-004
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:c2724b30aea1420aaddd11537c705b11"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0016:outcome:3aafc848f9384e88aef0a33e0fabb780"
authorization_scope_ids: ["certificate-inbound"]
authorization_scope_basis: "sha256:f396aea0cc4faad3592d428a81d280facae8c5af3ac2e4772d3581d208452d58"
---

# WORK-004: Deliver native certificate authentication and listener trust boundaries

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver native certificate authentication and listener trust boundaries

## Acceptance criteria

- [x] P05/P09/P11/P15: real native certificate integration validates explicit roots/issuer relationship, exact URI SAN, non-CA/usage/EKU, selected revocation and each-operation expiry. Separate actual listener bindings preserve probes without business bypass and development trust never authenticates production.

## Targeted validation

- [x] Real TLS tests cover permit/deny, chain/identity/usage/expiry/hostname failures, Online validation unavailable, ShortLived actual interval, persistent-connection operation expiry, profile isolation and hostile Host headers against operational listener. Include native/facade receiver-only path, guide/build/format/review.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Native receiving proof | Passed:38/38,0failed/skipped; assembly cleanup succeeds | `.artifacts/security/CIS-0016/providers/08-current-inbound-proof/results/certificate-native.trx` |
| Real expired TLS and key custody | Original negative retained and passes after supporting cleanup; exact owned files absent | `providers/current-key-custody-audit.json`; `providers/07-owned-key-restoration/restoration.json` |
| Build/style/review | Release0warnings/errors; scoped style and strict native trust/options/listener reviews pass | `docs/modules/security/certificates.md`; provider05/08receipts |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1781; failed=253; possibleTokenSavings=591; ledgerDigest=sha256:672a9038f8e2d660004988a4f98571667d04a5e2a5402c3420f9cf9aecee5c95 |
