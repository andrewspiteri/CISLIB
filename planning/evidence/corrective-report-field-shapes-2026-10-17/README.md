# Exact diagnostic/state fields and protected identity

17 October 2026, based on `b46857f5`. Corrects the diagnostic/state/provider subset of **EXACT-SHAPES** from the [approved findings](../../reviews/corrective-approved-2026-10-17/findings.md). Configuration member/null checks remain a separate subset.

Diagnostic and output-state readers compare the complete observed key set with the expected keys plus faultAtUtc. Equal property counts no longer allow fault:null to be renamed to faultCode:null. The two provider protection branches require env/version to be JSON strings before checking that their values differ from the caller's fake attributes. Missing or explicit null fields reject with the existing intended reason. Shipping behavior, APIs and dependencies are unchanged.

Two runtime-backed unit controls and eight physical provider controls all fail before the fix because no exception is thrown. [Unit counterexamples](before-unit.trx), [provider counterexamples](before-provider.trx). Each control first validates the original actual runtime/child report. All ten are bound in the required-case inventory on both lanes.

**78 unit and 135 integration cases pass per native lane**, zero failures/skips. Windows unit `44e4526ced5443a09a4a0c234f0fb1ca`, integration `3463d387cd034c86a7098165ee20c3e9`; Ubuntu unit `ed825c38e79f4f40b2a9d0be7b715c3d`, integration `6be71401a1164507927ee90a9841a8d1`. [Windows unit](windows-unit.json), [integration](windows-integration.json), [Ubuntu unit](ubuntu-unit.json), [integration](ubuntu-integration.json).

The [C# audit](RetainFieldShapeEvidence.cs) verifies complete native outcomes, all ten new controls, actual acceptance in the red runs and matching final source/inventory hashes on both lanes. [Audit](audit.json). Formatting passes. This focused batch does not repeat complete Developer, coverage, mutation, compatibility or release gates; the original roadmap stays paused.
