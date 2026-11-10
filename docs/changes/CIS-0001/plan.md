---
title: "CIS-0001 delivery plan"
type: delivery-plan
status: Approved
change_id: CIS-0001
graph_build_id: "sha256:4bfca6477c6d1b545bf66c31574359d1d49583ba15e1841dd15c27986276e163"
approved_by: "Andrew Spiteri"
approval_rationale: "Approve the bounded CIS-0001 plan and WORK-001 contract for one governed unit-only verification of the unchanged HealthDiagnosticRuntimeAcceptanceBoundaryTests, preserving product behavior and existing release scope."
authority: human-approved
---

# Delivery plan

This is the human approval view. Review scope, task boundaries, sequencing, complexity, repositories, and approval gates.
Detailed execution constraints, validation procedures, provenance, and evidence fields remain in the linked agent task documents.

## Approval summary

Current status at repair preparation: Andrew Spiteri approved this unchanged plan and task contract on 10 November 2026 at 15:22:33 UTC. The separate finite allocation was subsequently recorded. WORK-001 remains Blocked while its original native execution's ownership and settlement are unresolved; no accepted native result is claimed here. This repair restores the genuine approval attribution and reviewed scope narrative without granting a new approval, changing scope or moving task state.

### Reviewed preparation summary — historical wording

The following paragraphs are restored verbatim from the plan reviewed at bfbb21d834916af5aa8e44c2f466fc55447ac19b. Their references to Draft status, pending approval/allocation and proposed ceilings describe that earlier preparation checkpoint. The current approval is recorded above and in native metadata; the current task table and execution manifest are retained below.

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
| [WORK-001 — Address implementation impact: CIS.Tests.UnitTests.Healthcheck.HealthDiagnosticRuntimeAcceptanceBoundaryTests.ValidCallerIdentifiersAndRuntimeExceptionProjectionProduceADetachedDiagnostic(bool)](agent-tasks/WORK-001.md) | implementation | medium | — | — | — | Blocked |

