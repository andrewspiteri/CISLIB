---
title: Metrics focused acceptance map
type: planning-reference
status: Draft
---

# Metrics acceptance map

This maps the [preserved specification](sources/CISLIB_Metrics_Implementation_Specification_v1.0.md)
to the [approved implementation leaves](implementation-plan.md). The table
preserves the meaningful evidence planned for each accepted boundary; actual
executed cases, checkpoints, input currentness and outstanding checks belong to
[CIS-0009 evidence](../../docs/changes/CIS-0009/verification.md). It grants no
executable authority or new release qualification.

| Source scope | Owner | Meaningful evidence |
| --- | --- | --- |
| MET-01/02; A-01–A-03 | A | Counter/up-down/gauge semantics; independent histogram/count/sum observations; monotonic timer Stop/Dispose race, invalid-completion no-retry; definition/schema/name collisions; bounded invalid-input diagnostics/no-op capacity behavior. |
| MET-03/05/06; A-04/A-06–A-08 | A/B | Portable public API excludes instrumentation/SDK types; exact Core closure unchanged; pinned optional closure; one combined view; literal identity on native/CIS/histogram/overflow/target-info in every supported format; real small-capacity and oversize/concurrency cases. |
| MET-04/07/11; A-05/A-09/A-10 | E | Actual Kestrel scrape/parser/content type/cumulative data; success, 4xx, 5xx, handled exception, authorization failure, cancellation; one original-route observation after re-execution; changing IDs/unmatched paths never become labels; management exclusions preserve `/_healthcare`; active requests remain host-wide. |
| MET-08; A-11 | C/D | One actual normal/failure boundary per changed module with useful automatic observations; preserve exceptions, values, readiness, overwrite behavior and scheduler waiting. No per-type/logger/GUID dimensions or fabricated spool bytes. |
| MET-11; A-12 | B/E with C/D | Provider active before ordinary sequential startup; metrics reachable before business readiness; Configuration changes do not rebuild provider; pre-DI facts without replay; held callback across timeout/stop cannot read disposed owner; no duplicated provider/reader; two real hosts isolate CIS and native measurements. |
| MET-06; A-13 | B/E | One actual default-catalog footprint/response observation, plateau after new combinations, 64-MiB incremental retained-memory target, explicit four-MiB response-cap interaction and SDK reserved points. No exact SDK allocation claim. |
| MET-09/10/12; A-14 | A/E with C/D | Portable fixture meanings/exported names and current compiling API agree; one real example and PromQL; future RestClient headers/logical/transfer/cancellation/actual-byte boundaries retained without service implementation; concise installed repository skill, routing and read-only no-LLM skill validation. |

Common controls are exact EnvironmentOptions identity, base units, finite catalogs,
no arbitrary consumer scopes, no custom aggregation/exporter/scheduler/history,
host-owned callback lifetime, no bootstrap event replay and no behavior changes
for applications without the integration. A native framework histogram supplies
request count/rate; the library does not record a duplicate transport count/timer.

The final completion record must distinguish source review, native focused passes,
actual optional-package consumption, platform observations, footprint results,
unexecuted gates and work/storage accounting. Historical Configuration/Type Manager
passes are useful regression evidence only for their actual unchanged scope; they
do not establish that Metrics or its optional package passed.
