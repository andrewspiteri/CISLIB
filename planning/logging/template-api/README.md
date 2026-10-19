# C# interpolation implementation record

The user authorized implementation on 19 October 2026 using C# interpolation and the handler approach. The [design draft](design-and-plan-draft.md) records preparation; the [current contract](../../../docs/modules/logging/interpolation.md) records the implemented selection. Earlier frontend BRD copies remain unchanged, and the older roadmap remains paused.

The later [pre-commit review](../../reviews/logging-frontend-interpolation-2026-10-19/README.md) records scoped Claude reviews, the idle control-wake fix and follow-up qualification. The checkpoint below remains evidence for its original source snapshot.

| Stage | State | Outcome |
| --- | --- | --- |
| T0 | Complete | Existing method names, compiler handler overloads by ref, independent typed attributes, invariant formatting and owned built-in snapshots. Compiler compilation preserves existing target-typed event inputs. |
| T1 | Complete | Bounded handler capture, immutable freeze, .NET scalar formatting, span copying and resource guards. |
| T2 | Complete | All severity and general Write overloads, rich metadata, caller-side custom formatters and one consumer rendering step. |
| T3 | Complete | Native concurrency, ownership, redaction, eviction, failures, byte limits, expression/await behavior, output formats, duplicate suppression and lifecycle cases passed in the coherent run. |
| T4 | Complete | Contracts, conformance, compiler consumers, usage, measurements and the complete Developer checkpoint reconciled. |

## Completed qualification

Developer run `7952095e19d34f0c8eadd3a0b509897d` passed with exit zero and no validation issues on Windows x64 with the pinned .NET 10.0.401 SDK and 10.0.12 runtime. It started at `2026-10-19T16:18:00.8250713Z` and finished at `2026-10-19T16:36:41.8629626Z`. Source identity is commit `0288f8cdd86bdaafe9bc6be9c49e4f8e1644e425` plus local frontend/interpolation work; the exact input inventory SHA-256 is `b00855a698c82b465536586a3f493da6b89e56f5569327858428d1ef4c1c73d9`.

| Native family | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Unit | 3,840 | 0 | 0 |
| Integration | 2,120 | 0 | 0 |
| Architecture | 1,032 | 0 | 0 |
| Business | 24 | 0 | 0 |
| Total | 7,016 | 0 | 0 |

All enabled gates passed, including dependency/format/build checks, 25 startup compiler controls, 22 compatibility compiler controls, 12 actual-Core consumer controls, local packaging, package ownership inspection and the example host. All 21 interpolation case IDs reconcile to 22 actual native executions, including both output-format rows. The inventory contains 723 declared case IDs; it does not equate that count with native executions.

The [tracked evidence](../evidence/log-interpolation/README.md) contains exact native summaries, validation, the interpolation reconciliation subset and the actual measurement attachment. Raw reports, contracts, source inventory and the local package are preserved under `.artifacts/csharp/interpolation/developer-final-7952095e19d34f0c8eadd3a0b509897d/`. Package SHA-256 is `303EDC8A3F8641C448897DDDC5E86FCE9A7C5F4CE692A524F00DAA15B105B979`. The qualification label and completion reporting were updated after the successful run; the tested contract bytes are preserved separately, with all behavior fields unchanged.

The actual warm helper measurement recorded ordinary capture at approximately 1.37 microseconds and 848 allocated bytes per operation, and rendering at 1.58 microseconds and 832 bytes. Extreme alignment and guarded precision each produced 512 bounded truncated results. These are single-process mean observations; the attachment states included harness work and exclusions. They are not application callback costs, end-to-end latency, retained-memory measurements or an SLA.

Feature development is complete at this boundary. Mutation and historical compatibility families remain explicitly NotRun in Developer, and Linux/full release qualification remains separate. The older roadmap stays paused. No release or external review is credited.

## Focused evidence

Current focused run `6db32d4c39ff46fda2f58d28d7195435` passed all 22 interpolation cases after the scalar-box ownership fix, zero failed or skipped, including current capture/render measurement attachment. Earlier runs below describe their own source points.

Native run `d91b4081d9b441a88b1580ab4e516e20` passed all 18 interpolation and measurement cases, zero failed or skipped. The attachment `interpolation-costs.json` records separate actual capture/rendering helper observations, including extreme alignment and precision. It supplies no performance SLA or release qualification.

Run `54ceb494942d406688aa9e0a18b03801` passed 44 interpolation, frontend, convenience and processing cases at an earlier source point. Run `ca1657bdd20940f3bcef4054ac985131` passed the expanded 17-case behavior suite. Raw native evidence is under `.artifacts/csharp/focused/<run-id>/`.

