---
title: "RM-09 Management Portal"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0029
outcome: "Operators inspect authorized applications and deliberately publish, assign and recover management changes through an approved Next.js portal design and real delivered Management APIs."
baseline_kind: git
baseline: "6c70cee807f12caddf40cd1b443e4a20045d35aa"
graph_build_id: "sha256:040859e0abc408ccaa6ef0de23e6f2b2872e29d6b055e0d282b2fc384e1a4df5"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:8d6e04b8f096f94d0449061be0f8c8ee97785e6c49202e30a336331b419ad3de"
authority: derived-outcome-authorization
---

# CIS-0029: RM-09 Management Portal

## Outcome

The original full RM09 request includes inspection, publication, assignment and
recovery. Andrew Spiteri's later step-by-step R02–R06 decisions narrow this
implementation to the approved desktop read-only Applications/four-tab/detail
journey. Full Job history and Task/Event views depend on P6/P7/P8 RM-10 contracts;
production shows unavailable states until those services exist. The original
source and full feature adapter remain preserved without a full-completion claim.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- Deliver the current read-only desktop slice and its concrete acceptance in
  [implementation-plan.md](implementation-plan.md). Keep the original twenty
  scenarios in [the feature adapter](feature-specification.md) and
  [reconciliation.md](reconciliation.md); deferred scenarios are not delivered.
- Obtain explicit approval of the visible, versioned backoffice design and reconcile the historical no-UI applicability through normal CIS governance before production work. The [bounded draft plan](implementation-plan.md) retains this dependency.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `USER-RM09-DELEGATED-WORK-AND-R02-R06-DESKTOP-APPROVAL-20261125`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Deliver the approved R06 desktop read-only Management portal: authorized filterable Applications, Instances/Configuration/Capabilities/Scheduled jobs, quick details and Job full details, with honest unavailable RM-10 history/task/event states.

- `intake-design`: Preserve RM09 source and R01–R06 evidence, record exact approved desktop direction and current-slice bounded contracts; retain all intake/design costs. Paths: `.github`, `docs`, `planning`, `skills`.
- `integration`: Focused tests/review and runnable portal/service guidance within implementation tasks, reusing current native/provider fixtures. No new test framework or broad qualification campaign. Paths: `.artifacts`, `.cis/local`, `.github`, `conformance`, `contracts`, `docs`, `implementations`, `planning`, `skills`.
- `management-prerequisites`: Additive human/delegated native identity and bounded authorized application/environment and exact application Configuration-definition discovery through current service, backend and portable client boundaries, including tests/docs. Paths: `conformance`, `contracts`, `docs`, `implementations/csharp`, `implementations/typescript`, `planning`.
- `portal`: Independent Next.js/TypeScript host, maintained OIDC code/PKCE session integration and approved read-only desktop lists/tabs/panels/Job details. History/task/event views honestly unavailable without RM-10; no production synthetic data. Paths: `.github`, `docs`, `implementations/typescript`, `planning`, `skills`.

- Classify every UI surface as backoffice; preserve portable clients, independent Next.js host, native management authority and existing original transaction/recovery/Audit boundaries.
- Current user explicitly authorizes this next RM09 work and pauses only for UI screenshot-preview approval. Codex selects finite limits and organizes tasks; this is not a claim of a separate human numerical-profile review.
- Existing physical6GiB consumer ceiling and64MiB reserve remain one containing limit across old and new files; the existing2GiB Docker allowance stays inside it. No existing database access, credential access, trust-store change or external deployment is authorized.
- Later explicit user step-by-step steering narrows implementation to the approved desktop inspection slice. Metadata writes/recovery, configuration publication/assignment/adoption, mobile and RM-10 scheduler/history runtime remain deferred; preserve original full specification without claiming full completion.
- Local token/protocol tests cannot be reported as a live Entra deployment. Dependency/runtime pins must be supported and checked before use; no production dependency installation is needed for the design preview.
- No push, merge or release of RM09 work is included. No new generic framework, test harness, graph engine, design-management platform or tooling campaign.
- Production implementation waits for the exact persisted CIS R06 design approval and normal scope/task/finite-owner reconciliation. A materially changed journey requires approval of that revised design; routine workflow-preserving fixes do not. Write workflows remain deferred.
- Retain protected historical README/AGENTS/profile inputs unless a necessary explicitly scoped implementation change supersedes them; preserve original hashes/reports and do not reopen completed test owners.
- Use one shared allocation across all agents, already-performed intake, implementation, tests, documentation, design and review. Preserve all previous changes, consumed costs, reports, completed owners and protected launch settings.

The total is 172,800 active seconds and 48 logical attempts, including 3,600 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts` (count only), `.cis/local`, `implementations/csharp` (count only), `implementations/typescript` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
