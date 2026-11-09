# CISLIB cleanup disposition — 9 November 2026

Baseline: `f04d97c636c498e0e0ce686a87a3106660cf752e`, refreshed against origin on 9 November; origin/main remains `153c898bd2fdfe02801a306fca5149d0acb05ba3`. Resulting implementation revision: **`5db09f135becf2f3e84cf6bd88b0ae2d38a26229`**, on `codex/cislib-cleanup-2026-11-09`; the follow-up commit updates only operating guidance and this final record. Review source with `git diff f04d97c6..5db09f13` and the complete cleanup with `git diff f04d97c6..codex/cislib-cleanup-2026-11-09`. No merge, remote push or release is included.

| Decision | Group | Accepted purpose and applied change |
| --- | --- | --- |
| Keep | Logging, Type Manager, Healthcheck | Preserve implemented public behavior, defaults, actual consumers and single `CIS.Core` package. Product source changes are limited to removing obsolete test friend assemblies. |
| Keep | `CIS.TypeManager.Build` and package SDK/imports | Actual inventory generation and transitive packaged build integration; native MSBuild cannot infer this product-specific inventory. |
| Keep | Native MSTest families and behavioral fixtures | Unit, integration, architecture, compatibility and business projects cover meaningful boundaries, ownership, concurrency, errors and defect regressions. |
| Simplify now | Native development and package workflow | One ordinary solution; direct restore/build/test/pack. Delete duplicated Fast assemblies, partition configuration, preparation checkpoints and wrappers. |
| Simplify now | Product-specific verification | Direct assembly/package dependency checks, actual package consumer builds and a minimal child mutation adapter replace checker protocols and source/provenance/custody frameworks. Actual child execution must receive the instrumented Core; a native tool cannot stage that product-specific process fixture itself. |
| Keep | `ProcessRunner`, focused test setup and Type Manager task tests | Real child-process deadlines/drain/cleanup, shared deterministic behavior and real MSBuild task regressions. These are fixture support, not a general campaign engine. |
| Remove now | DependencyChecker, discovery logger and verification-only owner | Retire custom work/evidence/replay/receipt orchestration and its exclusive tests, projects, copied inputs and commands. |
| Remove now | Historical operators and composite receivers | Retire fixed-candidate controls, archive custody, synthetic release baselines and duplicate mutation routes. Retain native tool pins/configuration and the actual accepted obligations. |
| Simplify now | Guidance and requirements | README is the operating entry point; current decisions, BRDs, technical intent and contract addenda remain. Retire recovery plans, imported draft policies, copied review/source trees and campaign reports. |
| Keep | `.cis/repository.yml` and `.cis/workspace.yml` | Existing repository identity/registration only, pointed to the surviving `docs` root. Generated state and obsolete starter ownership/import/graph manifests are retired; adoption is a subsequent task. |

## Native commands

Run from `implementations/csharp` with SDK 10.0.401 and runtime/ASP.NET framework 10.0.12 installed:

```powershell
dotnet tool restore --configfile eng/Tools.NuGet.config
dotnet restore CisLib.slnx --locked-mode
dotnet build CisLib.slnx -c Release --no-restore
dotnet test CisLib.slnx -c Release --no-build --no-restore --filter "TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress"
dotnet test tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj -c Release --filter FullyQualifiedName~Healthcheck
dotnet pack src/CIS.Core/CIS.Core.csproj -c Release -o ../../.artifacts/packages
```

The ordinary regression command excludes `PerformanceObservation` and `ExtendedStress`. An unfiltered solution run or IDE Run All includes those extended categories. Performance/stress and full release qualification remain separate open work.

Native coverage and mutation configuration live under `implementations/csharp/verification/`; these optional release operations are not prerequisites for a focused edit. No whole-library coverage/mutation, release campaign or benchmark sweep is part of this cleanup.

ApiCompat 10.0.401 also requires its pinned .NET 8.0.31 tool host. This machine has 8.0.33 globally; an official Microsoft 8.0.31 runtime was downloaded, SHA-512 verified against the official release metadata and installed privately under `.artifacts/tool-host-8.0.31`. Set `CIS_COMPAT_API_DOTNET` to that host's `dotnet.exe` when using this private installation. SDK/product execution and all runtime/version pins are unchanged. A normally installed 8.0.31 needs no override.

## Local generated output disposition