The first sandboxed attempt stopped during restore. The next candidate exposed an overload ambiguity with existing target-typed `new()` inputs; ref handler parameters resolved it without changing those callers. The first executing candidate passed 11 of 13 cases; two test expectations were corrected to respect existing eviction-loss and encoded-byte contracts. These attempts remain retained as historical evidence and are not current passes.

Isolated package inspection passed for 27 projects, 33 target graphs and two packaged assemblies after removing a compiler-generated rendering closure. Run `134fbd82a4634ad4bee5094453b34453` passed all 51 selected ownership/public API cases, including four new negative controls for the private copied buffer and immutable part shape. The checker allows only that exact private buffer; it does not allow general object or array state in captured values.

Run `dfc7c46763bb45bf9be85734eb5412d2` passed 21 interpolation cases after adding actual Human/JSONL console output and duplicate-suppression checks. A subsequent ownership probe, `b2c05093b58246c3890ed34e0cefbd35`, exposed retained aliases to caller-owned scalar boxes. Typed unbox/rebox capture now creates owned scalar copies; the controlled alias-mutation test is retained as a regression. Current evidence must follow this fix.

Developer attempt `b80d95e2c36f419aa76d07730e49c3f2` was deliberately interrupted before qualification to complete the ownership checker coverage. Its source inventory, summary and current logs are preserved under `.artifacts/csharp/interpolation/developer-interrupted-b80d95e2c36f419aa76d07730e49c3f2/`; no complete checkpoint pass is credited.

Developer attempt `3527f84bd7314864b9ec5eb1fcec90c8` passed restore, project/dependency boundaries, formatting, the complete build and all 25 startup compiler controls. The compatibility sample that deliberately renames the old `data` argument produced a different diagnostic because the new rich overload also used that name. The new parameter is now `context`, keeping `data:` unique to the original API. Existing compatibility fixture text and expected diagnostics remain unchanged. Raw compiler reports and that attempt's inventory/summary are preserved under `.artifacts/csharp/interpolation/developer-attempt-3527f84bd7314864b9ec5eb1fcec90c8/`.

After that correction, the isolated startup reference passed all 25 compiler controls and the original compatibility samples passed all 22 controls with zero unmatched diagnostics, under `.artifacts/csharp/interpolation/compiler-startup-context/` and `compiler-compatibility-context/`. Native run `3f445691c9524aadb32c7f19a7b008e2` passed both final named/unnamed overload-selection and rich-context ownership cases. These focused checks precede the required coherent checkpoint.

Developer attempt `6d6cbf84174144a6b67447c1836c5d68` passed all compiler/package/ownership gates, all 3,840 unit cases and all 2,120 integration cases. Architecture passed 1,030 of 1,032 cases; two exact-shape checks needed updates for the new captured interpolation field and dictionary type-name spacing in the reference catalog. The captured-payload check now additionally verifies the internal sealed frame and private array of readonly parts; signature mismatch diagnostics identify the expected member. Native run `9e52f7f802c043fcada1304c297acea7` passed both corrected checks. Shipping runtime code did not change for these corrections. The failed candidate's current reports, contracts, source inventory and package inspection are retained under `.artifacts/csharp/interpolation/developer-attempt-6d6cbf84174144a6b67447c1836c5d68/`.

## Source and ownership review

The producer ring, index, P01 scan and control coordination were not changed. Handler construction and caller formatting happen upstream of publication. Capture retains at most 1,024 parts and 8,192 scalars of text, with bounded format metadata. Freeze copies the parts array. Only a fixed whitelist of immutable built-in scalar types and owned strings reaches consumer formatting; other objects and exceptions are not retained by interpolation parts.

Consumer rendering clears pending interpolation in the resulting facts before processing. The existing writer preparation accounts for later truncation. Native formatting failures flow through the incoming worker's established failure accounting. No additional queue, task, cache, producer lock or lifecycle operation is introduced.

The .NET numeric formatter can build intermediate text even for TryFormat. Guarding excessive precision and format length before invoking it is therefore necessary in addition to a fixed destination span. Measurements and source bounds apply to supported built-in formatting; application-defined ToString/IFormattable execution is caller work.

## Qualification boundary

The coherent Developer run above qualifies its original interpolation snapshot; the linked pre-commit record identifies later changes and their qualification. Full release qualification, mutation, historical compatibility, Linux execution and publication remain separate obligations. Earlier reports describe their own source points; the frontend checkpoint is not reused as interpolation evidence.
