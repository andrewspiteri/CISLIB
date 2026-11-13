---
title: "CIS-0003 delivery plan"
type: delivery-plan
status: Approved
change_id: CIS-0003
graph_build_id: "sha256:55ddaa8300bc2ef02d0e335fe9ff002e5045727d73315193a7b28f06f3f8b676"
approved_by: "Andrew Spiteri"
approval_rationale: "Approve the CIS-0003 plan and WORK-001 contract for the bounded ConsoleHuman Logging example and real package-consumer integration, preserving accepted product decisions, completed CIS-0001/CIS-0002 results and existing release obligations."
authority: human-approved
---

# Delivery plan

This is the human approval view. Review scope, task boundaries, sequencing, complexity, repositories, and approval gates.
Detailed execution constraints, validation procedures, provenance, and evidence fields remain in the linked agent task documents.

## Approval summary

One medium product task completes the existing ConsoleHuman Logging example and
verifies that same source as a clean consumer of the real CIS.Core package.
The [full task contract](agent-tasks/WORK-001.md) owns implementation, focused
Windows/Ubuntu checks, instructions, independent review, cleanup and completion;
there are no separate documentation, assurance or final-sweep tasks.

Andrew Spiteri accepted `IMPACT-C3273A4304` from proposal commit `9c57f0b2`.
Native CIS recorded that decision before generating this plan. The one accepted
impact is covered. Andrew Spiteri subsequently approved this exact plan/task and
separately authorized the published finite allocation. Native CIS recorded both
in order and transitioned WORK-001 to InProgress; the allocation advanced ledger
revision `:106` to `:107` without changing the completed owners' accounting.
Native plan validation reports zero errors, zero warnings and no open content
decisions. Strict documentation validation passed. Independent read-only review
of the exact contract/allocation found no required change. Subsequent actual
execution is recorded separately in [verification.md](verification.md).
Existing product and no-UI direction are reused; CIS-0001 and CIS-0002 remain
Complete with their accepted results, profiles, approvals and accounting intact.

The direct implementation uses the existing runtime/logger/scope/lifecycle API.
It adds no production responsibility or generalized verification framework.
The retained `CIS.Core.0.1.0-dev.1` package is reused while shipped inputs stay
unchanged. Main merge, publication, Healthcheck example integration and broader
regression/coverage/mutation/performance/release campaigns are excluded.

- Requirements covered: 0.
- Work items: 1 (1 actionable).
- Repositories: —.
- UI review: not required.
- Public-endpoint cache and database-isolation policy: not applicable.

## Approval gates

- All impact findings reviewed; no deferred impact hidden from scope.
- All required decisions resolved.
- Human authority recorded through `cis plan approve` or carried from a current approved feature by `cis plan derive`.

## Proposed allocation and controller

Andrew Spiteri separately authorized this unchanged proposal after exact plan/task
approval, and native allocation succeeded. Its original limits and reasons remain
below as the decision record. Original authority:
Andrew Spiteri. Controller: Codex. New owner: `CIS-0003/WORK-001`.

| Limit | Containing change | Sole task |
| --- | --- | --- |
| Active time ceiling | 3,600 seconds | 3,600 seconds, fixed/no redistribution |
| Logical attempts | 3 | 3 |
| Protected restoration time | 180 seconds included | 180 seconds included |