No current release or acceptance claim depends on the retired historical campaign payloads. Historical scoped results are explicitly retired, not relabelled as current passes. Tracked material remains recoverable through ordinary Git history; deleting it does not reduce existing Git history. User chat exports and their publication work under `.artifacts/chat-exports` and `.artifacts/chat-export-publication-2026-11-03` are preserved.

Already removed after ownership inspection: generated `.cis/local` graph/import/test state, `.code-review-graph`, root `pilot-*` results, the stopped Source47 Windows source/runner copy `.artifacts/w47`, old three-TRX commit-check results, empty format/TMP folders and obsolete generated C# dependency/package/consumer/focused/test output.

The following exact CISLIB-owned historical output scopes were removed after the user explicitly approved the listed deletions on 9 November. Their sampled manifests, reports, source snapshots and compiler output identify old Healthcheck/recovery/integration work; no live execution was found. Automatic approval review initially blocked the multi-directory deletion; the user's specific authorization resolved that restriction:

- `.artifacts/hc-assessment-20261028`
- `.artifacts/hc-collector-20261027`
- `.artifacts/hc-collector-20261028`
- `.artifacts/hc-gaps-20261028`
- `.artifacts/hc-incremental-20261027`
- `.artifacts/hc-keepalive-20261028`
- `.artifacts/hc-qualification-20261028`
- `.artifacts/hc-resume-20261028`
- `.artifacts/hc-survivors-20261028`
- `.artifacts/hc-unit-full-20261026`
- `.artifacts/incremental-cis-20261027`
- `.artifacts/csharp/performance-20261031`
- `.artifacts/csharp/recovery`
- `.artifacts/csharp/verification`
- `.artifacts/csharp/type-manager-compatibility`
- `.artifacts/csharp/recovery-child-control-spans-01`
- `.artifacts/publish-history-20261029`
- Remaining untracked/ignored `planning/healthcheck/evidence` payloads after authoritative contract amendments were moved to current documents.

The old CIS canary patch inside `hc-unit-full-20261026` is already represented in the current CIS implementation (`omittedCanaryFile`); it is not uncommitted product work. No remote host, shared resource, Docker data, outside-workspace path or Git history is deleted.

The separately inspected `.artifacts/hcg` contained 1,474 top-level old HC12 transfer archives (5,073,471,909 bytes) and 860 extracted duplicate campaign directories. Samples included old active182 readbacks and a193 mutation sessions/controller source; these belonged to the same retired Healthcheck campaigns, with no current claim or live use. This path was omitted from the first approval list; after automatic approval review required separate authorization, the user explicitly approved its deletion. The entire directory is now removed. A full recursive size scan was stopped because it was disproportionate; extracted-directory byte totals are unavailable and are excluded from the measurement.

## Verification and current gaps

| Check | Actual outcome |
| --- | --- |
| Native locked restore | PASS after native lock refresh for ArchitectureTests' genuine build-helper reference; no package versions changed. Network access was needed for NuGet audit data. |
| Ordinary Release solution build | PASS, zero warnings/errors. The solution includes its actual child fixtures so Release builds do not depend on stale Debug/Release output. |
| Unit | 5,307 PASS; performance observations and extended stress excluded. |
| Architecture | 331 PASS, including direct approved dependency origins, immutable snapshots and real Type Manager build-task tests. |
| Business | 28 PASS. |
| Integration | 2,119 PASS; performance observations and extended stress excluded. Includes actual package consumer and HTTP/pipe/probe children. |
| Compatibility | 175 initially PASS; the one API case failed because exact 8.0.31 was absent. That case subsequently PASS with the verified private host and minimal API-host selection. No product/runtime pin was relaxed. |
| Final package-fixture change | Existing 20-case CandidateLoggingProcessTests class PASS after changing feed/cache/source/build ownership to a disposed TemporaryDirectory. Assertions remain unchanged; successful loops no longer accumulate these payloads. |
| Native pack | PASS; package contains `CIS.Core.dll`, `CIS.TypeManager.Build.dll`, SDK imports and `buildTransitive/CIS.Core.targets`. Actual package consumer behavior is covered above. |
| Pinned tool availability and local review | Stryker/APICompat help checks PASS; independent review found and corrected two narrowed invariant guards before passing native verification. Current guide links and scoped whitespace/reference checks PASS. |

