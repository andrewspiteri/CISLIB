# WP07-S02-T02 — exact dependency allowlists

Implementation, native qualification and read-only Claude review are complete. Claude returned PASS with no required acceptance changes; the disposition records its optional observations and limits. These results form the WP07-S02-T02 commit checkpoint. The preceding logging-review corrections are committed as f24a68aa. The user-approved read-only payload contained 19 checker, policy, test and documentation files, 128,941 UTF-8 prompt bytes, SHA-256 `084f9fe4a497cb65085262a0abc9733379e4c6fd7ba075b997427f6a9b150ba0`. It excludes Type Manager implementation, credentials and binaries. See claude-payload.json and claude-input-manifest.json.

The [current guide](../../../../docs/architecture/csharp-dependency-allowlists.md) applies the user's single-Core/Microsoft-dependency amendment to the preserved task. The C# policy validator executes inside normal architecture inspection, across all 27 projects, 33 framework/RID graphs and both packaged assemblies. It checks exact project and package closures, build-only references, frameworks, compiler origins and selected assets, metadata identities, external type uses, package groups/origins and development-tool pins. The existing independently collected facts and lock/hash checks remain active. There is no shipping implementation, API, dependency-version or package-layout change.

The Web example's two dependencies are explicitly supplied/pruned by Microsoft.AspNetCore.App, so that example's restored package closure is exactly empty; Core still requires both approved NuGet members. The pinned SDK's observed WindowsDesktop runtime download is recorded as development restore metadata and mapped by exact identity to the existing source. It is not a Core PackageReference or additional framework dependency.

| Native lane | Unit | Integration | Architecture | Business | Total | Stages |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Windows x64 | 3,415 | 1,439 | 553 | 24 | 5,431 | 16 passed |
| Ubuntu 24.04 x64 | 3,415 | 1,439 | 553 | 24 | 5,431 | 16 passed |

Both Developer runs exit 0 with zero failures/skips, pinned SDK 10.0.401 and runtime 10.0.12. Twenty-four new C# cases comprise a real-native positive, twenty isolated observed-fact mutations and three tool-manifest controls, each starting from a passing positive. These qualify the policy branches; comprehensive isolated source/package/binary violation fixtures remain T04. Ownership and contract/output separation remain T03. Full release, coverage/mutation/cumulative compatibility and offline replay remain pending.

The input audit records 942 maintained inputs with identical Windows/Ubuntu hashes. Of these, 941 match current source exactly. The sole later change adds the exact SDK download source mapping in NuGet.config. Both native lanes then pass pinned locked restore and complete architecture reinspection with the new configuration hash, zero policy violations and the same 27/33/2 inventory. No shipping C# or test code changed after the suites. This is not cold-cache qualification.

The retained report archives contain complete Developer report trees, native discovery/TRX and source/reconciliation records; the post-mapping archives contain inspection JSON only. They are report archives, not offline SDK/package-cache replay bundles. The Release negative control exits 2 for the expected missing families/gates/cases and profile mismatch; it is not a Release run or qualification. Original plans, publication records and Type Manager implementation/foreign launch settings are preserved.

CIS graph rebuilding exits 0 with partial diagnostics; status still exits 4 with SQLite readonly-database failure. It is not treated as completion evidence. The evidence index identifies retained bytes and SHA-256 values.
