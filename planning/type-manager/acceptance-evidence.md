# Type Manager acceptance evidence map

14 October 2026. This maps the unchanged completion pack's A01–A30 scenarios to
current implementation evidence. It does not mark roadmap tasks complete. Evidence
is development evidence, not a release claim; remaining checks are explicit below.

## Evidence locations

- Unit fixtures: `implementations/csharp/tests/CIS.Core.UnitTests/TypeManagerCatalogTests.cs`,
  `TypeManagerInputTests.cs` and `TypeManagerManifestTests.cs`.
- Architecture fixtures: `implementations/csharp/tests/CIS.Core.ArchitectureTests/TypeManagerBoundaryTests.cs`
  and `TypeManagerRuntimeArchitectureTests.cs`.
- Runtime child processes: `implementations/csharp/tests/fixtures/TypeManager/Runtime/Host/Program.cs`,
  driven by `verify-runtime-processes.py` in the parent TypeManager fixture directory.
- Ordinary generated-inventory host: `implementations/csharp/tests/fixtures/TypeManager/Host/Program.cs`.
- Inventory lifecycle: `implementations/csharp/eng/verify-type-manager-inventory.ps1`;
  independent SDK/RID matrix: `implementations/csharp/tests/fixtures/TypeManager/verify-inventory-matrix.py`.
- Frozen runtime results and review corrections: [runtime closure](evidence/runtime-closure/README.md).
- Expanded runtime acceptance, source instrumentation and targeted mutant controls:
  [runtime acceptance](evidence/runtime-acceptance/README.md).
- Combined Windows developer result: [checkpoint](evidence/combined-developer/README.md).
- Exact-package consumer and boundary checks: [package delivery](evidence/package-delivery/README.md).
- Runnable console/Web examples, real scoped HTTP consumers and broken deployments:
  [host integration](evidence/host-integration/README.md).
- Exact-package reference/rename/clean/design-time lifecycle:
  [package lifecycle](evidence/package-lifecycle/README.md).
- Explicit culture and reversed-valid-inventory query comparisons:
  [query ordering](evidence/query-ordering/README.md).
- Compatibility: `implementations/csharp/eng/check-type-manager-compatibility.ps1`
  and `implementations/csharp/verification/contracts/type-manager/v1-development/`.
- Frozen behavior/schema fixtures and controlled regressions:
  [compatibility checkpoint](evidence/compatibility-behavior/README.md).

The runtime closure uses one frozen Core DLL on both OSs. The combined Developer
checkpoint uses a later Core DLL containing concurrent logging changes. Inventory
and compatibility checkpoints have their own recorded hashes and scope; they must
not be merged into an implied exact-artifact release result.

## Scenario mapping

