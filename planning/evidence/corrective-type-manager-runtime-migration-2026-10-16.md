# Native Type Manager runtime/instrumentation migration

16 October 2026, based on `631b9004`. Corrective Batch B changes development verification only. Shipping Core source, public behavior, dependencies and historical evidence remain unchanged. No Python execution or Claude transfer.

Three existing utilities now execute as commands in the existing C# checker: type-manager-runtime, type-manager-instrumentation and type-manager-instrumentation-controls. No new project or NuGet is introduced. Shared argument/file/hash/bounded-process operations serve these concrete checks; commands retain incomplete-before-work reports, original Core/source/project/template pins, empty output roots, byte-checked copies, fresh child processes and frozen managed replay.

All seventeen original producer checks pass on Windows and Ubuntu: fifteen actual runtime scenarios (including both version-order cases, partial types, validation-before-load, dynamic/foreign/default-context identity failures, both reference-attribute encodings, forwarding/collisions and outside-catalog late loading), plus both actual build-task reference rejections. All five original instrumentation checks pass on both: two positives and three deliberate source variants for repeated enumeration, query enumeration and omitted final cancellation. Both provenance controls also pass per OS: changed Core and changed source records fail for their exact categories before generated/binary directories exist.

Windows builds the producer and instrumented/control binaries. Ubuntu executes frozen copies using the **same unmodified Core bytes, fixture files, instrumented binaries and verifier assembly**. Candidate Core SHA-256 is `2d8a7be20a2ae171cce3c2865fb43b91d5854e401f7e633e397bbbd4a397bef5`; compiled verifier SHA-256 is `65a46cb05ed1bc9cda1c5fdf8d6156b2a09a01ea0d5d8497783c58cbc91fa0cf`. The latter replaces the old source-script identity. Replay requires exact tool bytes; rebuilding the tool requires fresh matching producer evidence. SDK 10.0.401 and child runtime 10.0.12 are selected on each OS. Build/format checks complete without warnings/errors. All 24 logical check identities match per lane.

Native reports and original command/process/output logs remain under `.artifacts/corrective/type-manager-runtime`: Windows producer windows/results.json (SHA-256 `98083c6a3fb64d50ffc9f01fd2cab91b99bef95eff5a9462f528fcf3e332ddc5`), Ubuntu producer runtime-linux/results.json (`18c320849dad54af7cf6d810c257fc926c53aa691439f6810a2f82835fc48490`), Windows instrumentation instrumentation-windows/results.json (`c9be3818b519b64e6bcf9f89cdba271333071efcb8f10b39e78960d056e0c198`), Ubuntu instrumentation instrumentation-linux/results.json (`c37052b56bd1ee4d62e2f078e2050e0c45c9d9378852bd4a586091930d7486d7`), Windows provenance provenance-windows/results.json (`df98f41d331d7f5edb82d747b17917336040d90426aa1ebbbbd7bae8f5e45dab`) and Ubuntu provenance provenance-linux/results.json (`2f88f61b99947760493cdbee040e04fca4d133f8e8301041478dcdfd55c89555`). Case identities and frozen binary/source/template maps are independently reconciled in case-audit.json.

After call-site reconciliation, three Python files are retired with guarded hashes (Git retains originals):

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| tests/fixtures/TypeManager/verify-runtime-processes.py | 10,588 | `f4c7cb7b8188e7ccd0872c3384a48087835af5a4e412e1cd5a71825975774415` |
| eng/verify-type-manager-instrumentation.py | 12,715 | `eeef4b2542e07052ff3942ae3f277c39d22a6ab6888e898b879cdd71f98dcb8c` |
| tests/fixtures/TypeManager/Instrumentation/verify-provenance-controls.py | 2,680 | `88b66d65cf7bd4558e45effaff7c01a941b526ae3fc10a0221c2a69a7c155f94` |

Six nonhistorical implementation Python files remain: inventory matrix, package, examples, mutation preparation/replay and measurement. Frozen historical files remain preserved. [Native commands](../../implementations/csharp/eng/CIS.DependencyChecker/README.md) document their current use and provenance limits.

Graph: schema 2, 10,872 nodes/53,108 edges, zero diagnostics, build `e5e10a12f62a24f7d3a92c16462a59134e8a42868a074a110cc27fbd92c535a5`, compiler/semantic extractors 6/3. Exact TypeManagerRuntimeVerification class find and depth-one related query are fresh/untruncated (three nodes/three traversals, limit ten, no proposed edges). This is bounded navigation evidence.

The 6,434-case full Windows Developer checkpoint predates these tool changes. These newly executed utility checks cover shared managed runtime/instrumentation binaries; they do not qualify the package, inventory matrix, examples, performance, mutation score or full Release. Instrumentation remains a test-only source copy, and source provenance remains the explicitly bounded Core C#/five project-input scope rather than a transitive build attestation. The original logging roadmap stays paused.
