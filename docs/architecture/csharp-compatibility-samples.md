# Initial C# compatibility samples

WP02-S04-T04 prepares and checks samples for the [four-layer replay protocol](csharp-compatibility-replay.md). This is fixture qualification before runtime implementation or publication. Reference-only APIs, compiler diagnostics and pure deterministic wire comparison are separate from a released package, source execution, unchanged-binary replay and complete layer A/D qualification.

## Compiling consumer patterns

The [C# catalog](../../implementations/csharp/verification/compatibility/samples/catalog.json) binds each source file and incompatible API sample to its covered contracts. Consumers compile as nonpackable libraries against the existing nonshipping API specification and exact approved Microsoft abstraction asset. The retained template fixes `net10.0`, C# `14.0`, nullable enabled, warnings-as-errors and deterministic builds. It has no project/package reference to an application or shipping placeholder implementation.

| Pattern | Covered call paths |
| --- | --- |
| `construction-names` | Setup/object construction, `EnvironmentOptions.Initialize(setup)`, runtime/logger creation, event construction, configuration issue/exception construction and named `Write(level,data)`. |
| `null-calls` | Binding for setup/data/factory/string null literals, with explicit `null!` where the API is nonnullable. This probes source binding; invalid null arguments still have their documented runtime errors. It is not advice to pass them in application code. |
| `numeric-values` | Byte/short/int-to-long and int/float/double-to-double conversions, the maximum safe integer, large integer-string and precise decimal-string forms. |
| `lazy-lifecycle` | Separately named lazy writing, scope construction, explicit flush/cancellation, Stop, asynchronous disposal and result/diagnostic access. |
| `logging-extensions` | Real Microsoft `ILogger` extension/params/EventId calls, generic scope calls and Core's contained provider construction/category surface. |
| `custom-scopes` | A complete caller-owned `IExternalScopeProvider` implementation delegating to Microsoft's real scope provider, installed through `ISupportExternalScope`; ordinary `ILoggerProvider` consumption. |

These methods are compile-only shapes. Do not run them against the reference assembly, which has no implementation. Later source/binary consumers must use real candidate Core packages, preserve relevant settings and execute frozen behavior. No custom Core interface, owning provider constructor or separate MicrosoftLogger package is invented.

## Deliberately incompatible APIs

The sample driver copies the four reference surface files into owned generated directories. Each catalog mutation requires one exact anchor occurrence and records original/changed hashes. Original surface files remain unchanged. A mutated reference must first build successfully; only then can the consumer's expected native diagnostic qualify the break. A missing tool or malformed reference cannot count as incompatibility detection.

| Sample | Change | Expected consumer diagnostic |
| --- | --- | --- |
| `API-REMOVE-LAZY` | Remove existing `WriteLazy`. | `CS1061` |
| `API-RENAME-DATA` | Rename the `Write` parameter while preserving its type. | `CS1739` |
| `API-REQUIRED-CTOR` | Replace parameterless event construction with a required argument. | `CS7036` |
| `API-REQUIRED-PROPERTY` | Add a required property to old event construction. | `CS9035` |
| `API-OBSOLETE-WARNING` | Add a warning-producing obsolete attribute to a supported call. | `CS0618` under warnings-as-errors |
| `API-NULL-OVERLOAD` | Add a competing `Write` overload whose named `data:null!` is ambiguous. | `CS0121` |
| `API-MESSAGE-TYPE` | Change message from string to integer. | `CS0029` |
| `API-NARROW-INTEGER` | Narrow supported long integer construction to int. | `CS1503` |

Six positive builds, eight successful mutated reference builds and eight expected consumer rejections make 22 native sample compiler controls. They prepare API/source breaks for the later harness; no current APICompat, unchanged binary or behavior upgrade pass is inferred from compiler results. The original 21 startup/runtime/value/provider controls remain separate, totaling 43 compiler controls in Developer verification.

## Wire meaning and validity

The [portable wire catalog](../../conformance/logging/compatibility/samples/catalog.json) maps every raw UTF-8 file to a contract, validity expectation and meaning-preservation expectation. Its fixed baseline contains Unicode/newline/whitespace text, explicit null, decimal-string precision, a number, an ordered array and supplied trace/span identifiers. None of these fixtures authorizes generated-value normalization.

Four compatible samples preserve meaning: identical bytes, reordered object members and unknown optional root/resource additions under the reader policy. Sixteen incompatible samples change status/service meaning, erase attribute null, change decimal precision or number/string kind, reorder an array, trim message text, switch schema, remove service, use an impossible timestamp, inject known operation context or user attributes, alter span correlation, change service's type, omit LF or add a BOM. Invalid schema/framing samples and structurally valid semantic breaks are counted separately.

The development-only comparer validates raw records with the existing schema/semantic checker before comparison. It preserves known protocol field presence/value meanings and exact deterministic payload maps/arrays/scalars; unknown protocol fields are tolerated only at schema objects that explicitly permit them. It does not produce logs, capture/truncate events, normalize runtime observations or enumerate history. Passing its 20 native cases qualifies these deterministic samples, not the entire future Layer D. Raw files intentionally include invalid framing and are protected from Git newline conversion.

The original [configuration catalog](../../contracts/logging/v1/configuration.json) and [configuration vectors](../../conformance/logging/configuration-cases.json) cover all 18 settings/defaults/aliases/bounds, 350 per-key and nine cross-option expectations. These complement the initial source/wire fixture set; actual resolver/default replay remains later work. No current runtime behavior is fabricated to execute them.

## Commands and evidence

After locked restore and startup specification compilation:

```powershell
pwsh -NoProfile -File implementations/csharp/eng/check-startup-contract.ps1
pwsh -NoProfile -File implementations/csharp/eng/check-compatibility-samples.ps1
pwsh -NoProfile -File eng/verify.ps1 -Profile Developer
```

The new `compatibility-samples` stage runs after startup controls and retains `contracts/compatibility-samples/summary.json`, native compiler logs, reference/consumer outputs and source/mutation hashes. It returns 0 only when all 22 expected outcomes match; 1 records an execution/qualification failure. Architecture tests discover and execute the 20 wire samples under `ARCH-COMPAT-WIRE-SAMPLES`. Runtime compatibility case groups remain Planned/NotRun; their minimum counts are not satisfied by these development checks.

WP02-S04's policy, ledger format, replay definition and initial fixture deliverables are now prepared. WP07-S04 implements cumulative exact-package API/source/binary/configuration/behavior qualification; WP08/WP09 retain real consumers and actual publication history. The ledger remains empty and Release verification refuses incomplete runtime, harness, platform, example and bundle evidence.
