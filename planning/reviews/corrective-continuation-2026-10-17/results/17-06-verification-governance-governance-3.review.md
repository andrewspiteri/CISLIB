# CISLIB review: 06-verification-governance-governance-3 (static, read-only)

**Scope:** the 17 assigned files plus the four inlined context files, read as data. I ran no code, tests, tools or agents. This review gives no whole-change PASS and no release qualification. JSON files arrived minified on one line, so locations are given as key paths and other locations as symbol names.

## Known-fix overlap check

- **F5/F6 evidence labels:** fixed in both maps that were changed.
  - `file-recovery-qualification-map.json` and `file-rotation-qualification-map.json` now carry `currentFailureEvidence`.
  - That field separates the unreadable-member preflight from actual post-intent deletion denial, which is covered by `rotation-denied`/`recovery-denied`.
  - The historical `*-permission` cases stay under `historicalSupersededCases`.
  - This is consistent with the schema-2 intent-first text in `runtime-contract.json` (`fileRecovery.interruptedTransitions`).
  - I could not verify that `FileGroupTransition.Begin` really fails before intent publication, because its source was not supplied.
- **T2 timing definition:** fixed. `CIS.DependencyChecker/README.md` now defines `processWallMilliseconds` and says it is not comparable with the retired Python timer. This matches `corrections.md`.
- **README counts** agree with `corrections.md`:
  - 15 scenarios plus 2 encoding checks make the 17 runtime checks.
  - 7 measurement controls, 66 example checks, and 12×4 measured runs plus 4 warmups make 52 processes.

## Confirmed findings

### 1. Low: rotation map cites a recovery case while its limits disclaim recovery, and the corrective lane has no structured binding
**File:** `conformance/logging/file-rotation-qualification-map.json`
**Location:** `criteria[WP05-S06-AC03|AC04].nativeLanes.corrective` and `.limits`

- AC03 and AC04 now cite `corrective: ["rotation-denied","recovery-denied"]`.
- The same criteria's `limits` still say "no … interruption recovery or release claim".
- The new `currentFailureEvidence` says those cases "retain intent/data and recover after access is restored". The map therefore both cites recovery evidence and disclaims it.
- Unlike the recovery map, which has a `lanes.corrective` entry pointing at `LoggingReviewProcessTests.cs`, this map has no `lanes` object. Its only structured verifier is `nativePolicyVerification.processSource` (`FileRotationPolicyProcessTests.cs`). The link from the `corrective` lane to `LoggingReviewProcessTests` exists only in prose.
- **Concrete failure:** a reader binding lane ids to verifiers from this map's own fields cannot resolve `corrective`. A human reader gets contradictory scope text for AC03 and AC04.
- **Fix:**
  - Narrow the AC03/AC04 `limits`, for example "no S07 interrupted-transition qualification; corrective deletion-denial resume is cited as supplemental".
  - Add a structured corrective verifier binding in the same form as the recovery map.

### 2. Low: the architecture inspection records HEAD as source identity with no dirty-tree indicator
**File:** `implementations/csharp/eng/CIS.DependencyChecker/ArchitectureInspection.cs`
**Location:** `Write`, report field `sourceCommit = git rev-parse HEAD`

- The report hashes the package, the project/assets/lock files, nine policy inputs and the compiled assemblies.
- It identifies source only as the HEAD commit, without `git status` or a source-inventory hash.
- The checkpoint record says the verified build used HEAD plus "five declared pre-existing uncommitted inputs".
- **Concrete failure:** an `inspection.json` produced from a modified working tree attests a commit whose sources differ from those that produced the inspected DLL. `ArchitectureAllowlistTests` can consume such a file through `CIS_ARCHITECTURE_INSPECTION` (`csharp-focused-tests.md`).
- **Fix:** do one of the following:
  - record a dirty flag and the modified paths;
  - record the existing source-inventory hash;
  - rename the field to make clear it is HEAD only and not source identity.

### 3. Low: the reconciliation text understates the mutable static `EnvironmentOptions` surface compared with the enforced policy
**Files:**
- `conformance/logging/functional-reconciliation.json` (`amendments[1]` and `architectureRules[2].currentDirection`)
- `ArchitectureOwnershipPolicy.cs` (`setup = ["EnvironmentKind","CommitId","BuildId"]`)
- `docs/architecture/csharp-ownership-boundaries.md`, paragraph 2

**The inconsistency:**
- The reconciliation describes `EnvironmentOptions` as readonly facts "with user-selected mutable logging settings".
- The checker permits three mutable static auto-property backing fields with setters on `EnvironmentOptions` itself, beyond `Logging`.
- The ownership doc confirms these remain "ordinary mutable setup properties".

**Why it matters:**
- The user decision as supplied is "static EnvironmentOptions with readonly constructor defaults and overrides".
- Either that decision covers the three mutable identity setters, in which case the reconciliation and amendment text is incomplete, or it does not, in which case the policy is too permissive.

**Fix:** state the three mutable setup properties explicitly in the reconciliation, or remove the setters.

### 4. Low: the repository profile has an unresolvable duplicate root and appears to omit components
**File:** `docs/cis/references/repository-profile.md`