There are **7,961 distinct passing native cases across these scoped runs**; the final 20-case rerun is not counted twice. This is not an all-scope release, coverage, mutation, performance or second-platform qualification. Actual Stryker activation of every retained child path remains a release obligation, not a claim established by an ordinary transport pass. Initial sandboxed Unit/Business attempts aborted at test-host IPC before executing tests; native IPC access resolved that without increasing the 90-second timeout. The first restore/solution attempt failed on stale lock metadata/network audit access; neither is relabelled as a passing attempt. Original current TRXs and diagnostic logs remain under ignored `.artifacts/cleanup-tests` and `.artifacts/cleanup-*.log`.

| Maintained group | Before files / source-config lines | After files / source-config lines |
| --- | ---: | ---: |
| Product source | 151 / 15,080 | 151 / 15,073 |
| Native tests and fixtures | 770 / 118,806 | 644 / 93,482 |
| C# build/tooling | 174 / 32,819 | 6 / 263 |
| Root command wrappers | 7 / 144 | 0 / 0 |
| Root historical verification operators | 15 / 2,847 | 0 / 0 |

Tracked files fell from **53,151 to 1,107**. Counts exclude generated `bin`, `obj`, TestResults and vendor output; maintained test files include actual frozen compatibility input assets. Lines use the same source/config extensions in both measurements, excluding prose/binaries. The retained C# verification directory holds actual frozen contract/consumer inputs plus one coverage configuration and two mutation configurations; it does not retain a campaign engine.

Measured deleted old local generated-file payloads total **17,086,322,170 bytes**; the guidance/spec cleanup separately measured **3,578,295,606 bytes** of deleted tracked working-tree files. Root historical operators add 297,389 measured bytes, and hcg's measured top-level archives add 5,073,471,909 bytes. Thus **at least 25,738,387,074 bytes of old file payloads were removed**. Unmeasured extracted hcg copies and source/tool deletions are excluded. These are logical file bytes, not a claim about filesystem allocation or reduced Git history. Newly created and disposed verification outputs/download ZIPs are not counted as reclaimed pre-existing data. No replacement archive was created.

Retained custom verification is limited to native assertions and necessary fixtures: frozen consumers/schema/wire bytes preserve actual development compatibility; byte/reference/IL/reflection assertions protect accepted package, ownership and no-loading invariants that native build alone cannot assert; deterministic clocks/writers and bounded process drain/cleanup exercise real behavior; and the small mutation child adapter propagates instrumented Core plus its active selector into an actual child. Plain build/test tooling cannot supply those product-specific expectations. The obsolete supporting campaign mechanisms are removed.

The retained [current decisions](../decisions.md) preserve coverage 95% line/90% conservative branch and mutation 90% conservative obligations, real child activation, platform/consumer compatibility and publication decisions. Full current release qualification, external Heartbeat/startup-provider integration and current performance targets remain open. No governed budget, approval or old failed result is changed.

No cleanup exception remains blocked. The untracked-output access restriction was resolved by explicit user authorization; significant user chat exports remain preserved. The open product/acceptance work above remains separate from completed cleanup.

The next CIS task should connect to the ordinary solution/projects and native commands, package build assets, retained behavioral tests and current obligations. It should not recreate retired command names, receipts, archive protocols or recovery campaigns, and should not implement another module as part of adoption.

## Cleanup closure — 9 November 2026

Follow-up to reviewed commit `6772ae960e893d469ad679facd711b55d0e4a605`: restored the accepted Healthcheck implementation dependency boundary from technical intent section 3. A local native reflection/IL assertion checks private members, signatures, attributes, method bodies and generated nested types. It preserves policy/state independence from HTTP, policy/transport ownership direction and the related infrastructure restrictions. The existing public-surface and immutable-snapshot checks remain. One compact compiled fixture has a harmless public API and a private `HttpRequest` cast/getter call; the same assertion rejects it at `ReadScheme`. No shipping source, dependency, project or runtime pin changed.

README, native test guidance and the native command block above now consistently document ordinary regression with `TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress`; unfiltered solution runs and IDE Run All include the extended categories. Purpose-specific focused, coverage and mutation examples retain their scopes.

Exact focused commands executed from `implementations/csharp`:

```powershell
dotnet build tests/CIS.Core.ArchitectureTests/CIS.Core.ArchitectureTests.csproj -c Release --no-restore --nologo -m:1 -p:UseSharedCompilation=false -v:minimal
dotnet test tests/CIS.Core.ArchitectureTests/CIS.Core.ArchitectureTests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~CIS.Tests.ArchitectureTests.Healthcheck.HealthcheckArchitectureTests' --logger trx --results-directory ../../.artifacts/cleanup-tests/closure-healthcheck-native --nologo -v:minimal
```

