# C# architecture violation controls

WP07-S02-T04 runs the normal architecture checker against isolated copies of the actual Core and packaged Type Manager build-helper source. Run the Developer profile with `pwsh -NoProfile -File eng/verify.ps1 -Profile Developer -MaxBuildNodes 1`. The focused C# class is `ArchitectureViolationTests`.

Each execution owns a fresh temporary repository, restores locked inputs, builds and packs the actual two-project shipping closure, and first completes the normal five-level inspection over that two-project closure with two assemblies, two approved runtime packages and fourteen package entries. The fixture registration contains only those two actual shipping projects. The full Developer inspection independently discovers and inspects all repository projects and target graphs; these fixtures do not replace that inventory or alter its policy.

The standalone positive and fourteen positive/negative pairs cover these material boundaries:

| Deliberate change | Required diagnostic |
| --- | --- |
| Add an unregistered project | `ARCH-PROJECT-INVENTORY` |
| Add the ASP.NET shared framework to Core | `ARCH-FRAMEWORK` |
| Add Microsoft.Extensions.Logging with PrivateAssets=all | `ARCH-PACKAGE-REFERENCE` |
| Change a lock content hash without changing restored assets | `ARCH-LOCK-CONTENT` |
| Add an unexpected build-transitive script | `ARCH-PACKAGE-ASSET` |
| Add unexpected content | `ARCH-PACKAGE-ASSET` |
| Add a case-colliding package path | `ARCH-PACKAGE-ASSET` |
| Put the architecture test PE under the approved Core DLL filename | `ARCH-PACKED-ORIGIN` |
| Append a byte to an otherwise valid Core PE | `ARCH-PACKED-CONTENT` |
| Compile an undeclared NLog-named fixture assembly reference into Core | `ARCH-COMPILED-REFERENCE` |
| Add an actual contract method calling runtime orchestration | `ARCH-CONTRACT-OUTPUT` |
| Give EventMetadata an inherited mutable base | `ARCH-OWNERSHIP-SHAPE` |
| Move an automatic environment identity into a private mutable field | `ARCH-ENVIRONMENT-MUTABLE` |
| Retain mutable caller settings in a nested runtime type | `ARCH-RUNTIME-CALLER-STATE` |

Source mutations are compiled and packed before inspection. The NLog-named assembly is a tiny local negative fixture, not the NLog package or an integration. Its build-time reference is removed before inspection so that the actual PE reference cannot be concealed by project evaluation. The private package case uses an already pinned Microsoft development dependency; it does not widen shipping approvals. Package mutations modify the actual ZIP bytes. No inspected assembly is loaded or executed by the checker.

Every negative must exit 1, report exactly one architecture diagnostic with its specific code and expected project/member/asset context, and remove the previous successful inspection report. A crash, build failure, unrelated diagnostic or stale success fails the test. The native TRX retains a structured `CIS-ARCH-FIXTURE` observation with positive inventory counts, copied source digest, inspector/package/report hashes and the actual negative error. Fresh directories and bounded child processes isolate rows; cleanup stays inside the fixture-owned root, including Git readonly files on Windows.

These controls supplement the existing exact allowlist, compiled-fact, package-observation and caller-mutation tests. Static signatures cannot prove caller collection copying or dynamically selected dependencies. The user's single CIS.Core package and approved Microsoft closure supersede original WP07-S02-AC02's zero-NuGet/separate-bridge wording; the original plan remains preserved. AC01 is checked by the full native inventory, AC03 by the deliberate diagnostic controls, and AC04 by the unknown-origin PE under the approved filename, private package, undeclared compiled reference and stale-report checks.

Complete release coverage, mutation, cumulative compatibility and retained offline replay remain separate roadmap obligations.

Mutation anchors assert exactly one occurrence; insertion requires the pinned file-scoped namespace, intended class declaration and single top-level class close. The environment case moves ApplicationName to a private mutable field and checks that exact field diagnostic, rather than relying on the blanket nested-type rule. Positive observations also require actual target, contract-use and compiled type-use facts.

Git init/add/commit use empty global/system configuration and templates, disabled prompting/hooks and cleared inherited repository overrides. The small foreign PE fixture has local empty build/central-package imports, cleared package feeds and a copied SDK pin. Build children clear inherited MSBuild SDK/executable overrides. The normal Developer profile builds the checker in Release before these tests; focused runs require that same prerequisite and record the inspector hash. Inspection has a finite ten-minute outer bound over its sequential one-minute checker children; restore/build/pack children each have a three-minute bound.

The fixture source commit records its initial input only; deliberately uncommitted mutations are the inspected negative inputs. The compiled-reference control intentionally evaluates the project after removing its build-time reference and inspects the already-built real PE. Raw assembly scratch observations may remain after a rejected check; only the successful inspection.json is authoritative and must be absent. This is architecture rejection qualification, not candidate source/build provenance or release artifact qualification.

The architecture family uses its own twenty-minute TestSessionTimeout in architecture.runsettings because its real isolated restores/builds/packs exceed the ordinary five-minute family session. Other families retain developer.runsettings and its existing limit. Both discovery and execution use the selected settings. An older consistency control now requires the new ARCH-PROJECT-INVENTORY diagnostic prefix; no rejection assertion is suppressed.
