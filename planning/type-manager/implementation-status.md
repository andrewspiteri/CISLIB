# Type Manager implementation status

14 October 2026. In progress, in the same CIS.Core assembly and solution as logging.
The supplied completion pack remains unchanged design input under `sources/`;
it does not approve publication or establish implementation evidence.

## Implemented in the repository

The `CIS.TypeManagement` module now provides asynchronous preparation, strict
schema-1 inventory validation, default-context loading with identity checks,
one complete type catalog, exact assembly-qualified lookup, CLR assignability
queries, immutable results, captured inputs and a synchronized query cache.
Preparation diagnostics retain selected/reused/loaded identities, counts and
elapsed time. Required loading/discovery failures prevent publication and preserve
inner/loader errors. No runtime dependencies were added to Core.

Argument errors are synchronous; cancellation and operational failures belong to
the returned task. Relative alternate manifest paths resolve from
`AppContext.BaseDirectory`. UTF-8 BOMs and additive fields are accepted, while
ambiguous duplicate keys and invalid UTF-8 are rejected. Invalid path diagnostics
can preserve the submitted relative path; validated paths are absolute.

The source build integration uses pinned SDK selections to write automatic build
and folder-publish inventories. It supports native/explicit win-x64 and linux-x64
framework-dependent, untrimmed output, excludes native/reference/resource assets,
and fails unsupported deployment modes. Its metadata-only helper remains a
nonshipping build tool with SDK references and no NuGet dependencies.

Real process fixtures cover broken base dependencies, wrong file/loaded/runtime
binding versions, both reference-attribute metadata forms, corrupt executables,
forwarding and same-name types from different assemblies. The ordinary generated
host now actually prepares Core before its container boundary and discovers an
initially unused private-constructor inherited implementation. A separate process
cancels during the real library load and proves no manager/container is published.

The initial reference DLL, original compiled consumer, matching source and hashes
are retained under `implementations/csharp/verification/contracts/type-manager`.
They are an unpublished development baseline, not a historical released API.

## Roadmap state

No full milestone or release gate is declared complete. The following separates
implemented behaviour from outstanding qualification; no original checkbox is
silently marked complete by a prototype or an uncommitted test result.

| Tasks | Current state and outstanding work |
| --- | --- |
| TM-001 | Repository boundaries inspected: one CIS.Core net10.0 assembly, SDK 10.0.401, CIS.TypeManagement namespace, existing approved Microsoft runtime closure preserved. |
| TM-002 | Public runtime contracts and frozen initial reference/source/compiled-consumer baseline present; API/source/binary checks pass against actual Core. Targeted runtime review closure is retained; this is an unpublished development baseline. |
| TM-003 | Real inventory and loader fixtures present; fresh-process ordinary unused discovery and failures exercised. |
| TM-004–TM-005 | Source SDK selection and manifest generation implemented; native/RID matrices pass on Windows/Linux. Package delivery remains open. |
| TM-006 | Strict reader and captured inputs promoted into Core; focused unit and process checks pass. |
| TM-007 | Reviewed Windows lifecycle has 32 passing checks, including fresh default-directory publish and disabled package-copy rejection. Earlier both-platform inventory matrices pass; Web SDK evidence uses CLI `-o`, not Visual Studio/PublishUrl copying. Packaging setup remains open. |
| TM-008 | M1 gate remains open pending complete supported-profile inventory qualification and setup reconciliation; reviewed source-mode checkpoint evidence is retained. |
| TM-009–TM-010 | Selected maps/default-context loading implemented; identity/reuse/failure cases pass on both platforms. |
| TM-011 | Complete definition/relationship enumeration implemented; forwarding/nonpublic/open-definition and side-effect tests pass. Explicit enumeration instrumentation remains. |
| TM-012 | All five failure kinds exercised with contextual diagnostics, including a locationless host; the full loader/publication gate still has outstanding cases. |
| TM-013 | Task/publication boundary implemented; pre-work and real mid-load cancellation pass. Deterministic final-publication cancellation evidence remains. |
| TM-014 | M2 gate remains open for the remaining loader/publication cases. |
| TM-015–TM-019 | Protected identity maps, assignability matcher, exact lookup, cache/concurrency and precomputed queries implemented with focused tests. |
| TM-020 | M3 gate awaits the remaining acceptance assertions and gate review. |
| TM-021 | Preparation report implemented and reconciled in tests, including newly loaded library classification. |
| TM-022 | Real pre-container fixture works; final minimal user-facing console example remains. |
| TM-023–TM-025 | DI/ASP.NET consumer example, Configuration boundary documentation and supported-host gate remain. Web SDK inventory is distinct from a DI runtime example. |
| TM-026–TM-028 | Initial A01–A30 mapping is recorded in `acceptance-evidence.md`, with executed checks and explicit gaps. Meaningful mutation and complete architecture qualification remain; initial build/dependency controls exist. |
| TM-029 | Initial API/source/unchanged-binary replay passes; full behaviour/schema compatibility bundle and negative qualification controls remain. |
| TM-030–TM-031 | Performance/memory measurements and final maintainability/six-family gate remain. |
| TM-032 | Package integration incomplete: clean restore cannot get an inferred RID from package props. An isolated SDK-owned second-restore experiment passes on Windows/Linux; no-restore/offline/incremental/concurrent graph behaviour and actual Core packaging still require qualification before adoption. |
| TM-033–TM-036 | Final documentation, exact-artifact verification bundle, clean package consumer smoke tests and handoff remain. |

