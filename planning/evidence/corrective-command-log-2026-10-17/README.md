# Fresh command logs for silent invocations

17 October 2026, based on d2785af7. Resolves the concrete evidence-tooling follow-up recorded in the preceding checkpoint. No shipping behavior or verification thresholds change.

Invoke-VerificationCommand now passes its captured array directly to Set-Content instead of piping it. An empty pipeline previously never opened the log: an old file survived, or no current log existed. A silent success or failure now creates an empty log; nonempty output replaces previous contents. The captured native exit code still controls success/failure.

Eight native C# cases invoke the actual exported module function, covering existing/absent logs, silent/nonempty output and zero/nonzero child exits. All assertions and observations are in C#; the small generated PowerShell file only invokes the established module bridge. Before the fix, all four silent variants fail for missing/stale logs; the four nonempty variants pass. Raw pre-fix TRX is retained.

All 15 selected architecture cases pass on each native lane (eight new command cases and seven existing source-inventory/bridge regressions), zero failures/skips. Windows c3f432dfb4c1479c8aa06450b15cd80b; Ubuntu 6072f733d8fe467fbda5630a781eb6f1. This focused result does not repeat full Developer, coverage, mutation, compatibility or release gates.

The seven outstanding Claude reviews are being completed only with the unchanged, explicitly approved 24-bundle manifest. Review findings are triaged against current code; earlier receipts are not rewritten. Original roadmap stays paused.