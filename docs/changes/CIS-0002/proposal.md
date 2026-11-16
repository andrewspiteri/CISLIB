---
title: "Complete Type Manager package-consumer integration"
type: change-proposal
status: Proposed
change_id: CIS-0002
outcome: "Console and ASP.NET Core applications consume one current CIS.Core development package through its versioned SDK, restore/build/folder-publish normally, and prepare a complete fixed type catalog before application or container construction; verify the scoped native package-consumer checks on pinned Windows x64 and Ubuntu x64 without reopening CIS-0001/WORK-001 or broader release qualification."
baseline_kind: git
baseline: "5d7db88cf79e41d627bc4cb82df07d8cbd49cdf5"
graph_build_id: "sha256:927e175440ceb1a56406399183b692c375ced7129e43c773a43d8f3faf5aff95"
impact_roots: "[{\u0022Id\u0022:\u0022cislib::source-file::implementations/csharp/tests/CIS.Core.ArchitectureTests/ShippingBuildInventoryTests.cs\u0022,\u0022Kind\u0022:\u0022source-file\u0022}]"
repository_baselines: "[{\u0022RepositoryId\u0022:\u0022cislib\u0022,\u0022BaselineKind\u0022:\u0022git\u0022,\u0022Baseline\u0022:\u00225d7db88cf79e41d627bc4cb82df07d8cbd49cdf5\u0022,\u0022WorkingTree\u0022:[],\u0022CreationInputs\u0022:null}]"
authority: human-reviewed
---

# CIS-0002: Complete Type Manager package-consumer integration

## Outcome

Console and ASP.NET Core applications consume one current CIS.Core development package through its versioned SDK, restore/build/folder-publish normally, and prepare a complete fixed type catalog before application or container construction; verify the scoped native package-consumer checks on pinned Windows x64 and Ubuntu x64 without reopening CIS-0001/WORK-001 or broader release qualification.

## Impact roots

- `cislib::source-file::implementations/csharp/tests/CIS.Core.ArchitectureTests/ShippingBuildInventoryTests.cs` (source-file)

## Constraints

- Human authority is required for impact disposition and plan approval; `cis plan derive` may carry forward an exact current feature approval without requesting it again.
- Scope expansion must be recorded as a new finding or decision.

## Acceptance criteria

- A clean Console and Web consumer restores the actual candidate's versioned SDK on its first restore, without a Core project reference, late source import, duplicate Core PackageReference, old assets or cached Core package. The SDK selects the current RID before restore. Ordinary deployed dependencies, including the untouched sample implementation assembly, appear in the generated inventory.
- Preparation precedes Console readiness and Web builder/container construction. The prepared manager and cached query are identical across root/scoped consumers; membership stays fixed and discovered application types are neither constructed nor automatically registered.
- Build, ordinary folder publish and matching `publish --no-build` produce runnable consumers and correct inventories. Unchanged manifests retain their bytes and timestamps.
- A missing selected assembly fails before readiness or container construction with the existing useful startup diagnostic and no partial successful catalog.
- Ordinary Core package consumption without enabled inventory remains usable. The build helper stays out of runtime output; package layout, runtime dependency boundaries, defaults and public APIs are preserved.
- Execute the supported framework-dependent, untrimmed .NET 10 path on Windows 11 x64 and Ubuntu 24.04 x64, using SDK 10.0.401 and .NET/ASP.NET Core 10.0.12. Record an unavailable platform as pending, not passed. This scope excludes publication and broader release qualification.

## Remaining gap and proposed single task

The versioned SDK, early restore hook, helper packaging, runtime and Console/Web
examples already exist. Current `ShippingBuildInventoryTests` directly loads the
source helper, `WriteTypeInventoryTests` covers inventory generation, and
`TypeManagerStartupStories` supplies its own manifests. None proves the maintained
examples through a clean restore of the actual packaged SDK. The older local
development package confirms asset layout only; it is not the new candidate.

The approved native plan contains one medium implementation task with the
integration checks, concrete fixes, documentation, independent review and
completion: [CIS-0002/WORK-001](agent-tasks/WORK-001.md). Andrew Spiteri approved
the exact plan and separately authorized its allocation on 12 November 2026;
CIS recorded both in order. The task is now Complete through the supported
lifecycle; [verification.md](verification.md) records actual execution, current
reuse, review and settled accounting separately from those decisions.
Keep `CIS-0001/WORK-001` and its accepted records untouched.

Required changes:

1. Add focused package-consumer tests and one small shared fixture in the existing
   native IntegrationTests project. Reuse the Console/Web/Contracts/Unused/Shared
   example sources and existing startup assertions; keep bounded `ProcessRunner`
   and temporary-directory cleanup. Do not add a generalized verification runner.