## Executed evidence and limits

See `verification.md` and `evidence/runtime-closure/` for exact reports and commands.
Current Core SHA-256 is `53619d263b200fa3d53bb5d9455dfa599110ce83ad30b83448b480bd542e1cb3`. Current frozen-candidate result: 63 Core unit cases plus 9 runtime architecture
cases (72 total) pass on both Windows and Ubuntu. Twelve runtime process cases
and two generator rejection cases (14 total) pass on each platform with verified
identical fixture/Core bytes. ApiCompat, original binary replay and historical-
source rebuild/run pass against that DLL. These are source-development results, not
package release or all-six-family qualification.

The initial Claude inventory/runtime reviews found actionable defects. Runtime
fixes and new regressions are present; final targeted Claude review returned PASS.
An incorrect review assumption about a collectible resolver fixture was caught
by a negative control; the fixture was strengthened and requalified on both OSs. The user
has given standing authorization to proceed with required read-only reviews
without further permission prompts. Reviews exclude unrelated logging source.

## Coordination and shared state

Logging chat `01a12085-7b0b-7e10-89ef-b152f9a4e4ba` authorized a shared source window
through runtime promotion, then resumed logging-only implementation. Type Manager
owns its module, dedicated tests/fixtures/build assets, compatibility baseline,
verification scripts and planning files. Solution/dependency registrations and the
UnitTests unsafe setting (only to test function-pointer rejection) are coordinated.
Logging's planning, conformance and Core changes are excluded from Type Manager
staging. Root Developer reports and graph refreshes are coordinated separately.

This implementation checkpoint does not authorize publication. Coordinated Windows Developer
run `06673e19d022465091e1f241285f454f` exited 0: all 3,251 unit, 1,276 integration
and 452 architecture tests passed with no skips. Restore, dependency boundaries,
formatting, build, contract/consumer checks, pack and package boundaries also passed;
the build reported zero warnings and errors. See `evidence/combined-developer/`.
This combined result includes logging's new shutdown tests and is separate from
the frozen Type Manager candidate evidence above. It is not a release gate result.
Graph extraction remains partial; its recorded diagnostics and source freshness
must be considered separately from executed verification.

The latest fixes validate every selected file before loading any new assembly,
reject dynamic and custom-context bind results, handle query-cache reentrancy,
and align report classification and host/path diagnostics. Regression controls
reject prior DLLs for early loading, dynamic substitution/reporting and foreign
resolver acceptance. These controls supplement the still-incomplete mutation gate.

The subsequent inventory review found and corrected disabled package-copy handling,
relative publish-directory resolution and verification false positives. The
32-case Windows lifecycle and a seeded stale-manifest negative control pass in an
isolated snapshot. Ubuntu passes the nine-case Web SDK CLI matrix plus separate
default-relative-publish and disabled-copy checks. The targeted follow-up review
returned PASS; see `evidence/inventory-review-fixes/`. These build-only changes
postdate the combined Developer result and have separate recorded qualification.
An exported Git index without uncommitted logging changes also builds the complete
solution with zero warnings/errors and passes all 63 Type Manager unit plus 14
Type Manager architecture cases. Frozen baseline hashes survive that checkout.
