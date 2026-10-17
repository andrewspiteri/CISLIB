# CISLIB static review: subpass 06-verification-governance-governance-4

**Scope:** I reviewed only the files supplied in this bundle, against head `396e87d6` and the pinned working-tree text. Nothing was executed. This is not a whole-change PASS or release qualification.

## Confirmed findings

### F1 — Medium: the coverage input inventory misses inputs that decide which tests run
**Where:** `eng/verify-shipping-coverage.ps1`, about lines 12–29 (building `$inputPaths`) and the later re-hash loop.

**What is hashed:** language-root build files, the checker sources, shipping sources and binaries, files under `tests/` and the test DLLs.

**What the measured families read but nobody hashes:**
- `conformance/logging/admission-cases.json` and `drain-result-cases.json`. `RuntimeContractTests` builds its `DynamicData` rows from these files.
- `contracts/logging/v1/runtime-contract.json`, read by `LoggerFoundationArchitectureTests`.
- `implementations/csharp/verification/contracts/startup/runtime-surface.json`.
- `eng/project-boundaries.json`, `runtime-dependencies.json`, `development-dependencies.json` and `.config/dotnet-tools.json`, used by the architecture policy tests.

**Concrete failure:**
1. Someone edits `admission-cases.json` before or during the run, adding or removing rows.
2. That changes which runtime paths run, so the measured hits change.
3. `inputs.json` and the post-run "Coverage input changed" check cannot detect it.
4. `environment.json` records only `git rev-parse HEAD`, and the checkpoint documents that uncommitted inputs exist.

`verify.ps1` uses `Get-SourceInventory $repositoryRoot`, so the coverage harness is weaker than the Developer profile.

**Fix:** pin these repository-level and verification inputs, for example by reusing `Get-SourceInventory`, and re-check them after the families finish.

### F2 — Low/Medium: the architecture family under coverage reads unpinned Developer artifacts and an unchecked override
**Where:**
- `verify-shipping-coverage.ps1` (no `CIS_ARCHITECTURE_INSPECTION` guard).
- `ArchitectureAllowlistTests.Current()`, which honours the variable.
- `ArchitectureOwnershipTests.Current()` and `RuntimeContractTests.RuntimeSpecificationHasExactMethodsAndImmutableOutputs`.

**Concrete failure:**
- Under coverage, the architecture tests read `.artifacts/csharp/verification/Developer/summary.json`, the inspection and package it names, and `contracts/startup/summary.json`. These come from whatever Developer run happened last, possibly from different source.
- None of them is in `inputs.json`.
- `verify.ps1` refuses to start when `CIS_ARCHITECTURE_INSPECTION` is set; the coverage script does not. If it is set, the allowlist tests use an arbitrary file while the ownership tests in the same run use the Developer summary. One run then mixes two inspections.
- The coverage report's `testsPassed` for this family can therefore rest on facts that are stale or not from the candidate.

**Fix:**
- Reject `CIS_ARCHITECTURE_INSPECTION` in the coverage script.
- Record and hash the consumed Developer artifacts.
- Make `ArchitectureOwnershipTests.Current()` follow the same override as the allowlist tests, or neither should honour it.

### F3 — Low: an incorrect count is copied into coverage evidence
**Where:** `verification/coverage/shipping.json`, line 1, the `exclusions` text.

The text says "Only the **ten** enumerated non-executable source files". The CIS.Core `nonExecutable` map lists **eleven**:
1. EnvironmentKind
2. ConfigurationIssueCodes
3. CoreLogLevel
4. CoreLogState
5. LoggingOutputEnums
6. LogOutputState
7. LogValueKind
8. LogWriteOutcome
9. ILogOutputAdmission
10. ILogWriter
11. TypeManagerFailureKind

`ShippingCoverage` copies `policy["exclusions"]` verbatim into every `coverage.json`, so each observation repeats the wrong number. Correct the text, or derive the count instead of hard-coding it.

### F4 — Low: two fail-closed rows would still pass if the guard they target were deleted
**Where:** `ShippingCoverageTests.InvalidEvidenceFailsClosed`, the `duplicate-point` and `invalid-branch-count` rows, plus the final `ThrowsExactlyAsync<InvalidDataException>` with no message check.

**Why they are masked:**
- **`duplicate-point`:** if the duplicate-identity guard were removed, parsing gives `lt=3` against the native `lines-valid=2`. The "Parsed denominator does not equal the native report totals" guard then throws the same exception type, so the row still passes.
- **`invalid-branch-count` (3/2):** without the guard, `bc = 3 + 2 = 5` against the native `branches-covered=4`. The same totals guard throws, so the row still passes.

The other rows looked at reach their intended guard, or would fail with a different exception type. This is the same class of problem that corrective F-series restored elsewhere with "stable intended-reason comparisons".

**Fix:** assert a stable message prefix for each row, or adjust the native totals so only the targeted guard can fire.

