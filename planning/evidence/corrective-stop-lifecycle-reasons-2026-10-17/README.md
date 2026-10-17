# Exact lifecycle and crash reader reasons

17 October 2026, based on `40591f0b`. Completes **REJECTION-REASONS** after the [flush](../corrective-flush-rejection-reasons-2026-10-17/README.md) and [shutdown/creation](../corrective-lifecycle-rejection-reasons-2026-10-17/README.md) subsets.

The existing T05 matcher now gives its 67 assertions stable reasons. Each of the 32 mode controls, five restart controls and four physical console controls carries its own literal expected reason, compared with the actual exception message after the valid native child observation. The four console controls include the newly added BOM check; there were 40 earlier lifecycle controls in total. No new validator framework, shipping behavior, API or dependency is added.

**Eleven integration and four unit cases pass per native lane**, zero failures/skips. Windows integration `01b1360f48af4ae0a0cf209561310ad6`, unit `1959de2b47934150bfb96df6c3ff046c`; Ubuntu integration `2145caf8ce8649f088881914115a55f1`, unit `5f75e966c20c4cdd98eb57e1d2eaec07`. [Windows integration](windows-integration.json), [unit](windows-unit.json), [Ubuntu integration](ubuntu-integration.json), [unit](ubuntu-unit.json).

The [C# audit](RetainStopLifecycleEvidence.cs) reads the original native TRX output to verify that all 41 distinct named rejection checks actually ran on each lane, compares both sets, and checks matching final hashes for the two changed C# files. [Executed IDs and audit](audit.json). Raw TRX files remain in the focused/local artifact directories. Formatting passes. These are 41 checks inside the 11 process cases, not 41 additional native case identities.

Together the three reason batches now check 245 corruption reasons: 88 flush, 116 shutdown/creation and 41 lifecycle. Do not add their overlapping test-family totals into a new complete checkpoint count. Full Developer, coverage, mutation, compatibility and release gates were not repeated for these later deltas. Other confirmed findings and seven approved Claude bundles remain pending. The original roadmap goal remains paused.
