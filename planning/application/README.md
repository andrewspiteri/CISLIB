---
title: CISApplication and CountryData requirements reconciliation
type: planning-reference
status: Draft
---

# CISApplication and CountryData intake

The user requested the next module and cleanup delivery on 18 November 2026.
[CIS-0011](../../docs/changes/CIS-0011/proposal.md) starts from merged main
`86a97e047e2a17590c8187cfeb7f441ea0e7646f` on
`codex/cisapplication-countrydata`. The originals below are preserved byte for
byte. Their embedded instructions are requirements reference material; the user
request and actual repository authority govern execution. No prior allocation,
product approval or historical result is transferred to this change.

| Supplied input | SHA-256 |
| --- | --- |
| [Specification v1.0](sources/CISLIB_CISApplication_and_CountryData_Specification_v1.0.md) | `25b8d453e5bc5b13c2d6432c1f2b78780093885de57694a12d3b46a54a643c15` |
| [Companion prompt](sources/CISLIB_CISApplication_and_CountryData_Implementation_Prompt.txt) | `705397efb4c229603561104ced2a6b3bf32102c8ce8ec35ec2dbb8f8b5d67fb3` |

Read the [bounded plan](../../docs/changes/CIS-0011/implementation-plan.md) for
ownership, acceptance, cleanup and proposed shared limits. The
[verification record](../../docs/changes/CIS-0011/verification.md) distinguishes
actual intake observations from future implementation evidence.

## Reconciliation with the working contracts

| Area | Required treatment in this checkout |
| --- | --- |
| Baseline | The source inspected `7b5923c0`; actual main includes the complete RestClient, logging, trace forwarding and Configuration source-host correction. Use those APIs. REST is available, not a blocked dependency. |
| Public facade | Proposed `CIS.Hosting.CISApplication`, four ordinary constructor forms, explicit `CreateWeb`, `StartAsync`, `RunAsync : Task<int>`, `StopAsync`, `DisposeAsync`. The thin builder exposes ordinary services/host configuration and deliberate existing module choices. Freeze the exact shared API in AP1; add no generic framework. |
| Preparation | Type Manager then initial Configuration before one final Build. Entry/explicit host assembly supplies Configuration scope; EnvironmentOptions supplies telemetry identity. Bootstrap store selection must work without resolving a temporary provider. |
| Defaults-only | Existing Configuration rejects a nonwatching store without polling. Add an explicit narrow defaults-only mode, reusing catalog, validation, retained roots and reader. Do not disguise a no-op watch as Reliable or weaken validation of real stores. No external failure falls back to defaults. |
| Startup | Existing AddCisConfiguration rejects prior IStartable enrollment. Add only the internal composition seam needed to place its activation before collected user participants. Preserve their order, construct all roots before starts and deduplicate by identity through the existing coordinator. |
| Replacement | Register lazy defaults before customization where appropriate; respect ordinary Replace/RemoveAll and explicit disablement. AddSingleton does not erase prior IEnumerable entries. Resolve one effective instance consistently; reject unsupported conflicts rather than silently restoring a default. |
| Optional Metrics | Core currently lacks the optional assembly's disabled IMetrics implementation. Provide/reuse a Core no-op fallback after optional registration. One narrow parameterless registrar, found through the prepared Type Manager, may register services and attach the existing web pipeline. Deterministic type deduplication, no scanning/loading or lifecycle graph. The existing adapter owns its options/provider/exporter. |
| Web and stopping | Native routing, optional metrics, health, business gate; early ready probe 500, business 503, ready probe 200. Keep the listener's existing early operational access. Tail bridges remain sequential. Signal owned REST/provider cancellation before joining startup, Heartbeat, Configuration and observable drains; disposal joins actual work. |
| Country source | Real keyed IRestClient, CountriesNow error/msg/data and name/Iso2/Iso3 mapping, exact local aliases and one atomic immutable snapshot. Existing transient-http GET policy is selected explicitly; timeout is 30 seconds overall. Both Heartbeat delay and interval default to one hour. |
| State and availability | Capture settings once through IConfigurationReader. Business/invalid refresh returns false, preserves data/timestamp and uses existing health reporting. Initial failure fails startup. Link application stopping to actual REST work; expected stopping is not an upstream fault. |
| REST composition | Existing REST clients are finite, explicitly named and keyed. Collect their existing options through the builder; do not invent a backend, unkeyed default or default retries. CountryData intentionally selects countries/refresh and the existing optional TransientHttp policy, then uses its captured absolute source URL and per-call timeout. No selected clients means no request owner to activate. |
| Metrics meaning | Only entries (Count) and last_success_timestamp (Seconds) are new application observable gauges; no dimensions. Emit zero entries before publication and omit unknown last success. Expected names require an actual scrape before claiming them verified. Reuse existing automatic timings and authoritative identity. |
| Tutorial cleanup | The actual checkout has eight runnable tutorials: the seven named paths plus newer RestApi. Consolidate all eight into CountryData. Retain only named distinct process/assembly/package fixtures under existing tests. No old application archive or self-test schema. |

The existing SDK 10.0.401/runtime 10.0.12 pins, approved Core NuGets/framework,
optional OpenTelemetry and Polly closures, public contracts, native MSTest and
completed CIS-0001 through CIS-0010 accounting remain protected. No new runtime
package or backend is proposed. The existing Metrics skill is used; no new skill
is created.
