---
title: CISApplication follow-up reconciliation
type: planning-reference
status: Draft
---

# Follow-up reconciliation

The pasted prompt is the user's implementation request. Its pinned source URLs
are evidence to inspect, not a replacement for current repository contracts.
The byte-preserved [request](sources/CISApplication_CountryData_Followup_2026-11-18.txt)
has SHA-256 `52743e20b77cce7f7466f0d014589a2408568f5c433e6d14715e1a1102398735`.
Tracked HEAD remains the reviewed and pushed
`cb464a477996e2c19162bf740e73e73c3b28a85d`, on
`codex/cisapplication-countrydata`. There are no tracked changes since that
baseline before intake. The existing untracked CountryData launch settings are
user work and remain untouched and outside this delivery.

[CIS-0012](../../../docs/changes/CIS-0012/proposal.md) is a separate proposed
follow-up. CIS-0011 and all earlier work owners stay complete. Their results
remain historical evidence; their unused recorded native allowance is not
authority for this new package or diff.

| Finding | Reconciled implementation direction |
| --- | --- |
| Late failed Build leaks its root | A delegating provider factory captures the actual returned default provider before native IHost resolution. Web mode installs through WebApplicationBuilder.Host.UseServiceProviderFactory. Successful Build transfers disposal to the host; failed Build awaits the captured provider. No second provider or provider wrapper. |
| Owner creation is concrete | Add narrow owner/factory interfaces; preserve concrete CreateOwner return and existing IHeartbeatService through explicit interface implementation. |
| Monitored extension requires internals | Dispatch through an optional public monitored-registration service capability. Production keeps its current Core monitor binding, validation, weak ownership and settlement. The manual capability accepts recording public monitors through the same extension. |
| Substitution scope | The supported isolated pair is manual Heartbeat plus recording health. Replacing a business factory alone does not replace hosted scheduling, readiness or the production binder. Arbitrary foreign monitor binding into the production scheduler is not silently introduced. |
| Interface aliases and disposal | Owned default aliases share the concrete singleton and idempotent actual cleanup. Final known caller-owned instance aliases use instance registration too, so aliases do not turn borrowed resources into DI-owned disposables. Explicit interface overrides remain respected. |
| Scripted results cannot be created publicly | Expose the existing guarded immutable result-context and success/empty/failure construction route. Retain success/status/failure validation and runtime semantics. No reflection or CommonTesting friend access. |
| Core no-op Metrics is internal | Expose the existing DisabledMetrics singleton for public consumers; reuse its implementation instead of adding another no-op engine. |
| Provider DTO is internal | Scripted REST may deserialize explicitly supplied JSON into the actual generic response type using BCL JSON. It scripts logical terminal outcomes; it does not reproduce HTTP, mapping, retry or transport cleanup. Provider DTOs remain internal. |
| Automatic REST logs bypass local reporting | Add captured per-client LogCalls=true. Only CountryData's countries profile explicitly selects false. Gate URI/log work locally; keep the shared logger and other clients' default automatic summaries. A real-runtime two-client proof is required. |
| Frequent callback diagnostics | One small private reporting helper, independent monotonic failure/recovery budgets, one-minute defaults and bounded suppressed counts. No routine normal-level success events, sleeps or changing-message suppression keys. Outcomes/health/metrics remain per execution. |
| Manual time already exists | Move its one implementation into CommonTesting. A tiny legacy TestSupport forwarding type may preserve existing source call sites; do not duplicate clocks. |
| Formatting | Existing editorconfig is the baseline. Add the requested formatter settings and written field/grouping rules, format changed files with the pinned SDK's standard formatter, and use targeted verify-no-changes. No tracked dedicated formatting command was found; do not create a new runner. |

The provider finding was checked against the pinned official
[HostApplicationBuilder](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Hosting/src/HostApplicationBuilder.cs),
[HostBuilder](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Hosting/src/HostBuilder.cs)
and [web factory application](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/DefaultBuilder/src/ConfigureHostBuilder.cs).
The actual .NET/ASP.NET 10.0.12 path still needs the proposed native regressions.
The supplied formatting settings match the
[official options](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/csharp-formatting-options).

Keep the ordinary CountryData Main, hourly refresh, local aliases, atomic
snapshots, retained usable data and the single maintained example. The current
modified-entry package test is component/package evidence. A new outside-friend
consumer references the compiled current CountryData assembly, not a copied
CountryCatalog source. An unchanged-Main smoke is supplementary only if the
existing supported configuration path permits a controlled provider without a
test mode or new configuration mechanism.
