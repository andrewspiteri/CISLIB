# Type Manager build inventory

Type Manager inventory generation is implemented in `CIS.Core`. The same package
contains the optional MSBuild SDK and `CIS.TypeManager.Build` helper; there is no
separate shipping build-tool package. These are development assets, not a published release.

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

Use the actual candidate package version in `Version`; `0.1.0-dev.1` is the current
development version. Use `Microsoft.NET.Sdk.Web` for an ASP.NET host. The SDK supplies an exact matching
Core runtime PackageReference; it does not add a second shipping package. With
central package management it supplies a matching PackageVersion when absent and
rejects a conflicting central version. Put the package in the consumer's configured
NuGet feed. The SDK import participates in the first restore, before runtime asset
selection; no restore is launched from a build target. An ordinary Core
PackageReference still works when Type Manager inventory is not enabled.
Both hosts require SDK **10.0.401**, .NET runtime **10.0.12** and ASP.NET Core shared
framework **10.0.12**, including Console because Core has the approved shared-framework
reference. Keep framework-dependent, untrimmed `net10.0` output. Set any existing
`BeforeMicrosoftNETSdkTargets` hook in `Directory.Build.props`, before the Core
SDK import; replacing the hook later prevents early runtime selection and fails.

For a clean package consumer, make the feed's `NuGet.Config` and an SDK-pinning
`global.json` visible before the first project evaluation. Set an isolated
`NUGET_PACKAGES` cache before the first restore. The SDK itself must resolve from
the candidate feed; a later `restore --configfile` argument cannot substitute for
that initial SDK setup. Do not carry repository imports, `bin`/`obj`, or source
project lock files into the consumer. The [Console/Web package setup](../../examples/type-manager/README.md#package-consumers)
shows complete projects, isolation files and ordinary commands.

When adapting the checked-in examples, remove the Core `ProjectReference`, the
source `CIS.TypeManager.targets` import and their broad `RuntimeIdentifiers` list.
Keep the sample Contracts/Unused references and shared example code. Do not add a
second Core `PackageReference`; the versioned SDK supplies it. Its helper is used
by MSBuild and must stay out of application runtime deployment.

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
Do not run different-RID builds/publishes against the same intermediate directory.
Keep configuration, target framework, RID and dependency inputs matching when using
`--no-build` or `--no-restore`.
Static-graph restore is outside the qualified matrix; version validation runs at
build time when the restore path does not execute its validation targets.
The target prints the selected RID and does not alter requested self-contained,
trimming, single-file or AOT settings. Disabling current-RID inference without
supplying a supported RID fails clearly.

This development target is exercised through the pinned `dotnet` CLI. Visual
Studio/MSBuild.exe task hosting is not qualified yet.
The supported publish route is the CLI with an explicit `-o` directory. It does
not qualify a Visual Studio/FileSystem profile's separate `PublishUrl` copy stage.
Custom helper output layouts, including `--artifacts-path`, fail with
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
assets labeled by the SDK are excluded. Existing focused tests exercise
native assets, a compile-only reference assembly, three same-identity RID variants,
satellites, unrelated DLL content, build and both folder-publish modes.

Single-file, trimming, AOT and self-contained settings are rejected without changing
the requested profile. Disabling `CopyLocalLockFileAssemblies` is also rejected:
build inventory requires deployed package runtime assemblies. Relative publish
directories are resolved explicitly against the host project directory.
Design-time builds do not generate inventories. Actual package-consumer Windows
and Ubuntu outcomes belong to the [CIS-0002 verification record](../../../../docs/changes/CIS-0002/verification.md);
source-reference results do not establish those passes. Complete release
qualification remains separate. Source task registration checks the actual helper
output path before execution, so an unsupported helper output layout fails rather
than silently using an older task DLL. Custom host `-o` output is supported.
The CLI task uses `TaskHostFactory` with `Runtime="NET"` so a later invocation can
use the rebuilt helper instead of retaining an in-process copy. This is not a
Visual Studio qualification claim.

Run from `implementations/csharp`:

```powershell
dotnet build tests/fixtures/TypeManager/Host/Host.csproj -c Release
dotnet test tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj -c Release --filter FullyQualifiedName~TypeManager
dotnet pack src/CIS.Core/CIS.Core.csproj -c Release -o ../../.artifacts/packages
```

Use a local NuGet feed containing that package for a package consumer, then run its ordinary `dotnet restore`, `dotnet build` and `dotnet publish` commands on the claimed platform. A source-reference build does not establish exact-package delivery or release qualification. Native package consumers and meaningful SDK lifecycle tests preserve those product obligations; the old inventory/package verifier command protocol is retired.
