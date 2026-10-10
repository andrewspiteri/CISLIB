# C# assembly versioning and signing policy

Selected 10 October 2026 for `WP01-S01-T03`, under the user's request to document the foundation decisions. This adopts the supplied guide's section 13 assembly-identity proposal after assessing the current consumer and loader scope. It is a repository decision before publication, not evidence of a released or compatible logging runtime.

## Identity and versions

All C# Core modules, including Logging and MicrosoftLogger, ship in one `CIS.Core` assembly/package. The default namespace is `CIS`; module namespaces such as `CIS.Logging` remain independent of the assembly's name.

| Value | Policy | Current development value |
| --- | --- | --- |
| Assembly name/culture | Preserve the same identity for compatible replacements. | `CIS.Core`, neutral culture |
| `AssemblyVersion` | Keep fixed throughout this package's compatible line, including subsequent package major versions that preserve that line. | `1.0.0.0` |
| NuGet package version | Advance for every published package; use a prerelease suffix for nonstable candidates. Consumer guidance cites this value. | `0.1.0-dev.1` |
| `FileVersion` | Use four numeric components: package major/minor/patch plus an explicitly assigned release revision. Advance the revision for successive candidates sharing those three components, including the stable candidate. Retain the assigned value when rebuilding the same inputs. | `0.1.0.1` |
| Informational version | Let the SDK generate package version plus source-revision metadata when available. Retain the full value in evidence. | `0.1.0-dev.1` before a Git revision exists |
| Public-key token | Preserve the unsigned identity for this compatible line. | null |

The current `0.1.0-dev.1` version remains a local scaffold version; setting an assembly version to `1.0.0.0` does not declare a stable public release. No previous CisLib version has been published, so replacing the scaffold's SDK-derived assembly version creates no historical release obligation.

Each candidate records package version, file/informational versions, source state, package and DLL SHA-256 values, and the assembly path/hash actually loaded by its consumer. An uncommitted workspace must have a source-file hash inventory; an informational version without a Git hash is insufficient to identify its inputs. Once history exists, replay old compiled consumers without recompilation and verify observable behavior and actual candidate bytes. A constant assembly version alone proves neither compatibility nor that a patched DLL was loaded.

## Loader assessment

| Scenario | Scope and consequence |
| --- | --- |
| Ordinary managed .NET application | Initial scope. The ConsoleHuman consumer uses normal application dependency resolution. Restore chooses the package; the consumer's resolved dependencies and loaded DLL identify the runtime payload. |
| Two Core package versions in one load context | A load context resolves one assembly per simple name. The fixed identity does not provide side-by-side package-version selection. The host must resolve one compatible Core dependency. |
| Plugin/custom load contexts | No plugin qualification is claimed. A host sharing Core types should share the Core assembly instance; independently loaded copies have distinct type identities. A concrete plugin scenario needs its own test lane before support is claimed. |
| .NET Framework, GAC or strong-name-required legacy host | Outside the initial `net10.0` scope. No GAC installation or Framework binding-redirect scenario is promised. |

The fixed version is a deliberate choice for the guide's compatible managed package line. Microsoft's [general versioning guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/versioning) recommends evolving assembly versions and warns about fixed versions in GAC scenarios. CisLib currently excludes those scenarios. Modern .NET's [load-context rules](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext) also require a resolved assembly version to be equal to or higher than the requested version; the chosen line keeps it equal. Future loader requirements must be assessed without silently changing an already published identity.

## Signing

Strong-name signing, public signing and delay signing are explicitly disabled in `CIS.Core.csproj`. There is no `.snk`, signing key or public-key token to manage. This fits the selected modern .NET-only scope: Microsoft's [strong-name guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/strong-naming) does not require strong naming for that scope and distinguishes it from publisher authentication.

After publication, do not silently add strong-name signing or change the assembly identity of a compatible replacement. A future legacy-target requirement needs a separate compatibility assessment before adoption; do not publish a parallel signed variant as an informal workaround.

NuGet author signing and Authenticode are separate distribution decisions. The local development artifacts are unsigned; certificate ownership, feed requirements and any distribution-signing step remain part of release preparation. If signing is introduced there, compute and test final candidate hashes after the configured signing steps.

## Decision boundary

This task selects assembly identity and versioning/signing policy. Public API spelling and the complete source/behavior/configuration/schema compatibility policy remain WP-02 work. Historical binary/load checks remain verification work; there is no historical release to replay yet. Future language implementations select their own package and runtime identities.