### F5 — Low (another check also covers it): package entry allowlist matching ignores case in `check-dependencies.ps1`
**Where:** the `$PackagePath` block, `-notin $allowedEntries` / `-notmatch` (about the entry loop).

PowerShell's `-notin` and `-notmatch` ignore case. As a result, case-variant extra entries pass this script undetected. Examples:
- `readme.md` alongside `README.md`
- `LIB/NET10.0/CIS.CORE.XML`
- `cis.core.nuspec`
- `_RELS/.RELS`

Only the Core DLL and Type Manager assets have a separate count check using case-insensitive `-eq`, which does catch their duplicates.

The C# inspector's `ARCH-PACKAGE-ASSET` duplicate check, exercised by the `duplicate-asset` tests, covers this in the Developer run. The script's own package check is still weaker than it appears. Use `-cnotin`, and add an explicit case-insensitive duplicate check.

### F6 — Low: reference inventories are compared by count plus membership, not as sets
**Where:** `check-dependencies.ps1`, the `$actualReferences.Count -ne $project.projectReferences.Count` and `$actualPackages.Count -ne $project.packageReferences.Count` checks, and the nuspec dependency count check.

Duplicate items satisfy the count while an expected entry is missing. For example, a project declaring `X` twice where `[X, Y]` is expected passes these checks.
- **Project references:** the restored-graph "Required restored project is missing" check covers this.
- **Packages:** an expected package that arrives only transitively would satisfy `Required restored package is missing`, so it is not fully covered.

Compare distinct sets, and reject duplicate items.

## Parity: deleted Python summarizer vs `ShippingCoverage.cs`

The checks below are equivalent:
- Source inventory and the `bin`/`obj` filter.
- Compile-inventory set equality.
- TRX test outcome.
- The single attachment and identical-hash rule.
- The assembly/owner/non-executable guards.
- Class/source/line identity.
- Branch regex and validity.
- Denominator drift.
- OR-merge of hits and MAX of covered branches.
- The native-totals check.
- The unmeasured-source and no-branch-denominator checks.
- Calibration, the output keys, and exit codes 0 and 2.

The C# version is stricter in these ways:
- It requires exactly one `Counters` element.
- It rejects lines numbered ≤0 and negative hits.
- It enforces a containment root.
- Its regex accepts ASCII digits only.
- Its non-executable check ignores generated files.
- It applies a 60-second evaluation timeout.
- It returns exit 1 for an existing output path, where Python's argparse returned 2 and collided with "not qualified".

**Differences that are not regressions:**
- **Output bytes differ:** there is no trailing newline, `System.Text.Json` escapes characters differently, and doubles format as `1` rather than `1.0`. Hashes of `coverage.json` are therefore not comparable with Python output.
- **CLI form changed:** named arguments became positional. Callers outside this bundle were not checked.
- **Crash path:** a policy file missing a key causes an uncaught `NullReferenceException` and a crash exit, not exit 1. It still fails closed.

## Questions and limits

1. **Temp copies, child processes and coverage attribution.** This is the highest-priority open question.
   - `ShippingCoverage` maps any path containing `/implementations/csharp/` with `LastIndexOf`, without checking the checkout location or the file hash. The Python version did the same.
   - `ArchitectureViolationFixture` builds in `<temp>/repository/implementations/csharp/...`, and its child processes inherit the profiler environment.
   - If `shipping.runsettings` does not restrict modules or child-process collection, execution of temp-copy or fixture binaries would be counted against the candidate's sources. Microsoft code coverage collects child processes by default.
   - The copy-local hash check only covers the four test-host `bin` directories.
   - `shipping.runsettings` was not supplied. Please confirm module path filtering and the child-process setting.
2. `dotnet run --project CIS.DependencyChecker.csproj` builds the summarizer after the integrity re-check, and the hash of the binary that actually runs is not recorded. The project's references, and whether this build can touch the shipping `bin` outputs, are not shown.
3. **Known fixes overlapping this bundle:** only the R1 follow-up touches it, through the `mutable-incoming-accounting` and `incoming-accounting-writer` ownership rows. Whether `IncomingAccounting` is now constructed readonly cannot be confirmed without the Core source and the ownership policy.
4. `planning/cis/evidence/graph-fixes-2026-10-15.json` is treated as frozen history. Its limits are stated and its claims are bounded, so there is no finding.
5. **Missing context:**
   - `shipping.runsettings`
   - `ArchitecturePolicy`, `ArchitectureOwnershipPolicy`, `FocusedTests`, `ProcessRunner`
   - `Evidence.psm1` / `Verification.psm1` (`Get-CaseBindings`, `Read-NativeDiscovery`, `Get-VerificationIssues`)
   - `project-boundaries.json` and the dependency policies
   - the DependencyChecker `.csproj`
   - the case inventory and the Core sources
   - any raw coverage or verification reports

   Without these, the `ARCH-SDK-REFERENCE` rows that mutate Core-target SDK properties, the role assignments and actual execution cannot be judged.