**Duplicate root:**
- `cis-dependencychecker` and `logging-wire-examples` both use root `implementations/csharp/eng/CIS.DependencyChecker`.
- The stated tie-break, "More specific component roots take precedence", cannot separate identical roots. Graph ownership of `JsonlInputSelector.cs` is therefore ambiguous.

**Missing components:**
- The profile lists ten projects and no Type Manager build-tool or example components. The bounded context shows they exist:
  - `ArchitectureOwnershipPolicy.AssertPackage` requires `tools/net10.0/CIS.TypeManager.Build.dll`.
  - `ArchitectureInspection` requires every packed DLL to match a unique evaluated project by `AssemblyName`.
  - `ArchitecturePolicy` has a `build-tool` role and an `example` role using `Microsoft.AspNetCore.App`.
  - The checker README references `implementations/csharp/examples/type-manager`.
- These fall under the `csharp-tooling` fallback, which carries the wrong roles.

**Fix:** give `logging-wire-examples` a file-level or distinct root (or merge it), and add components for the Type Manager build helper and examples. If the solution really has only ten projects, say so and still map the other directories.

### 5. Info: the graph guide's "next task" wording is stale
**File:** `docs/cis/graph-context.md`, section "Find context before a task"

- "The last command locates the next exception task's Draft route" presents WP04-S02-T04 as the next task.
- The current reconciliation is at WP07, and the roadmap goal is paused.
- The opening "at initialization" wording is historically qualified, but this sentence reads as current guidance. Reword it as historical.

### 6. Info: the selector wording contradicts itself
**File:** `docs/architecture/csharp-focused-tests.md`

- The doc says "Omit the selector to run that entire project" and also "Choose exactly one selector".
- The code (`FocusedTests.Parse`) enforces at most one. Use "at most one".

## Questions and limits (not confirmed defects)

1. **Nested and output-namespace coverage in `ArchitectureOwnershipPolicy`.**
   - The runtime caller-state guard and the contract→output edge rule filter by `namespace`.
   - In ECMA metadata, nested types usually have an empty namespace. If `AssemblyInspection` (not supplied) reports them that way, nested runtime and output types are not checked. That would contradict the doc's "including nested types".
   - Output implementations outside `CIS.Logging.Runtime`, `CoreLogRuntime`, `CoreLogger` and `MicrosoftLogger.*` would not be flagged as outputs.
   - Please confirm the namespace reporting and the output type namespaces, and whether one of the 26 compiled-fact negatives covers a nested type.
2. **Empty `caseIds` in `functional-reconciliation.json`.** All File policy entries, several Delivery entries and the integration scenarios "simultaneous console/file outputs", "graceful stop", "deadline reporting" and "burst load beyond capacity" have no required-case binding. Their credit depends only on method names. Is method existence or execution reconciled elsewhere (T05)?
3. **Plausibility of some scenario mappings.**
   - "burst load beyond capacity" maps only to `ProviderInterestAndRejectionUseActualCore`.
   - "Unicode paths" cites `FormatSwitch…` and `MissingInventory…` methods.
   - The fixture catalogs needed to confirm Unicode or over-capacity inputs were not supplied.
4. **Architecture levels 2 and 3.** They cite identical support. Level 3 omits `PackageRejectsBundledTestAssembly` (ARCH-PACKED-ASSETS) and the positive inspection itself.
5. **Executed-evidence claim without a report reference.** The startup map's `"T03 native27modes reexecuted with current assemblies"` has no report pin in this map.
6. **Possibly stale Python claim.** The `csharp-tooling` component still declares `python`. I cannot confirm whether any Python remains after the reader migrations.
7. **Command path in the graph guide.** `graph-context.md` tells users to run `eng/replay.ps1` from the repository root, but links to `implementations/csharp/eng/replay.ps1`. I cannot confirm that a root wrapper exists.
8. **`FocusedTests` failure handling.**
   - Exceptions outside its catch list are not caught; examples are `UnauthorizedAccessException`, and `OperationCanceledException` if `ProcessRunner` (not supplied) uses it on timeout.
   - Such a run still exits nonzero, but writes no `summary.json`. That weakens the doc's "retains native logs/reports" claim, though it cannot create a false pass.

## No findings within supplied scope

I found no actionable defects in these files:

- `eng/test.ps1`
- `FocusedTests.cs` (TRX counter, identity, staleness and zero-selection checks are consistent with the doc)
- `JsonlInputSelector.cs` (read-only; rejects a pending transition, partial or CR lines, BOM, duplicate keys, non-canonical ids and links; excludes damaged members; documented as requiring a stopped group, with no lease check)
- `Program.cs`
- `ArchitecturePolicy.cs`
- `file-recovery-qualification-map.json`
- `file-startup-qualification-map.json`
- `file-timer-qualification-map.json`
- `CIS.DependencyChecker/README.md`

The mutation arithmetic in the reconciliation is also internally consistent: 71/130 = 54.62%.

## Missing context

- `AssemblyInspection.cs` and `ProcessRunner`.
- `eng/project-boundaries.json`, the runtime/development dependency files and `toolchain.json`.
- Every cited test, fixture and reader source.
- The processing-policies and file-policy guides that were also relabelled under F5/F6.
- `planning/decisions.md` and `plan-amendments.md`.
- Raw TRX, summaries and logs.

This subpass contained no deleted Python readers, so I could not assess parity between them and the native replacements here. Nothing here establishes execution, complete case parity or whole-module correctness.