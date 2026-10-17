# Explicit configuration nulls and expected value shapes

17 October 2026, based on `9341ffff`. Completes **EXACT-SHAPES** after the [diagnostic/state/provider correction](../corrective-report-field-shapes-2026-10-17/README.md). No shipping configuration behavior or API changes.

Configuration/path/validation readers require nullable path and diagnostic members to exist, using one small presence-plus-null check where null is required. Constructor observations require values/metadata objects on valid input and explicit null members on invalid input. Metadata must omit Logging; an explicit Logging:null is not absence. Optional expected values must be an object when supplied, rather than silently bypassing subset validation for null, numbers or arrays.

Seven reader controls fail before the guards because no exception is thrown: one missing null member, three malformed expected subsets, two missing invalid-constructor members and one forbidden null Logging member. [Pre-fix TRX](before-fix.trx). The small shape/subset checks remain in the existing fixture/test classes; no validation framework is added.

**All 80 selected configuration cases pass on each native lane**, zero failures/skips. Windows `334ff0ef67dd4827b932b8fdef3ded8e`; Ubuntu `f7103c7b8cc443aeafdc9fff880be9cf`. [Windows](windows-configuration.json), [Ubuntu](ubuntu-configuration.json). Seven are DTO reader controls in the integration assembly, not seven new physical child-process scenarios. Existing configuration, validation, path/tilde, constructor and distinct-instance process checks remain in the selection.

The [C# audit](RetainConfigurationShapeEvidence.cs) verifies native totals, all seven new bindings, pre-fix silent acceptance and matching final code/inventory hashes on both lanes. [Audit](audit.json). Formatting passes. These focused checks do not repeat full Developer, coverage, mutation, compatibility or release gates. Other confirmed findings and seven approved Claude bundles remain pending; the original roadmap stays paused.