2. Build/pack one current candidate and reuse its unchanged bytes across the checks.
   Use an isolated local feed/cache and clean consumers outside inherited repository
   build configuration. Replace the Core project reference/source target import
   with the exact versioned SDK. Remove the examples' broad `RuntimeIdentifiers`
   list so it cannot mask a broken first restore; supply explicit isolated pinned
   build properties. Check package/runtime identities and helper deployment boundaries.
3. Exercise the acceptance criteria above. Reuse example `--verify` mode for fixed
   membership, preparation order and Web manager/query identity. Keep source-helper
   and runtime tests as supporting coverage; rerun only checks affected by actual fixes.
4. Correct the Console comment that incorrectly says no ASP.NET shared framework
   is needed. Update the package/example instructions with the actual consumer XML
   and normal commands. Fix production SDK/targets/runtime only for a demonstrated
   in-scope defect; do not replace working machinery.
5. Configure a separate scoped CIS profile, retain a compact native result and
   candidate/source identity with per-platform outcomes, and clean disposable
   consumers/caches/logs. Review actual changes and complete the native task only
   with current evidence and settled work. Commit/push the existing development
   branch; do not merge main or publish a package.

No Configuration, Heartbeat, Startup Pipeline, plugin/custom-load-context,
Visual Studio/MSBuild.exe, PublishUrl, AOT, trimming, single-file, self-contained,
coverage, mutation or broad performance/platform campaign is added.

## Reviewed finite allocation

Andrew Spiteri explicitly authorized this allocation separately from the plan on
12 November 2026. Native application succeeded at revision 39, producing revision
40. The reviewed limits and external-host boundary below remain binding.

Original authority: Andrew Spiteri. Controller: Codex. Proposed change and sole
task ceilings are each **7,200 active seconds, three logical attempts and a
180-second protected restoration reserve included within the time ceiling**.
The task is fixed with no redistribution. This is an upper bound, not permission
to repeat unchanged checks. Acknowledge earlier unmeasured preparation as unknown;
new profiles, platforms and run IDs grant no fresh capacity.

Proposed shared local storage ceiling: **6 GiB including existing owned files**,
with **64 MiB protected restoration space** and native private scratch permitted.
Use the existing `.cis/local` and Core/helper/TestSupport bin/obj roots, adding
IntegrationTests and its DiscoveryHost/HealthcheckHttpHost/CisHealthProbe bin/obj
roots only where the selected native build needs them. Keep candidate/feed and
current results under a scoped testing directory. This does not raise the old
owner's ceiling, transfer its required claims or delegate historical cleanup.
The allocation must reuse storage ceiling reference
`CISLIB-CIS-0001-STORAGE-2026-11-10`, joining that existing ceiling with the same
6 GiB/64 MiB limits and combined measured roots. The new storage decision citation
below authorizes joining the ceiling; it does not replace its identity or mutate
the completed task's policy or claims.

Both OS checks share one 7,200-second, three-attempt allowance with one included
180-second restoration reserve; no platform gets a separate allowance. The
installed CIS controller accounts its admitted local operations; it has no native
cross-host accounting/storage transport. Actual Ubuntu CLI execution over the
existing SSH access will use bounded commands and a disposable `/data` task
directory limited to 2 GiB for all regular files beneath it, including any new
toolchain, package/cache, consumer and build/publish output stored there. Inspect
before expansion and after commands; stop expansion and clean disposable output
if capacity is insufficient. This is controller inspection, not an enforced
filesystem quota. Record actual remote duration/output separately. Do not duplicate/reset the authority ledger or
claim remote process containment/storage enforcement that CIS does not supply.
The server is reachable; its checked CIS build installation has SDK 10.0.403 and
runtime 10.0.14, so the requested 10.0.401/10.0.12 toolchain must be located or
installed before claiming an Ubuntu pass.

Proposed decision references: `CISLIB-CIS-0002-ALLOCATION-2026-11-12` and
`CISLIB-CIS-0002-STORAGE-2026-11-12`. Apply only after explicit approval of the
materialized plan and allocation, using the then-current ledger revision and
exact generated task identity. No new authority or native execution is granted
by this proposal.

## Accepted impact decision

Andrew Spiteri explicitly accepted the sole finding `IMPACT-16713BDBE2` on
12 November 2026 using its published reason. Native impact review is complete,
high confidence and not truncated. Recorded acceptance reason: "Accept the existing inventory-test source as the
entry point for the one Type Manager package-consumer integration task described
here; retain the working runtime/helper/SDK and complete only its scoped coverage,
concrete fixes, instructions and review."

Native `plan build` materialized the single task after that disposition. Its
authored contract validated with one accepted impact covered and no plan errors
or warnings. Andrew Spiteri then approved the exact plan and allocation; the
accepted BRD, technical intent, solution bundle and no-UI approvals were reused.
