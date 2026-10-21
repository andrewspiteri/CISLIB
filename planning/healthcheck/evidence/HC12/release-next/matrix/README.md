# Release execution and evidence boundary

The committed starting point is `8fc2f9ccb78ba948f9794cfb4c0ea37cfd2956eb`.
This audit executes no build, test, coverage, mutation or release command. The
coordinator owns native execution, existing shared profiles/dispatch and final
candidate/acceptance records. Publication is not authorized.

The real Release profile can execute useful work now: its normal stages and all
five project-backed native families, including the complete CompatibilityTests
project. It cannot currently return a qualified release. Mutation has no project
and is not dispatched; `VerificationEvidence.GetIssues` unconditionally reports
the eight declared release gates, and its mutation-report branch explicitly says
the full mutation matrix validator is unimplemented. A successful native stage
followed by exit 2 is retained partial evidence, not a failing Healthcheck runtime
or a green release. An earlier stage/native failure remains a real failed attempt.

The [machine-readable inventory](gate-inventory.json) records the exact eight
gates, nine inherited planned case IDs, source references and current prerequisite
gaps. These are global single-package Core obligations. Running and reporting the
current profile does not resume the paused logging roadmap, repair its planned
cases or authorize a gate waiver.

## Concrete native schedule

Use self-contained local clones at the exact committed revision, with independent
working/output directories and Git objects. A Windows Git worktree's external
metadata is not a portable Linux clone; the earlier selected Healthcheck platform
snapshot also omits whole-profile inputs and Git HEAD. Native build/output streams
remain serialized by the coordinator. Separate immutable mutation copies can run
only when their own tool/output closures and source freezes are established.

Before starting either clone, retain the actual OS/architecture, Git HEAD and clean
source inventory; use the pinned native SDK `10.0.401`, subject frameworks
`Microsoft.NETCore.App`/`Microsoft.AspNetCore.App` `10.0.12`, PowerShell `7.6.5`
(the scripts require at least 7.5), and Git on PATH. Full compatibility additionally
needs ApiCompat `10.0.401` and tool-host runtime `8.0.31`. If a separate native tool
host is selected, `CIS_COMPAT_API_DOTNET` identifies it and `CIS_COMPAT_API_TOOL`
identifies the exact tool entry DLL. Record those actual tool hashes/versions.
No SDK upgrade is needed.

Run from each clone's repository root, with `CIS_ARCHITECTURE_INSPECTION` cleared.
Clear stale `CIS_COMPAT_CORE`, `CIS_COMPAT_REPORT` and `CIS_COMPAT_CORE_SHA256`
overrides, unless a complete independently pinned current producer set is
deliberately supplied. Do not globally disable runtime roll-forward without
checking every requested runtime configuration; the explicit subject child
invocations already select `10.0.12` with roll-forward disabled.

Windows:

```powershell
$env:UseSharedCompilation = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
pwsh -NoProfile -File eng/verify.ps1 -Profile Release -Lane windows-x64 -MaxBuildNodes 1 -OutputRoot .artifacts/healthcheck/release-next/windows-global-01
```

Native Ubuntu 24.04 x64, from its own full clone with native dotnet/pwsh/git on PATH:

```powershell
$env:UseSharedCompilation = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
pwsh -NoProfile -File eng/verify.ps1 -Profile Release -Lane linux-ubuntu2404-x64 -MaxBuildNodes 1 -OutputRoot .artifacts/healthcheck/release-next/linux-global-01
```

Both output paths must be new. The runner itself discovers and executes native
cases, builds/packs locally, captures architecture facts and reconciles native
IDs. Retain its original `summary.json`, `validation.json`, all stages, discovery,
TRX and source inputs. Under the unchanged validator a final exit 2 is expected
even when five native families pass. It is not safe to assume their counts or
outcome before the actual run. Full compatibility exercises existing Type Manager
and provenance controls as well as the 127 Healthcheck cases.

A separate read-only revalidation can reuse the existing checker and write a new
review output, without replacing the primary validation file:

```powershell
pwsh -NoProfile -File implementations/csharp/eng/check-release.ps1 -EvidenceRoot .artifacts/healthcheck/release-next/windows-global-01 -Profile Release -Lane windows-x64 -OutputPath .artifacts/healthcheck/release-next/windows-global-01-review.json
```

Use the native Linux lane and its corresponding root/output for the second run.
This revalidation is optional evidence inspection, not a substitute for execution.

## Fresh candidate and package consistency

The old `fde622...` Core DLL and `1d48cfd...` package belong to the previous
qualification receipt. Their records remain immutable historical evidence. The
new Git revision changes the informational version and can change Core bytes
without changing a Healthcheck method. The old exact package therefore cannot
automatically satisfy current release/package/consumer gates.

Freeze the actual harness/source inputs, then build/pack and record new package,
Core, helper, PDB, toolchain, lockfile and source hashes. The profile writes packages
to `.artifacts/csharp/packages`; retain the resulting exact `.nupkg` in a unique
candidate directory before any later build/pack can replace that version's path.
Each native lane may produce different binary/package bytes: never infer byte
equality from matching Git HEAD, version text or source. If a single reference
package is selected for consumers on both OS lanes, transfer and verify its exact
bytes and require each actual loaded Core DLL to match its package entry.

The package version is still `0.1.0-dev.1`; exact-package consumer runs need new
private feeds/caches and actual restored-assets/loaded-origin checks so a cached
older package of the same version cannot substitute. Current Healthcheck producer,
package-consumer, identity, coverage and mutation harnesses retain their own pins.
Reuse an earlier observation only after comparing every input relevant to that
gate, including source, project graph, fixtures, toolchain and runtime; an old
candidate package hash is never relabeled current.

## Separate gates after the profile

Healthcheck still needs complete automatic source/child-process mutation and
compiler rollback/ignored/non-killed disposition evidence on applicable lanes.
The older strict state-only 90.3225806% result and twenty critical controls do not
cover that closure. The module coverage result is actual sub-evidence; global
shipping coverage also includes the rest of Core and `CIS.TypeManager.Build`.

The existing whole-shipping coverage collector expects a successful checkpoint at
the fixed `.artifacts/csharp/verification/Developer` path. A retained Developer run
under another output root is not automatically accepted there. Its policy also
does not yet list the eight Healthcheck declaration-only files already pinned by
the module collector. That would produce `Unmeasured executable source` in the
global summary. This is a root-owned inventory integration issue, not permission
to exclude executable code or lower 95% line/90% branch thresholds. The exact
paths and reuse points are in the inventory.

Real Heartbeat/watchdog and generic IStartable integration remain external.
No aggregation of native local gates may erase those obligations, treat empty
publication history as historical replay or qualify the whole release. Existing
validators and explicit receipts are sufficient to record this wave honestly;
[no new release aggregator is introduced](aggregator-decision.md).
