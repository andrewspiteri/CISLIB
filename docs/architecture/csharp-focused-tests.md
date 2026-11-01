# Ordinary builds and focused native tests

Use the pinned .NET SDK **10.0.401** and native runtime **10.0.12** from `implementations/csharp/global.json` and `eng/toolchain.json`. A newer installed patch does not satisfy the deliberately disabled roll-forward policy. Dedicated compatibility tools require their separately pinned .NET 8 tool-host runtime; ordinary tests and the Developer profile do not run that compatibility gate. For a private SDK installation, put that installation first on process `PATH` and set `DOTNET_ROOT` to it so child test processes use the same installation.

From `implementations/csharp`, ordinary commands need no prior package or verification checkpoint:

```powershell
dotnet restore CisLib.slnx --locked-mode
dotnet build CisLib.slnx -c Debug --no-restore
dotnet build CisLib.slnx -c Release --no-restore
dotnet test tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj -c Debug --filter 'FullyQualifiedName~Severity'
dotnet test tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj -c Debug --list-tests
```

The selected SDK supplies build-tool reference assemblies beside its imported `NETCoreSdkBundledVersionsProps` file. The running MSBuild host and its default `MSBuildSDKsPath` are separate facts, especially under Visual Studio. References stay non-copy-local, build-only and outside Core runtime dependencies. Policy checks require the imported SDK path to equal `NetCoreRoot/sdk/NETCoreSdkVersion`, matching pins in global.json and toolchain.json, plus approved reference identities and resolved paths.

Visual Studio needs a version supporting .NET 10 and the selected SDK, with the relevant .NET workload. See Microsoft's [SDK/MSBuild/Visual Studio compatibility table](https://learn.microsoft.com/dotnet/core/porting/versioning-sdk-msbuild-vs). This host now also has Visual Studio 18 Insiders. Its Test Explorer results exposed missing PowerShell and Debug/Release prerequisite mismatches on 17 October. CLI/native test results still do not qualify the IDE UI or design-time behavior. Custom Type Manager helper output layouts remain unsupported with CISTM005.

## Solution selections

`CisLib.slnx` is the ordinary IDE/CLI development selection. Run All contains `CIS.Core.FastTests`, `CIS.Core.FastLoggingTests`, `CIS.Core.FastIsolationTests` and `CIS.Core.FastArchitectureTests`: self-contained native product and source/reflection rules, with no previous Developer checkpoint or manually selected package. The logging host owns whole value, serialization, projection and front-end capture test classes; runtime, queues, writers, files, diagnostics and Healthcheck remain in the main host. The isolation host retains the existing process-global serial fixtures and all their `DoNotParallelize` attributes. These independent hosts can execute concurrently. All original fast cases remain selected exactly once, with a scheduling control in each host. Restore/build normally in Debug or Release, then `dotnet test CisLib.slnx -c Debug` (or Release). `-m:1` is available on constrained hosts but serializes independent project work.

`verification/CisLib.Verification.slnx` exposes complete product Unit/component, Integration, Architecture/package/compiler, Compatibility, Business and checker tests. Its original Unit project retains forced-GC/lifetime and OS pipe probes, observations and stress work outside the fast selection. `CIS.Verification.Tests` references the real checker. Ordinary fast projects compile no checker implementation source. Complete Integration tests and the real HTTP fixture retain only the narrow child-mutation protocol/support links needed to prove activation inside an actual child. Required release obligations remain unchanged. The root `eng/verify.ps1` prepares matching artifacts and runs the project/family checkpoint selected by its profile. That checkpoint does not consume the standalone [Healthcheck composite mutation acceptance](../../verification/healthcheck-grouped-acceptance/README.md) result or establish complete six-family release qualification. Run the explicit module acceptance commands separately and retain all remaining release gates. The complete solution excludes the extra fast assemblies so they are not duplicated in a checkpoint.

The ordinary solution projects select `verification/fast.runsettings`: eight method workers, with every explicit `DoNotParallelize` boundary retained. For the complete fast edit loop, run these commands from the repository root:

```powershell
pwsh -NoProfile -File eng/test.ps1 -Fast -Configuration Release
pwsh -NoProfile -File eng/test.ps1 -Fast -Configuration Release -NoBuild
```

