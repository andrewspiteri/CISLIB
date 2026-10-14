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
- Combined Windows developer result: [checkpoint](evidence/combined-developer/README.md).
- Exact-package consumer and boundary checks: [package delivery](evidence/package-delivery/README.md).
- Compatibility: `implementations/csharp/eng/check-type-manager-compatibility.ps1`
  and `implementations/csharp/verification/contracts/type-manager/v1-development/`.

The runtime closure uses one frozen Core DLL on both OSs. The combined Developer
checkpoint uses a later Core DLL containing concurrent logging changes. Inventory
and compatibility checkpoints have their own recorded hashes and scope; they must
not be merged into an implied exact-artifact release result.

## Scenario mapping

| Scenario | Current executed evidence | Remaining qualification |
| --- | --- | --- |
| A01 Initially unused library | Ordinary source and exact-package hosts check the library is initially unloaded, prepare from the generated manifest and find its inherited private implementation. Source tests also reconcile newly loaded reporting. | Final user-facing examples and release bundle replay. |
| A02 Automatic inventory | Source lifecycle and matrix include project/transitive assets; explicit inclusion and duplicate/conflict cases execute. Actual Core package SDK integration discovers project/transitive and RID-selected package assets. | Final consolidated release bundle. |
| A03 Build/publish selection | Console and Web SDK matrices exercise build and CLI folder publish with explicit `-o`, including native/resource/reference/RID selection on both OSs. | Consolidate the complete matrix against the final exact helper/package; qualify PublishUrl copying separately. Web inventory is not a DI runtime sample. |
| A04 Lifecycle | Source lifecycle covers reference removal/readdition, unchanged manifest, rename, clean/rebuild and publish without build. Package checks cover forced manifest regeneration, unchanged incremental output, first parallel restore/build, parallel no-restore and forced offline locked restore. | Packaged reference-change/rename/clean cases and final consolidated lifecycle bundle. Concurrent different-RID operations against one intermediate directory remain unsupported. |
| A05 Invalid inventory | Manifest tests reject missing, malformed, duplicate-key, host-mismatch, invalid UTF-8 and unsupported-schema inputs with original failure context. | Exact candidate/release bundle replay. |
| A06 Unsupported inputs/profiles | Input/path tests, runtime children for dynamic/foreign-context/reference/corrupt selections, and inventory unsupported-profile checks. | Consolidated supported-profile matrix for final delivery. |
| A07 Deduplication/conflicts | Manifest deduplication and path-case tests; explicit duplicate/conflict lifecycle; file, loaded and runtime-binding identity child cases. | Dedicated reversed-order conflict control and final traceable bundle. |
| A08 Matching | `MatchingIncludesInheritedPrivateClassesAndClrVarianceButExcludesOpenAndAbstractTypes` tests inherited interface/base and self matching; ordinary host tests inherited discovery. | Final artifact replay. |
| A09 Candidate filters | Catalog tests retain all definition kinds and select concrete closed classes without public/default-constructor requirements. | Final artifact replay. |
| A10 No activation | `FullCatalogKeepsAllDefinedKindsWithoutActivatingApplicationCode` asserts zero constructor, attribute and initializer counters; architecture inspects forbidden activation calls. | Explicitly retain runtime-controlled module-initializer distinction in final documentation. |
| A11 Identity | `forwarding-and-collisions` child and exact lookup tests distinguish actual runtime Types from different assemblies; version-conflict children verify version diagnostics. | Final artifact replay. |
| A12 Runtime universe | Ordinary source/package hosts and selected reuse/identity children; noncollectible foreign-resolver negative control fails the prior DLL and passes the current guard. | Final immutable release evidence bundle. |
| A13 Generics/arguments | Stable string DataRows exercise unsupported Type shapes; closed generic/value/array arguments and CLR variance tests pass. | Explicit generic/non-generic equivalence assertion and final baseline replay. |
| A14 Lookup misses | Exact lookup test checks false/null and unchanged assembly count; queries also run after source files are removed; IL architecture rejects loading/enumeration paths. | Dedicated deployed-but-outside-catalog lookup target. |
| A15 Loading failure | Missing manifest/assets, corrupt executable, reference assembly and identity mismatch tests preserve cause/identity/path. Two-pass negative control checks validation before any new load. | Final complete diagnostics bundle. |
| A16 Discovery failure | `partial-types` child requires TypeDiscoveryFailure, retained loader errors and no published manager/container. | Final artifact replay. |
| A17 Cancellation | Already-cancelled task test and real mid-load cancellation in ordinary host pass on both OSs. | Deterministic final-publication cancellation, post-success cancellation and explicit cleanup checks. |
| A18 Task lifetime | Catalog test awaits the same successful task twice and checks reference identity; independent-manager test checks separate results. | Multiple concurrent awaiters and shared failure replay. |
| A19 Query concurrency | Prepared/lazy/empty/parallel queries, ordinal ordering and cache tests; architecture identifies the sole GetTypes callsite. | Executed per-assembly enumeration counts, including proof queries do not enumerate. Structural IL evidence is insufficient for that count. |
| A20 Ownership | Captured options/list mutation and read-only catalog/query/report mutation tests pass; query results remain stable after files are removed. | Complete report-collection mutation matrix and final artifact replay. |
| A21 DI ordering/reuse | Ordinary host and failure children expose a pre-container boundary. | Actual container construction, exact singleton registration and reuse across scopes in console/ASP.NET examples. A boolean fixture boundary does not establish DI behaviour. |
| A22 Fixed membership | Implementation owns fixed arrays/maps and architecture checks prohibit load subscriptions. | Dedicated later-load test proving unchanged catalog and cached/uncached query membership. |
| A23 Fresh processes | Ordinary host and 12 runtime child cases use fresh processes; replay verifies Core and every fixture hash. Package consumers run in fresh processes with a private feed/cache and exact Core/helper hashes. | Final user samples and consolidated release replay. |
| A24 Dependencies | Core retains its approved runtime closure. Package/helper separation, exact asset checks, altered-Core/helper/SDK negative controls and private build-only project references are exercised. | Final release artifact and dependency bundle. |
| A25 Compatibility | ApiCompat, frozen binary consumer and historical source rebuild/run pass; baseline bytes are checked. | Full behaviour/schema compatibility and meaningful negative controls; no released historical baseline exists yet. |
| A26 Diagnostics | Catalog/report reconciliation, contextual manifest/loading/discovery errors and dynamic-load classification regression execute. | Complete acceptance matrix reconciliation against final artifact. |
| A27 Performance | No speedup or latency guarantee is claimed. | Reproducible startup/query/allocation/memory measurements with machine/runtime/catalog metadata. |
| A28 Release evidence | Focused native results, hashes, reviews and combined Developer checkpoint are preserved. | All six verification families, exact-artifact bundle and clean package-consumer qualification. |
| A29 Scope/readability | Runtime is nine focused Core files with no new runtime dependency; architecture guards and read-only runtime review pass. | Final maintainability review and coordinated owned-file commits. |
| A30 Manifest compatibility | Schema-1 reader accepts additive fields/BOM, rejects duplicate keys/unsupported schema, and generator output is consumed by ordinary host. | Frozen schema behaviour fixtures and explicit compatibility replay as part of the final package bundle. |

This map is an initial TM-026 traceability deliverable. A row's existing test does
not imply its remaining qualification, task commit or milestone gate has passed.