<!-- cis:execution-manifest
| ID | Category | Complexity | Parent | Task document | Title | Requirement IDs | Impact IDs | Depends on | Acceptance criteria | Validation | Status | Task type | Type version | Approval gate | Targets | Frontend type | Decision IDs | Negative criteria | Required outputs | Story ID | Instance key | Work owner | Verification obligations |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| WORK-001 | implementation | medium |  | agent-tasks/WORK-001.md | Address implementation impact: CIS.Tests.UnitTests.Healthcheck.HealthDiagnosticRuntimeAcceptanceBoundaryTests.ValidCallerIdentifiersAndRuntimeExceptionProjectionProduceADetachedDiagnostic(bool) |  | IMPACT-C74657D845 |  | The accepted impact IMPACT-C74657D845 is satisfied without unrecorded scope expansion. | Build the affected component and run its connected deterministic tests. | Blocked | core.legacy | 1.0 | none |  | not-applicable | json64:W10= | json64:WyJEbyBub3QgZXhwYW5kIHRoZSBhY2NlcHRlZCBjaGFuZ2UvaW1wYWN0IHNjb3BlIG9yIGJ5cGFzcyBwcm9kdWN0LCBwbGFuLCBkZXNpZ24sIGFsbG9jYXRpb24gb3IgY29tcGxldGlvbiBhdXRob3JpdHkuIiwiUHJlc2VydmUgc2hpcHBpbmcgYW5kIHRlc3Qgc291cmNlLCBhc3NlcnRpb25zLCBMb2dnaW5nL1R5cGUgTWFuYWdlci9IZWFsdGhjaGVjayBiZWhhdmlvciwgcHVibGljIGNvbnRyYWN0cywgcGFja2FnZSBsYXlvdXQsIFNESyBcdTAwNjAxMC4wLjQwMVx1MDA2MCBhbmQgLk5FVC9BU1AuTkVUIFx1MDA2MDEwLjAuMTJcdTAwNjAuIERvIG5vdCBjaGFuZ2UgaW5wdXRzIHRvIG1hbnVmYWN0dXJlIGEgcGFzcy4iLCJQcmVzZXJ2ZSBcdTAwNjAuY2lzL2xvY2FsL3Rlc3RpbmcvcHJvZmlsZXMvaGVhbHRoY2hlY2stZGlhZ25vc3RpYy11bml0Lmpzb25cdTAwNjAsIHJldmlld2VkIFNIQS0yNTYgXHUwMDYwMWMzNjA3OTk0OGYwNTQxNDY5ODMxNzUyN2M3MjU5MTE0MGYyYjQ2Y2RmZmExN2NmYjBmNmU4NmQ0ZmVkMDJlNFx1MDA2MCwgaW5jbHVkaW5nIGl0cyAxMjAtc2Vjb25kIHRpbWVvdXQsIDEyOCBNaUIgcGVyLXJ1biBjYXAgYW5kIDI1NiBNaUIgcmV0YWluZWQgY2FwLiBQcmVzZXJ2ZSB0aGUgZXhhY3QgY2xhc3MgZmlsdGVyOiBcdTAwNjBGdWxseVF1YWxpZmllZE5hbWV+Q0lTLlRlc3RzLlVuaXRUZXN0cy5IZWFsdGhjaGVjay5IZWFsdGhEaWFnbm9zdGljUnVudGltZUFjY2VwdGFuY2VCb3VuZGFyeVRlc3RzXHUwMDI2VGVzdENhdGVnb3J5IT1QZXJmb3JtYW5jZU9ic2VydmF0aW9uXHUwMDI2VGVzdENhdGVnb3J5IT1FeHRlbmRlZFN0cmVzc1x1MDA2MC4iLCJEbyBub3QgdXNlIFx1MDA2MC0tZm9yY2VcdTAwNjAgb3IgcnVuIHRlc3RzIGEgc2Vjb25kIHRpbWUgc29sZWx5IHRvIGRlbW9uc3RyYXRlIHJldXNlLiBQcmVzZXJ2ZSByZWFsIGNvbnN1bXB0aW9uLCBsb2dpY2FsIGF0dGVtcHRzLCByZXN0b3JhdGlvbiByZXNlcnZlcyBhbmQgdW5jZXJ0YWluIHJlc2VydmF0aW9uczsgbm8gbmV3IHJ1biBJRCwgcmV0cnkgb3IgYWNjb3VudGluZyBvcHRpb24gcmVzZXRzIHRoZW0uIiwiQW5kcmV3IFNwaXRlcmkgYXV0aG9yaXplZCB0aGUgZXhhY3Qgc2VwYXJhdGUgYWxsb2NhdGlvbiBwcm9wb3NhbDogY29udGFpbmluZyBjaGFuZ2UgMSwyMDAgYWN0aXZlIHNlY29uZHMgLyAzIGxvZ2ljYWwgYXR0ZW1wdHMgLyAxMjAgcmVzdG9yYXRpb24gc2Vjb25kczsgdGhpcyBmaXhlZCB0YXNrIDkwMCBhY3RpdmUgc2Vjb25kcyAvIDIgYXR0ZW1wdHMgLyAxMjAgcmVzdG9yYXRpb24gc2Vjb25kczsgNiBHaUIgdG90YWwgb3duZWQgc3RvcmFnZSBpbmNsdWRpbmcgZXhpc3RpbmcgZGVjbGFyZWQgb3V0cHV0IC8gNjQgTWlCIHJlc3RvcmF0aW9uIHJlc2VydmUuIE5hdGl2ZSBhbGxvY2F0aW9uIHJlY29yZGVkIG9yaWdpbmFsIGF1dGhvcml0eSBBbmRyZXcgU3BpdGVyaSwgY29udHJvbGxlciBDb2RleCwgdGhlIHJldmlld2VkIG93bmVkIHJvb3RzIGFuZCBkZWNpc2lvbiByZWZlcmVuY2VzLiBQcmVzZXJ2ZSBhY3R1YWwgY29uc3VtcHRpb24sIHJlc2VydmF0aW9ucyBhbmQgcmVjb3ZlcnkgaG9sZHM7IHRoaXMgZ3JhbnQgZG9lcyBub3QgZXN0YWJsaXNoIHNldHRsZW1lbnQgb3IgYSBwYXNzaW5nIHJlc3VsdC4iLCJObyBmZWF0dXJlIHBhY2ssIG5ldyBwcm9kdWN0IGZlYXR1cmUsIHByb3ZpZGVyLWFnZW50IGV4ZWN1dGlvbiwgdW5yZWxhdGVkIHdvcmtzdHJlYW0sIHF1YWxpdHktcG9saWN5IGFkb3B0aW9uLCBicm9hZCByZWdyZXNzaW9uLCBtdXRhdGlvbi9jb3ZlcmFnZSBvciBwZXJmb3JtYW5jZSBjYW1wYWlnbiwgcGFja2FnZSBwdWJsaWNhdGlvbiBvciByZWxlYXNlIHF1YWxpZmljYXRpb24gaXMgcmVxdWlyZWQgYnkgdGhpcyB0YXNrLiBFYXJsaWVyIGhpc3RvcmljYWwgcmVzdWx0cyBhbmQgY2FtcGFpZ24gYWxsb3dhbmNlcyBkbyBub3QgdHJhbnNmZXIgaGVyZS4iXQ== | json64:WyJUaGUgYm91bmRlZCBjaGFuZ2UgYW5kIHJlcHJvZHVjaWJsZSBldmlkZW5jZSBmb3IgdGhlIGFjY2VwdGVkIGltcGFjdC4iLCJSZWFsIENJUyBuYXRpdmUgZXhlY3V0aW9uL3JlcG9ydCByZWZlcmVuY2VzLCBwYXJzZWQgVFJYIGNvdW50cyBhbmQgYWRtaXR0ZWQgY3VycmVudC1pbnB1dC93b3JrLW93bmVyIGFzc29jaWF0aW9uOyBleHBlY3RlZCBjb3VudHMgYWxvbmUgYXJlIG5vdCBldmlkZW5jZS4iLCJTZXR0bGVkIGFjY291bnRpbmcgYW5kIGFzc29jaWF0ZWQgb3duZWQtc3RvcmFnZSBpbnZlbnRvcnksIHBsdXMgcmVhZC1vbmx5IHN0YXR1cyBzaG93aW5nIHRoZSBkZWNsYXJlZCB1bml0IHNsaWNlIGNvbXBsZXRlIHdpdGggemVybyBwZW5kaW5nIHVuaXQgYmF0Y2hlcy4iLCJDb25jaXNlIGFjdHVhbCBldmlkZW5jZSBhbmQgYW55IHVubWV0IGNyaXRlcmlvbiBpbiB0aGlzIHRhc2sgYW5kIFx1MDA2MC4uL3ZlcmlmaWNhdGlvbi5tZFx1MDA2MDsgZ2VuZXJhdGVkL2xvY2FsIG91dHB1dCByZW1haW5zIG91dHNpZGUgR2l0LiJd | json64:bnVsbA== | json64:ImFkLWhvYy1pbXBhY3Q6SU1QQUNULUM3NDY1N0Q4NDUi | json64:bnVsbA== | json64:W3siSWQiOiJ0YXNrLWNvbnRyYWN0IiwiQ3JpdGVyaWEiOlsiVGhlIGFjY2VwdGVkIGltcGFjdCBJTVBBQ1QtQzc0NjU3RDg0NSBpcyBzYXRpc2ZpZWQgd2l0aG91dCB1bnJlY29yZGVkIHNjb3BlIGV4cGFuc2lvbi4iXSwiRGVzY3JpcHRpb24iOiJCdWlsZCB0aGUgYWZmZWN0ZWQgY29tcG9uZW50IGFuZCBydW4gaXRzIGNvbm5lY3RlZCBkZXRlcm1pbmlzdGljIHRlc3RzLiIsIkV2aWRlbmNlUm91dGUiOiJyZXZpZXciLCJFdmlkZW5jZUV4cGVjdGF0aW9ucyI6WyJCdWlsZCB0aGUgYWZmZWN0ZWQgY29tcG9uZW50IGFuZCBydW4gaXRzIGNvbm5lY3RlZCBkZXRlcm1pbmlzdGljIHRlc3RzLiJdLCJTaGFyZWRHYXRlSWRzIjpbXSwiQmFzaXNSZWZlcmVuY2VzIjpbXX1d |
cis:execution-manifest -->