The first command builds the development runner and incrementally builds the ordinary solution with locked restore. The second runs already built assemblies directly, with no restore, MSBuild invocation or historical checkpoint. Both execute every case in all four selected assemblies through the pinned SDK test platform, one assembly per distinct native test host. The fast runner uses 22 method workers in Main, 4 in Logging and 4 in Architecture; the existing Isolation serial boundaries remain intact, allowing 31 active test methods across the four hosts. Generated per-owner settings and raw TRX reports are retained beneath that invocation's `.artifacts/csharp/fast/<id>/` directory. Empty, failed, skipped, stale or wrong-owner results fail the command, as does a missing or shared host identity. `-Fast` has no class/category/filter selector. Debug remains supported, and `dotnet test CisLib.slnx` continues to work for IDE/CLI use. Timings must be measured for the current source and command on the named host; historical measurements and named diagnostic profiles grant no current timing credit. Mutation projects retain their separately serialized scheduling.

Use `pwsh -NoProfile -File eng/regression.ps1` for required current-platform non-mutation regression. It preflights the exact SDK/subject/API tool-host runtimes and prepares current inputs once, including the four existing Fast outputs. Compiled method signatures and test-attribute blobs partition identical Unit/Architecture methods into the Fast hosts; additional partial-class methods, differing attributes and ambiguous identities remain in their original host. Every original case executes freshly once. All six original scheduling controls remain, with four additional controls identifying the real Fast hosts; raw TRX files remain separate. The default native budget and engineering target are 300 seconds after preparation. Residual hosts use four method workers; Fast hosts reuse the supported 22/4/8/4 settings and existing Isolation serial boundaries. Residual Unit starts alongside the Fast hosts through the selected project-worker limit (maximum four active hosts). The remaining original owners become eligible only after residual Unit finishes; Integration and Verification then take priority over a queued Fast Architecture host, sharing the same native deadline while any Fast tail completes. Packaged logging cases own their process paths and pipes and may run concurrently after their shared private-feed restore/build initialization. `-PreparationRoot <current-preparation-directory>` reuses an explicitly supplied successful eleven-stage preparation: source membership/bytes, package/Core identity, compiler inputs, architecture inspection and current native binaries are still validated before native execution and rechecked afterwards. Missing or changed prerequisites fail before the matrix; no previous checkpoint is selected automatically. The output must be outside that prepared-input directory. Preparation and its build/pack work are reported separately; a reused preparation performs zero full-solution builds.

For example, after preparing the current source once with `pwsh -NoProfile -File eng/verify.ps1 -PrepareOnly -OutputRoot .artifacts/csharp/current-preparation`, run `pwsh -NoProfile -File eng/regression.ps1 -PreparationRoot .artifacts/csharp/current-preparation -NativeWorkers 4`. Four project workers require a suitable measured host; the default remains one. Incomplete or failed suites do not pass. Select observations/stress explicitly for their additional budgets; coverage, mutation and full release requirements remain separate. The retained09g Linux run selected one project worker and a1,500-second native ceiling; that historical result did not meet the five-minute target.

`-NativeWorkers 2`, `3` or `4` permits that many independent native suites on a suitable host, with a single shared 300-second deadline. Ordinary launchers retain one frozen checker runtime. Actual fallback bootstraps use isolated source copies; the copied-reader control explicitly clears the override for its source-independent invocation. Preparation remains one coordinated build. Its `maintained-csharp-verification-inputs/v1` identity includes current C# source/test/build/configuration, `eng`, contracts, conformance, C# compatibility, documentation, relevant root files and the small explicit plan/baseline/native fixtures read by current tests, including dirty/new inputs. It excludes historical archive traversal, other languages and generated build/test outputs. Added, deleted or changed maintained inputs invalidate the result. Complete passing suites that exceed the native deadline retain their passes while failing the budgeted command. The general source-inventory/source-replay route retains full repository semantics; this current verification identity makes no historical replay claim.

Use `eng/test.ps1 -Project FastTests`, `-Project FastLoggingTests`, `-Project FastIsolationTests` or `-Project FastArchitectureTests` for ordinary focused work. Select `UnitTests`, `IntegrationTests` or `CIS.Verification.Tests` explicitly for their additional work; a fast pass cannot qualify those unexecuted obligations. Source ownership is explicit in `tests/VerificationSuites.props`. Large-boundary and forced-GC methods live in `.Qualification.cs` partials: their small direct counterparts remain in the fast assemblies and their complete original methods execute in UnitTests. `PerformanceObservation` and `ExtendedStress` are explicitly selectable categories; the ordinary non-mutation regression filter is `TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress`. Run those extended categories with a separate stated budget; Release obligations retain them.

## The edit loop

From the repository root:

```powershell
pwsh -NoProfile -File eng/test.ps1 -Project UnitTests -Class CIS.Tests.UnitTests.FocusedTestsTests
pwsh -NoProfile -File eng/test.ps1 -Project UnitTests -Category FocusedLoop
pwsh -NoProfile -File eng/test.ps1 -Project UnitTests -Filter 'FullyQualifiedName~Severity|FullyQualifiedName~TypeManagerInput'
pwsh -NoProfile -File eng/test.ps1 -Project IntegrationTests -Class DiscoveryProcessTests -Configuration Release
```

`-Project` accepts a registered family short name, full project name, or its language-root-relative path. Omit the selector to run that entire project. `-Class` uses native FullyQualifiedName containment followed by a dot; give the full class name when short names are ambiguous. `-Category` uses native TestCategory equality. Use `-Filter` for combined VSTest expressions; the launcher does not implement a filter language. Choose at most one selector. `-TimeoutSeconds` defaults to 300 and bounds each native build/test child; timeout is a failure.

The thin PowerShell entry builds the existing development checker. Normal incremental build servers remain enabled; coordinate shared checkout builds rather than running a focused command concurrently with a checkpoint. Its C# command incrementally builds only the selected project and dependencies, then uses ordinary `dotnet test --no-build --no-restore`. Each invocation owns a fresh `.artifacts/csharp/focused/<id>/` directory. After validating a passing run, it keeps only `summary.json` with command arguments, selected count, actual native case names, method identities, sources, outcomes and timings; build/test logs, native `selected.trx` and test attachments are disposable and removed. The summary marks those artifacts as cleaned, rather than advertising a locally available native report. Empty selection, missing/malformed/stale native results, failed or skipped cases, inconsistent counters/identities, timeout or nonzero tool exit fail the command.

A failed run retains available diagnostics for investigation: process capture keeps at most 64 KiB per output stream, preserving the beginning and end and recording total byte counts and truncation flags. Logs mark omitted content explicitly, including bounded partial output on timeout. Native TRX is limited to 8 MiB; an oversized TRX fails validation and is discarded with an explicit unavailable state. Other native attachments are disposable. The summary never credits a partial case list as passing. Cleanup covers only that invocation's new GUID directory and never prunes other runs, sources or caches. Failed diagnostics remain transient: remove them explicitly when fixed or superseded; the runner does not infer that a later pass resolved an earlier issue.

The summary measures selected-project build and combined native discovery/execution/report time separately. Tool bootstrap and PowerShell startup are outside that timer; compare total command wall time separately, on the same host, and label cold setup versus an unchanged incremental run. Native VSTest discovers and executes the selection; this command does not claim an independent discovery census or release evidence reconciliation.

Unselected cases and families, package/API and architecture checkpoint prerequisites, coverage, mutation, performance and release qualification remain unexecuted. Do not run the comprehensive Developer profile after every small edit. Run it at a coherent checkpoint, and retain all required release gates. The subsequent native verification migration retired maintained Python scripts; historical snapshots remain preserved.

## Healthcheck mutation assembly

An isolated Healthcheck Unit mutation build can set both `CisMutationTesting=true`
and `CisHealthcheckMutationCohort=true`. The Unit project then compiles its whole
`Healthcheck/` tree plus the shared, later-linked `TestAssembly.cs`, retaining
the existing Core/TestSupport references and package closure. Ordinary builds
keep their complete source set. Use a separate source/output tree for this mode;
its assembly is the selected Healthcheck family rather than the ordinary Unit
family consumed by full-checkpoint `--no-build` runs.

The initial private validation matched all1,396 frozen Healthcheck case
identities and passed every case. It also checked all105 Unit,149 Core and12
TestSupport PDB documents and the exact100 Healthcheck source files plus shared
assembly setup. This establishes the selected compilation's correctness; cold
Stryker collector startup and completed-host throughput still need calibration
on the selected mutation host. The property does not select mutants, alter an
assertion, set a timeout or satisfy mutation/release qualification.

The Integration project supports the same two properties. It retains the
whole `Healthcheck/` tree, the three existing child-recorder links,
`JsonlInputSelector` and the later-linked assembly setup. A private build and
unfiltered discovery preserved the frozen 121 Pipes and 61 ASP.NET identities,
181 unique cases sharing one scheduling witness. Its default 202 compile
items remained byte-identical; the opt-in compilation has 34 items. All 203
Integration/Core/TestSupport/receiver PDB documents matched their sources.
These are build and discovery checks; Linux execution and performance still
need their own evidence. Use isolated outputs for both opt-in projects.