Both actual OS lanes share this single allowance. Join the existing storage
ceiling `CISLIB-CIS-0001-STORAGE-2026-11-10`, retaining 6,442,450,944 bytes total
including existing output and 67,108,864 restoration bytes. Use the
[fifteen explicit roots and storage rules](proposal.md#proposed-finite-allocation-and-controller):
`.cis/local` is ordinary; the fourteen listed bin/obj paths are count-only scratch.
Native private scratch is allowed. No new ceiling, historical cleanup delegation
or engineering-quality policy is adopted. Unknown historical preparation is
explicitly acknowledged; no command/profile/platform resets consumption or attempts.

Windows CIS accounts admitted local commands. Ubuntu uses bounded externally
supervised SSH with an inspected 2 GiB task-directory limit, inside this same
allowance. Limit its remote lane to 1,200 seconds; verify actual pinned tools,
record elapsed/output and clean disposable copies. CIS does not enforce remote
containment/storage or transport its ledger. Do not copy/reset authoritative
accounting or claim cross-host enforcement.

Exact request: `.cis/local/testing/logging-example-package-integration/allocation-proposed.json`.
SHA-256 `74a415e48ae66aac05c407e48b8ae99acd1dd01f210bfccf9e127eb9686bfafc`.
It was prepared against revision `8351035b241f4939ace19b3cc8d71d81:106`.
Refresh only that revision token for legitimate intervening work; preserve all
reviewed limits, roots, flags, actors and decision references. Read current
remaining time/attempts and shared storage capacity before actual execution.

Decisions: `CISLIB-CIS-0003-ALLOCATION-2026-11-13` and
`CISLIB-CIS-0003-STORAGE-2026-11-13` (joining the existing ceiling).

Proposed plan-approval reason: "Approve the CIS-0003 plan and WORK-001 contract
for the bounded ConsoleHuman Logging example and real package-consumer integration,
preserving accepted product decisions, completed CIS-0001/CIS-0002 results and
existing release obligations."

Proposed allocation reason: "Authorize CIS-0003/WORK-001 with Andrew Spiteri as
original authority and Codex as controller, the exact proposed fixed time/attempt/
restoration limits and fifteen roots under the existing shared storage ceiling,
acknowledging historical unknown effort and the stated external SSH accounting
and storage limitation."

## Tasks for approval

| Task | Area | Complexity | Depends on | Requirements | Repositories | Status |
| --- | --- | --- | --- | --- | --- | --- |
| [WORK-001 — Address implementation impact: Program.cs](agent-tasks/WORK-001.md) | implementation | medium | — | — | — | InProgress |

<!-- cis:execution-manifest
| ID | Category | Complexity | Parent | Task document | Title | Requirement IDs | Impact IDs | Depends on | Acceptance criteria | Validation | Status | Task type | Type version | Approval gate | Targets | Frontend type | Decision IDs | Negative criteria | Required outputs | Story ID | Instance key | Work owner | Verification obligations |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| WORK-001 | implementation | medium |  | agent-tasks/WORK-001.md | Address implementation impact: Program.cs |  | IMPACT-C3273A4304 |  | The accepted impact IMPACT-C3273A4304 is satisfied without unrecorded scope expansion. | Build the affected component and run its connected deterministic tests. | InProgress | core.legacy | 1.0 | none |  | not-applicable | json64:W10= | json64:WyJEbyBub3QgZXhwYW5kIHRoZSBhY2NlcHRlZCBjaGFuZ2UvaW1wYWN0IHNjb3BlIG9yIGJ5cGFzcyBwcm9kdWN0LCBwbGFuLCBkZXNpZ24sIGFsbG9jYXRpb24gb3IgY29tcGxldGlvbiBhdXRob3JpdHkuIl0= | json64:WyJUaGUgYm91bmRlZCBjaGFuZ2UgYW5kIHJlcHJvZHVjaWJsZSBldmlkZW5jZSBmb3IgdGhlIGFjY2VwdGVkIGltcGFjdC4iXQ== | json64:bnVsbA== | json64:ImFkLWhvYy1pbXBhY3Q6SU1QQUNULUMzMjczQTQzMDQi | json64:bnVsbA== | json64:W3siSWQiOiJ0YXNrLWNvbnRyYWN0IiwiQ3JpdGVyaWEiOlsiVGhlIGFjY2VwdGVkIGltcGFjdCBJTVBBQ1QtQzMyNzNBNDMwNCBpcyBzYXRpc2ZpZWQgd2l0aG91dCB1bnJlY29yZGVkIHNjb3BlIGV4cGFuc2lvbi4iXSwiRGVzY3JpcHRpb24iOiJCdWlsZCB0aGUgYWZmZWN0ZWQgY29tcG9uZW50IGFuZCBydW4gaXRzIGNvbm5lY3RlZCBkZXRlcm1pbmlzdGljIHRlc3RzLiIsIkV2aWRlbmNlUm91dGUiOiJyZXZpZXciLCJFdmlkZW5jZUV4cGVjdGF0aW9ucyI6WyJCdWlsZCB0aGUgYWZmZWN0ZWQgY29tcG9uZW50IGFuZCBydW4gaXRzIGNvbm5lY3RlZCBkZXRlcm1pbmlzdGljIHRlc3RzLiJdLCJTaGFyZWRHYXRlSWRzIjpbXSwiQmFzaXNSZWZlcmVuY2VzIjpbXX1d |
cis:execution-manifest -->
