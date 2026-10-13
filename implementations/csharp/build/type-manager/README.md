# Type Manager build inventory (development integration)

This source integration generates inventories for the runtime Type Manager now
implemented in `CIS.Core`. Packed build assets are not yet qualified.

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
The target prints the selected RID and does not alter requested self-contained,
trimming, single-file or AOT settings. Disabling current-RID inference without
supplying a supported RID fails clearly.

This development target is exercised through the pinned `dotnet` CLI. Visual
Studio/MSBuild.exe task hosting and NuGet import delivery are not qualified yet.
Web SDK matrix results cover CLI publication with an explicit `-o` directory.
They do not qualify a Visual Studio/FileSystem profile's separate `PublishUrl`
copy stage. Custom helper output layouts, including `--artifacts-path`, fail with
CISTM005 and remain unsupported by this source integration.
Late imports from `Directory.Build.targets` are unsupported: SDK RID inference
has already run there. An isolated clean NuGet consumer demonstrated that even
an early package props import cannot supply RID inference to NuGet's restore
graph: restore excludes those package imports, and build fails NETSDK1047.
Moving only the import or warming the package cache does not solve this.
Package delivery and one-flag setup remain an explicit TM-032 implementation gap.

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
and Linux x64 inventory executions have passed; full runtime qualification and
packaged use remain pending. Source task registration checks the actual helper
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