| Scenario | Current executed evidence | Remaining qualification |
| --- | --- | --- |
| A01 Initially unused library | Runnable console/Web examples in source and exact-package modes find both direct and inherited/nonpublic implementations in an initially unloaded library, display defining identities and reconcile newly loaded reporting. | Final release bundle replay. |
| A02 Automatic inventory | Source lifecycle and matrix include project/transitive assets; explicit inclusion and duplicate/conflict cases execute. Actual Core package SDK integration discovers project/transitive and RID-selected package assets. | Final consolidated release bundle. |
| A03 Build/publish selection | Console and Web SDK matrices exercise build and CLI folder publish with explicit `-o`, including native/resource/reference/RID selection on both OSs. Runnable console/Web DI examples also pass Debug/Release, publish and no-build publication in source and package modes. | Consolidate the complete matrix against the final exact helper/package; qualify PublishUrl copying separately. |
| A04 Lifecycle | Source and exact-package lifecycle cover reference removal/readdition, unchanged manifests, rename, clean/rebuild and publish without build. Package removal is tested with stale DLLs/satellites present during generation. Separate design-time controls, parallel builds and forced offline locked restore pass on both OSs. | Final consolidated release lifecycle bundle. Concurrent different-RID operations against one intermediate directory remain unsupported. |
| A05 Invalid inventory | Manifest tests reject missing, malformed, duplicate-key, host-mismatch, invalid UTF-8 and unsupported-schema inputs with original failure context. | Exact candidate/release bundle replay. |
| A06 Unsupported inputs/profiles | Input/path tests, runtime children for dynamic/foreign-context/reference/corrupt selections, and inventory unsupported-profile checks. | Consolidated supported-profile matrix for final delivery. |
| A07 Deduplication/conflicts | Manifest deduplication and path-case tests; explicit duplicate/conflict lifecycle; file, loaded and runtime-binding identity child cases. Two manifest version selections are rejected in both orders before either is loaded. | Final traceable release bundle. |
| A08 Matching | `MatchingIncludesInheritedPrivateClassesAndClrVarianceButExcludesOpenAndAbstractTypes` tests inherited interface/base and self matching. The new ordering cases compare prepared/lazy results under en-US/tr-TR/sv-SE and reversed valid inventories, with independent ordinal expectations. | Final artifact replay. |
| A09 Candidate filters | Catalog tests retain all definition kinds and select concrete closed classes without public/default-constructor requirements. | Final artifact replay. |
| A10 No activation | `FullCatalogKeepsAllDefinedKindsWithoutActivatingApplicationCode` asserts zero constructor, attribute and initializer counters; architecture inspects forbidden activation calls. | Explicitly retain runtime-controlled module-initializer distinction in final documentation. |
| A11 Identity | `forwarding-and-collisions` child and exact lookup tests distinguish actual runtime Types from different assemblies; version-conflict children verify version diagnostics. | Final artifact replay. |
| A12 Runtime universe | Ordinary source/package hosts and selected reuse/identity children; noncollectible foreign-resolver negative control fails the prior DLL and passes the current guard. | Final immutable release evidence bundle. |
| A13 Generics/arguments | Stable string DataRows exercise unsupported Type shapes; closed generic/value/array arguments and CLR variance tests pass. Generic and non-generic results agree for prepared and lazy contracts, including empty results. | Final baseline replay. |
| A14 Lookup misses | Exact lookup test checks false/null and unchanged assembly count; queries also run after source files are removed; IL architecture rejects loading/enumeration paths. A deployed, bindable but unselected assembly stays unloaded during an exact lookup miss. | Final artifact replay. |
| A15 Loading failure | Missing manifest/assets, corrupt executable, reference assembly and identity mismatch tests preserve cause/identity/path. Two-pass negative control checks validation before any new load. Every example layout rejects three broken deployments with exit 1, the exact file path and original failure detail before startup. | Final complete diagnostics bundle. |
| A16 Discovery failure | `partial-types` child requires TypeDiscoveryFailure, retained loader errors and no published manager/container. | Final artifact replay. |
| A17 Cancellation | Already-cancelled and real mid-load cancellation pass. Post-success cancellation cannot revoke the published manager; success/failure fixtures exclusively reopen manifests. A test-only source copy deterministically cancels at final publication; removing that check is rejected. Loaded assemblies are not claimed to roll back. | Final artifact replay; the publication probe is source instrumentation, not a probe inside the unmodified DLL. |
| A18 Task lifetime | Sixteen concurrent awaiters receive the same result or failure; separate preparations remain independent. These tests document the public task contract, not internal scheduler behaviour. | Final artifact replay. |
| A19 Query concurrency | Unmodified Core passes parallel/cache tests and the sole-GetTypes-callsite architecture check. A separate instrumented source copy counts each selected assembly once per preparation, including reuse, and records no added enumerations during concurrent queries. Double-enumeration and query-enumeration mutants are rejected. | Final release evidence and full mutation qualification; instrumented counts do not directly measure the unmodified DLL. |
| A20 Ownership | Catalog, definition, query and all three report lists reject mutation, including index replacement when nonempty. A plain-array negative control proves the assertion catches fixed-size mutable collections. Process cases exercise newly loaded report entries. | Final artifact replay. |
| A21 DI ordering/reuse | Web sample awaits preparation before builder/container creation, registers that instance and checks root/multiple-scope manager and cached-query identity. Two HTTP requests have distinct scoped consumers, same-consumer reuse within each request, and the same prepared manager/cache. Broken deployments prevent even builder creation. Console stays container-free with an explicit application boundary. | Final exact-artifact replay and milestone reconciliation. These checks prove DI identity/lifetime, not direct enumeration counts inside the unmodified DLL. |
| A22 Fixed membership | A real later assembly load leaves the catalog, definitions, exact lookup, cached and uncached query membership, and preparation report unchanged. Architecture prohibits load subscriptions. | Final artifact replay. |
| A23 Fresh processes | Ordinary host and 15 runtime child cases use fresh processes; replay verifies Core and every fixture hash. Two generator rejection cases run separately. Runnable examples cover 66 checks per OS with fresh child processes, private feed/cache, exact package Core/helper hashes and actual HTTP requests. | Consolidated release replay. |
| A24 Dependencies | Core retains its approved runtime closure. Package/helper separation, exact asset checks and altered-artifact controls pass. The Web framework exception is restricted to its registered example; shipping/test-support policy widening is rejected, and removing only the role guard makes the forbidden Core reference pass. The full 26-project checker passes. | Final release artifact and dependency bundle. |
| A25 Compatibility | Both OSs pass 86 checks including original API/binary/source replay, 22 frozen behavior cases, byte-identical original-API consumer rebuild, nine behavior mutants and a public-property API/binary/source regression with positive twins. Three provenance guards reject mismatches. | Final exact-package replay; no released historical baseline exists yet. |
| A26 Diagnostics | Catalog/report reconciliation, contextual manifest/loading/discovery errors and dynamic-load classification regression execute. | Complete acceptance matrix reconciliation against final artifact. |
| A27 Performance | No speedup or latency guarantee is claimed. | Reproducible startup/query/allocation/memory measurements with machine/runtime/catalog metadata. |
| A28 Release evidence | Focused native results, hashes, reviews and combined Developer checkpoint are preserved. | All six verification families, exact-artifact bundle and clean package-consumer qualification. |
| A29 Scope/readability | Runtime is nine focused Core files with no new runtime dependency; architecture guards and read-only runtime review pass. | Final maintainability review and coordinated owned-file commits. |
| A30 Manifest compatibility | Frozen raw fixtures replay schema-1 additive fields/BOM, malformed/invalid UTF-8, duplicate properties, unsupported versions, paths and selected assets on both OSs. Targeted schema, duplicate-property and traversal mutants are rejected. Generator output is consumed by the ordinary host. | Final exact-package bundle replay. |

This map is an initial TM-026 traceability deliverable. A row's existing test does
not imply its remaining qualification, task commit or milestone gate has passed.
