# Type Manager examples (TM-022–025): review findings

The examples cover the scoped behaviours: preparation before builder/container, exact-instance DI reuse, unused-library discovery with inherited and nonpublic candidates, fixed membership after a late load, and a non-inventive Configuration boundary. I found no correctness bug in the sample code. Most findings are verification gaps or verifier behaviour that differs from the documented commands. Line numbers are approximate, counted from the pasted text.

## Medium

**M1. The global `-p:CisTypeManagerEnabled=true` hides what the project file actually does.** `eng/verify-type-manager-examples.py:174-176, 181`
- Every build, msbuild and publish call passes it as a global property, even though both `.csproj` files already set it. The README commands do not pass it, so the verifier is not testing the documented commands.
- In the package lane, `<Sdk Name="CIS.Core">` props are evaluated before the project body sets the property. The global property hides any props-time dependence on it, which is exactly the consumer shape the README tells people to copy.
- Global properties also flow into `CIS.Core.csproj` (source lane), `Contracts` and `Unused`. This can change how they build compared with a normal consumer build.
- **Fix:** drop the `-p` from all four calls. If you also want a negative control, add one with `-p:CisTypeManagerEnabled=false`.

**M2. The HTTP check does not prove what the README says it proves.** `Web/Program.cs:71-76`, `verify-type-manager-examples.py:95-100`
- The README says `GET /catalog` "demonstrates the same relationship from a real request scope", meaning distinct scopes plus exact manager and cached-query reuse.
- The endpoint only reports `samePreparedManager`. Nothing shows that the two requests got different `CatalogConsumer` instances, or that `GetImplementations` returned the prepared cached list.
- **Fix:** emit two extra pieces of evidence:
  - `sameCachedQuery = ReferenceEquals(candidates, consumer.Manager.GetImplementations<IDiscoveryExample>())`
  - a per-consumer identity, such as a `Guid` field on `CatalogConsumer`.
  
  Then assert in the verifier that the identities differ across the two requests and that both flags are true.

**M3. The broken-deployment checks are too weak to support the "useful diagnostics" claim.** `verify-type-manager-examples.py:61, 228-231`
- Missing-manifest and missing-library both pass on the bare substring `FileNotFoundException`. That cannot tell the two faults apart, and it cannot confirm that the missing library was actually the cause.
- The README promises the output includes "the selected file path", but nothing checks for it.
- The README promises exit code 1, but the check accepts any nonzero exit code.
- **Fix:**
  - Require `returncode == 1`.
  - Require the specific missing file name in stderr: `CIS.TypeManager.Examples.Unused.dll` or `<host>.types.json`.
  - Optionally reject output containing `Unhandled exception`.

## Low: verifier robustness and isolation

- **L1. stdout and stderr are merged before parsing** (`:58, 175-177, 193`).
  - `TargetPath` is taken from `stdout+stderr`, and the result JSON is taken from the last combined line. Any stderr warning, or a first-run banner caused by the fresh `DOTNET_CLI_HOME`, breaks parsing or the SDK version comparison (`:120-122`).
  - **Fix:** parse only stdout, and set `DOTNET_NOLOGO=1`.
- **L2. The package transform removes every `<Import>`** (`:160-162`). The verifier claims to replace "only the Core reference/import". Match on `CIS.TypeManager.targets` instead, and assert that exactly one reference and one import were removed.
- **L3. Dependency packages are hardcoded and unverified** (`:127-129`).
  - Version `10.0.12` is hardcoded and the dependency packages are not hashed. A version bump fails loudly, which is acceptable.
  - Better: read the versions from the nuspec dependencies or `Directory.Packages.props`, and record their SHA-256 hashes in the report.
- **L4. Isolation gaps.**
  - The output root is not rejected when it is inside the repo, so ancestor `NuGet.config` settings, `Directory.Build.*` files and `.editorconfig` can still apply.
  - `os.environ` is inherited wholesale, so `DOTNET_*`, `ASPNETCORE_*`, `MSBuild*` and `NUGET_*` settings can leak in.
  - `Directory.Build.targets` is not copied, if one exists at `implementations/csharp`.
  - **Fix:** require an output root outside the repo, and strip or allowlist those environment prefixes.