Final affected-project build: **PASS, zero warnings/errors**. Focused tests: **4 PASS, zero failed/skipped**, covering actual Core's implementation boundary, the private-body rejection fixture, public surface and immutable snapshots. Compatible restored assets and unchanged dependencies were reused; no restore was needed. The only retained native result for this follow-up is its TRX under `.artifacts/cleanup-tests/closure-healthcheck-native`; the negative fixture is inspected without invocation or temporary output. Scoped whitespace, ordinary-command consistency, relevant links and independent local review passed.

The earlier solution build, pack and **7,961-case** results above remain historical evidence with their original scope; they were **not rerun** for this correction. No whole-suite run, coverage/mutation campaign, benchmark or platform qualification was added.

**Cleanup is complete; CIS adoption is next.** Release/performance obligations remain separately open. Neither CIS adoption nor the separate CIS proportionality amendment is implemented here, and no new prerequisite or recovery campaign is introduced.

## Minimal CIS adoption preparation — 9 November 2026

**Prepared, but step 1 is blocked and not complete.** CISLIB baseline is `2f5100ff6a14938b4dca4fe0d99c62c044993876`. CIS was rebuilt from the clean reviewed `1eab89f68c635513a20bca8aa1a7ce6ae02cb8e5` source; the actual host reports `0.3.0+1eab89f68c635513a20bca8aa1a7ce6ae02cb8e5` and SHA-256 `e2cd83872c92bd41e38cd36a81dd006edfe154be57ef604552255bd2d3ec3cf9`. That host, rather than an executable guessed from PATH, was used below. Its build passed with zero warnings/errors. CISLIB's SDK 10.0.401 and .NET/ASP.NET 10.0.12 pins are unchanged.

Minimal import was reviewed and applied with its actual input-bound hash `sha256:8f1fecce294d1c6257c9b66364d844511e406e56b0ff71974e836a60e5e4ac38` and supported `--merge-edits`. The existing `cis` ecosystem, `cislib` authority/product/repository, `docs` root, owned participation and `none` relationship are preserved. Runtime profiles, scoped import guidance, catalogue/navigation and five native suite bindings were added. The bindings were corrected to the C# working directory, Release and actual TRX paths, and remain configured/unverified. No fixture/example became a new shipping module.

The first graph was partial solely because 42 existing guides were uncatalogued. They received catalogue entries and minimal metadata with their original inferred IDs/types and `unknown` lifecycle; every original body was preserved byte-for-byte. This registers existing human-authored source, without approving or generating product specifications. Product source, assertions, package layout and build machinery are unchanged.

The complete machine-specific unit-only profile is ignored local configuration at `.cis/local/testing/profiles/healthcheck-diagnostic-unit.json`, SHA-256 `1c36079948f05414698317527c72591140f2b46cdffa17cfb0f6e86d4fed02e4`. It declares `Healthcheck/Contracts/HealthDiagnostic.cs`, the existing `HealthDiagnosticRuntimeAcceptanceBoundaryTests.cs`, 21 configuration/dependency inputs and 15 verified absolute tool files. Its expected two Boolean DataRows exercise real Unicode sanitization, explicit caller-message projection, detached values and scalar bounds. The configured native command would build current Unit inputs with compatible restored assets and intersect the selected class filter with both ordinary exclusions; no mutation/canary/pilot/recovery command is configured. The graph conservatively finds 76 candidate test files and includes `UnicodeScalarText.cs`; the final native filter and Unit project restrict execution to the selected two-case class. Static candidates are not runtime coverage or assertion-quality proof.

## CIS tooling evidence

