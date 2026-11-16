---
title: "CIS-0002 delivery plan"
type: delivery-plan
status: Approved
change_id: CIS-0002
graph_build_id: "sha256:927e175440ceb1a56406399183b692c375ced7129e43c773a43d8f3faf5aff95"
approved_by: "Andrew Spiteri"
approval_rationale: "Approve the CIS-0002 plan and WORK-001 contract for the bounded Type Manager package-consumer integration described here, preserving the accepted CISLIB baselines, completed CIS-0001 result and existing release obligations."
authority: human-approved
---

# Delivery plan

This is the human approval view. Review scope, task boundaries, sequencing, complexity, repositories, and approval gates.
Detailed execution constraints, validation procedures, provenance, and evidence fields remain in the linked agent task documents.

## Approval summary

One product task completes the maintained Type Manager package-consumer path using
the existing SDK/helper/runtime and Console/Web examples. The accepted impact is
an entry point into existing inventory coverage. The [full WORK-001 contract](agent-tasks/WORK-001.md)
carries implementation, focused native tests, documentation, independent review
and completion within that one medium owner. The [proposal](proposal.md) records
the accepted product scope. Nothing reopens CIS-0001/WORK-001.

Andrew Spiteri accepted IMPACT-16713BDBE2 and explicitly approved this exact plan
and WORK-001 contract on 12 November 2026. The separately authorized allocation
was then applied through CIS, advancing the global ledger from revision 39 to 40
without changing the completed adoption owner's allocation or accounting.
WORK-001 is Complete through the supported lifecycle. The [verification record](verification.md)
records the actual Windows/Ubuntu passes, current reuse, review and settled work.
Plan/allocation approval itself did not certify execution or release readiness.

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

The following is the reviewed proposal that Andrew Spiteri separately authorized
on 12 November 2026. Native allocation succeeded after plan approval, using the
exact request and reason retained below. The original proposal wording preserves
the decision's scope; it is no longer an outstanding approval question.

Original authority: Andrew Spiteri. Controller: Codex. The new task is
`CIS-0002/WORK-001`, distinct from the completed adoption owner.

| Limit | Containing change | Sole task |
| --- | --- | --- |
| Active time ceiling | 7,200 seconds | 7,200 seconds, fixed/no redistribution |
| Logical attempts | 3 | 3 |
| Protected restoration time | 180 seconds included in the ceiling | 180 seconds included in the ceiling |

Join existing storage ceiling `CISLIB-CIS-0001-STORAGE-2026-11-10`:
6,442,450,944 bytes total and 67,108,864 restoration bytes, including existing
owned output. Native private scratch is permitted. No historical cleanup
controller is delegated. Existing CIS-0001 policy, required claims and accounting
stay unchanged. The additional roots do not create another six GiB allowance.

Exact new policy roots, relative to this registered CISLIB checkout:

```text
.cis/local
implementations/csharp/src/CIS.Core/bin
implementations/csharp/src/CIS.Core/obj
implementations/csharp/eng/CIS.TypeManager.Build/bin
implementations/csharp/eng/CIS.TypeManager.Build/obj
implementations/csharp/tests/CIS.Core.TestSupport/bin
implementations/csharp/tests/CIS.Core.TestSupport/obj
implementations/csharp/tests/CIS.Core.IntegrationTests/bin
implementations/csharp/tests/CIS.Core.IntegrationTests/obj
implementations/csharp/tests/fixtures/DiscoveryHost/bin
implementations/csharp/tests/fixtures/DiscoveryHost/obj
implementations/csharp/tests/fixtures/HealthcheckHttpHost/bin
implementations/csharp/tests/fixtures/HealthcheckHttpHost/obj
implementations/csharp/tools/CisHealthProbe/bin
implementations/csharp/tools/CisHealthProbe/obj
```

Only `.cis/local` is an ordinary CIS root; the fourteen bin/obj roots are count-only
scratch. Candidate/feed/results stay under scoped testing output; temporary
consumers use native-owned private temporary storage. Read-only inspection at
revision `8351035b241f4939ace19b3cc8d71d81:39` measured 4,681,962,202 existing owned
bytes. The eight additional roots contain 56,028,066 bytes, leaving a projected
1,637,351,812 ordinary bytes after the restoration reserve, before new growth.
Inspect current capacity again before execution; these measurements grant no write.

Both OS lanes share one 7,200-second, three-attempt allowance with one included
180-second restoration reserve; there is no separate allowance per platform.
Windows CIS accounts admitted local operations. Its existing tooling provides no
cross-host accounting/storage transport: actual Ubuntu CLI execution over existing
SSH is externally supervised and uses bounded commands. Its disposable `/data`
task directory has a 2 GiB limit for all regular files beneath it, including any
new toolchain, package/cache, consumer and build/publish output stored there.
Inspect bytes before expansion and after commands; stop expansion and clean
disposable output if capacity is insufficient. This is controller inspection, not
an enforced filesystem quota. Record actual remote duration/output separately.
Do not duplicate/reset the ledger or claim remote containment/storage enforcement.
Install or locate SDK 10.0.401 and .NET/ASP.NET Core 10.0.12 before an Ubuntu pass;
the previously checked CIS build installation contains 10.0.403/10.0.14.

