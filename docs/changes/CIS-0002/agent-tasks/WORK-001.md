---
title: "WORK-001 Address implementation impact: ShippingBuildInventoryTests.cs"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-001
task_type: core.legacy
task_type_version: 1.0
parent_id: none
category: implementation
complexity: medium
requirement_ids: []
impact_ids: ["IMPACT-16713BDBE2"]
depends_on: []
approval_gate: none
frontend_type: not-applicable
targets: []
decision_ids: []
instance_key: "ad-hoc-impact:IMPACT-16713BDBE2"
work_owner_task_id: null
verification_obligations: [{"Id":"task-contract","Criteria":["The accepted impact IMPACT-16713BDBE2 is satisfied without unrecorded scope expansion."],"Description":"Build the affected component and run its connected deterministic tests.","EvidenceRoute":"review","EvidenceExpectations":["Build the affected component and run its connected deterministic tests."],"SharedGateIds":[],"BasisReferences":[]}]
ad_hoc_impact_id: "IMPACT-16713BDBE2"
ad_hoc_impact_basis: "sha256:85175af9f8734a3862330cf189da1ae7bca393c87b5a2fa0bfca1946d38f4cbc"
ad_hoc_task_path: "agent-tasks/WORK-001.md"
ad_hoc_acceptance_criteria: "The accepted impact IMPACT-16713BDBE2 is satisfied without unrecorded scope expansion."
ad_hoc_validation: "Build the affected component and run its connected deterministic tests."
authority: human-approved-plan
---

# WORK-001: Address implementation impact: ShippingBuildInventoryTests.cs

## Objective

Complete the actual CIS.Core package-consumer path for the existing Console and
Web examples: clean first restore through the versioned SDK, normal build/folder
publish, and preparation of the fixed type catalog before application/container
construction. Reuse working implementation and existing examples; add the missing
focused native integration coverage and fix only demonstrated in-scope defects.

## Required changes

- Address the accepted implementation impact `IMPACT-16713BDBE2`: ShippingBuildInventoryTests.cs.
- Keep the work bounded to `cislib::source-file::implementations/csharp/tests/CIS.Core.ArchitectureTests/ShippingBuildInventoryTests.cs` and the reviewed change outcome: Console and ASP.NET Core applications consume one current CIS.Core development package through its versioned SDK, restore/build/folder-publish normally, and prepare a complete fixed type catalog before application or container construction; verify the scoped native package-consumer checks on pinned Windows x64 and Ubuntu x64 without reopening CIS-0001/WORK-001 or broader release qualification.
- This root anchors the existing inventory coverage; the implementation scope is the package SDK/targets, Console/Web examples and focused tests below, rather than a rewrite of that source-helper test. Add package-consumer checks and one small shared fixture to the existing native IntegrationTests project, using bounded ProcessRunner and disposable directories.
- Build and pack one current development candidate. Reuse its unchanged bytes across related checks. Transform clean copies of Console/Web/Contracts/Unused/ Shared into consumers with the exact versioned CIS.Core SDK and enabled Type Manager; remove the Core ProjectReference, source target import and broad RuntimeIdentifiers list. Keep sample project references and supply explicit isolated pinned properties, local feed/cache and repository-import boundaries. No separate Core PackageReference or warm assets may mask first restore.
- Reuse the example verification modes for preparation order, unused dependency discovery, fixed membership and Web root/scoped manager/query identity. Exercise build output, folder publish and matching no-build publish, incremental manifest preservation, one broken selected assembly and ordinary non-enabled Core use.
- Keep the helper inside the single package's build assets and outside runtime deployment. Fix SDK/targets/runtime only for a demonstrated integration defect. Correct the Console's inaccurate ASP.NET shared-framework comment and document the working consumer XML and normal commands in the existing READMEs.
- Use a separate scoped CIS profile for the affected package checks. Run on the actual supported Windows and Ubuntu environments within the approved owner and limits; preserve valid unchanged results. Retain compact native/candidate/source and per-platform outcomes, clean disposable consumers/caches and obtain focused independent review before completion. Include the proportionate implementation assessment in that review; do not add another framework to verify this fixture.

## Required outputs

- The bounded change and reproducible evidence for the accepted impact.
- Maintained native package-consumer tests, any necessary focused fix and updated package/example instructions. Completion evidence records the candidate identity, actual platform executions, reuse, work settlement and any precise open item.

## Constraints and exclusions

- Do not expand the accepted change/impact scope or bypass product, plan, design, allocation or completion authority.
- Preserve CIS-0001/WORK-001 Complete, its accepted report/current-result metadata, approvals, allocation, consumed time and both counted attempts. Its ledger change remains unchanged when the new owner is added; no adoption test replay is allowed.
- Keep existing native MSTest/VSTest projects, meaningful assertions, fixture support, Stryker routes, public contracts, defaults and single-package design.
- Do not add a runner, replay engine, evidence/archive framework, new module or integration platform. No package publication, main merge, coverage/mutation or broad performance/release campaign is authorized here.
- Supported scope is framework-dependent, untrimmed net10.0 on Windows 11 x64 and Ubuntu 24.04 x64, SDK 10.0.401 and .NET/ASP.NET Core runtime 10.0.12. Visual Studio/MSBuild.exe, PublishUrl, AOT, trimming, single-file, self-contained, plugins and custom load contexts remain outside this task.

## Context and evidence

