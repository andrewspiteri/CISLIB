# First-release compatibility controls

This is an executable qualification of the current `0.1.0-dev.1` candidate, with deliberate regressions. It applies only while the real publication ledger is empty. It reports `NoBaselineForIdentity` and zero historical versions replayed. Published history must use cumulative replay; this command refuses a nonempty ledger.

Build the engineering tool from `implementations/csharp` so `global.json` selects SDK 10.0.401:

```powershell
dotnet build eng/CIS.DependencyChecker/CIS.DependencyChecker.csproj -c Release -m:1
dotnet exec --fx-version 10.0.12 --roll-forward Disable eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll first-release-compatibility --repo-root <clean-candidate-checkout> --candidate-package <exact-nupkg> --dependency-directory <candidate-runtime-directory> --fixture-directory verification/compatibility/first-release --output-root <new-empty-output>
```

The candidate checkout must have a successful locked restore/build and the pinned tools available. The dependency directory supplies the exact packaged Core DLL and its two approved runtime dependency DLLs. Candidate source is inspected and copied for isolated controls; it is never edited. Outputs must be new and empty. The command never publishes a package or modifies the publication ledger.

The harness extracts Core from the actual package, requires equality with the selected deployment DLL, and builds unchanged consumer source through a package reference and an isolated local feed/cache. It executes ordinary and interpolated logging, owned attributes, flush and stop. It freezes the resulting managed deployment, then independently executes CoreReplacement and CompleteUpgrade. For this candidate both approved dependency versions are unchanged, so both upgrade deployments have identical managed bytes.

API checks resolve all assemblies from the pinned .NET 10.0.12 reference pack and selected dependencies. ApiCompat 10.0.401 runs on the explicitly selected .NET 8.0.31 host with attribute and parameter-name rules enabled. Any unresolved-reference diagnostic fails the run. The unchanged API and a real additive public API must pass. Removing the interpolation overload from a source copy must produce CP0002, unchanged consumer source must fail compilation with CS1503, and the unchanged frozen binary must fail with MissingMethodException. A diagnostic from an unrelated failure cannot satisfy these controls.

To execute the same frozen Windows consumer on native Ubuntu, copy its complete successful output and pass `--frozen-fixtures <prior-output>`. Supply the same package and use that prior output's `frozen` directory as `--dependency-directory`. The harness validates the previous package identity and every frozen file hash before replay. Use the explicit fixture-directory option when the new utility/fixture is outside the immutable candidate checkout. Run from the candidate language directory with the native SDK/runtime on PATH.

`results.json` records scope, package/Core/verifier/ledger/source/reference-pack/frozen-file hashes and eight control results. Native command diagnostics and exit codes are retained in `logs/`. A failed attempt is retained and must use another output directory on retry. Keep the whole ignored run directory: the small tracked report is not a complete offline replay kit.

These eight controls do not replace the full configuration, wire, lifecycle, target-asset and cumulative historical matrix defined in [replay rules](../../../../../compatibility/csharp/replay-rules.json), or automatically satisfy the Release profile. Four native unit guard cases additionally reject changed toolchain pins, published history and evidence overwrite.
