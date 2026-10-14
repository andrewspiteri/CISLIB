# Closure review: revised `verify-type-manager-package.py`

This is based only on the supplied text. I ran nothing.

## Prior findings

| # | Finding | Status |
|---|---|---|
| 1 | Design-time positive case could pass vacuously | **Closed for the `Compile` case.** It is renamed to what it proves. The explicit-target case is mostly closed; see the defect below. |
| 2 | `rebuild-after-clean` used `--no-restore` on assets from the rename | **Closed.** The rebuild now restores after the original project is put back. |
| 3 | Solution-level imports not isolated | **Closed.** Root `Directory.Solution.props` and `Directory.Solution.targets` are stubbed. |

The pairing is now correct. The design-time run and the control differ only by `-p:DesignTimeBuild=true`, so a CISTM005 in the control shows that the property is what causes the skip. Success with the helper deleted also proves that neither target ran its generator in the design-time run.

## Remaining defect

**The missing-helper control only proves non-vacuity for `CisWriteBuildTypeInventory`.**
- With `-t:CisWriteBuildTypeInventory;CisWritePublishTypeInventory`, MSBuild stops at the first failing entry target. The control's CISTM005 therefore comes from the build target, and `CisWritePublishTypeInventory` never runs.
- So the design-time pass for the publish target is unpaired. It could pass because the `DesignTimeBuild` guard works. It could also pass for an unrelated reason when the target is invoked directly outside a publish, such as:
  - a publish-only condition being false,
  - an `Inputs`/`Outputs` up-to-date skip,
  - an empty `ResolvedFileToPublish` set.
- The recorded name `explicit-design-time-inventory-targets-skip-with-missing-helper-control` claims both targets.
- **Fix:** run the control once per target, with the same arguments each time and both expecting CISTM005. Optionally do the same for the design-time runs, so each target has its own matched pair.

## Unchanged minor point

- `packageSha256` is recorded but never compared with an expected value. Package identity remains the caller's responsibility.

**Verdict:** Fix the per-target control. Once that is done, I see no remaining correctness defects against the stated CLI contract.
