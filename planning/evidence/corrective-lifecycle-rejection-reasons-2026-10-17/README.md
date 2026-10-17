# Exact shutdown and creation rejection reasons

17 October 2026, based on `8c2bf707`. Corrects the shutdown/cleanup/creation subset of **REJECTION-REASONS** from the [approved findings](../../reviews/corrective-approved-2026-10-17/findings.md). The separate T05 lifecycle reader controls remain a following subset.

All 116 retained corruption rows now assert their literal expected exception message. The five existing matchers give their 119 assertions stable reasons. Stop-start and stop-drain validate the common result shape, UTC labels and accounting partition before per-field dispositions, so their two partition controls now reach the intended identity check. The creation delivery mutation preserves the partition by changing written/pending together, so it reaches the delivery guard.

The pre-cancelled join reader explicitly requires an integer deadline counter before calling GetValue<int>(); the existing boolean control must fail with pre-admission deadline type. A framework conversion exception cannot satisfy that named check. No new validation framework, shipping implementation, public API or dependency is introduced.

**177 integration and 47 unit cases pass per native lane**, zero failures/skips. Integration includes all 116 exact-reason controls, the 50 start/drain/join/cleanup/creation scenarios and 11 existing T05 lifecycle scenarios. Windows integration `479f1085cf134e3588bcff1b5f017839`, unit `fb6c719f3d1246a6801a101efb6d98a6`; Ubuntu integration `529e567cf299488a8f7fb398c79d462c`, unit `e9dfda495bd3491d8ef23e63d9c924da`. [Windows integration](windows-integration.json), [unit](windows-unit.json), [Ubuntu integration](ubuntu-integration.json), [unit](ubuntu-unit.json). The T05 scenarios passing here does not correct their separately pending reason assertions.

The [C# audit](RetainLifecycleEvidence.cs) verifies complete native outcomes, all 116 control instances and matching final source hashes for the six C# files in both workspaces. [Audit](audit.json). Formatting passes. The related guides now distinguish internal output-stage drain evidence from the already-existing public incoming-pipeline expiry test and describe implemented creation/cleanup work accurately.

This focused verification does not repeat full Developer, coverage, mutation, compatibility or release gates. Other confirmed findings and seven approved Claude bundles remain pending. The original roadmap goal remains paused.
