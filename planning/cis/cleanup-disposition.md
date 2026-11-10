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

Historical checkpoint: the implementation and host status below are superseded by [current correction delivery and continuation](#cis-corrections-delivered--current-continuation-checkpoint).

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

## Adoption continuation decision package — 10 November 2026

Historical preparation checkpoint at `4bf684c8`; read [current correction delivery and continuation](#cis-corrections-delivered--current-continuation-checkpoint) for the delivered capabilities and remaining decisions. The downstream product drafts and allocation below remain unapproved unless a later record explicitly changes their status.

**Prepared for a specific decision; minimal adoption remains open.** “Step 1” in the earlier record means the whole minimal-adoption demonstration, not the first decision below. The accepted minimal import is retained. This continuation prepares real product-baseline content, downstream design/no-UI content, one bounded native-verification task and finite allocation terms. None of the proposals supplies human approval, canonical task identity, accounting authority or a passing native result. The controller's current command evidence and reviewer/build-host findings accompany this section.

### Checkpoint A: complete entry route

This table records the prior entry route. Its accounting/task capability gaps are now corrected; see [current delivery](#cis-corrections-delivered--current-continuation-checkpoint). Product approval and actual allocation are separate outstanding requirements.

Existing source requirements and current explicit decisions remain authoritative. Registering their product-level mapping is a new canonical governance action; source acceptance is not a current CIS approval record. The actual CIS source remains reviewed `1eab89f68c635513a20bca8aa1a7ce6ae02cb8e5` and CISLIB preparation starts from `f05b02c0c4967bfee2aabb1f4140709923ad5d3a`.

| Prerequisite | Prepared/current state | Supported operation and remaining decision |
| --- | --- | --- |
| Product BRD | Substantive supplied registration draft in `docs/specs/business-requirements.md`; no canonical approval claimed | `definition documents --role business --path ...`, reconcile/validate/status; exact source dispositions and explicit BRD approval remain human decisions. |
| Product technical intent | Substantive supplied registration draft in `docs/specs/technical-intent-spec.md`; no canonical approval claimed | Select supplied technical document and prepare current direction; ordinary adoption/approval requires a current accepted BRD. Resolve only genuine remaining choices, then approve the exact TI. |
| Solution design/components | Complete substantive content proposal below; upstream-bound canonical payload cannot yet be current | `solution-design init`, refine/validate both files and obtain one whole-bundle approval after TI acceptance. |
| No visual UI | Explicit short applicability/direction proposal below | After accepted/current solution, initialize questions, retain or record exact authorized `UI-Q-001` answer, initialize/validate and explicitly approve the resulting direction. |
| Adoption change | Coherent title/outcome/scope/task proposal below; no change ID invented | `change create` after current accepted TI, solution bundle and UI direction, against a fresh graph. |
| Impact and plan | Native roots and scope proposed; no analysis/disposition/plan approval fabricated | `impact analyse`, human `accept/reject/defer`, then planning. Current `plan build` writes rows without durable task records; task-materialization restriction below must be resolved. |
| Canonical owner | One draft adoption/native-unit owner contract below; no existing task document or approved plan claimed | Work ownership resolves an actual unambiguous `agent-tasks/*.md` and approved current plan with dispositioned impacts; plan rows alone do not establish this. |
| First allocation | Finite terms below; no adopted accounting or allowance claimed | Current `agent work allocate` rejects fresh minimal entry before reading the payload. No supported explicit accounting-only first-use operation exists at the reviewed version. |
| Native result/reuse | Existing local Windows unit profile retained; execution remains conditional | Once actual owner/authority/allocation are valid, run exactly the selected unit layer, settle, then inspect unchanged plan/status and owned inventory. |

Source checks establish the whole chain: `ChangeDossierStore.cs:48–57` evaluates workspace foundation gates; `TechnicalIntentService.cs:484–508` requires the current accepted BRD; `AgentService.WorkControl.cs:19–20,433–476` checks accounting entry and actual canonical ownership; `AgentService.cs:2529–2570` checks current approved plan, impacts and task eligibility. `PlanningService.cs:64–114,1351–1377` shows ad-hoc build rows with null task paths; `:188–207` materializes tasks only through feature import. Current work/task help exposes no first-use opt-in or standalone task author/import command. These restrictions were inspected once; no unchanged rejected allocation was retried.

### Minimum product-baseline mapping

The two supplied registration drafts preserve the original source documents and map their current accepted obligations without regenerating module plans. Their source traceability distinguishes observed implementation from future specifications and open qualification. The BRD covers the existing reusable Logging/environment, Type Manager and Healthcheck outcomes, host ownership, single-package compatibility and native verification. Technical direction retains SDK `10.0.401`, .NET/ASP.NET `10.0.12`, explicit module boundaries and the approved Microsoft runtime closure.

No future module, later language, Heartbeat scheduler, generic startup-provider integration or release claim is accepted by this registration. The exact imported source assessment, unresolved questions and validation state must be reviewed before approval. The original requirements and current decisions are not superseded by draft status or generated identities.

### Overall solution-design proposal — unapproved downstream content

Intended canonical roles are `architecture` and `components`, normally `docs/architecture/overall-solution-design.md` and `docs/references/component-sheet.md`. This content is ready for incorporation after supported initialization binds the actual accepted TI and managed component identities. It deliberately asserts no source hash, managed approval metadata or Active/current status.

#### Architecture drivers

Provide reusable Logging, Type Manager and Healthcheck in one C# `CIS.Core` package/runtime assembly, with direct implementation, bounded detached observations and application-owned lifecycle. Preserve nullable analysis, warnings as errors, the pinned toolchain and approved Microsoft/ASP.NET dependency closure. CIS remains development tooling outside runtime references.

#### System context and boundaries

Applications call Core contracts, own startup/shutdown/resources/hosting and retain those resources through actual worker/cleanup settlement. Existing Microsoft logger adaptation, host-integrated Healthcheck HTTP, local-pipe reporting and the matching non-Core probe are existing interfaces/consumers, not graphical products or new services. External scheduling, startup coordination and deployment recovery remain application responsibilities.

#### Logical component topology

The component catalogue below identifies three runtime responsibilities inside the single assembly plus genuine package build machinery. It does not split repositories, packages, processes or deployments. TestSupport, fixtures, examples, frozen consumers and the probe retain development/consumer roles.

#### Data ownership and consistency

Logging preserves bounded capture/incoming/output storage and truthful admission, consumption, delivery and cleanup observations: acceptance is not durable delivery and canceled waiting is not stopped work. Healthcheck coordinates policy/state and detached snapshots under its short state boundary; callbacks, serialization and I/O remain outside it. Type Manager fixes prepared membership and cached query results without constructing application objects. Borrowed resources never silently transfer ownership.

#### Integration architecture

Application/Microsoft logger consumers use existing Logging/runtime/provider contracts. Host build invokes the inventory helper, whose deterministic build-selected inventory feeds runtime Type Manager. Applications/optional collectors/external fact producers feed Healthcheck through existing ownership/collection/execution-fact seams. Detached Healthcheck snapshots flow through the shared formatter to HTTP/local-pipe consumers; transport does not evaluate policy, execute business callbacks or become a Healthcheck state dependency.

#### Security and trust boundaries

Applications supply exposure/access policy and own listeners/resources. Retain actual HTTP route/method, pipe framing/identity, bounded diagnostics, timeouts and cancellation rules without claiming a new deployment/container pass. Inventory is a trusted supported build artifact, not arbitrary plugin or assembly discovery. Development governance and native reports do not enter library runtime authorization or package closure.

#### Deployment, operations, and recovery

`0.1.0-dev.1` remains a development version with zero publications. Selected Windows/Linux x64 qualification lanes remain obligations, not results supplied by this draft. Type Manager retains the supported framework-dependent, untrimmed .NET 10 conventional-build/folder-publish profile; no AOT, single-file, self-contained, arbitrary-load-context or new MSBuild host support is inferred. Applications own restart/recovery. Generated output and ephemeral remote scratch are not protected evidence custody.

#### Quality and verification architecture

Preserve the five native MSTest/VSTest families and meaningful architecture/consumer/behavior assertions. Ordinary regression excludes `PerformanceObservation` and `ExtendedStress`. Existing release coverage, conservative branch/mutation, child activation, compatibility, platform/package/business and performance obligations retain their original scope/status. The proposed demonstration would prove only the existing selected two DataRows, their native report, settlement and unchanged-result reuse; it would run no broader qualification.

#### UI design handoff

Proposed exact scope: **No user-facing UI**. Library APIs, health-reporting HTTP, local pipe/probe and developer tools are not graphical surfaces. Record that decision through the native questionnaire and approve its resulting short direction; request no screen, framework, visual token, image or preview work.

#### Traceability

Bind the product BRD registration and technical-intent registration to actual accepted canonical versions before adoption. Existing references are `README.md`, `planning/decisions.md`, architecture dependency/ownership/layout policies, Logging contracts, Type Manager definition/build README and Healthcheck definition/technical intent/contract catalogue/addenda. The draft's proposed `TI-MOD-*`/`TI-INT-*` labels must be reconciled to native retained/assigned identities; they are not installed canonical identities.

#### Design decisions and accepted exceptions

Retain the existing one-Core-package decision, approved Microsoft runtime closure including ASP.NET in Core, application-owned lifecycle, Type Manager build assets, native MSTest/IDE workflow and no CIS runtime dependency. This proposal grants no new exception or approval and closes no future-scope or release gap.

### Paired component-sheet proposal — unapproved

#### Component catalogue

| Proposed identity | Responsibility/ownership and business mapping | Explicit exclusions |
| --- | --- | --- |
| `TI-MOD-LOGGING` | Core `CIS.Logging`/`CIS.Logging.MicrosoftLogger` and existing environment identity/options; bounded capture, frontend/backend processing, writers and lifecycle observations; proposed `CIS-BR-002/003` | Application lifecycle, deployment recovery, collector SDK, durable business-audit store or new global startup/reset framework. |
| `TI-MOD-TYPE-MANAGEMENT` | Core `CIS.TypeManagement`; prepared CLR identity/membership and cached queries; proposed `CIS-BR-004` | Constructing application/configuration objects, DI registration, dynamic plugins or arbitrary load contexts. |
| `TI-MOD-HEALTHCHECK` | Core `CIS.Healthcheck`; monitor policy/state, snapshots, safe collection/execution-fact binding, formatter and HTTP/pipe integration; proposed `CIS-BR-005/006` | Business callback execution policy, Heartbeat scheduling, generic startup provider, restart decisions or forced termination. |
| `TI-MOD-TYPE-INVENTORY-BUILD` | `CIS.TypeManager.Build`, packaged SDK/targets and generated inventory contract; genuine build-only asset inside Core package; proposed `CIS-BR-004/008` | Runtime application dependency/instantiation, test campaign engine, deployment orchestration or unsupported publish profiles. |

#### Component responsibility profiles

Logging captures detached bounded values and reports lifecycle/output facts separately; preserve 64 KiB entry default, known overrides, bounded identity interning and documented scope/resource-transfer limits. Type Manager fixes trusted prepared definitions and cached mappings without rescanning/constructing application objects. Healthcheck owns state and actual owner leases; reporting never mutates health and empty Healthy does not certify startup. The inventory helper inspects SDK-resolved deployed metadata, generates the host inventory and retains supported build/publish/clean errors.

#### Component interaction catalogue

Proposed links are application/ILogger→Logging (`TI-INT-LOGGING-APPLICATION`), host build→inventory helper→Type Manager (`TI-INT-TYPE-INVENTORY`), application/collector/fact producer→Healthcheck (`TI-INT-HEALTH-APPLICATION`), and snapshot→formatter→HTTP/pipe→consumer (`TI-INT-HEALTH-REPORT`). Each preserves the existing direction, contract, trust, compatibility, bounded failure and verification expectations; final identities come from actual TI adoption rather than a second contract registry.

#### Ownership rules

Keep existing module owners for mutable state/contracts, host ownership for global lifecycle/resources, detached public observations where promised and no reverse dependency merely because modules share an assembly. Tests/tooling/CIS stay outside runtime closure. Examples/probe remain consumers. Splitting modules/packages or implementing future specifications requires separate direction.

#### Component-specific notes and accepted exceptions

Approved ASP.NET integration remains in Core while Healthcheck policy/state stays independent of HTTP implementation types. The Type Manager helper is necessary build machinery. No new exception or approval is asserted. Whole-bundle source and approval hashes must come from supported CIS preparation after actual TI acceptance.

### No-visual-UI direction proposal — unapproved

#### UI applicability

Proposed `UI-Q-001` answer: **No user-facing UI**. Visual questions, graphical screens, shell/navigation, tokens, typography, imagery, responsive layouts and previews do not apply to this product/adoption scope. Logging's incoming frontend is an event-processing stage. Existing API/wire/content/access/operational requirements remain applicable.

#### Constraints and exclusions

Implement no frontend package, brand decision or visual artifact to pass foundation setup. A later graphical surface requires a new scope decision and applicable technical/architecture/UI review. Missing frontend code alone is not acceptance of this proposal.

#### Traceability

Use the accepted product surface and solution bundle with the current native questionnaire. An actually recorded, resolved `TI-Q-002` answer exactly `No user-facing UI`, supported by a human decision or accepted current technical-document authority, can derive `UI-Q-001`. A bare line in this unapproved draft is not that authority. Otherwise record the genuinely authorized answer through `ui-direction questions answer`. Preserve a valid existing accepted-document derivation without demanding a duplicate human answer. Supported no-UI generation supplies `visual_ui:false`, actual solution/questionnaire hashes and `not-applicable` guideline/framework hashes without installing visual profiles.

#### UI-specific decisions and accepted exceptions

No visual UI is the only proposed UI decision. No additional brand/layout/accessibility-screen exception is invented. The generated direction still requires review and explicit approval; proposal text is not that record.

### One bounded adoption task — draft contract, no canonical identity

| Field | Prepared proposal |
| --- | --- |
| Title/outcome | Execute the existing HealthDiagnostic unit slice through CIS, admit its native report, settle ownership and prove unchanged reuse. |
| Identity/status | Proposed logical label `adoption-native-unit`; actual change/task IDs and canonical task path remain unassigned. Draft/unapproved, not Ready/InProgress/Complete. |
| Authority/owner/target | Existing `cislib` authority; sole target `cislib`; one task owner. Actual original allocation authority and delegated controller must be explicitly confirmed; no model/provider/Git username is treated as authority. |
| Category/complexity | Low-complexity configuration/native verification, without a new product feature or broader feature-pack workstreams. |
| Required changes | Preserve minimal import/native workflow; establish legitimate owner and allocation; finalize current material inputs/graph; use the compatible prepared local profile; execute one unit-only invocation; admit real TRX; settle; inspect unchanged status/inventory; update this handoff. |
| Required outputs | Retained actual TRX and native CIS result/input/owner association; settled execution/storage references; read-only current/zero-pending unit result and owned inventory; exact commands/outcomes in existing handoff. |
| Constraints/exclusions | Preserve Core behavior, public contracts, assertions, package/dependency/toolchain pins. No CIS implementation, product feature, coverage/mutation/pilot/canary/stress/release campaign, deliberate variant, replacement collector/archive or unrelated module. |
| Source/impact | Current goal, product decisions, prepared profile and actual HealthDiagnostic/test/config/tool inputs. Fill precise graph roots and impact IDs only from fresh native analysis. |
| Dependencies/gates | Accepted current foundations; fresh graph; real change; dispositioned impacts; actual durable canonical task and approved current plan; supported accounting first use; finite allocation; eligible native task and valid inherited parent if present. |
| Acceptance 1 | Execute existing `HealthDiagnosticRuntimeAcceptanceBoundaryTests.ValidCallerIdentifiersAndRuntimeExceptionProjectionProduceADetachedDiagnostic`. Source inspection finds one MSTest method in this class with Boolean DataRows false/true; expected result is 2 passed / 0 failed / 0 skipped. This is an acceptance target, not an observed result. |
| Acceptance 2 | CIS parses/accepts the nonempty real TRX against current declared inputs and exact canonical work association; discovery/process exit alone is insufficient. |
| Acceptance 3 | Native time and storage settle with no unresolved reservation/recovery state for that execution; necessary current evidence remains protected and owned. |
| Acceptance 4 | Inputs remain unchanged; read-only plan/status reports current unit result, zero pending unit batches and complete declared unit slice. No second native run is used just to prove reuse. |
| Acceptance 5 | Associated artifact inventory reports owned/accounted evidence and finite remaining capacity; historical passes and other test families retain their actual scope/status. |
| Targeted validation | Current strict profile/suite checks and fresh graph; incremental run explicitly `--layer unit`, never `--force`; ordinary category exclusions retained. Inspect real parsed two-case TRX, input digest, work/storage settlement, then only plan/status/inventory. |
| Review/evidence | Independent review is advisory, not approval. Retain criterion/command/actual path/result/owner association in this handoff; do not replace missing evidence with a model success claim. |
| Deferrals/residual risk | Other families remain configured/unverified through CIS; broader release obligations remain open. If native evidence is valid but settlement fails, reconcile that exact execution through supported controls instead of rerunning automatically. |

### Proposed finite allocation — awaiting actual authority and supported entry

| Term | Exact proposed value/effect |
| --- | --- |
| Containing change | 1,200 active seconds, 3 logical attempts, 120 seconds restoration reserve. |
| Single task | 900 active seconds, 2 logical attempts, 120 seconds restoration reserve; fixed limit, no redistribution. |
| Capacity | Before measured consumption/reservations: 1,080 ordinary change seconds and 780 ordinary task seconds; 180 change seconds beyond task + outer reserve and 1 outer attempt remain unassigned. These are estimates for remaining admitted preparation/execution/report settlement, not retroactive zero-cost history. |
| Storage | 6 GiB = `6,442,450,944` regular-file bytes total, including existing declared cache/output; 64 MiB = `67,108,864` restoration bytes. Profile caps remain tighter: 128 MiB per run and 256 MiB retained; they allocate nothing themselves. |
| Measured basis | Existing nine proposed roots contain `4,596,330,147` bytes; UnitTests/bin alone `4,236,769,799` bytes. A 2 GiB proposal cannot fit this existing state and is rejected. 6 GiB leaves `1,779,011,933` bytes after measured owned bytes and reserve, before pending claims/headroom; it is a proposed ceiling, not 6 GiB of necessary new growth. |
| Decision/identity | Actual allocation actor, controller delegation, decision reference and storage-ceiling reference are pending explicit decision. Initial allocation acknowledges unknown historical effort and omits prior revision; updates require the actual revision and preserve consumption/history. |
| Owned roots | `.cis/local` as owned CIS output; exact count-only scratch `bin`/`obj` under each of `implementations/csharp/src/CIS.Core`, `implementations/csharp/tests/CIS.Core.UnitTests`, `implementations/csharp/tests/CIS.Core.TestSupport`, `implementations/csharp/eng/CIS.TypeManager.Build`. All are in the actual authority repository. |
| Cleanup scope | Only specifically delegated superseded disposable CIS output through existing controls. Preserve accounting/adoption/lock, current graph/profile/report and independent holds. Scratch inclusion counts bytes and grants no retention deletion authority; no old output is deleted by this proposal. |
| Other work | Accounting adoption is authority-wide for supported routes. Siblings require their own canonical owner and finite capacity, or valid inherited ownership; this task allocates nothing to them and adopts no engineering-quality policy. |

The current input schema already supports finite time/attempts/reserves, task flags, authority decision/storage references, controllers, historical-unknown acknowledgment and finite `storage` roots/cleanup controllers. It rejects unknown fields. Native admission includes declared writable roots so build growth is counted as well as the TRX. The actual payload can be finalized only with real canonical IDs/authority; no draft marker or proposed opt-in property is inserted into live accounting JSON. The supplied Ubuntu `/data` is ephemeral but is not a registered Windows-authority storage root or an allocation.

### Historical CIS correction proposal — two parts, not implemented at this checkpoint

This is the specification that was subsequently authorized and implemented; the current delivery record below supersedes its implementation-status statements.

**Part A — accounting entry:** extend the existing allocation boundary with explicit authorized accounting-only first use (a proposed name such as `--adopt-accounting` is not delivered CLI syntax). This is a new public CIS option: wire it in `AgentModule.Work.cs` and the `AgentService.AllocateWork` entry check in `AgentService.WorkControl.cs`. Retain original-authority actor/reason/decision provenance and explicit acknowledgment of the authority-wide effect. Resolve the real canonical owner/approved plan/dispositioned impacts and finite storage/time through existing checks. Reuse `AgentWorkLedger.Allocate` and its existing locked ledger/adoption/lock writes and interrupted-write recovery behavior; add only necessary explicit-intent provenance, without a second store or a new cross-file atomicity claim. Reject reset of corrupt/missing adopted state or historical governed-run references. Existing revisions, consumption, reservations, attempts, reserves, history, inherited parents and independent holds remain binding.

**Part B — proportional canonical owner:** extend ad-hoc construction in `PlanningService.cs` to materialize durable catalogue-linked task documents for its bounded accepted rows, retaining scope, exclusions, acceptance/validation, identities and authored evidence. Reuse existing task-contract/catalogue rendering instead of introducing another task store. No feature specification or mandatory coordination/documentation/assurance/delivery feature pack should be necessary merely to obtain this configuration task's owner. Current `plan import-spec` does create durable tasks but unconditionally includes broader feature workstreams; it is not used as a workaround. Preserve normal plan/impact/task approval and eligibility, user edits, unchanged rebuilds and the approved-plan rewrite prohibition.

**Quality-policy separation:** neither operation installs/reconciles full starters nor writes/enables `requireTaskCompletion:true`, engineering-adoption history, coverage/mutation thresholds or completion-quality gates. Installing a provider profile remains insufficient authority. Accounting adoption affects sibling supported direct/story/native routes, not product behavior or approval state.

**Part A validation:** authorized fresh accounting-only adoption against a real approved owner succeeds; absent explicit authority, missing/ambiguous/unsafe owner, unapproved/stale plan, proposed impacts and invalid bounds/roots reject before writes. Engineering-policy bytes/applicability remain unchanged; sibling unallocated work holds; existing accounting update/revision/consumption/reservation/history and corrupt/interrupted-state behavior remain.

**Part B validation:** ad-hoc task materialization yields an actual linked document without unrelated workstreams, preserves author edits/identity/evidence and leaves feature import behavior and ordinary eligibility/approval gates unchanged. Each part requires its own explicit CIS correction decision; neither is implemented under this CISLIB goal.

### Exact staged decisions and subsequent operations

Historical staging sequence: Parts A and B were subsequently authorized. Do not request their implementation authorization again; use the current remaining-decision record below.

1. Review the actual supplied BRD registration and its source assessment. Record the human's chosen assessment and rationale through `brd sources assess` with the current token, then validate. Only when ready, request approval of that exact canonical mapping through `brd approve --reviewer <actual-human> --reason <actual-decision>`; existing source acceptance does not approve it automatically. Rebuild graph as required.
2. Reconcile the supplied TI against that actual accepted BRD; preserve interpreted source facts and resolve only reported necessary new choices with the real human identity/decision. Validate and approve the exact current TI. This approval cannot precede its final upstream-bound payload.
3. Initialize/refine the native solution/component bundle from the accepted TI, incorporating the substantive content above and actual identities; validate both and obtain one exact whole-bundle approval. Draft approval now cannot cover a later unknown hash-bound bundle.
4. Preserve a valid already authorized no-UI questionnaire answer, including supported derivation from accepted current technical-document authority, or record the actual human decision through the supported questionnaire. Unapproved draft text supplies no authority. Generate/validate the short no-UI direction and approve its actual payload. No screens or visual profiles are part of that decision.
5. Decide each separately specified CIS correction part above, or supply a legitimate already-supported canonical owner/accounting route from a verified descendant. No source/quality/approval gate may be waived to simulate support; this goal does not implement either correction.
6. After current foundations and supported task materialization exist, create the bounded adoption change, analyze precise roots, disposition actual impacts, prepare the one actual canonical task and validate/approve its current plan. Present actual findings/IDs/payload before requesting their new decisions; do not promise one blanket approval across unavailable downstream records.
7. Finalize the finite allocation with the actual original authority/controller/decision/storage references and scope acknowledgment, through supported first-use controls. Confirm current owner, remaining time/attempt/storage and inherited parent before admission. Broader engineering-quality adoption is excluded.
8. Execute the unchanged prepared selection once through the verified CIS host with actual association and explicit `--layer unit`; admit the two-case TRX and settle. Then inspect only read-only incremental plan/status and associated artifact inventory. Close step 1 only when current native result, zero pending unit work and settled owned output all pass; otherwise retain the exact missing decision/result and keep it open.

### Actual continuation evidence and remaining first decision

At this prior checkpoint, the verified host was `0.3.0+1eab89f68c635513a20bca8aa1a7ce6ae02cb8e5`, SHA-256 `e2cd83872c92bd41e38cd36a81dd006edfe154be57ef604552255bd2d3ec3cf9`; its source was clean at that revision. No host rebuild was necessary for that checkpoint. CISLIB started clean at the reviewed pushed `f05b02c0` and retained the same minimal import, workspace/repository identity, product source, assertions, packages and native toolchain. There was no inherited `CIS_WORK_*` context or canonical change/task.

These supported operations were actually applied; `cis` is the verified invocation in README:

```text
cis definition documents --workspace . --role business --path docs/specs/business-requirements.md --format json
cis brd reconcile --workspace . --format json
cis definition documents --workspace . --role technical --path docs/specs/technical-intent-spec.md --format json
cis graph build --repo . --format json
cis brd validate --workspace . --format json
cis docs validate --repo . --strict --format agent
cis definition status --workspace . --format json
```

Both document selections returned `loaded`, `applied=true`, no errors. BRD reconciliation returned `review-required`, `applied=true`; its source/body was preserved while CIS wrote real identity/evidence and null approval fields. The final BRD validation returned **exit 5, current=true, Review Required, no content issues or unanswered question rows**. Its only two errors require the assessment and rationale for the same actual source below. The initial authoring draft used an unrecognized table heading and allowed the managed source row's `TODO` rationale to fall under Open questions; the Requirement heading and separate registration section corrected these formatting issues without changing product scope or the managed evidence. Strict documentation validation now passes for **52 documents/entries, zero warnings/errors**.

| Exact source-review field | Current value / concrete decision |
| --- | --- |
| ID/path | `BRD-SRC-77aaeee8b0d1`; `planning/logging/priority-spool/brd-v1.2.md` |
| Content hash | `sha256:1cb6b678d7ea252075ca6cb90a0125df33557c31fb956cf9357475ce5fa36477` |
| Review token | `sha256:562990f0eeb1796c9324efc0a3aca525250840bb677b0681241d40db3082b0b7`; re-read current status before saving a decision |
| Assessment/rationale | Native `Unreviewed` / `TODO`; no human disposition has been selected or recorded. |
| Review context | This original Logging incoming-frontend BRD supplies retained behavior, while its October implementation-status and task instructions are superseded by current decisions/cleanup. The product draft maps it through S04 and `CIS-BR-003`; retain the original and distinguish that source role from current implementation or qualification. |
| Operation after the actual human decision | `brd sources assess --input <reviewed-array.json> --workspace . --format json`; the array uses `id`, the chosen `assessment`, substantive `reason` and current `reviewToken`. This saves source decisions, not BRD approval. Validate before requesting approval of the actual current BRD. |

The TI is a **selected, substantive working draft**, not an imported/current canonical TI. Its explicit component ownership table is source content, not assigned managed identities. Ordinary `technical-intent init` needs the accepted BRD and can then supply actual scope/schema/baseline/approval metadata. The coordinator's review-only `definition prepare --page technical` can import drafts earlier, but also invokes preparation across fifteen observed-reference families and can include placeholder-only dictionaries. That broader inventory preparation is unnecessary for this supplied-document mapping and was not run; no empty dictionaries or visual artifacts were published. Full TI/module/questionnaire/baseline validation remains conditional, not passed by generic document validation.

The prior first-use gap was confirmed at the reviewed [allocation entry](https://github.com/andrewspiteri/CIS/blob/1eab89f68c635513a20bca8aa1a7ce6ae02cb8e5/src/Cis.Modules.Agent/AgentService.WorkControl.cs#L12-L23); the distinct task gap was in [ad-hoc plan construction](https://github.com/andrewspiteri/CIS/blob/1eab89f68c635513a20bca8aa1a7ce6ae02cb8e5/src/Cis.Modules.Plan/PlanningService.cs#L64-L114). Neither rejected operation was retried at that checkpoint. Both capabilities are now implemented as recorded below; a feature pack is unnecessary merely to manufacture an owner.

Ubuntu readiness was inspected over the authorized LAN connection without transferring a repository or running a build/test. `/data` had zero available bytes; installed SDK/runtime were `10.0.301` / `10.0.9`, rather than CISLIB's `10.0.401` / `10.0.12`. The user explicitly permits deleting `/data` contents when required. No cleanup or installation was needed while native execution remains blocked, so neither was performed. Future required builds may use a bounded disposable `/data` directory after exact toolchain provisioning; raw remote output is outside this Windows authority's supported accounting and cannot substitute for the governed local demonstration. Credentials and connection details are not retained here.

`cis generate` is not applicable: this continuation maps existing requirements and prepares development-governance records; it generates no product implementation. Historical cleanup results remain historical. There is still no governed native run ID, accepted adoption TRX, settlement or reuse receipt; other native families and all release obligations retain their previous status.

| Final read-only/setup check | Actual result |
| --- | --- |
| Graph build/status | Exit 0; fresh graph, 19,278 nodes / 73,261 edges, no diagnostics; post-review build `sha256:12a5962826b9a732e910a27335af08b889d96bfacc55c7bc1b994f4e629645fc`. |
| Unchanged local profile | SHA-256 remains `1c36079948f05414698317527c72591140f2b46cdffa17cfb0f6e86d4fed02e4`; installation-specific paths stay ignored. |
| Incremental plan/status | Both exit 0 with no diagnostics; `pendingUnitBatches=1`, `pendingMutationBatches=0`, `executedUnitBatches=0`, `reusedUnitBatches=0`, batch `unitCurrent=false`, `scopeComplete=false`, `qualified=false`; `workCoverage=unaccounted-non-adopted`. |
| Artifact inventory | Exit 0; `ownedStorage.status=unaccounted`, no legitimate allocation association. A successful non-adopted inventory is not settled ownership. Proposed remaining bytes above are estimates, not current capacity. |
| Product/native scope | No implementation, assertion, package/runtime pin, source requirement, existing decision, workspace identity or native profile changed; no CISLIB build/test ran on either host. |
| Review | Source-aware Codex review checked current decisions, cited product sources, selected test and CIS entry/task code; no material finding remained. A separate context-limited reader correctly identified the outstanding source decision, conditional baseline/task/allocation, sibling effects and zero execution/reuse. Three wording clarifications were applied. This is model-assisted review, not human approval. |

The final native read-only commands were `graph status`, `test incremental plan`, `test incremental status` and `artifacts inventory`, with `--repo . --format json` and the same local `--profile` on both incremental commands. Their actual JSON is retained under ignored `.cis/local/adoption-continuation/`; no execution ID or report was fabricated. Existing feedback records preserve command usage.

### Requested Claude review and disposition

The initial mixed private-input review request was rejected before transmission. A narrower public-only review was admitted after visibility checks; the user also explicitly authorized the four-document scope during the run. The actual review used only the BRD, TI, this continuation section and current decisions fetched without authentication from public commit `3456e19c388d8eb617a0c0442264a04cbc93d3d2`; it excluded the private attachment, local research and connection details. Installed Claude CLI `2.1.295` ran with tools/MCP disabled, restricted mode and no session persistence. It returned success using `claude-opus-5-5`. The raw advisory result is retained locally at `.cis/local/adoption-continuation/claude-public-review.json`, SHA-256 `04ec437572ea24c9879b5e882b6e59cd9c247c88f031d9ad10e3529845e64333`.

| Advisory finding | Source-backed disposition |
| --- | --- |
| Build-helper requirement link | Corrected the TI/component mapping to `CIS-BR-004/008` (inventory and release verification), and named the existing helper in BR-004. Examples/fixtures remain BR-007. |
| Class selection versus expected method count | Inspected the actual source: one MSTest method, DataRows false/true. Kept the compatible local class filter unchanged, documented it exactly, and made 2/0/0 explicitly an expected acceptance target. A descendant must recheck class membership. |
| No-UI draft text versus authority | Clarified that a draft line grants no questionnaire/approval authority. Preserved actual CIS-supported human or accepted-document provenance; `TechnicalIntentQuestionnaireService.Assessment.cs` recognizes both. The suggested requirement to repeat every accepted-document fact as a direct human questionnaire answer was not adopted. No UI answer or approval was fabricated. |
| Correction scope/touch points | Made accounting entry and ad-hoc task materialization independently decidable parts of one compact proposal. Named the new public CLI surface and service/planning touch points, separated focused validation, and reused existing locked accounting writes/recovery instead of claiming new cross-file atomicity. Neither part was implemented at that historical review checkpoint; both are delivered below. |
| TI review metadata | Set `last_reviewed: null`; this preparation and model review do not imply human acceptance. |

Also changed S08 to cite the retained handoff rather than relying on an unavailable attachment. The BRD's one native discovered-source row does not cover all S01–S08 mappings: the exact consolidated text must be reviewed against its linked accepted inputs before canonical approval. Source acceptance is preserved; canonical registration is a distinct decision.

Claude's scope was the four published documents, not a fresh inspection of module source, native reports, tool binaries, measurements or CIS implementation. Source-aware Codex checks supplied those narrower factual checks where reported. Its usage metadata is a provider observation, not settled CIS work accounting. No model review supplies human product/plan/impact/UI/allocation approval or a native test result.

After these edits, strict documentation validation still passes for 52 entries/documents, the refreshed graph is fresh with zero diagnostics, and BRD validation still has no content issues and only the same two human source-review errors. Read-only incremental plan/status still reports one pending unit batch and zero executed/reused batches; the native profile is unchanged. The separate unfamiliar reader rechecked its three specific wording findings and found them resolved, with no new material contradiction. No build, test, approval, allocation or native settlement was added by review.

## CIS corrections delivered — current continuation checkpoint

**Parts A and B are implemented; CISLIB's governed demonstration remains open.** This record supersedes the earlier capability/host stopping points, while preserving their historical evidence. The existing allocation proposal above is unchanged and remains a proposal.

The source-review stopping point below was subsequently resolved in [Logging source assessment recorded](#logging-source-assessment-recorded--canonical-brd-decision).

CIS branch: `codex/minimal-accounting-ad-hoc-tasks-2026-11-10`. Part A is [fa38a103](https://github.com/andrewspiteri/CIS/commit/fa38a103a125eaecef2d720de83a176df9520ae9); Part B is [440b5b96](https://github.com/andrewspiteri/CIS/commit/440b5b96f057ce34337bd1ec4a4d82481ea912b6), with bounded review corrections in [30ebacaf](https://github.com/andrewspiteri/CIS/commit/30ebacafcc0e97e2f62ac3da64eca1a7fa91d75c). All three commits are pushed; neither repository was merged or released.

Part A changed `AgentModule.Work.cs`, `AgentService.WorkControl.cs`, `AgentWorkAccountingAdoptionTests.cs`, the work manual and agent-execution skill. Part B changed `PlanningService.cs`, its ad hoc partial, `AdHocPlanningTests.cs`, `FeatureRetirementPreflightTests.cs`, the plan-build manual and bounded-planning skill. Existing task contracts, catalogue and ledger stores remain the canonical machinery.

- **Accounting entry:** `cis agent work allocate <change> --input <allocation.json> --actor <actual-authority> --reason <actual-decision> --adopt-accounting --repo <authority> --format json` is now supported. Explicit first use requires a real eligible canonical owner and finite storage/time/attempt authority. Existing accounting, history, reservations, inherited ownership and independent holds remain binding; sibling work needs its own capacity. This adopts accounting without enabling engineering-quality policy.
- **Ad hoc tasks:** `cis plan build <change> --repo <authority> --format json` now materializes catalogue-linked task documents without a feature pack. Impact-bound identities persist across added/reordered/retired work; retired IDs and catalogue history are not recycled. Existing authored contracts survive rebuilds; changed impact scope, invalid bindings/documents and unsafe paths hold for reconciliation. Ordinary validation/approval checks apply; approved plans remain protected.

| CIS verification | Actual evidence and limit |
| --- | --- |
| Focused corrections | Part A: 28 passed. Final Part B/re-import batch: 22 passed, zero failed/skipped (20 ad hoc cases, one new retirement-preflight case, one existing re-import case). Release builds and scoped whitespace checks passed. |
| Agent regression | Initial 416-case run: 411 passed and 5 infrastructure failures. Short-temp run: 415 passed and 1 remaining failure; that case subsequently passed using the real PowerShell executable. These are scoped observations, not an all-green full-suite run. |
| Planning compatibility | 71 passed / 1 failed of 72. A production-baseline control restoring all changed production files to `1eab89f` with the unchanged existing test host reproduced the feature-import failure; a separate story control passed. This does not turn the failed current run into a pass. |
| Broader Delivery attempt | Timed out at 300 seconds without a TRX. A story-provenance failure was observed and has not been confirmed against baseline; no complete Delivery-suite pass is claimed. |
| Applicable configuration gates | Strict docs: 530; skills: 53; no-model skill audit: 8 pairs, zero issues; test bindings: 3 suites; standards: 13 standards / 98 rules — passed. |

These were direct native checks whose actual TRXs were inspected, not `cis test reconcile` results. That API cannot import these executions outside a workflow; no CIS workflow run ID or settlement was fabricated. CIS correction work does not consume or grant CISLIB's proposed demonstration allowance.

The final native focused report is `review-followup-focused.trx`, SHA-256 `ab75d0f67ab08d288c58f9fbcd04d33e73dc3ee9465d11faa81582cbe8fa62d7`. Strict `agent evidence validate` returns exit 5 with the single warning that a workflow `test reconcile` result/run ID is absent. The direct native results remain useful, but do not satisfy that workflow-evidence gate; no complete workflow or release readiness is claimed.

The actual commands were Release `dotnet build` on Agent.Tests and Delivery.Tests, then `dotnet test --no-build --no-restore` with the focused `WorkAccountingAdoption_`, `AdHocPlanBuild_` and `PlanReimport_` filters. Broader filters/results are preserved separately, including failures and the timeout. Genuine TRXs, command logs and source/report SHA-256 manifests are retained in the CIS checkout under ignored `.cis/local/accounting-task-corrections/`; they are ordinary native evidence, not CIS-accounted runs. The disposable CIS verification copy had no graph generation; its graph-status error is retained, and no graph-qualified workflow claim is made.

Claude reviewed only the publicly accessible CIS diff from `1eab89f` to `440b5b96`, using `claude-opus-5-5` with tools, customizations, MCP and persistence disabled. The advisory output is retained locally (SHA-256 `57860f4d51639bc5670ece22bb19ad6cd1f46819a9bce83f04fd2939a69c90a1`). Source-aware review added the prerequisite hold, unproved-legacy-basis hold and feature-retirement preflight; partial writes now report their actual hold and preservation requirement. Legacy approved row-only plans remain held by ordinary document validation and the approved-plan rewrite restriction; they never supplied an executable canonical owner. Historical feature rendering already includes task-type versions, and existing ledger loading enforces finite storage and incomplete-state holds, so those hypothetical bypass findings required no weaker validation. No cross-file rollback or automatic legacy migration is promised.

The corrected host was built on Ubuntu with SDK `10.0.403`, delivered as 257 portable files / 82,598,013 bytes and verified on Windows. Its version at this checkpoint is `0.3.0+30ebacafcc0e97e2f62ac3da64eca1a7fa91d75c`; verified host SHA-256 is `37d798df581db6f6c4c6bec0f061e9c52041e2435826d58a378d12dbc5cff1a2`, and its Plan module SHA-256 is `4f072d6dc2ec41cc379d2c47e6ceeefd0a7fd4804f583d38596bb96d0953bab3`. This is a CIS producer toolchain, not a retargeting of CISLIB's SDK `10.0.401` or .NET/ASP.NET `10.0.12` pins. Further calls must use the corrected verified host, not the old `1eab89f` executable.

At this checkpoint, CISLIB BRD status was current and had only the two source-assessment/rationale errors for `BRD-SRC-77aaeee8b0d1`. The actual source was `planning/logging/priority-spool/brd-v1.2.md`, SHA-256 `1cb6b678d7ea252075ca6cb90a0125df33557c31fb956cf9357475ce5fa36477`. Its review token was `sha256:562990f0eeb1796c9324efc0a3aca525250840bb677b0681241d40db3082b0b7`.

The concrete request was prepared, **not yet applied at that checkpoint**, at `.cis/local/adoption-continuation/cis-corrections/source-assessment-proposed.json`:

```json
[{"id":"BRD-SRC-77aaeee8b0d1","assessment":"Adopted","reason":"Retain its Logging frontend requirements; historical October implementation status, task sequencing and execution authorization are superseded by current decisions.","reviewToken":"sha256:562990f0eeb1796c9324efc0a3aca525250840bb677b0681241d40db3082b0b7"}]
```

The specific source-assessment authorization and identity questions were pending at that checkpoint and are resolved below. Canonical BRD approval, controller delegation and resource allocation remain distinct decisions. Ordinary TI intake and the existing solution/no-UI drafts can follow their actual upstream approvals; preserve accepted-document derivation of the settled no-UI fact.

Corrected-host incremental plan/status and artifact inventory all return exit 0, but still show **one pending unit batch, zero executed/reused, and unaccounted output**. The local profile is unchanged and the selected class's two Boolean DataRows were rechecked. No CISLIB build/test or native result admission/settlement occurred. At 12:16 UTC on 10 November, the nine proposed regular-file roots measured `4,596,873,608` bytes, including `4,236,769,799` bytes under UnitTests/bin; this is a proposal measurement, not allocated capacity. Confirm actual owned bytes, reservations, authority/controller and decision references before allocating; profile limits and prior campaign allowances grant no capacity.

## Logging source assessment recorded — canonical BRD decision

This checkpoint's canonical BRD decision was subsequently authorized and recorded in [Canonical BRD approved](#canonical-brd-approved--technical-intent-decision).

On 10 November 2026, the user explicitly approved recording `BRD-SRC-77aaeee8b0d1` as **Adopted** and confirmed **Andrew Spiteri** as the human name for approvals explicitly authorized by the user. This establishes the recorded source decision and reviewer identity; canonical baseline approval and controller/allocation authority remain separate.

The verified CIS host `0.3.0+30ebacafcc0e97e2f62ac3da64eca1a7fa91d75c` refreshed `brd status`, confirmed the exact source path/hash and current token shown above, and ran:

```text
cis brd sources assess --input .cis/local/adoption-continuation/source-assessment-approved/assessment.json --workspace . --format json
```

CIS returned `saved`, `applied=true`, exit 0, no errors/warnings. The recorded rationale is exactly: “Retain its Logging frontend requirements; historical October implementation status, task sequencing and execution authorization are superseded by current decisions.” The operation has no actor option; the human authorization is preserved here rather than invented as a native approval field.

The requirements and source mapping remain unchanged. Two surrounding status paragraphs now distinguish canonical approval, source assessment and allocation, and point to native metadata for approval state. They no longer claim that the now-recorded source decision is pending. Source-aware independent review found no product-mapping blocker; the prior content review remains applicable to the retained requirements.

The final review payload is `docs/specs/business-requirements.md`, SHA-256 `b345befc6cd1f201703a5bc01271023f005739ae54830f33117e12d9181a948d`. Native BRD validation reports **valid/current, Ready for Approval, zero errors, zero content issues and zero unanswered questions**. Its 43 nonblocking discovery warnings identify 15,209 symbol/test nodes without confirmed component ownership and 42 existing documents with unregistered `unknown` lifecycle metadata. These warnings remain visible; this registration does not assign component identities or adopt broader metadata policy merely to suppress them.

The specific remaining decision at this checkpoint was approval of that exact canonical product-requirement mapping as Andrew Spiteri, with proposed reason: “Approve this mapping of retained CISLIB requirements and current decisions for canonical CIS registration.” That exact decision was subsequently authorized and applied below. The reviewed payload hash and current validation were checked immediately before the native approval; CIS computed its own approval digest.

Actual operation/validation JSON is retained under ignored `.cis/local/adoption-continuation/source-assessment-approved/`. No technical-intent approval, solution/UI approval, canonical work owner, allocation, native test execution or settlement is inferred from this source assessment. The proposed time/attempt/storage terms above remain unchanged.

## Canonical BRD approved — technical intent decision

This checkpoint's technical-intent decision was subsequently authorized and recorded in [Technical intent approved](#technical-intent-approved--paired-solution-bundle-decision).

The user explicitly approved the exact canonical BRD published at `eb870e250ec6a31e2c8612078f53b7fecbc27ede`, file SHA-256 `b345befc6cd1f201703a5bc01271023f005739ae54830f33117e12d9181a948d`, as **Andrew Spiteri** with the exact reason above. The verified CIS host rechecked that hash and valid/current native validation immediately before running:

```text
cis brd approve --reviewer "Andrew Spiteri" --reason "Approve this mapping of retained CISLIB requirements and current decisions for canonical CIS registration." --workspace . --format json
cis graph build --repo . --format json
cis brd status --workspace . --format json
cis technical-intent init --workspace . --format json
```

Approval returned `approved`, `applied=true`, exit 0 and no errors. CIS recorded `approved_by: Andrew Spiteri`, approval time `2026-11-10T13:17:18.6806330+00:00`, the authorized reason and approved content digest `sha256:6190ff89fbcb18f0e009e516f8cd1bafa6855e5891713c66dbc9eed57e3bc5ce`. After graph refresh, the BRD was **Active, valid and current**. Its existing 43 nonblocking discovery warnings retain their scope from the preceding checkpoint.

Ordinary technical-intent intake returned `initialized`, `applied=true`, exit 0 and no errors. It preserved the supplied narrative and created the supported schema-1 `cislib:spec:technical-intent`, `scope: Workspace`, a managed baseline bound to the actual approved BRD semantic digest, and null technical-intent approval fields. CIS retained the original selected document under ignored `.cis/local/document-import/4c2dc0c2c2e1efb823fc70464e02c1802c01f762354a2cce6e73637e42269008.md`. No full wizard, empty reference dictionaries or invented technical decisions were introduced.

Seven factual/status paragraphs now refer to the actual Active BRD, native approval metadata and delivered CIS accounting/task capabilities. Independent review confirmed the native import preserved the supplied body and that these edits leave the product requirements, four-component ownership table, five-boundary integration table, dependency/toolchain pins, selected native filter and release constraints unchanged. The exact `No user-facing UI` source direction remains available for accepted-document derivation after technical-intent approval; no repeat human answer is requested or fabricated.

The review payload is `docs/specs/technical-intent-spec.md`, SHA-256 `dd13a7bc1a52d82663e7113ac09be5ad65f1f3654d84eadc07412b345e9f4138`. After graph refresh, native technical-intent validation reports **valid/current, Ready for Approval, zero errors and no unresolved decision rows**. Its 15 existing classification warnings describe unverified conditional test/package declarations and fixture/example runner bindings; they do not turn those projects into product modules or establish any test qualification. Strict documentation validation still passes for 52 documents/entries.

`graph validate --workspace . --strict --format json` returns **exit 5**: the graph is fresh and structurally readable with zero errors, but its 43 existing metadata warnings remain (15,209 symbol/test nodes without a confirmed component identity and 42 unregistered `unknown` document lifecycles). Current graph build is `sha256:eff0a9ff0e019ce4cfd8b028c337b492e1290cbc56f0dc11cd63deb92b4bd104`, 19,278 nodes / 73,261 edges. The TI's native approval readiness does not mean this separate strict graph gate passed. No component/lifecycle state was invented to suppress warnings; this checkpoint claims neither a complete governed workflow nor native/release qualification.

The specific remaining decision at this checkpoint was approval of that exact technical mapping as **Andrew Spiteri**, with proposed reason: “Approve this technical mapping of the accepted CISLIB business baseline and retained implementation boundaries for canonical CIS registration.” The user subsequently authorized that exact decision, which was applied below after checking the reviewed file hash and current validation. CIS computed the actual approval digest.

For the subsequent solution draft, native CIS interpretation supports the existing four-component table and supplies its own deterministic component identities. Compare the resulting requirement/integration links to the authored tables: the legacy table projection does not populate per-component requirement IDs, and its integration aliases omit the exact current API/integration heading. Preserve those supplied relationships in the actual bundle's permitted authored sections before presenting that bundle for approval. Do not invent module IDs or expand this supported legacy import into a new schema/framework solely for registration.

Native receipts are retained under ignored `.cis/local/adoption-continuation/brd-approved-ti-intake/`. No solution/UI approval, canonical change/task/plan, controller delegation, work allocation, CISLIB build/test, native report admission or settlement was created by BRD approval or technical-intent preparation. The bounded demonstration and its proposed allowance remain open and unchanged.

## Technical intent approved — paired solution bundle decision

The user explicitly approved the exact technical intent at `6407f3a3ca7b2ff9933ef5af116b73273e5a1561`, file SHA-256 `dd13a7bc1a52d82663e7113ac09be5ad65f1f3654d84eadc07412b345e9f4138`, as **Andrew Spiteri** with the exact reason above. The verified CIS host checked that hash and valid/current native validation immediately before running:

```text
cis technical-intent approve --reviewer "Andrew Spiteri" --reason "Approve this technical mapping of the accepted CISLIB business baseline and retained implementation boundaries for canonical CIS registration." --workspace . --format json
cis graph build --repo . --format json
cis technical-intent status --workspace . --format json
cis solution-design init --workspace . --format json
cis technical-intent questions init --workspace . --format json
```

CIS returned `approved`, `applied=true`, exit 0, no errors. It recorded approval time `2026-11-10T13:31:53.6000483+00:00`, the authorized reviewer/reason and approved content digest `sha256:6d31eb9fb1effa30260edd940f99eba1c27c87512f30ceee847ab5269890b1c5`. After graph refresh, technical intent was **Active, valid and current**. Its existing classification/test-binding warnings retain their original scope.

Native solution initialization returned `initialized`, `applied=true`, exit 0, **valid/current, Ready for Approval**, with no bundle errors/warnings. It created the following actual paired canonical documents, both still Review Required with null approval fields:

| Review payload | Raw SHA-256 |
| --- | --- |
| `docs/architecture/overall-solution-design.md` | `1fc6356bc9585d2387e8d3d66c65188da35ad50bde6cf14fc2c538713c688101` |
| `docs/references/component-sheet.md` | `471f97cb1db49c8cda897728b731446c2435f6e34afa4d705ca5693b81b13a7d` |

The bundle's native technical-intent source version is `semantic-v1:90692b384b0041bb71ed8ae26100042726f34453dc27ead65f89fad14b52695c`. This source-version value and the technical intent's approval digest above are separate native digest contracts, not manually substituted hashes.

CIS assigned four actual component identities from the accepted technical table: Logging/environment `TI-MOD-DOC-2ED774F339B1`, Type Manager runtime `TI-MOD-DOC-D7BF0EECC9BE`, Healthcheck `TI-MOD-DOC-2083BB5AF8F0`, and inventory helper `TI-MOD-DOC-C62609274AA1`. Durable authored notes/profiles preserve their exact BRD authority, ownership/exclusions and all five accepted boundary contracts. Native metadata and managed projections were retained unchanged; notes explain the legacy projection's empty requirement cells and missing structured integration rows. No integration identity, new architecture choice, approval, package or product module was invented. Independent source-aware review found no mapping or authority inconsistency.

After those refinements and graph refresh, final bundle validation again reports **valid/current, Ready for Approval, zero errors/warnings**, while technical intent remains Active, valid and current. Strict documentation validation passes for **55 entries/documents**. Strict workspace graph validation still exits **5** for the same 43 metadata warnings, with zero structural errors and fresh graph `sha256:a16eff19689ace6bf6247e9cd691e54c560062467cf9f69a414cfeb8a1115ad0`, 19,278 nodes / 73,261 edges. This is not a strict graph pass or native qualification.

The technical questionnaire is current with 13 resolved and three unanswered projections (`TI-Q-006`, `TI-Q-012`, `TI-Q-015`). Those are extraction limitations for already accepted data-store, operational and AI scope; they do not create new product decisions or invalidate this supported whole-document technical baseline. `TI-Q-002` is legitimately **Derived / accepted-document / Current**, with null human answer identity/time and retained source evidence. Its recorded answer contains prose rather than the exact scalar consumed by the later no-UI matcher. Preserve that accepted-document record. After the solution bundle is approved, the supported UI questionnaire may transcribe the already-approved **No user-facing UI** fact with accurate attribution and an explicit reference to this technical-intent approval; do not claim that such a transcription is automatic UI derivation or a new human decision. No repeated UI-scope question or fabricated answer is needed.

The specific remaining decision is one approval of the exact overall design and component sheet together as **Andrew Spiteri**. Proposed recorded reason: “Approve this CISLIB overall solution design and component sheet as one canonical bundle preserving the accepted business and technical baselines.” Before applying a decision, recheck both reviewed raw file hashes and current bundle validation; let `solution-design approve` compute and record the actual shared bundle digest. No solution-bundle approval is recorded yet.

Actual operation, validation and questionnaire JSON is retained under ignored `.cis/local/adoption-continuation/ti-approved-solution-intake/`. No UI-direction approval, canonical change/task/plan, controller delegation, work allocation, CISLIB build/test, native TRX admission or settlement was created. The prepared unit profile remains unchanged at SHA-256 `1c36079948f05414698317527c72591140f2b46cdffa17cfb0f6e86d4fed02e4`; the proposed time/attempt/storage terms remain ungranted and unchanged.
