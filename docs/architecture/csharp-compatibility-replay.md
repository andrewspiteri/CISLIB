# C# cumulative compatibility replay rules

WP02-S04-T03 defines the rules for the later package harness. The [structured protocol](../../compatibility/csharp/replay-rules.json) is `DefinitionOnly`: no replay runner, historical pass, Linux qualification or release approval is supplied by this document. It applies the [permanent preservation policy](compatibility-policy.md), [publication format](../../compatibility/csharp/publications/README.md), [assembly policy](csharp-assembly-policy.md) and [platform policy](csharp-platform-policy.md).

The guide and WP07-S04 supply the four layers. User amendments select `CIS.Core` with `CIS` namespaces, `EnvironmentOptions`, approved Microsoft dependencies, provider code inside Core and bounded truncation. Replay uses those decisions. Neither the source guide nor this definition establishes an additional user instruction to publish or claim support.

## Membership and inputs

Read the complete trusted append-only ledger before examining candidate targets or APIs. Enumerate every applicable package/contract identity, exposed version including intermediate/prerelease versions and partial promotions, retained target asset, fixture and promised execution lane. A candidate cannot make a historical case inapplicable by deleting its API, asset or integration. Removing a target is a failure.

Verify exact ledger/record/package/API/source/binary/fixture/toolchain hashes and preserve all archived inputs. Use an owned working directory for candidate substitution, restore, probing adaptations and reports. Hash original inputs before and after the run. Missing material or an unavailable required environment is an unexecuted prerequisite failure, not a passing comparison. Do not replace an old expectation with candidate output, reconstruct an old baseline from current source or select only first/latest packages.

Derive an expected case matrix before execution. Its key includes identity, historical package version and record hash, layer, fixture, original contract target, lane, runtime scenario and binary substitution scenario when relevant. Record every expected row and its native evidence. Require all applicable rows to pass with no unexpected skips. Counts alone cannot prove membership. A narrowly scoped `NotApplicable` row needs the fixture's retained scope and a reason tied to what that historical contract actually promised; candidate omissions and missing tools are not applicability reasons.

Core's Microsoft provider is part of the Core DLL. Register separate identities only for actual optional third-party integrations. A future identity without a baseline gets `NoBaselineForIdentity` and first-publication controls; it does not erase existing Core history or declare the complete ledger empty.

## Layer A: package and API

For every historical package, run pinned .NET package validation/APICompat against the exact candidate and all original TFM/RID assets. One `PackageValidationBaselineVersion` compares one release; the cumulative harness must iterate history. Retain per-invocation native reports, tool/options, candidate/baseline hashes and target membership.

Use normal compatibility comparison so compatible additions can pass. Strict API equality is not the additive-release policy. Explicitly enable supported parameter-name and relevant attribute checks. Independently inspect promised construction paths, named parameters, optional defaults, constant/enum values, nullable usage, class/struct/interface/layout and assembly identity details that the pinned tool cannot establish. Source/behavior layers still prove overload binding and supported usage. Preserve minimum runtime/target assets and allowed dependency closure.

Never ignore a baseline TFM, regenerate a baseline or suppress a real incompatibility to obtain a pass. A tool false positive needs a narrowly recorded tool/version/input-specific explanation and an independent regression proving the historical contract. It does not remove any case or contract. A new required interface member, renamed parameter, warning-producing obsolete attribute or changed default remains a supported-consumer hazard even when a basic API diff is green.

## Layer B: preserved source

Copy each archived source consumer and build settings into an owned workspace. The original sources, call sites, contract TFM/RID, declared language version, nullable, warning-as-error settings and other meaningful compilation settings remain fixed. Use the archived SDK/toolchain for the original-target lane; current tooling/runtime checks are separately labeled supplements. Do not silently retarget or suppress warnings, and do not change `null` calls or named arguments to repair binding.

The necessary substitution is narrowly bounded: change selected CisLib package-version references or equivalent generated version overrides in the workspace, resolve their necessary approved runtime closure and produce a temporary substituted lockfile. Historical source archives, project settings and lockfiles remain unchanged. Retain exact version/identity substitutions, workspace project/restore differences, resolved assets and every changed dependency. No source edits, namespace rewrites or extra warning suppressions are allowed. The corrected `EnvironmentOptions` spelling is an unpublished user choice; it is not permission to rename a future published call.

Restore exact staged candidate packages from isolated sources/caches. Prove the restored and loaded candidate assembly hashes. Compile preserved constructors, named arguments, `null`, extension methods, overload conversions and supported public-interface implementations. Warnings-as-errors stays enabled. Then execute the compiled consumer and compare frozen supported behavior: compiler success alone does not complete layer B.

## Layer C: unchanged consumers

Copy the frozen consumer DLL, any retained PDB, dependency/runtime manifests and other retained material to an isolated workspace. Never recompile, instrument or edit the consumer. Check consumer hashes before and after execution and retain original manifests. Build commands belong to layer B or first-release capture, never to historical binary replay.

| Scenario | Library replacement | Required preservation |
| --- | --- | --- |
| `CoreReplacement` | Exact candidate Core, including MicrosoftLogger. | Frozen consumer and old optional integration DLLs where present. Keep unaffected dependencies frozen; record only permitted closure changes required for candidate Core. |
| `CompleteUpgrade` | Exact candidate Core and every applicable candidate optional integration package. | The same frozen consumer; record the complete approved candidate closure. |

When Core is the only library identity, these scenarios may select identical payloads. Retain distinct scenario evidence and record that fact; do not invent an old/new MicrosoftLogger package or claim an optional integration was exercised. Once optional integration history exists, both scenarios preserve the old consumer while testing their different integration payloads.

