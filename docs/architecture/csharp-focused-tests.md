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

Developer and Release checkpoint entry points reject a nonempty override before changing reports. That environment override is only a fixture input for `ArchitectureAllowlistTests` and `ArchitectureOwnershipTests`; it does not approve a checkpoint or bypass the inspection's policy. Without it, the class uses the Developer inspection recorded by the existing checkpoint summary. Other architecture classes retain their own explicit package/compiler/evidence prerequisites. Do not overwrite existing checkpoint reports to run a selected test.

## Test Explorer prerequisites

Install PowerShell 7.5 or later for the Windows user (for example, `winget install --id Microsoft.PowerShell --exact --scope user`), as well as the pinned SDK/runtime. Codex's private PowerShell directory is not a normal host installation and must not be relied on by Test Explorer.

Open the solution normally, build it and run tests in Test Explorer. The shared C# MSTest assembly initializer in `tests/TestAssembly.cs` prepares each test host automatically:

- On Windows, append the standard PowerShell 7 installation directory and user WindowsApps directory only when they contain `pwsh.exe` and are absent from the inherited PATH. Existing PATH entries keep precedence. This covers an IDE inheriting only the machine PATH while PowerShell is installed for the user.
- Default `UseSharedCompilation=false` and `MSBUILDDISABLENODEREUSE=1` for isolated fixture builds. Existing nonempty values are preserved.

These are test-process settings; no special Visual Studio launch command or registry change is needed. The installed tools are still prerequisites. Test startup does not install or download anything. The earlier [environment diagnosis](../../planning/evidence/corrective-visual-studio-environment-2026-10-18/README.md) records the initial workaround; the [automatic setup correction](../../planning/evidence/corrective-visual-studio-normal-launch-2026-10-18/README.md) supersedes that launch requirement.

Ordinary test runs default to four MSTest workers, matching the Developer runner's bounded settings. Developer's shared session budget is ten minutes because the complete Windows integration family exceeds five minutes; architecture retains its separate twenty-minute budget.

Debug packaged logging tests create a fresh package from their current project build under their own candidate directory, then verify its assembly hash and consume it from an isolated local feed. Release runs retain the preselected package and reject stale bytes. Architecture tests that inspect compiler-control evidence check the Release assembly that produced that evidence; a Debug test host is not that candidate. The full Developer checkpoint remains the supported preparation for retained architecture/compiler evidence.

Compatibility cases also need the pinned ApiCompat 10.0.401 entry DLL and .NET runtime 8.0.31 on its selected host. The SDK 10.0.401/runtime 10.0.12 installation alone does not supply that .NET 8 tool host. Existing `CIS_COMPAT_API_TOOL` and `CIS_COMPAT_API_DOTNET` overrides can select an explicitly prepared tool/host. A local-tool resolver may reuse an earlier custom package cache, so a successful `dotnet tool restore` alone does not prove the entry DLL exists under the test process's default NuGet cache. See [toolchain qualification](csharp-toolchain.md) for the isolated restore procedure and pins.
