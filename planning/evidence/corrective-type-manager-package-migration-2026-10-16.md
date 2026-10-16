# Native Type Manager exact-package verification

16 October 2026, based on `bffecc4d`. Corrective Batch B changes development verification only. Shipping source, public behavior, dependencies and frozen evidence remain unchanged. No Python execution or Claude transfer.

The existing C# checker now provides type-manager-package. All **57 original logical checks pass on Windows and Ubuntu**, with complete case identities independently matched against the original loop/case set. They cover Console/Web clean and parallel builds; exact Core/helper/RID bytes; private feed/cache and runtime dependency boundaries; preserved SDK hooks; unchanged incremental/no-restore files; native restore after cross-RID publication; offline no-restore/locked restore; dependency removal/readdition with real stale files immediately before generation; rejection of stale manifests; host rename, registered clean and rebuild; design-time compilation/explicit targets without a helper and real missing-helper rejection; central exact/mismatched versions; duplicate/ordinary references; ten unsupported build/publish profiles; unsupported SDK/hook replacement; and disabled portable output.

The two OS lanes use the same exact packages and compiled verifier:
- Core package SHA-256 `6ebf90713b29ec2608a4d32e3593de76c6662aad5146d13847aef95518be6e54`.
- Packed Core DLL `1b96c4ba377ad51b3656873a6957905909f9f26cbfa5381318675ca6ff138846`.
- Packed inventory helper `ca13796ff7a9bafe0a4819527af1054618f7bd9666f58ec97ffd8310c4a2c73d`.
- Compiled verifier `66ce00eb47ac498b2b3653a58087ab53bc58ce9091a17bf57d99bbf4dd5c4a6c`.

Core/RID package, runtime-policy, dependency archives and fixture-input identities match across lanes. Each consumer must load the exact packed Core bytes and consume the exact packed helper; selected managed RID bytes are independently compared. Private environment/feed/cache controls, output hashes, original child commands/PIDs/exit codes and exact intended rejection categories remain. The offline directory move/restoration checks both absolute targets stay directly within the owned output root before moving.

Reports are `.artifacts/corrective/tm-package/windows/results.json` (SHA-256 `c5281170f5584e482bd4d996ee177a38cb1634e5025de2149a3f9d38b9551c67`) and linux/results.json (`cf76c0e2b0c757f4cff67e8ef06c353aeac376d4b358f5accc52074399f664f7`). Original logs, per-command observations and explicit 57-case/input reconciliation remain alongside them. Both commands exit zero with complete reports; build/format checks pass without warnings/errors.

After call-site/documentation reconciliation, `eng/verify-type-manager-package.py` is retired (32,196 bytes; SHA-256 `9ce458cec5f01115cb1a23fc0fc20b2ca592b06ca6586ed5c747d200916ecc31`). Git retains the original; the guarded retirement audit remains in the same ignored evidence directory. Four maintained Python utilities remain: examples, mutation preparation/replay and measurement. [Native command instructions](../../implementations/csharp/eng/CIS.DependencyChecker/README.md) replace the old package invocation.

Graph refresh initially failed with access denied in CIS FrontendContextService.AtomicWrite. A retry succeeded without cache edits. Final graph: schema 2, 10,906 nodes/53,399 edges, zero diagnostics, build `23386dfe07f07157f3c9d010b7adeb3d177f0fad86ec321bfd17ddd3d40a891c`, compiler/semantic extractors 6/3. Exact package-verifier related query is fresh/untruncated at depth one (three nodes/three traversals, limit ten, no proposed edges); the original boundary-test root had four/four with the same bounds. Navigation is not complete semantic coverage.

The earlier 6,434-case full Windows Developer checkpoint predates these tool changes. These 57-per-OS checks concern this exact development package and explicit package-consumer boundaries; they do not close cumulative released compatibility, mutation/coverage, performance, application-example qualification or the full Release gate. The original logging roadmap remains paused.
