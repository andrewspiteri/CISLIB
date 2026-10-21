# Compatibility dependency deployment correction

The first native Linux Release attempt ran 239 compatibility cases: 152 passed,
87 failed, none skipped. Every failure belonged to
`TypeManagerCompatibilityTests` and reported the same missing
`Microsoft.Extensions.Logging.Abstractions.dll` at fixture construction. The
fixture assumed the approved dependency DLLs were copied beside the loaded
Core. The approved ASP.NET shared framework can now satisfy those references
without private copies. This is a current Healthcheck integration regression in
the existing compatibility harness, separate from the eight declared global
Release gate deficits. The original failed profile and native TRX remain retained.

The failed methods comprise 66 frozen-behavior rows, nine deliberate regression
controls, four original-consumer rows, three renamed-boundary rows, two API rows,
and three single controls. No Healthcheck method failed. With all 87 failures in
that class and no skips, the remaining 152 passes comprise the existing 127
Healthcheck cases, 24 Type Manager guard cases and one scheduling case. Native identity/TRX
reconciliation remains in the coordinator's retained execution evidence.

The correction changes only `TypeManagerCompatibilityInputs.cs`,
`TypeManagerCompatibilityFixture.cs` and `TypeManagerCompatibilityGuardTests.cs`.
The fixture resolves the same two approved `10.0.12` NuGet packages from the actual
parent NuGet cache, using Core's unchanged `packages.lock.json`. It requires the
approved package identity/version and the expected managed assembly identity,
version and Microsoft public-key token. The exact `lib/net10.0` DLL must equal
its unique archive entry. Every selected lockfile, DLL, archive, restore metadata
and raw-hash sidecar is pinned and rechecked by the existing immutable-input
mechanism; the report records their paths and hashes.

## Selected restore attestation

NuGet distinguishes signed-package content hashes from raw archive hashes.
Its primary implementation exposes separate `GetContentHash` and
`GetArchiveHashAsync` paths; signed content hashes use
`GetContentHashForSignedPackage`. See the
[NuGet PackageArchiveReader implementation](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/PackageArchiveReader.cs).
This reference explains the distinction; the consulted moving source is not a
substitute for the pinned SDK/toolchain in the execution receipt.

For the selected restored packages, `.nupkg.metadata.contentHash` must equal the
independently selected Core lockfile entry. The actual archive's recomputed raw
SHA-512 must separately equal its `.nupkg.sha512` sidecar. The extracted DLL's
SHA-256 must match the exact ZIP entry, and its assembly identity must match the
approved dependency. The fixture records both content-hash and raw-archive facts.

This explicitly trusts the selected locked restore's metadata as its restore
attestation. It does not independently recompute NuGet's signed-package content
hash, verify package signatures/certificates, or establish cryptographic trust in
a cache whose metadata, archives and source lockfile were all substituted.
Successful current locked restore, independently pinned checkout inputs and
retained immutable evidence remain prerequisites. No new signature algorithm,
SDK assembly loader, dependency, package upgrade or gate waiver is introduced.

## Preserved frozen controls and native follow-up

Frozen consumers keep their original .NETCore runtime configuration and original
deps files. The two exact NuGet DLLs are deployed through the existing separate
`approved-dependencies.deps.json`; shared-framework implementation DLLs are not
substituted. The existing `FrozenConsumerRequiresApprovedDependencyDeployment`
case still omits `--additional-deps` and requires the same actual failure evidence.
No frozen input or expected original-API compiler bytes are rewritten.

Nine new guard cases cover both approved identities and rejection of wrong
version, wrong raw-archive hash, mismatched restore metadata, changed extracted
DLL, missing/duplicate ZIP entry and wrong assembly identity. These are authored
tests, with no native pass credit. The unchanged 127 Healthcheck cases remain a
separate known scope. The expected whole compatibility discovery count becomes
248 (239 existing plus nine new guards), subject to actual native confirmation.

The native coordinator can run the full compatibility project through the
standard launcher:

```powershell
pwsh -NoProfile -File eng/test.ps1 -Project CompatibilityTests -Filter 'FullyQualifiedName~CIS.Tests.CompatibilityTests'
```

Use the pinned subject SDK/runtime and independently pinned ApiCompat tool host;
clear stale candidate/provenance overrides unless a complete current producer
set is deliberately selected. The parent NuGet cache must contain the two exact
restored archives, DLLs, `.nupkg.metadata` and `.nupkg.sha512` files. Native
Windows/Linux compatibility and both full Release profiles require new output
roots after the updated harness freeze. Any subsequently exposed ApiCompat,
dependency-omission or deterministic original-API compiler-byte failure must be
retained and diagnosed separately; this source correction supplies no future
native result or release qualification credit.
