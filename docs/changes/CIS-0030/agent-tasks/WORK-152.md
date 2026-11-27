---
title: "WORK-152 Implement compatible CSharp TypeScript scheduler codecs clients and public substitutes"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-152
parent_id: WORK-080
category: contract
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-080"
instance_key: "outcome:dfb5a312ee1d4068b3f22c7131edae19"
depends_on: ["WORK-151"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["capability-contracts","scheduler","configuration-portal","integration"]
authorization_scope_basis: "sha256:d0cee10bc1e8d1353a4adad56edeefda278d6673ba8bbd9f76196f13feb800bc"
---

# WORK-152: Implement compatible CSharp TypeScript scheduler codecs clients and public substitutes

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement compatible CSharp TypeScript scheduler codecs clients and public substitutes

## Acceptance criteria

- [x] Implement the agreed trusted descriptor/scheduler/eligibility/cohort portable wire and CSharp/TypeScript client interfaces, explicit native REST aliases/codecs and useful public CommonTesting substitutes. Preserve closed command validation, exact case-sensitive IDs, decimalInt64/revisions, UTC timestamps, combined body/envelope/depth limits, unknown-safe response interpretation and old protocol support. Optional companions avoid compulsory new members on independent backends/substitutes. Retain existing transport deadline/cancellation/actual cleanup and no automatic writes; no new SDK transport or framework. This owns portable contract/consumer portions of RM09-AC04, RM09-AC05, RM09-AC08, RM09-AC09, RM09-AC10, RM09-AC16, RM09-AC17, RM09-AC19, RM09-AC20 and RM10-AC08, RM10-AC24, RM10-AC25, RM10-AC28, RM10-AC32, RM10-AC36, RM10-AC39, RM10-AC40.

## Targeted validation

- [x] Run cross-language exact wire/conformance and old-client/substitute cases with large counters, unknown fields/statuses, strict commands, forbidden hints and complete encoded-byte/depth admission. Check public API/codec/alias registration and consumer documentation; include proportionate independent review. Native service semantics are proved by their implementation owners. Verify this owner consumes only the declared medium prerequisites (WORK-151) and keeps the original parent accounting pool; broad provenance containers are not early completion prerequisites.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |


| Native C# build | Passed, zero warnings/errors | rm09c-152-unit-build-corrected; exact Core bytes match unit output |
| Management unit contracts/client/substitutes | 487 passed, 0 failed, 0 skipped | work152-management.trx |
| Canonical API/schema references/examples | 94 passed, 0 failed, 0 skipped | work152-contracts.trx |
| Portable TypeScript affected tests | 309 passed, 0 failed/skipped/cancelled | rm09c-152-wire-final-tests/native.log |
| TypeScript/browser types | Passed | rm09c-152-wire-final-build; rm09c-152-browser-types |
| Scoped C# format and verify | Passed | rm09c-152-format-final; rm09c-152-format-final-verify |
| Independent source review | Required findings resolved; exact snapshots retained | work152-client-independent-review.md; work152-wire-peer-review.md |
| Latest Scheduler BRD | Same exact imported v1.1 bytes; contracts-only reconciliation | scheduler-contract-reconciliation.md |

Evidence manifest: `.cis/local/testing/rm09-completion/work152-evidence.json`.
Earlier test/build/review snapshots, including the test-initializer compilation
failure, remain preserved and charged. Parent WORK-080 retains the single shared
8-hour/8-attempt pool; child separation creates no fresh allowance. This completion
covers optional passive contracts, clients and substitutes. It does not establish
Scheduler runtime, provider, real history or completed portal UI delivery.
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2902; failed=367; possibleTokenSavings=591; ledgerDigest=sha256:e5c8912201aef3cc9df51195e89b778926b5d52adb24a5e813e47b0ed64558ec |