- **L5. Provenance is incomplete** (`:134-136`).
  - `sourceCoreInputs` hashes only `*.cs` files.
  - It does not hash `CIS.Core.csproj`, `build/type-manager/*`, `eng/CIS.TypeManager.Build`, or the copied `Directory.*.props`.
  - It does not record the git commit or dirty state.
- **L6. The helper-hash check is narrow** (`:190`). It hashes the restored package-cache copy, which proves the package was extracted unchanged, not that this helper ran. That is acceptable for a checkpoint, but see D1 for the README wording.
- **L7. The malformed-manifest check relies on an internal type name** (`:229`). `JsonReaderException` is internal to System.Text.Json. Prefer the manifest file name, or `JsonException` if Core wraps the error.
- **L8. Watch item for the native runs:** in missing-library layouts, check that the .NET host does not reject the missing `deps.json` app asset before `Main` runs. If it does, the verifier fails closed, but the README's missing-library walkthrough would then be wrong.

## Low: documentation accuracy

- **D1.** `README.md` (Exact-package section) says the verifier checks "the package's Core and helper bytes in consumer outputs". Core is hashed in the outputs; the helper is hashed in the private package cache and is asserted to be absent from outputs. Reword to say that.
- **D2.** `README.md` (supported-output paragraph) says single-file, trimming, Native AOT and self-contained publishing are "explicitly rejected". This verifier does not exercise any of them. Either cite the tests that do, or add rejection controls.
- **D3.** `README.md` says "Web Release builds also serve two real HTTP requests". Only `build-release` does this, once per lane; the publish layouts are also Release but get no HTTP check. Say "the Web Release build output (source and package)", or add HTTP checks to the publish layouts.
- **D4.** `README.md` says "The SDK version and runtime reference must match." This is ambiguous. State that the `<Sdk Version>` must equal the exact `CIS.Core` package version, and that no separate `PackageReference` should be added.
- **D5.** `configuration-boundary.md` says the examples "compile this usage". They do not print `AssemblyQualifiedName`, and "closed" (generic) types are not demonstrated. Soften the wording, or add the AQN line to the examples.

## Optional improvements (not defects)

- **Prove the Type Manager never initialises types.** Add a throwing static constructor to `InheritedExample` and `DirectExample`, alongside the existing throwing instance constructors.
- **Make the "builder after preparation" ordering observable.** Emit a `BUILDER_CREATING` marker before `WebApplication.CreateBuilder`. Assert that it follows `PREPARATION_COMPLETE` and that it is absent from fault runs.
- **Replace the constant verify-mode flags with evidence.** The Web `--verify` JSON emits `rootAndScopesShareManager = true` and `queryIdentityStable = true` as literals. They are only meaningful because the code throws first. Emitting identity evidence, as in M2, would let the verifier check something real.
- **Compare manifests across layouts.** Assert that manifest content and assembly identities are equal across build, publish and no-build publish within each lane.
- **Avoid naming a future interface.** `configuration-boundary.md` names a hypothetical `IConfigurationObject`. If that name is not already fixed by the roadmap, use a placeholder such as `<ConfigurationMarker>` so it is not read as a commitment.

## Confirmed sound (no action)

- The expected count of 66 checks matches the loop structure: 2 lanes × 2 hosts × 4 layouts × (1 run + 3 faults), plus 2 HTTP checks.
- The checks for the exact candidate set and defining assembly are correct, and the set equality rules out the abstract base and the interface.
- The fixed-membership checks are correct: the late fixture is a real matching implementation, and both cached and previously uncached queries stay fixed, along with `TryGetType` and `GetDefinedTypes`.
- The shared-framework checks are correct, and there is no build tooling in `deps.json`.
- The fault paths stop before any startup marker, and all Web scope assertions run before `RunAsync`.
- The samples add no new public contracts, and the late-load helper is confined to `--verify` mode.

None of this implies the current native runs pass.