## Tests with explicit checkpoint prerequisites

Most unit tests build directly. Integration/business projects build and copy their ordinary DiscoveryHost dependency. Some Type Manager process/package tests and architecture tests inspect externally prepared artifacts; a successful project build alone does not prepare those artifacts. Read the selected fixture's prerequisites before running it. Compatibility currently has no historical publication matrix and an empty selection must fail rather than count as passing.

For the dependency-policy class alone, build the solution in Release, pack the current Core into a dedicated directory, then collect a fresh native architecture inspection:

```powershell
# From implementations/csharp; use a fresh output directory for each candidate.
dotnet pack src/CIS.Core/CIS.Core.csproj -c Release --no-build --no-restore -o ../../.artifacts/local-policy/package
dotnet eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll architecture . ../../.artifacts/local-policy/package/CIS.Core.0.1.0-dev.1.nupkg ../../.artifacts/local-policy/inspection
$env:CIS_ARCHITECTURE_INSPECTION = (Resolve-Path ../../.artifacts/local-policy/inspection/inspection.json).Path
try {
    dotnet test tests/CIS.Core.ArchitectureTests/CIS.Core.ArchitectureTests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~ArchitectureAllowlistTests'
} finally { Remove-Item Env:CIS_ARCHITECTURE_INSPECTION -ErrorAction Ignore }
```

Developer and Release checkpoint entry points reject a nonempty override before changing reports. That environment override is only a fixture input for `ArchitectureAllowlistTests` and `ArchitectureOwnershipTests`; it does not approve a checkpoint or bypass the inspection's policy. Without it, qualification requires explicit `CIS_VERIFICATION_ROOT` and matching `CIS_VERIFICATION_REPOSITORY` for the currently prepared candidate; a previous Developer checkpoint is never selected automatically. Other architecture classes retain their own explicit package/compiler/evidence prerequisites. Do not overwrite existing checkpoint reports to run a selected test.

## Test Explorer prerequisites

Install PowerShell 7.5 or later for the Windows user (for example, `winget install --id Microsoft.PowerShell --exact --scope user`), as well as the pinned SDK/runtime. Codex's private PowerShell directory is not a normal host installation and must not be relied on by Test Explorer.

Open the solution normally, build it and run tests in Test Explorer. The shared C# MSTest assembly initializer in `tests/TestAssembly.cs` prepares each test host automatically:

- On Windows, append the standard PowerShell 7 installation directory and user WindowsApps directory only when they contain `pwsh.exe` and are absent from the inherited PATH. Existing PATH entries keep precedence. This covers an IDE inheriting only the machine PATH while PowerShell is installed for the user.
- Default `UseSharedCompilation=false` and `MSBUILDDISABLENODEREUSE=1` for isolated fixture builds. Existing nonempty values are preserved.

These are test-process settings; no special Visual Studio launch command or registry change is needed. The installed tools are still prerequisites. Test startup does not install or download anything. The earlier [environment diagnosis](../../planning/evidence/corrective-visual-studio-environment-2026-10-18/README.md) records the initial workaround; the [automatic setup correction](../../planning/evidence/corrective-visual-studio-normal-launch-2026-10-18/README.md) supersedes that launch requirement.

Ordinary test runs default to four MSTest workers, matching the Developer runner's bounded settings. Developer's shared session budget is ten minutes because the complete Windows integration family exceeds five minutes; architecture retains its separate twenty-minute budget.

Debug packaged logging tests create a fresh package from their current project build under their own candidate directory, then verify its assembly hash and consume it from an isolated local feed. Release runs retain the preselected package and reject stale bytes. Architecture tests that inspect compiler-control evidence check the Release assembly that produced that evidence; a Debug test host is not that candidate. The full Developer checkpoint remains the supported preparation for retained architecture/compiler evidence.

Compatibility cases also need the pinned ApiCompat 10.0.401 entry DLL and .NET runtime 8.0.31 on its selected host. The SDK 10.0.401/runtime 10.0.12 installation alone does not supply that .NET 8 tool host. Existing `CIS_COMPAT_API_TOOL` and `CIS_COMPAT_API_DOTNET` overrides can select an explicitly prepared tool/host. A local-tool resolver may reuse an earlier custom package cache, so a successful `dotnet tool restore` alone does not prove the entry DLL exists under the test process's default NuGet cache. See [toolchain qualification](csharp-toolchain.md) for the isolated restore procedure and pins.
