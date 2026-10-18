# Visual Studio test environment diagnosis

The original roadmap remains paused. No Claude service was used. No library, test assertion, test deadline, package dependency or persistent Windows environment setting changed.

## Saved IDE failures

The user reported 18 failures in Test Explorer. The local saved store (`.vs/CisLib.slnx/v18/TestStore/0/129.testlog`) contains 20 Failed and 6,838 Passed result records for its latest recorded run (index 3), ending 18 October 2026 at 08:39:23 UTC. These counts describe that saved snapshot, rather than overriding the user's current UI count. All 20 failed records contain an inability to start `pwsh`; they span ten methods. Older run-index-2 failures are not treated as current failures.

The store was read without writing to it, using the installed Visual Studio TestLog reader with schema 34 and its MessagePack dependency. A raw snapshot and decoded records remain under `.artifacts/corrective/visual-studio-failures-2026-10-18/`. No repository content was transmitted externally.

Read-only inspection of the running Visual Studio process's PATH found 1,904 characters and no user WindowsApps alias directory. Its Explorer parent had the 1,797-character machine PATH. Both processes belong to the same Windows user. Registry machine/user PATH values contain the installed user alias, but are not the environment inherited by this IDE. A normal environment-change broadcast returned successfully without changing the observed Explorer or running-IDE PATH. The IDE was left open and no registry values were edited.

PowerShell 7.6.7 is installed as the user's Windows Store package. Both its full executable path and `%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe` launch successfully. Reconstructing machine plus user PATH for a new native .NET test process removes the missing-executable failure without using Codex's private PowerShell.

## Focused native verification

The existing Debug binaries are the same binaries selected by the saved IDE run; no build was needed for these reruns. SDK 10.0.401 and runtime 10.0.12 are unchanged.

* [Unit report](unit.trx): all four previously failing unit cases pass, zero failed/skipped, with normal machine plus user PATH.
* [Architecture report](architecture.trx): all sixteen previously failing architecture cases pass, zero failed/skipped, with that PATH and process-scoped `UseSharedCompilation=false` / `MSBUILDDISABLENODEREUSE=1`. The settings are inherited by fixture-build children.

The initial architecture attempt with only corrected PATH exposed the existing two-minute source-inventory bootstrap timeout and a 90-second publication-launcher bootstrap timeout. That diagnostic process tree was stopped after repeated hangs; its log is preserved and it earns no pass credit. The successful architecture rerun uses the original tests and deadlines. It does not establish which compiler setting alone is sufficient. The same compiler-isolation approach was already used for the Windows shipping-coverage checkpoint.

A C# cross-check verifies that the twenty passing native case names correspond exactly to the twenty saved failures, including data rows, and checks the native counters/outcomes. The [verification record](verification.json) retains those names and the original report SHA-256 values. Neither a complete solution rerun nor a new Visual Studio UI run is claimed. Both full Developer and release qualification remain separate from this focused diagnosis.

## Action still required in the IDE

Save work and close Visual Studio, then use the environment-restoring launch command in the [Test Explorer guidance](../../../docs/architecture/csharp-focused-tests.md#test-explorer-prerequisites). A newly launched IDE receives the corrected PATH and scoped compiler settings. Rerun Run All there to confirm the current UI outcome. Existing running processes do not acquire a new PATH from a terminal command.