- Accepted impact `IMPACT-16713BDBE2`: `cislib::source-file::implementations/csharp/tests/CIS.Core.ArchitectureTests/ShippingBuildInventoryTests.cs`.
- Impact evidence: implementations/csharp/tests/CIS.Core.ArchitectureTests/ShippingBuildInventoryTests.cs:file=implementations/csharp/tests/CIS.Core.ArchitectureTests/ShippingBuildInventoryTests.cs
- [Reviewed task scope and allocation proposal](../proposal.md). Existing package SDK/helper/runtime already implement the intended path; current tests do not restore that packaged SDK and run these examples as clean package consumers.
- This is medium scope because it composes existing assets across restore, build, publish and two actual platforms. It introduces no new product lifecycle or public API; exact native prerequisites and packaging behavior need verification.

## Dependencies and approval gates

- No task dependencies.
- The current plan requires actual approval; execution requires an eligible task and applicable finite work/storage authority.
- Andrew Spiteri approved the exact plan and separately authorized its finite allocation on 12 November 2026; CIS recorded both and transitioned this task to InProgress. Existing foundations and the accepted impact were reused. Controller/root ownership and external SSH limits remain as stated in [the plan](../plan.md#proposed-allocation-and-controller).

## Acceptance criteria

- [x] The accepted impact IMPACT-16713BDBE2 is satisfied without unrecorded scope expansion.
- [x] Clean first restore/build uses the actual candidate's exact versioned SDK, no source Core reference/import, duplicate Core reference, cached Core or old assets, and the early selected native RID. Inventory includes the deployed sample implementation assembly that the host has not touched.
- [x] Preparation completes before Console APPLICATION_READY and Web builder/container creation. Root and scoped consumers share the completed ITypeManager and cached query; fixed membership and no discovery-time object activation hold.
- [x] Build output, ordinary folder publish and matching publish --no-build all run successfully; unchanged manifests retain bytes and modification time.
- [x] A missing selected assembly produces the existing useful failure before readiness/container construction, without publishing a partial successful catalog.
- [x] Non-enabled ordinary package use works; only one Core runtime dependency is supplied, the build helper stays outside runtime output and public/default/package boundaries remain intact.
- [x] Actual Windows and Ubuntu checks use the pinned supported toolchain and shared candidate identity. Any unavailable execution stays open, not inferred.
- [x] Focused independent review has no required finding, current native evidence is retained and this owner's work/storage has settled. Record any remaining acceptance item honestly; no broader qualification claim is made.

## Targeted validation

- [x] Build the affected component and run its connected deterministic tests.
- [x] Before native execution, approve the exact plan and allocation, transition this task through supported lifecycle controls, inspect finite remaining time/ attempts/storage and verify the actual SDK/shared frameworks. Do not use the completed Healthcheck owner or profile.
- [x] Build only IntegrationTests and connected project dependencies for the new package checks. Run selected native checks through the scoped CIS unit profile; reuse compatible helper/runtime results and broaden only for a concrete fix.
- [x] Use the same candidate across both actual OS lanes and the related host modes; repack only after shipped bytes change. Keep matching restore/RID/build inputs, never share an intermediate directory across different RID builds.
- [x] Perform Ubuntu CLI checks through existing SSH access with bounded commands, actual duration/output observation and the declared external-host limitation; do not copy/reset the authoritative ledger or invent remote CIS containment.
- [x] Record actual compact results, review and settlement in this task and ../verification.md. Read-only plan/status/verification may demonstrate current reuse; do not rerun unchanged work solely to prove reuse.

## Completion evidence

| Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- |
| Accepted impact and package boundary | New native `TypeManagerPackageConsumerTests` and shared fixture; existing Console/Web/Contracts/Unused/Shared | Satisfied on both actual platforms | Four cases cover clean versioned-SDK first restore/build, both publish modes, preparation/identity/fixed membership, broken deployment and non-enabled use. Existing product implementation was reused. |
| Windows | Scoped CIS unit profile; `file-754f922ff7fc05e162f8-unit.xml` | 4 passed, 0 failed, 0 skipped | Actual Windows 11 x64, SDK 10.0.401 and NET/ASP.NET Core 10.0.12; graph selects only the four cases. |
| Ubuntu | `linux/type-manager-package-linux.trx` and `linux-outcome.json` under the scoped local testing directory | 4 passed, 0 failed, 0 skipped | Actual Ubuntu 24.04 x64, same pinned toolchain and unchanged candidate; remote disposable source/build/cache directory removed after collection. |
| Candidate and detailed outcome | [Verification record](../verification.md) | Same package reused across platforms | Candidate SHA-256 `de794bafcee21b8a667bb3558e45242fddbab3f8eb7ca3b04974c5037628d93e`; source and report identities, measured costs, profile correction and review are recorded there. |
| Current reuse and preserved adoption | Read-only unit plan/status/verification after documentation updates; original adoption artifact hashes | Verified with zero pending batches; no additional native launch | CIS-0001 remains Complete with its unchanged report and accounting. |
| Review and settlement | Independent focused review; native `agent work progress` close at revision `:106` | No required finding; logical attempt closed | 602,214 ms native consumption, zero reserved, one of three attempts used, no recovery hold. Separately observed Ubuntu work stays within the shared limit. Temporary consumers and remote job were removed. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=362; failed=49; possibleTokenSavings=123; ledgerDigest=sha256:2961851e19b156a2b999e4d5f9dbcc8d02c8e74ad4308f94e69e9cdbbd723924 |

## Deferrals and residual risk

- No acceptance item is deferred. Full release qualification and the excluded deployment profiles remain outside this completed integration task.
- Ubuntu used the existing pinned SDK/runtime installation; no installation or toolchain copy was needed.
- CIS accounts its admitted local operations, not independent remote process/ filesystem enforcement. External SSH observations must be explicit and bounded.
- No new volume-sensitive workload is introduced; conventional example assemblies and inventories exercise the accepted package/startup contract. Existing broader product and release obligations remain unchanged and outside this task.
