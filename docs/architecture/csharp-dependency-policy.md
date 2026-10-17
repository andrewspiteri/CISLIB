# C# dependency boundaries

Implemented for WP01-S02 on 10 October 2026, applying the user's single `CIS.Core` package and Microsoft-maintained runtime dependency decisions.

## Dependency directions

| Project role | Allowed local dependencies | Packaging |
| --- | --- | --- |
| `CIS.Core` | No runtime project reference; one manifest-registered build-only tool reference. Approved Microsoft runtime packages plus the implicit `Microsoft.NETCore.App` framework. | The only shipping package. |
| Five test families | Core and `CIS.Core.TestSupport`. | Nonpackable; test packages have `PrivateAssets=all`. |
| `CIS.Core.TestSupport` | No project or NuGet dependency. | Nonpackable. |
| `CIS.DependencyChecker` | TestSupport for metadata inspection. | Development executable; nonpackable and absent from Core's graph. |
| ConsoleHuman | Core through an explicit development project reference. | Nonpackable example. |
| Future third-party runtime integration | May depend on Core in a separately inventoried optional package. Core cannot depend back on it. | Separate package when actually implemented. |

The [project manifest](../../implementations/csharp/eng/project-boundaries.json) lists exact edges and direct package IDs. New projects require an explicit manifest entry. Conditional/imported project items are evaluated with the pinned SDK; source XML inspection alone does not establish the boundary.

Core's [runtime inventory](../../implementations/csharp/eng/runtime-dependencies.json) remains two packages at `10.0.12`. The separate [development inventory](../../implementations/csharp/eng/development-dependencies.json) records 12 resolved test-tool packages and four fixture-only packages, exact versions, Microsoft provenance and licences. The shared framework is declared separately from NuGet packages. The direct test inputs are MSTest framework/adapter `4.5.1` and `Microsoft.NET.Test.Sdk` `18.10.1`, using explicit VSTest selection. These are setup inputs; the coverage/mutation/API compatibility proof is still pending.

MSTest imports three additional assembly references from its build targets. Only those named entries, coming from the recorded package/version directory for both their target and HintPath, are permitted in test projects. Manual assembly references remain forbidden in Core and the other roles.

## Executed gates

The stable [check-dependencies.ps1](../../implementations/csharp/eng/check-dependencies.ps1) launcher invokes [DependencyChecks.cs](../../implementations/csharp/eng/CIS.DependencyChecker/DependencyChecks.cs) in the existing C# tool. The native policy checks:

1. All registered evaluated projects, their target/packability, exact project edges, explicit package IDs and frameworks. Convenience frameworks such as ASP.NET cannot enter Core.
2. The registered base restored graph, including exact runtime/development versions and local project origins. The separate [all-target inspection](csharp-dependency-allowlists.md) also checks declared target/RID and locked inventories. Core's runtime closure must equal its approved inventory.
3. Packed dependency groups and versions, and a closed asset list. Bundled third-party/test DLLs, native assets and unapproved content/build scripts are rejected.
4. Compiled Core DLL references using .NET managed metadata APIs, without executing the inspected assembly. Allowed names come only from the SDK's resolved `Microsoft.NETCore.App` reference pack and approved runtime packages. The packed DLL must match the inspected build when both paths are supplied.

Normal developer verification runs every project gate before build, then checks the built and packed Core DLL. `-ProjectPath` scopes isolated negative-fixture checks to one already registered project; it does not alter the normal whole-manifest verification. `-LanguageRoot`/`-AssetsPath` permit copied fixture inputs rather than corrupting real source/restore data.

The architecture suite proves rejection of a direct and imported Core-to-tests reference, a convenience framework, an unapproved explicit package, an unapproved restored package/version, bundled test assets and forbidden compiled AssemblyRefs. The unit support checks prove cleanup isolation and drainage beyond pipe-buffer sizes.

## Remaining qualification

Type/namespace directions, resolved metadata ownership and public API collection/static-field rules are declared responsibilities and will be inspected as real Core types exist, completed under WP07-S02. Logging and MicrosoftLogger types now exist; their type/namespace direction checks remain WP07-S02 work. The populated discovery integration family executes; no claims are made for empty compatibility/business-case or full mutation suites. Historical compatibility, coverage/mutation and Linux qualification remain pending; release verification must remain nonzero until required evidence exists.

WP03-S01-T04 registers a nonpackable `process-fixture` executable depending only on Core. Its restored closure may contain only the exact approved Microsoft runtime packages, with no direct package references. Integration tests build/copy that host through an explicit development project reference. Core grants friend access to this named fixture without introducing a shipping assembly reference or public discovery surface.

## Microsoft host filter fixture

WP06-S02-T03 adds `Microsoft.Extensions.Logging` 10.0.12 only to the nonpackable DiscoveryHost fixture, with `PrivateAssets=all`. Its exact six-package closure includes the two approved Core packages plus development-only `Microsoft.Extensions.Logging`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Options` and `Microsoft.Extensions.Primitives`, all Microsoft-maintained/MIT at 10.0.12. Exact source mappings, central pin and development inventory are recorded. The process-fixture manifest explicitly permits only these additional development package identities; this does not broaden Core or other process-fixture dependencies. Integration tests copy the compiled fixture and its dependencies for isolated child execution. New negative controls reject the factory package and AssemblyRef in Core.

Fixture-only inventory roles are excluded from all test-role restored graphs. Four evaluated negative cases first pass the isolated positive boundary, then remove fixture PrivateAssets, inject factory into a test graph, inject it into an unlisted process-fixture graph, or inject an unlisted test package into the named host fixture. These checks complement the Core explicit-package/compiled-AssemblyRef controls.

## Embedded Microsoft logging package

[Microsoft logging packaging](../modules/logging/microsoft-logging-packaging.md) and the [S03 scope record](../../planning/logging/microsoft-packaging-scope.json) make the single-Core topology explicit. Repository lock files pin the tested resolved closure; nuspec `10.0.12` remains an inclusive minimum for application restore. The factory implementation is still fixture/application-owned. Original standalone bridge optionality is superseded by the user decision; all behavioral/dependency/compatibility/release assurance stays required.