Allocation decision: `CISLIB-CIS-0002-ALLOCATION-2026-11-12`. The separate citation
`CISLIB-CIS-0002-STORAGE-2026-11-12` records the new decision to join the existing
storage ceiling, rather than replacing its reference. Explicitly acknowledge
unmeasured historical preparation as unknown. New runs/profiles/platforms do not
reset time or attempts. Apply only after plan approval, using a fresh ledger
revision. No engineering-quality policy or release qualification is adopted.

The approved native request is retained at ignored
`.cis/local/testing/type-manager-package-integration/allocation-proposed.json`.
SHA-256: `77a56145b477d42b33d27b44e85a7226e423b127a8fc69c33301fe4e39ae4406`.
It bound the fifteen listed roots to this registered checkout and was applied
against the inspected `:39` revision. For any authorized later use, refresh only
the expected-revision token if legitimate intervening
work changes it; preserve all authorized limits, roots, actor/controller and decision
references. CIS has no allocation preview operation; the request was checked against
the installed schema, storage rules and current read-only inventory. Do not apply
it as a way to obtain validation before actual allocation authority.

Proposed plan-approval reason: "Approve the CIS-0002 plan and WORK-001 contract for
the bounded Type Manager package-consumer integration described here, preserving
the accepted CISLIB baselines, completed CIS-0001 result and existing release obligations."

Proposed allocation reason: "Authorize CIS-0002/WORK-001 with Andrew Spiteri as
original authority and Codex as controller, the exact proposed fixed time/attempt/
restoration limits and listed roots under the existing shared storage ceiling,
acknowledging historical unknown effort and the stated external SSH accounting
and storage limitation."

## Tasks for approval

| Task | Area | Complexity | Depends on | Requirements | Repositories | Status |
| --- | --- | --- | --- | --- | --- | --- |
| [WORK-001 — Address implementation impact: ShippingBuildInventoryTests.cs](agent-tasks/WORK-001.md) | implementation | medium | — | — | — | Complete |

<!-- cis:execution-manifest
| ID | Category | Complexity | Parent | Task document | Title | Requirement IDs | Impact IDs | Depends on | Acceptance criteria | Validation | Status | Task type | Type version | Approval gate | Targets | Frontend type | Decision IDs | Negative criteria | Required outputs | Story ID | Instance key | Work owner | Verification obligations |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| WORK-001 | implementation | medium |  | agent-tasks/WORK-001.md | Address implementation impact: ShippingBuildInventoryTests.cs |  | IMPACT-16713BDBE2 |  | The accepted impact IMPACT-16713BDBE2 is satisfied without unrecorded scope expansion. | Build the affected component and run its connected deterministic tests. | Complete | core.legacy | 1.0 | none |  | not-applicable | json64:W10= | json64:WyJEbyBub3QgZXhwYW5kIHRoZSBhY2NlcHRlZCBjaGFuZ2UvaW1wYWN0IHNjb3BlIG9yIGJ5cGFzcyBwcm9kdWN0LCBwbGFuLCBkZXNpZ24sIGFsbG9jYXRpb24gb3IgY29tcGxldGlvbiBhdXRob3JpdHkuIl0= | json64:WyJUaGUgYm91bmRlZCBjaGFuZ2UgYW5kIHJlcHJvZHVjaWJsZSBldmlkZW5jZSBmb3IgdGhlIGFjY2VwdGVkIGltcGFjdC4iXQ== | json64:bnVsbA== | json64:ImFkLWhvYy1pbXBhY3Q6SU1QQUNULTE2NzEzQkRCRTIi | json64:bnVsbA== | json64:W3siSWQiOiJ0YXNrLWNvbnRyYWN0IiwiQ3JpdGVyaWEiOlsiVGhlIGFjY2VwdGVkIGltcGFjdCBJTVBBQ1QtMTY3MTNCREJFMiBpcyBzYXRpc2ZpZWQgd2l0aG91dCB1bnJlY29yZGVkIHNjb3BlIGV4cGFuc2lvbi4iXSwiRGVzY3JpcHRpb24iOiJCdWlsZCB0aGUgYWZmZWN0ZWQgY29tcG9uZW50IGFuZCBydW4gaXRzIGNvbm5lY3RlZCBkZXRlcm1pbmlzdGljIHRlc3RzLiIsIkV2aWRlbmNlUm91dGUiOiJyZXZpZXciLCJFdmlkZW5jZUV4cGVjdGF0aW9ucyI6WyJCdWlsZCB0aGUgYWZmZWN0ZWQgY29tcG9uZW50IGFuZCBydW4gaXRzIGNvbm5lY3RlZCBkZXRlcm1pbmlzdGljIHRlc3RzLiJdLCJTaGFyZWRHYXRlSWRzIjpbXSwiQmFzaXNSZWZlcmVuY2VzIjpbXX1d |
cis:execution-manifest -->
