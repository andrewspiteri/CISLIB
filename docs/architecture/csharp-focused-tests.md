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

Visual Studio needs a version supporting .NET 10 and the selected SDK, with the relevant .NET workload. See Microsoft's [SDK/MSBuild/Visual Studio compatibility table](https://learn.microsoft.com/dotnet/core/porting/versioning-sdk-msbuild-vs). The present machine only has Visual Studio 2022 Build Tools: that is not qualification of an executable supported IDE build, Test Explorer or design-time behavior. CLI results must not be presented as those results. Custom Type Manager helper output layouts remain unsupported with CISTM005.

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
