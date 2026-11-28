---
title: "WORK-011 RM11 portal grid and version deployment workflows"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-011
parent_id: none
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:b601028d6f24407bbb3aa52be2ef1fe2"
depends_on: ["WORK-003","WORK-004","WORK-005","WORK-010"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0031:outcome:e825fd00dd9f42388e09d9835f4bf34a"
authorization_scope_ids: ["RM11-BACKOFFICE","RM11-MANAGEMENT"]
authorization_scope_basis: "sha256:551bffc90c8308124d9b881a1d93be9c4e1c130ab0dfed247753fc9b344bce99"
---

# WORK-011: RM11 portal grid and version deployment workflows

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

RM11 portal grid and version deployment workflows

## Acceptance criteria

- [x] Connect the reviewed grid to real Management APIs for typed row and rule editing, previous versions, Save, Deploy and rollback, and instance adoption visibility. Preserve validation/conflict drafts and exact decimals throughout. Own RM11-AC12 and supply AC29 editing/deployment evidence.

## Targeted validation

- [x] Run portal browser journeys against the native service for typed cells, markers, add/copy/remove/reorder, row fields and mode, older-version editing, conflict retention, saved-only state, deployment and rejected/pending instances.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Native saved notes and latest deployment requests | Passed | Locked .NET SDK 10.0.401 Release build and six focused Management provider and host cases, 0 failed or skipped. Native version summary and detail preserve the exact Unicode note. Current assignment reports the committed request timestamp, including after rollback, original replay and restart. `.cis/local/testing/rm11-management-isolated/results/rm11-portal-metadata-provider.trx` SHA-256 `85027b2f79d68e6bfa393734a50d5a54398362176eac7caa4455b84e6bd5bf08`. |
| Typed Management Rule Engine client | Passed | Pinned Node 24.16.0 compilation and 8 focused tests. Includes immutable notes, requested time, scoped deployment-instance paging and strict native adoption observations. |
| Native Edge checkpoint | Partial | The retained browser run covered applied/pending/rejected adoption, typed grid editing, exact values, and a real competing-save conflict that retained the note and decimal. The driver stopped at its metadata locator. The corrected driver is ready for the combined AC29 journey in WORK-012. `.cis/local/testing/rm11-management-isolated/results/rm11-portal-native-edge-conflict-recovery.trx` SHA-256 `acb53dfff18093d51fe47ad1ffcda85eca36ed230b16e6e0419609629528b895`. |
| Combined native Edge grid and deployment | Passed | All five WORK-011 browser cases completed in the combined live Edge run. Covers exact typed edits, notes and metadata, retained older versions, competing-save recovery with the original key, Save without activation, explicit Deploy and exact rollback, actual instance adoption, and cross-application reads. The same run later stopped at a WORK-012 execution-history assertion. `.artifacts/management-portal/CIS-0031/WORK-012/browser-826f43e8-5344-465d-9d41-41126b0fc81a/browser-failure.json` records the completed browser cases, SHA-256 `d70ae00d0776e93b9d33d8c15eb68e4865dca57b69387a6d73eba58668ca7fbc`. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3843; failed=406; possibleTokenSavings=591; ledgerDigest=sha256:8b5b9e21bf6f38326c010f3d528f88e4c57343e32712098c203cd045d9d2b11a |
