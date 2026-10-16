# Native Type Manager example verification

16 October 2026, based on `361c4f40`. This corrective batch replaces development verification only. Shipping code, public behavior, dependencies and frozen evidence remain unchanged. No Python execution or Claude transfer.

The existing C# checker provides `type-manager-examples`. All **66 original logical checks pass on Windows and Ubuntu**: source/exact-package Console/Web, Debug/Release builds and Release publish/publish-without-build, three broken-deployment controls for every layout, and two real HTTP sessions. Each HTTP session makes two requests and verifies distinct scoped consumers share the prepared manager and cached query. Fresh-process checks retain preparation-before-startup, fixed catalog membership after a later assembly loads, exact inventory/candidate identities, shared-framework requirements, package Core/helper bytes and absence of deployed build tooling. Missing/malformed inventories and missing libraries must exit 1 with the intended file/category before startup.

Both lanes use compiled verifier `487b4ee00135b8b651eda6b6434a8be35411c6319896ecff8ec3fe52c0008537`, SDK 10.0.401/runtime 10.0.12 and package `6ebf90713b29ec2608a4d32e3593de76c6662aad5146d13847aef95518be6e54`. Core/helper pins remain those recorded in the preceding package migration. Approved dependency archives, seven exact Microsoft SDK pack archives, policy, example and Core source inputs match across lanes. The four generated-input hash differences are two OS-specific private-feed paths and two XML project newline encodings; the latter were independently compared after newline normalization.

The first Windows attempt failed before any logical case because the examples now declare both supported RIDs and the private feed lacked SDK packs. The corrected command retains those project settings and seeds seven exact 10.0.12 SDK packs. Eight of nine total Microsoft archives came from the existing cache; missing microsoft.netcore.app.host.win-x64 10.0.12 was downloaded from the official NuGet flat-container endpoint into the ignored batch cache. No project dependencies/global cache were changed. Archive hashes are retained in dependencies/inputs.json and the reports.

Evidence under `.artifacts/corrective/tm-examples`:
- windows-pinned-sdk/results.json: `e34eb916d0ed179672c30fbf71a81626df3ab276f975b5d04de51c263b25633e`.
- linux/results.json: `5812e2c92a69ab54e34b14433610d2fce1a7ec366c73072c8ef0d67c8f7aa1a7`.
- case-audit.json independently enumerates all 66 identities and reconciles inputs. Child logs, PID/exit/output hashes and owned-server shutdown records remain. Ubuntu ran in its temporary filesystem and the complete output was archived here.
- The initial failed Windows report remains uncredited. Both corrected commands exit zero, complete=true, with no error. Build/format checks pass.

Retired `eng/verify-type-manager-examples.py` is 20,103 bytes, SHA-256 `5363db8399fe9142e0ff46638f5e535d14c1a2cc9d6884889c285b1690ce2bb5`. Git retains it. Three maintained Python utilities remain: mutation preparation/replay and measurement. The pre-existing untracked launch profile remains unchanged at `1509170e5a2d67d067d16e8a3c69db9e67755f5761bc0b5413fe11558054b6be` and is not staged.

Graph: schema 2, 10,925 nodes/53,596 edges, zero diagnostics, build `78ad6332b5917f768203628c386b39d7e53dde11151e634eb3e90429dec4b3d1`, compiler/semantic extractors 6/3. Before retirement, the exact examples-verifier related query was fresh/untruncated at depth one, three nodes/three traversals, limit ten, no proposed edges. Navigation is not complete semantic coverage.

These exact development-example checks do not establish final-package/released compatibility, performance, mutation/coverage or the full Release gate. The earlier full 6,434-case Windows Developer snapshot predates this utility. The original logging roadmap remains paused.
