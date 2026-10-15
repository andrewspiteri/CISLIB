# Registered writer checkpoint

15 October 2026, following coverage migration commit `d2f72a23`. User-selected startup-only registration is implemented with `ILogWriter`, `LogWriterRegistration` and `CoreLogRuntime.Create(writers)`. Registered destinations use independent existing output workers and participate in generic lifecycle accounting. The runtime exposes actual worker/cleanup `Completion` separately from bounded stop. Custom writer ownership transfers only after successful startup; borrowed writers remain caller-owned. No shipping dependency was added. The publication ledger remains empty.

Twelve native registered-writer cases cover invalid/duplicate registrations before launch, copied registration and event ownership, thresholds, fixed flush boundaries, independent progress, fault isolation, startup rollback, borrowed/owned disposal, timeout and late cleanup, and the public API from an external implementing assembly. Existing output, flush and stop cases are retained. The architecture API-shape guard now checks the added overload/interface/readonly registration and completion property.

Executed with pinned SDK 10.0.401/runtime 10.0.12:

| Observation | Native result and retained artifact |
| --- | --- |
| Windows complete unit project | 3,478 passed, zero failures/skips; `.artifacts/csharp/focused/f917d8501ebc41799de9671115a1e362` |
| Ubuntu 24.04 WSL selected writer/lifecycle cases | 185 passed, zero failures/skips; `.artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/453cbc2bcb7843bfae165f1ec288b162` |
| Windows source integration | 1,425 passed; 19 package consumers rejected stale package identity in `.artifacts/csharp/focused/ae5cda0da2c145b1b3bfe9e5bc9a92f4` |
| Windows exact-package consumer rerun | Current Debug Core rebuilt/packed locally; all 19 passed, zero skips, `.artifacts/csharp/focused/6a8cd98b4e0b4a949c9946aec6416e0a` |
| Windows selected API/dependency/runtime architecture | All 99 passed, zero skips, `.artifacts/csharp/focused/4dc9c9edbdf04debb18b5d1e00e7d3ab` |

The first Linux filter also selected `DeterministicSupportTests.HungOwnedChildIsStoppedAtTheFiniteDeadline`, which failed because WSL has no `pwsh`; 185 other cases passed. The corrected selection excludes that unrelated prerequisite-dependent class. The initial architecture run passed 89 and failed ten: nine isolated project cases lacked the newly required `eng/toolchain.json`, fixed in their shared ProjectFixture; one inspected an older compile-only reference assembly. Regenerating the existing startup contract fixture passed all its compiler controls and restored metadata parity. Neither prerequisite failure is hidden or credited as passing in its original run. No assertion was weakened.

Linux used the existing isolated Batch A checkout with the changed writer source/test files copied in, rather than claiming a full current-HEAD Linux run. XML documentation changes after the full Windows unit run do not change executable behavior; the fresh package and architecture checks include those changes.

The declaration-only `ILogWriter.cs` joins the explicitly inventoried non-executable coverage sources. Executable registration/lifecycle code remains in the shipping denominator. No fresh coverage or mutation qualification is claimed.

Graph refresh observed 10,317 nodes, 48,121 edges and no build warnings/errors at `cf686a3e64b59e2c0fa19caa3803d53f0cb42326c40e60c91d5b409a235baf31`. Bounded RegisteredWriterTests find/related queries were truncated at three results and correctly reported subsequent source/doc staleness. Referenced source was inspected directly. A final refresh follows this evidence update; derived graph is local-only.

The logging chat remains paused; progress coordination did not authorize or trigger its old mutation queue. This chat retains the shared build/package/index window. Claude reviews remain stopped. Incoming spool integration, built-in composition cleanup, extensions, remaining verification migration and full release/IDE qualification remain open.
