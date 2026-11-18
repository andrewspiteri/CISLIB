---
title: "WORK-004 Deliver real REST-backed CountryData with atomic refresh and state gauges"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-004
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:b7c2e88b54c241929d5de64923976a77"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0011:outcome:ad93dd647e1f41ad9dac004ce6061dda"
authorization_scope_ids: ["country-data"]
authorization_scope_basis: "sha256:ff8405b6d99e531c21f982abc0d4ddee69968067f7d53497f566090559afbea8"
---

# WORK-004: Deliver real REST-backed CountryData with atomic refresh and state gauges

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver real REST-backed CountryData with atomic refresh and state gauges

## Acceptance criteria

- [x] AP4 / DATA-01–DATA-06: a small normal Program uses facade, intentional app/named client and explicit existing TransientHttp policy choices, native route and RunAsync. One catalog implements startup and heartbeat; capture retained settings once, fetch actual provider envelope with accepted keyed REST/RestResult and overall30second timeout, map business failure, validate complete data and atomically swap one immutable snapshot. Serve exact case-insensitive name/ISO aliases locally with stable errors. Initial successful publication precedes readiness; both default Heartbeat delay/interval1hour, no overlap/duplicate prime. Failed refresh preserves data/time and expected stop cancels real work/drains neutrally. Add only two dimensionless snapshot gauges with proper unknown omission. Include focused controlled-HTTP/timing/scrape tests, concise real run guide and review; no test modes in app.

## Targeted validation

- [x] A01/A05–A11: equivalent Malta variants and zero lookup I/O, invalid identifiers400/unknown404, malformed/null/empty/business/HTTP/conflict failures, atomic complete alias replacement and old state/time retention. Real Heartbeat scheduling/failed-result recovery; actual in-flight REST/body shutdown and callback barrier. Real exporter names/central labels/count vs aliases/time omission/retention and no collection I/O. Defaultsonehour/30seconds; no public internet test dependency.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Windows CountryData | All19 cases passed, including actual pending native body cancellation/drain, real hourly scheduling, atomic replacement/failure retention, exact aliases and actual exporter state | application-consumer-corrections; corrections-observed.trx; consumers-first-observed.trx |
| Review and documentation | Independent component/fixture/probe reviews resolved; no copied applications or weakened byte guards | CIS-0011 verification.md consumer coverage table; CountryData README |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1477; failed=220; possibleTokenSavings=591; ledgerDigest=sha256:e9193c859fa6a2e76285355914a19b4324aebc88574d0595d3cd26fef412e63b |
