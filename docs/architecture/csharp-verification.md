# C# verification and source replay

WP01-S04 provides repository-owned commands for the current foundation. Run them from the repository root with PowerShell 7, Git and .NET SDK **10.0.401**. Developer verification and its fresh-cache replay require HTTPS access to the explicitly mapped NuGet source. The selected target runtime is **10.0.12**. Separate coverage/mutation/API tool qualification also requires **8.0.31**; see [toolchain qualification](csharp-toolchain.md).

```powershell
pwsh -NoProfile -File eng/verify.ps1 -Profile Developer
pwsh -NoProfile -File eng/check-release.ps1 -Profile Developer
pwsh -NoProfile -File eng/verify.ps1 -Profile Release
pwsh -NoProfile -File eng/check-release.ps1
pwsh -NoProfile -File eng/replay.ps1 -Profile Developer
```

Developer verification performs locked restore, evaluated dependency checks, format validation, Release build, Core packaging, compiled/packed dependency checks, actual discovery and execution of populated unit and architecture projects, and host execution. Stage exit codes, timestamps, source hashes, native reports and validation issues are retained. Exit **0** means the selected foundation checks passed; **1** means an execution stage failed; **2** means evidence validation refused the selected profile. Check-release returns 0 for accepted evidence and 2 for refused or malformed evidence. Replay propagates verification's exit code, or returns 1 for materialization/checksum failure.

Reports use stable paths under `.artifacts/csharp/verification/<Profile>/`: `summary.json`, `source-inputs.json`, `validation.json`, stage `logs/`, and independent `families/<id>/discovery.json`, `execution.json` and native report slots. A new invocation replaces its structured slots and selected native reports; retain any run needed as evidence before running again. Unexecuted families explicitly record `NotRun`, with no native-report reference or pass claim. Legacy populated-family TRX copies remain under `.artifacts/csharp/tests/` for the separate toolchain qualification command.

[The family manifest](../../implementations/csharp/verification/families.json) declares unit, integration, mutation, architecture, compatibility and business families, with Windows x64 and Ubuntu 24.04 x64 applicability. Mutation remains a separate development CLI rather than a sixth C# test project. Its parser qualification is an explicit separate command, not part of fast Developer verification. Linux remains unexecuted. The [case inventory](../../conformance/logging/case-inventory.json) distinguishes implemented native-method bindings from required planned case groups; the [requirement map](../../conformance/logging/requirement-case-map.json) preserves acceptance obligations and the user's amendments. A mapping or method binding is not passing evidence. Extend planned groups with concrete case IDs as behavior is implemented.

The validator verifies referenced report hashes and confined paths, native TRX schema/definitions/counters, populated discovery, required method/case counts, failures and skips. Architecture tests exercise a retained native positive baseline and six independent negative controls: missing family, missing report, missing required case, failed case, unexpected skip and unrecognized native schema. These deliberately altered copies test the checker and never qualify a candidate package.

Release verification currently inventories missing work and returns nonzero. Full logging implementation, candidate package suites, complete coverage/mutation validation, compatibility history, package-consuming examples, both platform lanes and the retained offline verification kit remain later roadmap work. Bootstrap commands cannot certify a release, and parser-only mutation evidence cannot satisfy the full shipping mutation gate.

## Source replay

Replay defaults to the Release profile, which currently fails as expected. Use `-Profile Developer` to rebuild the available foundation. Each replay retains a unique directory under `.artifacts/csharp/replay/<UTC>-<id>/` and a stable `latest.json` pointer containing the result. It copies maintained inputs into separate `input/` and `work/` directories, creates fresh CLI-home/NuGet/HTTP caches, runs verification in `work/`, and checks hashes of caller inputs, retained inputs and working sources before and after execution. Reports and generated build outputs stay outside the input inventory. Repository discovery cannot borrow the enclosing checkout's Git identity.

Optional `-InputRoot` and `-InventoryPath` accept another source directory and a JSON array of `{ "path": "relative/path", "sha256": "lowercase SHA-256" }` entries. An inventory is nonempty, and every path must stay inside its input root. An input checksum mismatch refuses execution. Fresh-cache replay checks source reproducibility within the installed pinned SDK/runtime environment; unchanged historical binaries, exact candidate package consumers, offline prerequisite restoration and historical runtime replay are not yet implemented.

## Deterministic shared support

`CIS.Core.TestSupport` remains nonpackable and dependency-free. Its manual `TimeProvider` separates UTC corrections from monotonic time and advances timers explicitly. Metadata and UUID sequences are supplied by fixtures, with visible exhaustion instead of random fallback. One-shot asynchronous signals/barriers have finite deadlines. Controlled text writers signal entry and wait for explicit release or failure, letting tests observe ordering without sleeps. Temporary directories own their cleanup. Child processes use argument lists, finite deadlines, per-child environment overrides and concurrent independent stdout/stderr drainage; only the owned process tree is stopped on timeout.

These helpers support tests; they are not shipping Core API or implementations of the logging runtime.
