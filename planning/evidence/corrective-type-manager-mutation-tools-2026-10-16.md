# Native mutation preparation and historical replay retirement

16 October 2026, based on `f41ac6f3`. This corrective batch changes development tooling only; no shipping source, dependency, mutation score or original roadmap state changes. No Python execution or Claude transfer.

The existing C# checker provides `type-manager-mutation-prepare` and `type-manager-mutation-prepare-controls`. The preparer retains the original independent producer-report and Core pins, complete module source/hash binding, nested-source/glob guard, eight canonical test sources, pinned configuration and approved dependency DLL pins. It writes an isolated source-module project, generated-file inventory and baseline/Stryker commands. Its report explicitly says prepared=true, qualified=false; preparation is not mutation qualification.

Both Windows and Ubuntu pass the **same 100-case native baseline**, with zero failures/skips. Four actual child-command controls per OS reject changed report, Core, module source provenance and dependency bytes for the intended reason, with no success preparation record or compiled Core. The original candidate and producer remain unchanged. Shared preparer DLL hash is `a21942f4d4b2eabbf069efe326c1eb2e23cfefb559dedbb00c02a3901ca0ece9`; candidate Core is `2d8a7be20a2ae171cce3c2865fb43b91d5854e401f7e633e397bbbd4a397bef5` and producer report `98083c6a3fb64d50ffc9f01fd2cab91b99bef95eff5a9462f528fcf3e332ddc5`. Copied input hashes and native case names match across OSes.

Evidence under `.artifacts/corrective/tm-mutation-prepare`:
- windows-final/preparation.json: `6ddc193787d494e45bc6095cd7e74f6b6a67fbbfc0f493dcb4d37961b7f907bc`.
- linux-final/preparation.json: `e2e3cc4395001f04d7b5032777da238a541fb983e758f5e4635d9040d35bd6dc`.
- windows-controls/results.json: `e4f0150569e6f275f55409484b654f854e22973e0fcc5b2b35faf0016515899e`.
- linux-controls/results.json: `a8f216511c4002880e50374bfbb94cb3150bf8a960fd91fbf1616e889abfcec0`.

Raw TRX, build logs, commands/PIDs/exit/output hashes and audit.json are retained. An earlier Ubuntu baseline also passed but its /tmp workspace disappeared on WSL restart before archival; it is not the credited evidence. The final retained run uses /home/user/cislib-verification and is archived in the repository's ignored directory. Initial graph refresh failed with SQLite access denied; retry succeeded without cache edits. Build/format checks pass without warnings/errors.

Two scripts retire after SHA-guarded removal:
- prepare-type-manager-mutation.py: 7,510 bytes, `d2d8ba8dba83bbd8b739df70a1b551b28e9c08da7479d398782bbafdc05d5308`, replaced by the native commands.
- replay-type-manager-mutants.py: 22,769 bytes, `573e7d0a4fc14dfbb0c427cf524e5c125625221ffa0bf4f95d16c8d479b69b26`, retired as a completed one-off historical diagnostic.

Replay retirement is deliberate scope reduction, not a claim to have ported a general mutation engine. The script explicitly diagnoses the old report and hard-codes exactly 483 mutants/238 CompileError entries. Searches found no maintained caller outside the corrective proposal; its reproduction reference is in frozen historical evidence. That evidence, all original classifications and the canonical C# reconstruction helper remain unchanged. Exact replay source is retrievable from Git `f41ac6f3:implementations/csharp/eng/replay-type-manager-mutants.py`; the [historical diagnostic](../type-manager/evidence/mutation-replay/README.md) explains its original boundaries. Its report validation/restoration checks remain part of that preserved experiment, not newly credited current checks. Future current-source mutation/disposition work must use pinned native tools and native evidence validation; this batch does not resume it or reclassify mutants.

One maintained Python utility remains: measurement. Historical implementation and frozen planning/evidence Python remain unchanged. The earlier full 6,434-case Developer snapshot predates this tooling change. The original logging goal stays paused.

Final graph snapshot: schema 2, 10,924 nodes/53,650 edges, zero diagnostics, build `e7c8ddaf09683cd6637891e2ef43946e7e48134614732dc4798c49244d254f69`, compiler/semantic extractors 6/3. The exact preparation root returned three nodes/three traversals at depth one, limit ten, no truncation or proposed edges; its intervening stale warning after script removal was followed by this refresh. Bounded queries are navigation evidence, not complete semantic or release coverage.
