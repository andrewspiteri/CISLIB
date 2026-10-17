# Exact flush reader rejection reasons

17 October 2026, based on `71fada22`. Corrects the flush subset of **REJECTION-REASONS** from the [approved findings](../../reviews/corrective-approved-2026-10-17/findings.md). Lifecycle/cleanup/creation controls remain the next subset.

All 88 existing corruption rows now carry a literal expected reason, asserted against the actual returned exception message after the unchanged valid physical-child baseline. This prevents an unrelated guard or framework exception of the same type from satisfying the control. The two formerly generic result/stress matchers give their 47 existing assertions stable reasons. No new validation framework, shipping behavior, public API or dependency is added.

The pending-count control also changes written from zero to one, preserving its accounting partition so it exercises the intended disposition comparison. The freeze control alters a reported original result disposition; its expected reason is covered dispositions. Other original mutations and all four scenario sets remain.

Before naming the assertions, the strengthened test produced 31 failures/57 passes: 30 failures exposed the generic messages, and one exposed an initially incorrect expected label for freeze. The latter was corrected after inspecting the actual mutation and its existing disposition guard. [C#-extracted counterexample summary](before-named-reasons.json); the unmodified TRX is retained locally under `.artifacts/corrective/flush-rejection-reasons-2026-10-17/before-named-reasons.trx` with its SHA-256 in that summary. No runtime defect is inferred from this report.

After correction, **129 integration and 63 unit cases pass on each native lane**, zero failures/skips. Integration includes all 41 modes and 88 exact-reason controls (12 boundary, 16 coordination, 26 result, 34 stress). Windows integration `add5a28e14c14e2c850cad82533d53de`, unit `3bfea1eb3bf44b7b8940f6208e831905`; Ubuntu integration `919e060c94314e398716f13967a9365c`, unit `b357513619e941298ebd7165713181fe`. [Windows integration](windows-integration.json), [unit](windows-unit.json), [Ubuntu integration](ubuntu-integration.json), [unit](ubuntu-unit.json).

The [C# audit](RetainFlushReasonEvidence.cs) checks complete native outcomes, all 88 control method instances, the intended red categories and matching final hashes for the three changed C# files in both native workspaces. [Audit](audit.json). Formatting passes. The four current flush guides now describe C# readers, link the latest correction and distinguish historical task qualification from open release work.

This is focused development verification, not a repeated full Developer checkpoint, coverage, mutation, compatibility or release qualification. Other confirmed reader findings and seven approved Claude bundles remain pending. The original roadmap goal remains paused.
