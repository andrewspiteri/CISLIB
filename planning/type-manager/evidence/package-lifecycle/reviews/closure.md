# Closure review: `verify-type-manager-package.py` (revised)

I reviewed only the supplied text and did not run anything. I assessed it against the definition's contract (§4.2–4.4, D10), not against a whole-directory cleanup guarantee.

## Prior findings: status

| # | Prior finding | Status |
|---|---|---|
| 1 | Rename and clean never assert old-name absence; rebuild could pass on a surviving manifest | **Closed.** Clean asserts both build manifests are absent. The rebuild starts with `manifest` absent and `validate` requires `is_file()`, so a fresh write is proven. Leftover publish-directory files are recorded rather than promised removed, which matches §4.3's "current host manifest" rule. |
| 2 | Design-time only proves the target condition text | **Partly closed.** See defect 1. |
| 3 | Stale files absent at publish generation; satellite not restored | **Closed.** The `BeforeTargets` hook plants all three files immediately before each generator, and `Error` checks presence. The marker proves the hook ran. The manifest content changing (Unused removed) proves the generator actually executed and did not skip. That matters because `BeforeTargets` hooks run even when the target's own condition is false. |
| 4 | Negative control writes a pass entry | **Closed.** `record=False`, the error prefix is checked, and the case name describes the verifier's own assertion. |
| 5 | Upward `Directory.Packages.props`/`.rsp` search; user/machine NuGet config | **Mostly closed.** Root stubs and `RestoreConfigFile` are in place. See defect 3. |

## Remaining defects

**1. The design-time positive case may pass without testing anything.**
- It runs `-t:Compile` with `DesignTimeBuild=true`. The missing-helper control runs a different entry point, `-t:CisWriteBuildTypeInventory`.
- If the generator hooks after `CopyFilesToOutputDirectory` or `Build`, which writing to the output directory implies, the `Compile` target graph never reaches it. The case then passes whether or not the `DesignTimeBuild` guard exists.
- What it does prove: SDK import, evaluation and the `Compile` graph succeed with the helper missing. That is useful, but it is weaker than the recorded name `design-time-skips-generation-with-missing-helper-control` claims.
- Fix: pair the two runs on the same entry target.
  - Run an entry target that reaches the generator in a normal build, such as `-t:Build -p:SkipCompilerExecution=true -p:ProvideCommandLineArgs=true`, with `DesignTimeBuild=true`. Assert success and an unchanged manifest.
  - Run the identical command without `DesignTimeBuild=true` and expect `CISTM005`.
  - Alternatively, keep the `Compile` run and rename the case to describe what it proves, such as evaluation and compile succeeding without the helper.

**2. `rebuild-after-clean` uses `--no-restore` on assets restored for the renamed project.**
- The last restore came from `rename-host`, where `PackageId`/`projectName` was `TypeManagerRenamedHost`. The `finally` block puts the original `AssemblyName` back, then the rebuild skips restore.
- This probably still builds, because nothing validates the project name in the assets file. But it is the same non-representative stale-restore path that the rename fix just removed.
- Fix: drop `--no-restore` here, or restore explicitly after putting the original project back.

**3. Solution-level imports are not isolated.**
- The `Consumers.slnx` builds and restores import `Directory.Solution.props` and `Directory.Solution.targets`, found by searching upward from the solution directory. The root writes neither file.
- Unlike the earlier NU1008 case, an inherited file could change solution-level properties or targets without failing.
- Fix: write `<Project />` stubs for both at `root`.

## Minor (unchanged)

- `packageSha256` is recorded but never compared with an expected value. Package identity is still the caller's responsibility. That is acceptable if the CI wrapper enforces it, but the verifier does not.

## Coverage now sound

- Removing a reference: stale files are present at both build and publish generation, there is no directory-scan regression at runtime, and the stale-manifest rejection is checked.
- Re-adding a reference regenerates the manifest. The satellite file is re-checked.
- Renaming the host now restores. The renamed build and publish manifests are correct, and the manifest is registered in `FileListAbsolute`.
- Clean removes both build manifests. The rebuild is proven fresh.
- The missing-helper control checks the exact `CISTM005` text and restores the helper in `finally`.

**Closure verdict:** Defects 2 and 3 are small fixes. Defect 1 is the only one that affects what a recorded pass actually proves. Fix it, or rename the case, before calling design-time behaviour qualified.
