---
title: "WORK-002 Install review-corrected CIS and repeat the exact RM-08 native check"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-002
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:58e448ecfe534a85a445b0253f77bd20"
depends_on: ["WORK-001"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0028:outcome:f1068f406dc84823ace4293a490f5c28"
authorization_scope_ids: ["tool-adoption"]
authorization_scope_basis: "sha256:06340d863b0d648f0b5e3cf5575ab7714f642bb8ffc442ccbe2dbf0816b1194b"
---

# WORK-002: Install review-corrected CIS and repeat the exact RM-08 native check

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Install review-corrected CIS and repeat the exact RM-08 native check

## Acceptance criteria

- [x] Install the exact review-corrected source distribution through the unchanged explicit wrapper with byte-verified inventory and preserved rollback. Under this fresh owner run exactly the existing AC-10 acceptance case once, then verify unchanged with zero additional native launches. Preserve completed WORK-001, original reports and unrelated launch settings; no broader qualification is implied.

## Targeted validation

- [x] Retain source/build/install version and SHA256 manifests, use a fresh incremental unit profile selecting the same existing ConfigurationCentralAcceptanceTests.PairedInitializationUnchangedAcceptanceAndExplicitRemovalKeepOneRootAndTheActualRevision case, correlate the native TRX and settled execution, verify current evidence without rerun and record actual timings, hashes, costs and limitations.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Exact reviewed installation | Passed; 258 files match source manifest | .cis/local/testing/review-d449dcf/install-review-d449dcf-receipt.json |
| Stable one-case native scope | One passed, zero failed/skipped; settled and no pending batch | .cis/local/configuration-publication-intake/review-d449dcf-rm08-stable-run.json |
| Unchanged read-only Verify | Verified; no new execution; state and report unchanged | .cis/local/testing/review-d449dcf/stable-first-verify-proof.json |
| Actual unchanged cached Run | Completed; executed zero, reused one; no extra test launch | .cis/local/testing/review-d449dcf/cached-run-proof.json |
| Historical preservation | Six explicit protected files, 388 original files and 301 prior incremental files unchanged | .cis/local/testing/review-d449dcf/preservation-proof.json |

The first sandbox-context native pass is retained with its failed metadata
publication and changed-LOCALAPPDATA verification refusal. The stable profile
pins an owned app-data directory and uses the same existing named test. All
prior costs and the same WORK-002:1 attempt remain counted. These observations
establish only this installation and unit scope; whole AC-10 satisfaction, the
other 23 mappings and broader source/product qualification remain unassessed.
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2542; failed=341; possibleTokenSavings=591; ledgerDigest=sha256:b8139c76ece42872d8be6ecd4784207de6f3b63fb3fc37df37cd4c5d77584668 |
