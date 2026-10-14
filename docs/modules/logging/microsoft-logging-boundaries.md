# Microsoft logging dependency boundaries

WP06-S03-T02 qualifies the existing single `CIS.Core` boundary. Its approved net10.0 runtime graph remains Microsoft.Extensions.Logging.Abstractions and Microsoft.Extensions.DependencyInjection.Abstractions at 10.0.12. The Microsoft factory implementation is supplied by applications and appears only in the named development fixture; third-party runtimes belong in separate optional integrations. [Packaging policy](microsoft-logging-packaging.md) explains the user amendment and nuspec minimum semantics.

The existing checker evaluates project/framework/package items, actual restored graphs, compiled AssemblyRefs and the package asset list, dependency groups/IDs/versions and packed DLL references. The new tests use copies of real Core restore/package outputs; each begins with a passing unmodified control before one mutation. No unapproved package is downloaded or loaded. Restored-graph additions change an existing abstraction dependency edge and target package entry, without adding a direct project PackageReference. These are synthetic graph mutations, not claims that NuGet successfully restored an unapproved dependency.

| Boundary | Rejection controls |
| --- | --- |
| Copied restored closure | NLog; Microsoft factory implementation; full dependency-injection implementation; wrong approved transitive version; missing approved transitive member |
| Copied packed assets | Bundled factory DLL or NLog DLL, outside the closed allowed asset list |
| Copied nuspec | Replacing/adding a runtime dependency, changing its minimum version, or silently tightening it to an exact-only constraint |
| Compiled metadata | Existing factory/test/foreign AssemblyRef controls plus a new NLog AssemblyRef control |

The packed DLL-name controls use deliberately invalid content because the closed asset-list check must reject the path before assembly inspection. Compiled-reference controls instead use real managed PE fixtures and the actual metadata checker. Existing source/imported-project/direct-package/framework and bundled-test controls remain part of full verification. This adds twelve architecture cases, with fifteen focused checks when the existing compiled rows are included, and no shipping API/dependency/framework/helper.

The initial focused package positives detected an old nupkg paired with a freshly built current-commit helper. They failed on the existing whole-package helper byte consistency check. Repacking the actual current tree restored all positives; the gate was preserved. Full Developer verification builds/packs before architecture execution. This protects the combined Core package and does not modify the Type Manager implementation or excuse an unrelated positive failure as a valid rejection.

Task evidence records actual restored/compiled/package inspection and raw test results. This development qualification runs on Windows; full recorded-platform release verification remains later work. Coverage/mutation, cumulative provider compatibility, release-bundle integration and package-consumer examples remain T03-T05/WP07-WP09. The original separate bridge package requirement remains superseded by the user's decision, and the full release gate must stay nonzero.

Package inspection checks the canonical dependency version spelling, not just semantic range equivalence. Nuspec frameworkReferences and dependency include/exclude attributes are outside these dedicated mutation controls; evaluated source framework directions, the actual restored closure and compiled references are checked separately. No broader modified-package declaration guarantee is claimed by these cases.