The harness may create isolated dependency-manifest/probing copies needed to select candidate libraries and their permitted closure. Record every adaptation and changed artifact; original manifests stay byte-exact. Reject fallback to old Core or an unapproved dependency. From inside the actual consumer process, capture loaded Core/integration/dependency paths, assembly identities and SHA-256. Package restoration, a copied DLL or stable AssemblyVersion alone does not prove which code ran. Compare frozen behavior as well as load identity.

Run an `OriginalRuntime` case using the retained original execution environment. An exact-runtime test can use a recorded host override such as `dotnet exec --fx-version <original-version> --roll-forward Disable <consumer.dll>` without changing the archived runtime configuration. Run explicitly selected supported `CurrentRuntime` cases separately and record actual runtime, OS/architecture and any needed roll-forward override. A current major runtime is not silently selected by the default host. Its pass cannot replace original-target/runtime evidence. Vendor servicing status and unavailable historical tooling remain explicit, following the platform policy.

## Layer D: configuration, wire and behavior

Run all accumulated frozen configuration, schema and supported-observation fixtures against the exact candidate. Include original defaults/aliases/precedence and blank/invalid/bounds behavior; startup identity and immutable snapshots; one-time path resolution; public errors; ownership, disposal and cancellation meaning; stream separation; severity/filter/lazy capture; scope flow/context precedence; bounded projection/truncation; independent per-output admission/order/failure; fixed flush boundaries, one shutdown deadline and truthful accounting/loss.

Compare parsed JSON meaning while preserving required/known optional presence, null versus omission, value kinds and numeric/string precision. Validate raw UTF-8/no-BOM/LF framing, escaped physical-line integrity, bounds, UTC/UUID forms and the selected schema before comparison. Object-member order is not promised; array and per-output delivery order are. An unknown optional field follows the frozen reader policy, while existing field types/meaning and producer projection remain protected. Do not change decimal-string precision, reinterpret a numeric string as a number or collapse tagged constructor checks just because JSON numeric syntax cannot distinguish two input kinds.

Human layout may evolve within its display-format policy. Compare promised information availability, captured text, streams, levels and bounds; presentation tolerance cannot erase message whitespace or structured content. Do not infer a cross-output total order or strengthen userspace flush into disk/collector acknowledgement.

## Normalization is opt-in and validated

Start with exact documented semantic comparison. A frozen fixture must authorize a named normalization rule and scope; deterministic injected inputs stay exact. Preserve raw observations and report every transformation and prerequisite predicate. Normalizing a field cannot repair an invalid form, missing required fact or violated relationship.

| Rule | Permitted varying observations | Checks before canonicalization |
| --- | --- | --- |
| `N-INSTANCE` | Generated `/resource/instance_id`. | Lowercase D UUID; one stable identity across snapshot/events; independent processes receive distinct IDs. Supplied UUID attributes remain exact. |
| `N-UTC` | Generated event timestamp, initialization/process-start resource instants, flush/stop `BoundaryCapturedAtUtc`. | Seven-digit UTC form, actual process-start versus initialization meaning, immutable snapshot values and fixture-defined clock/deadline relationships. Deterministic instants remain exact. |
| `N-HOST` | Resource process ID, host name, OS description and runtime description. | Optional availability follows the retained capture policy; values match independently observed child/process/runtime facts and snapshot stability. Preserve service/environment/build identity, OS family, architectures and environment kind. |
| `N-WORKSPACE` | A fixture-declared owned workspace-root alias in `CIS.Logging.LoggingOptions.FilePath`. | Exact suffix, absolute path, containment and one-time resolution despite later directory changes. Literal paths and wrong-directory resolution fail. No source/input rewrite. |

These named prepublication choices are explicit, limited environment adaptations. A frozen fixture that did not opt into them cannot acquire a new broad ignore list to make a future candidate pass. New normalization policy must preserve its published contract and original expectations. User attributes, operation/trace/span identifiers, severities, names, messages, exception/error meaning, counters, truncation markers, completion/success/loss, timeouts, ownership and validation order are never erased. Do not sort record streams, normalize all timestamps recursively, treat every UUID as generated or hide distinct outputs under one aggregate result.

## Zero history and first exposure

The global exception requires the trusted complete ledger to contain zero actual publication rows. No stable releases, no matching candidate target or no current-tool support is insufficient. Report `NoHistoricalPublications` with historical comparison count zero, separately from candidate/harness qualification. A previous prerelease ends the exception, including for the first stable candidate.

With zero history, still qualify all four layers using real candidate-consuming source shapes, candidate-built binaries frozen for that qualification run, schema/configuration/behavior fixtures, positive controls and deliberately incompatible controls. Replaying those newly frozen consumers proves harness operation; it is not an upgrade from a historical release. Negative controls must fail the intended compatibility checks rather than crash because a tool or prerequisite is missing. Execute applicable replacement/runtime scenarios and required platform coverage with actual candidate loads.

Retain source/settings, binaries/dependencies, toolchains, expected observations and the complete verification kit. Append the first immutable publication baseline only when the exact package actually becomes available. Every exposed package/version establishes history, including partial aggregate promotion; resume promotion using the same verified bytes. Synthetic controls and local development candidates never enter that ledger.

## Definition and execution boundary

The four existing compatibility case groups remain `Planned`, without native method bindings. This task records their rules, not passing cases. T04 adds compiling source patterns and incompatible samples; WP07-S04 implements and qualifies cumulative package execution. WP08-S04 prepares the upgrade consumer and WP09 freezes actual publication history. Release qualification still requires the full runtime, six-family evidence, examples, both platform lanes and retained material.
