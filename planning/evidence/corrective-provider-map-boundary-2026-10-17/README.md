# Provider map truncation boundary correction

17 October 2026, based on `da992dff`. Fixes the confirmed **PROVIDER-BUDGET** finding from the [approved Claude review](../../reviews/corrective-approved-2026-10-17/findings.md). This batch changes one guard in the existing provider map capture loop: when no child node/depth can be retained, mark truncation and stop before validating that entry's string key. Retained malformed keys still reject capture. No public API, package, dependency or capture abstraction is added.

## Native C# regressions

Eight cases cover event state and provider scopes: four node-budget cases move a malformed UTF-16 key between the first omitted position and the following position; two depth cases omit a malformed key/unsupported value below the maximum map depth; two controls keep retained-key rejection strict. Assertions check complete retained key/value prefixes, node/depth counts, truncation provenance and the reserved EventId. The required-case inventory binds all eight on both native lanes.

Before the fix, the original implementation produced **four failures and four passes**, all four failures raising the expected key-validation exception. [Raw counterexample](before-fix.trx). After the fix, **208 provider unit cases pass on Windows and native Ubuntu**, including all eight regressions, with zero failures/skips. [Windows](windows-provider.json), [Ubuntu](ubuntu-provider.json).

Windows focused run: `114cefef1e604c2d9700dd3e776721ae`. Native Ubuntu focused run: `a0ec4494a41b4eb59020be0a0fadfd66`. Ubuntu reuses the previously qualified isolated source workspace and pinned SDK/runtime, copying only the changed provider, new test and case inventory. It rebuilds its own Debug Core/test assemblies; this is not shared-package qualification or a new complete Ubuntu checkpoint.

## Windows Developer checkpoint

Run `880f143009fb4bef9f2886e7dfac9add` passes **6,451 native cases**: 3,718 unit, 2,085 integration, 624 architecture and 24 business. Exit 0, zero failures/skips/validation issues. Locked restore, formatting, build, compiler/dependency/package inspection and the existing example-host check pass. [Summary](windows-developer-summary.json), [validation](windows-developer-validation.json).

[Native C# audit](audit.json) checks the before/after case identities, both focused selections and the three changed source hashes against the checkpoint input inventory. [Audit source](RetainProviderEvidence.cs). Full raw reports, native TRX/discovery, inputs and logs are retained under `.artifacts/corrective/provider-map-boundary-2026-10-17/`, including `windows-Developer/`. Receipt/report metadata was written after execution and is outside that captured source. The five declared user-owned files remain unchanged and unstaged.

The existing wrapper leaves an older stage log when a current command emits no output; its old `format.log` error text is not fresh failure evidence. The current standalone pinned-SDK formatting check and the Developer format stage both returned 0. This inherited log-retention issue remains separate evidence-tooling work; no wrapper changes are bundled here.

The CIS graph is refreshed and fresh: **11,009 nodes / 54,348 edges**, zero diagnostics, build `58bf014c9113475d9d23f633563a1fc205ee5f82a95300900c4a62b41a2ace1b`. The exact new test-class lookup is untruncated. This is bounded navigation, with the existing configured verification-directory exclusion, not complete semantic/test coverage.

Local source review is complete. No new Claude transfer or rereview is claimed; the existing seven approved bundles remain pending service availability and do not contain this new correction. Full Ubuntu, coverage/mutation, shared exact-package/release qualification and the original roadmap remain separate. The roadmap goal stays paused. **PHYSICAL-BYTES is the next confirmed correction.**
