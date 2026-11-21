---
title: "WORK-004 Deliver privileged retention maintenance and retained-result recovery"
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
instance_key: "outcome:8ac06f0e1d5a43bab7a234e22d104e12"
depends_on: ["WORK-003"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0018:outcome:a5124e88d31b486fafd34f4241c8fa4c"
authorization_scope_ids: ["retention-maintenance"]
authorization_scope_basis: "sha256:d4c8eec9ad63219778702e9ebd039017ed30ddc7b082814d4e8e0a6c104fa33c"
---

# WORK-004: Deliver privileged retention maintenance and retained-result recovery

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver privileged retention maintenance and retained-result recovery

## Acceptance criteria

- [x] P11-P13/P15 and AC09/11/13: persisted immutable expiry with indefinite defaults, tombstones and logicalexpiry independentofmaintenance; explicit authorizer-gated singleoperation/trustedscope maintenance in ownRM02transaction, max256rows and SKIPLOCKED onlythere. Unsupported/corrupt versions or decode failure retain protection; actual full deletion ends it. Native-commit decorator after realcommit qualifies simulated acknowledgementloss/Unknown recovery without a publicPending/job lifecycle.

## Targeted validation

- [x] Native expiry-before-maintenance, mismatch-vs-expiry/version precedence, finite256batch boundary, retained tombstones, whole-row deletion, maintenance/lookup/arbitration races and actual deadlock/retry settlement. Simulated post-realcommit failure must traverse RM02commitboundary toUnknown; freshlookup recovers original, no terminalrollback/commitretry, Committedcleanup errors remainCommitted. Maintenanceauthorization/countnondisclosure and uncertain batchcounts; provider docs/style/build and independentreview.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Privileged retention | Separate authorized256-row batch, fixed expiry/tombstones/deletion and truthful completion-qualified counts | Core/Operations maintenance port/provider; retention guide |
| Native | Clean builds/scoped format;119Operations Unit and33PostgreSQL cases passed each actualOS, zero skips | .artifacts/operations/CIS-0018/WORK-004/retention-receipt.json |
| Recovery | Real commit then reported failure through RM02 yieldsUnknown; fresh lookup recovers original; real disposal failure preservesCommitted | NativeCommitBoundarySource and actual retention cases |
| Preserved correction | BEFORE DELETE did not requireblocking; causal AFTER DELETE hold fixed the test, focused/full reruns passed, production unchanged | retained failed33-case TRX; integrated receipt |
| Review/restoration | Independent46source/nineTRX alignment clean; exactnewownedfixture/image/secrets removed after clients/schemas0 | ../review-004.md; .cis/local/operations-intake/postgres-restoration.json |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1931; failed=270; possibleTokenSavings=591; ledgerDigest=sha256:8016a8b56437b3f07245d4c7a16ae88b015cc0d5a4d1184ca53cf6d532b4c35a |
