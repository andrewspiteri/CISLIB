---
title: CIS-0017 intake and verification boundary
type: verification-record
status: Draft
change_id: CIS-0017
---

# Intake — 20 November 2026

The new module starts from merged main `539a05d1`, on branch
`codex/local-transaction-integration`. Completed outcomes retain their own
authority, costs, receipts and qualification limits.

| Observation | Actual result |
| --- | --- |
| Supplied specification | Read and preserved byte-for-byte under `planning/transactions/sources`; SHA-256 `8a9e056a7ad30a2d2d6af475a1d1f0e40a138f10e1f61905a374f6f1d1fcbbb7`. Its handoff is requirements input, not execution approval. |
| Existing contracts | Reviewed Core ownership/hosting, Workflow/REST/Startup, Metrics composition, CommonTesting and package consumers. No new framework or second provider is needed. |
| Dependency metadata | Official Npgsql 10.0.4 nuspec net10 group contains only Logging.Abstractions >=10.0.0, satisfied by existing 10.0.12. Exactly one new mapping, Npgsql, is proposed; no restore was executed. |
| Local prerequisite | Initially no PostgreSQL executable/service or Docker engine. The user started Docker; local Linux/amd64 engine now reports 29.6.1, with no official PostgreSQL image cached. No pull, container, volume or database connection was created. |
| Image metadata | Official PostgreSQL 18 Alpine Linux/amd64 manifest is pinned in the dependency profile; metadata reports 120,245,127 compressed bytes. Actual image/server execution and server minor version remain unverified. |
| Independent intake/review | Three read-only slices used 8 active minutes each, plus 3-minute plan reviews each. No material product/verification-framework expansion found. Two reviews caught the same WORK-005 sequencing mismatch: formal start now depends on WORK-002; final WORK-003/004 evidence remains an explicit completion gate. |
| User file | Untracked CountryData launch settings remain outside changes; original SHA-256 `ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784` is preserved. |

No RM-02 product code, native test, package consumer execution, PostgreSQL
qualification or approved execution allocation exists at this checkpoint.
All source AC-01–28 groups remain pending implementation and actual verification.
The [five-outcome plan](implementation-plan.md) includes each task's tests, docs,
formatting and review. Closed owners, provider profiles and source prompts do not
grant this fresh scope.

The proposed allocation includes intake: conservative root estimate 35 active
minutes plus 33 combined agent minutes, retained separately from future measured
native receipts. These are estimates, not automatic CIS measurements.
The proposed physical-group amendment is 8 GiB including a counted 1 GiB Docker
allowance and the retained 64 MiB reserve. It is not an uncounted new pool.
Only exact newly owned RM-02 material may be restored/removed; no historical
cleanup is proposed. No pull/start/implementation runs before the actual human
outcome, dependency and limit decision is recorded.

## Required future evidence

Record actual provider/server versions, candidate hashes, affected build/style,
native Windows/Ubuntu execution, PostgreSQL independent-connection proof, safe
telemetry/scrape, public packages, skill validation and independent review with
their owning task. Preserve failures and unexecuted scope. Scripted Unknown
proof is not a real network-failure qualification; the EF recipe is not an
executed adapter. No full-suite, mutation, coverage, performance or release
campaign is authorized by intake.
