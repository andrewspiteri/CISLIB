---
title: "CIS-0001 delivery plan"
type: delivery-plan
status: Draft
change_id: CIS-0001
graph_build_id: "sha256:4bfca6477c6d1b545bf66c31574359d1d49583ba15e1841dd15c27986276e163"
authority: human-approved
---

# Delivery plan

This is the human approval view. Review scope, task boundaries, sequencing, complexity, repositories, and approval gates.
Detailed execution constraints, validation procedures, provenance, and evidence fields remain in the linked agent task documents.

## Approval summary

This Draft plan has one actual durable owner: `CIS-0001/WORK-001`, bound to accepted finding `IMPACT-C74657D845`. Its generated title/category describe the existing compiler-bound test symbol; the authored task limits work to CIS configuration, accounting association and one native verification of the unchanged two-case test. No implementation or assertion edit is requested. The two called-method findings are rejected as context, not removed from verification.

Review this plan together with [the complete WORK-001 contract](agent-tasks/WORK-001.md) and [the change acceptance criteria](proposal.md#acceptance-criteria). The existing local profile builds the selected project/dependencies and runs only the selected class through CIS with `--layer unit`, no `--force`, exact canonical ownership and actual finite authority. Acceptance requires a real admitted nonempty TRX with 2 passed / 0 failed / 0 skipped, settled accounting, current unchanged-result status with zero pending unit batches and accounted owned output. No second test run solely for reuse is authorized.

The single task remains conservatively medium because native admission, finite accounting and settlement must be verified together. It uses the existing profile and corrected CIS controls directly. No feature specification, UI chain, new runtime behavior, additional workstream or release campaign is introduced. Native requirement coverage is zero because this ad hoc verification does not create feature requirements; accepted product foundations remain binding.

Plan approval is still pending and grants no resource allocation. The unchanged proposed ceilings are containing change 1,200 active seconds / 3 attempts / 120 restoration seconds; WORK-001 900 active seconds / 2 attempts / 120 restoration seconds; 6 GiB total owned storage including existing declared output / 64 MiB reserve. A separate actual allocation/controller decision and native finite allocation are required before admission. Native metadata labels do not substitute for those decisions.

- Requirements covered: 0.
- Work items: 1 (1 actionable).
- Repositories: —.
- UI review: not required.
- Public-endpoint cache and database-isolation policy: not applicable.

## Approval gates

- All impact findings reviewed; no deferred impact hidden from scope.
- All required decisions resolved.
- Human authority recorded through `cis plan approve` or carried from a current approved feature by `cis plan derive`.

## Tasks for approval

| Task | Area | Complexity | Depends on | Requirements | Repositories | Status |
| --- | --- | --- | --- | --- | --- | --- |
| [WORK-001 — Address implementation impact: CIS.Tests.UnitTests.Healthcheck.HealthDiagnosticRuntimeAcceptanceBoundaryTests.ValidCallerIdentifiersAndRuntimeExceptionProjectionProduceADetachedDiagnostic(bool)](agent-tasks/WORK-001.md) | implementation | medium | — | — | — | Draft |

<!-- cis:execution-manifest
| ID | Category | Complexity | Parent | Task document | Title | Requirement IDs | Impact IDs | Depends on | Acceptance criteria | Validation | Status | Task type | Type version | Approval gate | Targets | Frontend type | Decision IDs | Negative criteria | Required outputs | Story ID | Instance key | Work owner | Verification obligations |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| WORK-001 | implementation | medium |  | agent-tasks/WORK-001.md | Address implementation impact: CIS.Tests.UnitTests.Healthcheck.HealthDiagnosticRuntimeAcceptanceBoundaryTests.ValidCallerIdentifiersAndRuntimeExceptionProjectionProduceADetachedDiagnostic(bool) |  | IMPACT-C74657D845 |  | The accepted impact IMPACT-C74657D845 is satisfied without unrecorded scope expansion. | Build the affected component and run its connected deterministic tests. | Draft | core.legacy | 1.0 | none |  | not-applicable | json64:W10= | json64:WyJEbyBub3QgZXhwYW5kIHRoZSBhY2NlcHRlZCBjaGFuZ2UvaW1wYWN0IHNjb3BlIG9yIGJ5cGFzcyBwcm9kdWN0LCBwbGFuLCBkZXNpZ24sIGFsbG9jYXRpb24gb3IgY29tcGxldGlvbiBhdXRob3JpdHkuIl0= | json64:WyJUaGUgYm91bmRlZCBjaGFuZ2UgYW5kIHJlcHJvZHVjaWJsZSBldmlkZW5jZSBmb3IgdGhlIGFjY2VwdGVkIGltcGFjdC4iXQ== | json64:bnVsbA== | json64:ImFkLWhvYy1pbXBhY3Q6SU1QQUNULUM3NDY1N0Q4NDUi | json64:bnVsbA== | json64:W3siSWQiOiJ0YXNrLWNvbnRyYWN0IiwiQ3JpdGVyaWEiOlsiVGhlIGFjY2VwdGVkIGltcGFjdCBJTVBBQ1QtQzc0NjU3RDg0NSBpcyBzYXRpc2ZpZWQgd2l0aG91dCB1bnJlY29yZGVkIHNjb3BlIGV4cGFuc2lvbi4iXSwiRGVzY3JpcHRpb24iOiJCdWlsZCB0aGUgYWZmZWN0ZWQgY29tcG9uZW50IGFuZCBydW4gaXRzIGNvbm5lY3RlZCBkZXRlcm1pbmlzdGljIHRlc3RzLiIsIkV2aWRlbmNlUm91dGUiOiJyZXZpZXciLCJFdmlkZW5jZUV4cGVjdGF0aW9ucyI6WyJCdWlsZCB0aGUgYWZmZWN0ZWQgY29tcG9uZW50IGFuZCBydW4gaXRzIGNvbm5lY3RlZCBkZXRlcm1pbmlzdGljIHRlc3RzLiJdLCJTaGFyZWRHYXRlSWRzIjpbXSwiQmFzaXNSZWZlcmVuY2VzIjpbXX1d |
cis:execution-manifest -->
