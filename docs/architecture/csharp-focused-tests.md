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

The thin PowerShell entry builds the existing development checker. Normal incremental build servers remain enabled; coordinate shared checkout builds rather than running a focused command concurrently with a checkpoint. Its C# command incrementally builds only the selected project and dependencies, then uses ordinary `dotnet test --no-build --no-restore`. Each invocation owns a fresh `.artifacts/csharp/focused/<id>/` directory with command arguments, build/test logs, native `selected.trx` and `summary.json`. A successful summary contains the actual native case names, method identities, sources and outcomes. Empty selection, missing/malformed/stale native results, failed or skipped cases, inconsistent counters/identities, timeout or nonzero tool exit fail the command. A failed run retains native logs/reports where produced, without crediting a partial case list as passing.

The summary measures selected-project build and combined native discovery/execution/report time separately. Tool bootstrap and PowerShell startup are outside that timer; compare total command wall time separately, on the same host, and label cold setup versus an unchanged incremental run. Native VSTest discovers and executes the selection; this command does not claim an independent discovery census or release evidence reconciliation.

Unselected cases and families, package/API and architecture checkpoint prerequisites, coverage, mutation, performance and release qualification remain unexecuted. Do not run the comprehensive Developer profile after every small edit. Run it at a coherent checkpoint, and retain all required release gates. The subsequent native verification migration retired maintained Python scripts; historical snapshots remain preserved.

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

Install PowerShell 7.5 or later for the Windows user (for example, `winget install --id Microsoft.PowerShell --exact --scope user`), as well as the pinned SDK/runtime. Codex's private PowerShell directory is not a normal host installation and must not be relied on by Test Explorer. Restart Visual Studio if an existing session has an older PATH. Ordinary test runs default to four MSTest workers, matching the Developer runner's bounded settings. Developer's shared session budget is ten minutes because the complete Windows integration family exceeds five minutes; architecture retains its separate twenty-minute budget.

The 18 October saved IDE run has 20 failed case records across ten methods, all unable to start `pwsh`. The running IDE and its Explorer parent have only the machine PATH; the installed user PowerShell alias directory is absent. Merely reopening the same shortcut can inherit that environment again. A notification to refresh the environment did not change their observed PATH values. No registry PATH values were changed.

Close Visual Studio after saving your work, then run the following in PowerShell to reopen this checkout with the existing machine and user PATH values. These settings apply to this terminal and the IDE it launches. The compiler-isolation settings also avoid the independently reproduced fixture-bootstrap timeouts; they do not extend test deadlines or change test selection.

```powershell
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
$env:UseSharedCompilation = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'
& 'C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.exe' 'C:\miscwork\portfolio\cislib\implementations\csharp\CisLib.slnx'
```

The installed user PowerShell alias is under `%LOCALAPPDATA%\Microsoft\WindowsApps`; the command above does not add Codex's private runtime to PATH. With the ordinary user PATH, all four affected unit cases pass. With that PATH and the two scoped compiler settings, all sixteen affected architecture cases pass against the same existing Debug test binaries. This is a focused native rerun of the saved failures; Visual Studio Run All acceptance remains open. See the [diagnosis and native reports](../../planning/evidence/corrective-visual-studio-environment-2026-10-18/README.md).

Debug packaged logging tests create a fresh package from their current project build under their own candidate directory, then verify its assembly hash and consume it from an isolated local feed. Release runs retain the preselected package and reject stale bytes. Architecture tests that inspect compiler-control evidence check the Release assembly that produced that evidence; a Debug test host is not that candidate. The full Developer checkpoint remains the supported preparation for retained architecture/compiler evidence.

Compatibility cases also need the pinned ApiCompat 10.0.401 entry DLL and .NET runtime 8.0.31 on its selected host. The SDK 10.0.401/runtime 10.0.12 installation alone does not supply that .NET 8 tool host. Existing `CIS_COMPAT_API_TOOL` and `CIS_COMPAT_API_DOTNET` overrides can select an explicitly prepared tool/host. A local-tool resolver may reuse an earlier custom package cache, so a successful `dotnet tool restore` alone does not prove the entry DLL exists under the test process's default NuGet cache. See [toolchain qualification](csharp-toolchain.md) for the isolated restore procedure and pins.