`cis` below denotes the verified invocation described in [README](../../README.md#cis-commands). These exact argument sets were used with the absolute local CISLIB path as `--repo`/`--workspace` (shown as `.` here to keep machine paths out of published guidance):

```text
cis repo import --workspace . --source . --root docs --participation owned --relationship none --guidance-mode minimal --dry-run --format json
cis repo import --workspace . --source . --root docs --participation owned --relationship none --guidance-mode minimal --merge-review sha256:8f1fecce294d1c6257c9b66364d844511e406e56b0ff71974e836a60e5e4ac38 --merge-edits .cis/local/import/reviewed-edits.json --yes --format json
cis repo doctor --repo . --root docs --format agent
cis repo doctor --repo . --root docs --format json
cis test inventory --repo . --format agent
cis test validate --repo . --strict --format agent
cis docs validate --repo . --strict --format agent
cis graph build --repo . --format agent
cis graph status --repo . --format agent
cis test incremental plan --repo . --profile .cis/local/testing/profiles/healthcheck-diagnostic-unit.json --format json
cis test incremental status --repo . --profile .cis/local/testing/profiles/healthcheck-diagnostic-unit.json --format agent
cis artifacts inventory --repo . --format agent
```

| Acceptance item | Actual evidence |
| --- | --- |
| Baseline and minimal import | Verified host/source above; native apply status `imported`, `applied=true`, no errors/collisions. Native apply/assessment results remain in `.cis/local/import/`. |
| Binding and setup validation | Five suites inventory/strict validation PASS; strict docs validation PASS for 50 catalogue entries/documents, zero warnings/errors. Skill inventory/strict validation/local-only audit PASS with zero installed skills; no full skill library was installed. A leftover empty skill directory was removed after verifying it contained no data. |
| Graph | Fresh and complete, 19,276 nodes / 73,259 edges, zero errors/warnings; build `sha256:44dd24a7452b6fa49610dc772aed8f2700ae31b19a6664bb26d3a7cae9418ee8`. |
| Actual execution | BLOCKED: no approved adoption change/task, adopted accounting, finite owner allocation or inherited `CIS_WORK_*` context exists. No native unit run or TRX was fabricated. |
| Reuse | UNPROVEN: plan/status return `pendingUnitBatches=1`, `pendingMutationBatches=0`, `executedUnitBatches=0`, `reusedUnitBatches=0`, `unitCurrent=false`, `scopeComplete=false`. |
| Output control | Existing native inventory reports protected graph output and no archives/candidates. `ownedStorageStatus=unaccounted`; time/storage ownership and remaining capacity are unknown without a legitimate allocation, not zero. Necessary local configuration and native metadata remain; redundant import preview/edit payloads were removed after successful apply. |
| Honest scope | Other native families are configured/unverified through CIS. The historical 7,961-case cleanup and four-case closure results are not new adoption results or CIS reuse receipts. No mutation, coverage, stress, benchmark, package or platform qualification ran. |

The precise failed prerequisite command was:

```text
cis change create --title "Minimal CISLIB native-workflow adoption" --outcome "Execute one governed unit slice and prove unchanged reuse" --repo . --format json
```

It returned **exit 2, `applied=false`**: missing Active/current canonical product technical intent; missing Active/current overall solution design and component sheet; and missing Active/current high-level UI applicability/direction. The original result is `.cis/local/import/change-prerequisite.json`. These workspace-foundation gates apply independently of fixture/frontend classification. A legitimate no-visual-UI record is possible, but requires its own valid governance; this failure does not require implementing UI features in CISLIB. Module source requirements are not substitutes for those approval records.

The next required decision is an existing legitimate approved adoption change/task association, authority and finite time/storage allocation, or separately authorized governance/accounting setup through supported CIS controls. Inspect existing work with `cis change list --repo . --format json` and foundation state with `cis technical-intent status --workspace . --format json`; the current results are an empty change list and missing canonical technical intent. A timeout, provider profile, invented approval, removed workspace marker or unaccounted diagnostic run cannot provide that authority. Full reconciliation and CIS product/proportionality changes were not used as workarounds. No product-gap inventory or feature work started.

`cis generate not applicable: this change registers and binds the existing native workflow; it does not generate product code or specifications.` The local usage ledger is `.cis/local/feedback/tool-usage.jsonl`. Doctor's final result is valid configuration with zero errors and 21 warnings for classification review, optional index/local-AI setup, security and standards capabilities. Those pending capabilities were left explicit. No governed unit run ID exists, and `cis test reconcile` was not run without native results. The accounting/settlement and unchanged-result acceptance rows must pass before step 1 can close.

Strict `cis agent evidence validate` returns warnings (exit 5) because the changed suite binding is test-related and no native run ID/reconciliation exists. This is recorded as outstanding, not satisfied with a placeholder or a fabricated run. A model-assisted, context-limited reader check correctly identified the incomplete step, zero executed/reused unit cases, legitimate governance decision, optional no-visual-UI route and installation-specific profile; two wording clarifications were applied. This is a comprehension check, not human approval.
