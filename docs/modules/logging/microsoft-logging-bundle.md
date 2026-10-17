# Microsoft provider release bundle inventory

> Current status, 17 October 2026: runtime, output and provider implementation and the native C# reader migrations are complete development work. Task-numbered milestones, earlier Python comparisons and task-local passing counts below describe historical execution, including their then-pending work. Use the [feature report](../../../planning/feature-status-2026-10-15.md) for current corrective/release status and [native test instructions](../../../implementations/csharp/tests/README.md) for maintained commands. Historical receipts retain their original source and do not qualify the current tree.

WP06-S03-T05 adds the provider slice to the [first-release bundle registration](../../../implementations/csharp/verification/bundle/manifest.json). The provider remains part of CIS.Core. Its API XML, package and assembly digests, documentation, source/fixture/harness inputs, approved runtime/development inventories and prior reports must accompany complete release assurance.

After the normal locked Developer verification and pack, run from the repository root with a new output directory:

```powershell
pwsh -NoProfile -File implementations/csharp/eng/write-microsoft-logging-bundle.ps1 -PackagePath .artifacts/csharp/packages/CIS.Core.0.1.0-dev.1.nupkg -OutputRoot .artifacts/csharp/bundle-draft
```

The command reads the selected nupkg, requires its CIS.Core identity, the net10.0 DLL/XML and all five documented provider public member IDs, and compares the packed DLL to the current Release build output. The compared path/hash are recorded separately; this is not a cryptographic binding to the Developer run. It copies those exact products and records their SHA256. Package repository commit, Developer source commit and current repository HEAD/tree are separate facts. Individually hashed working inputs are not a complete immutable source snapshot.

Maintained shared Core test/project/lock sources are also indexed with a declared SharedValidationSource role, including the Microsoft factory fixture and its private development dependency closure. This shared test corpus includes other Core module tests without claiming provider ownership of them.

The draft inventories all files under the fourteen existing provider-task evidence roots, with their original receipts, commands, reports, fixtures and checksums. Earlier evidence is explicitly PriorDevelopmentEvidenceOnly and receives no qualification credit for the selected candidate. This preserves the failed mutation observations and the below-policy integration branch result rather than promoting them. Only the qualified T03 run's complete native Stryker JSON/HTML are separately indexed as local-only prerequisites. Its JSON must match the hash in the tracked mutation audit; earlier failed runs remain identified through their audit hashes in the indexed evidence. These native files include whole Core source and are excluded from logging-only remote reviews. They still require durable retention and source-bound validation before release.

Every file reference records its repository-relative path, length, SHA256 and role. A separate manifest.sha256 hashes the generated manifest; it does not place a manifest's own digest inside itself. Indexed historical inputs remain references to existing storage. The command retains the selected product bytes but does not materialize all indexed source/evidence or a complete verification archive.

The output always says IncompleteReleaseDraft and releaseQualified=false. All nine section-18 release areas remain Pending. Full six-family exact-candidate suites, both platform lanes, complete coverage/mutation and compatibility, licences/notices and retained toolchain/dependency materialization remain WP07. Candidate-package examples/instructions/skills remain WP08. Complete source/test/history/example/product assembly, final external index and clean extracted replay remain WP09. Existing release checks continue to refuse the incomplete release, and no publication record is added.
