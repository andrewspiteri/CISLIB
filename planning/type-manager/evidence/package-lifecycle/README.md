# Exact-package inventory lifecycle qualification

14 October 2026. This extends the package-delivery checkpoint with the same
development package, not a new runtime or release candidate.

Windows x64 and native Ubuntu 24.04 x64 each passed 57 checks: the existing 35
package-consumer checks and 22 lifecycle checks, 11 for each console/Web SDK host.
Both reports use the same SDK 10.0.401, package, helper, verifier, dependency
archives and source fixtures. `checkpoint.json` records their identities and
cross-platform consistency checks. Host binaries are compiled separately per OS.

Package SHA-256: `d3249a77c8ee4c504e87b9c3f52b3be691430bcca47a7bc70468757dc9452ea9`.
Core SHA-256: `abb66e6159e3fd111f341e32c4a61cec12c2bcde738510be9229e5fe76155025`.

## What the new checks establish

- Removing the unused project reference removes both it and its transitive
  library from the current build and publish inventories. A test-only target
  plants their DLLs and satellite immediately before the real generator; its
  marker and existence checks prove the stale assets were present. Fresh runtime
  processes also see these stale files and must leave both removed libraries
  unloaded and return no marker implementation.
- Restoring the reference restores discovery. Renaming the host with restore
  produces the correctly named manifest and host identity for build and publish.
- Clean removes both generated build-manifest names. The following rebuild
  starts without its manifest and must generate a working current one.
- SDK evaluation/Compile succeeds with design-time flags and the helper absent.
  Separately, each explicit inventory target skips under `DesignTimeBuild=true`;
  the identical entry target without that flag must fail with CISTM005. Build and
  publish have separate controls so the first failure cannot mask the second.
- Substituting the old manifest after removal must fail the verifier's selection
  assertion. This is an assertion control, not a claim that runtime validation
  can infer an undeclared project's intended selection.

The SDK can retain an obsolete manifest for a differently named host in a publish
directory. The reports record this observation. Current-host selection and
`dotnet clean` of tracked build outputs are checked; arbitrary publish-directory
garbage collection is not promised by the manifest contract.

## Isolation, review and reproduction

The verifier uses project-local opt-in, an environment allowlist, private package
feed/cache, explicit restore configuration and stubs for parent project/solution
imports. It checks actual restore asset paths and exact Core/helper hashes. This
protects against accidental mixed inputs, not malicious evidence manipulation or
a complete transitive build attestation. The caller must select the intended
package; retention here explicitly verifies its expected hash.

`windows/` and `linux/` retain final reports, command logs and generated manifests.
`inputs/` preserves the executed verifier, source fixtures and exact synthetic RID
fixture package. `reviews/` retains the read-only reviews. The final review's exact
per-target-control correction was applied and followed by both complete native
runs. No production Core or packaged build asset changed.

From the repository root, with the exact package and approved dependency archives
available:

```powershell
python implementations/csharp/eng/verify-type-manager-package.py --repo-root . --package <CIS.Core.0.1.0-dev.1.nupkg> --dependency-cache <NuGet-cache> --rid-package planning/type-manager/evidence/package-lifecycle/inputs/CIS.TypeManager.RidFixture.1.0.0.nupkg --output-root <empty-output-root>
```

Run the same command with native Python/dotnet on Ubuntu. Native outputs execute;
cross-RID outputs are inspected without execution on the wrong OS. The first
attempt selected a different earlier RID fixture and correctly failed its native
asset assertion. Those incomplete reports are retained under `superseded/` and
are not counted. Intermediate runs superseded by review corrections are likewise
not final qualification.

This covers CLI build and folder-publish lifecycle. It does not qualify Visual
Studio task hosting/PublishUrl or replace the final clean-source, six-family,
exact-artifact release bundle. Actual HTTP/DI examples have separate evidence in
[host integration](../host-integration/README.md).
