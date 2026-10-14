# Type Manager build inventory

This source integration generates inventories for the runtime Type Manager now
implemented in `CIS.Core`. The same development package also includes an optional
MSBuild SDK for package consumers. These are development assets, not a published release.

For a package consumer, replace its separate `CIS.Core` PackageReference with the
versioned SDK reference and enable inventory generation:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="CIS.Core" Version="0.1.0-dev.1" />
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <CisTypeManagerEnabled>true</CisTypeManagerEnabled>
  </PropertyGroup>
</Project>
```

Use `Microsoft.NET.Sdk.Web` for an ASP.NET host. The SDK supplies an exact matching
Core runtime PackageReference; it does not add a second shipping package. With
central package management it supplies a matching PackageVersion when absent and
rejects a conflicting central version. Put the package in the consumer's configured
NuGet feed. The SDK import participates in the first restore, before runtime asset
selection; no restore is launched from a build target. An ordinary Core
PackageReference still works when Type Manager inventory is not enabled.
This package requires .NET SDK 10.0.401. Set any existing
`BeforeMicrosoftNETSdkTargets` hook in `Directory.Build.props`, before the Core
SDK import; replacing the hook later prevents early runtime selection and fails.

For source-reference development, use the source target instead:

A net10.0 executable host opts in before importing the targets:

```xml
<PropertyGroup>
  <CisTypeManagerEnabled>true</CisTypeManagerEnabled>
</PropertyGroup>
<Import Project="path/to/implementations/csharp/build/type-manager/CIS.TypeManager.targets" />
```

Import this source target inside the project body, before the implicit SDK targets.
It selects the pinned SDK's current runtime identifier when the host supplies none.
Outputs therefore target one platform: `win-x64` or `linux-x64`. For another target,
pass `-r win-x64` or `-r linux-x64` explicitly when building and publishing. A folder
published with the default RID is not a portable cross-platform deployment.
Cross-RID publishing replaces the project's restored assets graph. Run an ordinary
native restore before returning to native builds or publishing without restore.
Do not run different-RID builds/publishes concurrently against the same project
and intermediate directory. Parallel builds of distinct host projects are tested.
Static-graph restore is outside the qualified matrix; version validation runs at
build time when the restore path does not execute its validation targets.
The target prints the selected RID and does not alter requested self-contained,
trimming, single-file or AOT settings. Disabling current-RID inference without
supplying a supported RID fails clearly.

This development target is exercised through the pinned `dotnet` CLI. Visual
Studio/MSBuild.exe task hosting is not qualified yet.
Web SDK matrix results cover CLI publication with an explicit `-o` directory.
They do not qualify a Visual Studio/FileSystem profile's separate `PublishUrl`
copy stage. Custom helper output layouts, including `--artifacts-path`, fail with
CISTM005 and remain unsupported by this source integration.
Late imports from `Directory.Build.targets` are unsupported: SDK RID inference
has already run there. An isolated clean NuGet consumer demonstrated that even
an early package props import cannot supply RID inference to NuGet's restore
graph: restore excludes those package imports, and build fails NETSDK1047.
Moving only the import or warming the package cache does not solve this.
The versioned SDK import above addresses this first-restore restriction. Plain
package build props and late target imports cannot replace it.

Normal project and package runtime dependencies are automatic. Additional trusted
discovery-only assets use `CisTypeManagerAssembly` before the import. Each asset is
copied to the host output under its filename; include any additional dependencies
that are not already ordinary deployed runtime dependencies. Conflicting names fail.

Build writes `<host-assembly-name>.types.json` next to the host. Folder publish
writes its own manifest, including `dotnet publish --no-build` after a valid build.
The pinned SDK's resolved copy-local selections supply the inventory; neither
directory enumeration nor compile-reference discovery is used. Metadata inspection
does not instantiate types, attributes or execute the application.

Unchanged manifests retain their bytes and modification time. Build-generated
manifests participate in MSBuild clean tracking. Every selected managed file is
required; corrupt metadata, duplicate-name conflicts or reference assemblies fail.
Satellite resource assemblies and unrelated content files are excluded. Native
assets labeled by the SDK are excluded. An isolated real-package matrix exercises
native assets, a compile-only reference assembly, three same-identity RID variants,
satellites, unrelated DLL content, build and both folder-publish modes.

Single-file, trimming, AOT and self-contained settings are rejected without changing
the requested profile. Disabling `CopyLocalLockFileAssemblies` is also rejected:
build inventory requires deployed package runtime assemblies. Relative publish
directories are resolved explicitly against the host project directory.
Design-time builds do not generate inventories. Windows x64
and Linux x64 source inventory executions have passed; complete release
qualification remains pending. Source task registration checks the actual helper
output path before execution, so an unsupported helper output layout fails rather
than silently using an older task DLL. Custom host `-o` output is supported.
The CLI task uses `TaskHostFactory` with `Runtime="NET"`; a source-change probe
verified that a later build executes the rebuilt helper instead of a retained
in-process copy. This is not a Visual Studio qualification claim.

Run from `implementations/csharp`:

```powershell
dotnet build tests/fixtures/TypeManager/Host/Host.csproj -c Release -m:1
pwsh -NoProfile -File eng/verify-type-manager-inventory.ps1
dotnet test tests/CIS.Core.ArchitectureTests/CIS.Core.ArchitectureTests.csproj -c Release --filter FullyQualifiedName~TypeManagerBoundaryTests
```

The inventory regression script restores during dependency removal/readdition and
requires the normal configured NuGet access. It writes evidence only under the
chosen output root (default `.artifacts/type-manager/inventory`) and normal fixture
build output directories. It leaves the fixture in its original reference state.

To check an exact locally packed Core artifact in clean package consumers, run
`eng/verify-type-manager-package.py` from the repository root with `--repo-root .`,
`--package <Core.nupkg>`, `--dependency-cache <NuGet-global-packages-directory>`,
`--rid-package <CIS.TypeManager.RidFixture.1.0.0.nupkg>` and an empty `--output-root`.
The RID fixture is produced by
`tests/fixtures/TypeManager/verify-inventory-matrix.py`; it is test input only.
The package verifier copies the exact package and approved Microsoft dependency
archives into a private feed and uses a fresh package cache. Run it under each
supported OS with the pinned SDK. Its JSON report records hashes and individual
outcomes, and logs are retained under the output root. It covers first restore,
unchanged incremental/no-restore builds, default folder publish, cross-RID assets,
parallel solution builds, cached offline restore, central versions, disabled
integration and unsupported deployment settings. It does not establish Visual
Studio support or complete the six-family release gate.
