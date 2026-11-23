---
title: CIS-0023 verification
type: verification-record
status: Draft
change_id: CIS-0023
---

# RM-06 Audit verification

Implemented on `codex/rm06-audit-contracts` from merged baseline
`dcb61dc13c8af138f409db4e13c477cc3a06a0b5` under the user's approved
[P01–P16 profile and shared allocation](implementation-plan.md). The exact
[requirements source](../../../planning/audit/sources/CISLIB_RM06_Audit_Contracts_and_Delivery_Integration_Specification.md)
retains SHA256
`1dba943444b4228d086d765152aff61c2197d9a0cb422dbe4c607394cad2a402`.
The attachment supplied requirements; execution authority came from the user's
separate approval. No push, merge, release or existing database access was used.

## Delivered behavior

- One closed, versioned portable envelope and strict exact-byte codec; trusted
  immutable capture stays distinct from decoded records and source eligibility.
- `StageAsync` uses the original transaction participant and poisons usable
  contexts on Audit-side refusal/fault. Applications complete their owners.
  `SubmitAsync` preserves actual NotSubmitted, OutboxCommitted and Unknown facts,
  including receipt, cleanup and original errors, without automatic Unknown retry.
- Passive route definitions, selected native hosting/facade integration and actual
  capture/publication drain preserve sequential startup and RM-05 job dependencies.
- Explicit singleton-safe Security bridge, two portable detail schemas and narrow
  configured infrastructure-observation suppression preserve actual authorization.
- One optional bounded capture-result counter, public bounded CommonTesting
  writer, explicit CountryData observation examples, one
  [consumer guide](../../modules/audit/README.md) and
  [repository skill](../../../.github/skills/cislib-audit/SKILL.md).

## Actual selected native results

Execution used SDK **10.0.401**, selected .NET/ASP.NET **10.0.12**, ordinary
MSTest/VSTest and the existing projects and fixtures. The table identifies final
passing selections; some selections overlap and must not be summed into a
distinct release count. Every listed test selection has **zero failures and
zero skipped/not-executed cases**.

Local detailed logs, TRX and exact invocation/cost receipts remain beneath
`.artifacts/audit/CIS-0023/`; these ignored artifacts are local evidence, not
shipping dependencies. The complete local result index is
`.cis/local/audit-intake/final-trx-index.json`.

| Check | Passed | Owned evidence phase |
| --- | ---: | --- |
| Current Windows capture, wire, publication, hosting, Security and CommonTesting units | 117 | WORK-004/current-windows-audit-unit-regression |
| Same current managed unit candidate on actual Ubuntu 24.04 runtime | 117 | WORK-004/current-ubuntu-audit-managed-units |
| Independent common/Security schema, raw-wire and identity vectors | 63 | WORK-005/security-contract-architecture-tests |
| Actual collector/exporter and Console/Web failed-Build cleanup | 9 | WORK-002/publication-metrics-facade-integration-tests |
| Existing affected application/workflow/host lifecycle regressions | 62 | WORK-002/existing-host-impact-regressions |
| Initial PostgreSQL/RabbitMQ Audit service selection | 13 | WORK-004/audit-native-service-acceptance |
| Additional actual denied-grant/no-recursive-publication service case | 1 | WORK-004/audit-native-denied-grant-corrected |
| Outside-friend Core/CommonTesting package consumer | 1 | WORK-004/audit-public-package-consumer-corrected |
| CountryData opt-in Audit unit examples | 7 | WORK-004/country-audit-example-tests |
| Shipping dependency boundary | 1 | WORK-004/current-shipping-dependency-boundary |
| Public API compatibility | 1 | WORK-004/current-public-api-compatibility |

The **14 distinct native service cases** ran against fresh owned PostgreSQL and
RabbitMQ Testcontainers using the existing digest-pinned images, real durability,
TLS, generated endpoints and authenticated readiness. Both final service TRX
files passed the existing strict native report guard with zero skips. The cases
exercise original-transaction commit/rollback/unfinished ownership, pre-Messaging
refusal poisoning, reconstruction from committed source state, exact retained
retry IDs/bytes, deliberate same-AuditId/new-MessageId submission, native
connection/byte pressure, real Generic Host startup/drain, allowed ingress/retry
without recursion, forged producer rejection and denial without the required
grant. The denied case checks the rejection and its quarantine handoff share the
native transaction, retain the incoming bytes/provenance and produce no recursive
Application-purpose Audit publication.

Standalone Unknown and confirmation-loss tests inject uncertainty **after an
actual successful native boundary**. They establish conservative result handling
and retained identity, not arbitrary physical network-failure coverage.

The public package fixture packed and restored the selected Core/CommonTesting
candidate outside friend assemblies, built and ran it, and checked the exact
deployed closure. It exercises immutable public capture, source eligibility,
original-owner Stage substitutions, exact submission/cleanup facts, bounded safe
script metadata, exhaustion and cancellation/fault distinctions. These public
substitutions establish no SQL or broker proof by themselves.

Ubuntu execution used the retained native Linux ELF .NET/ASP.NET **10.0.12**
runtime and the existing SDK's managed VSTest runner with ReadyToRun disabled.
The executable SHA256 was
`01d89e0a0191052bfea616cd4ce624c8faf13b05bbddf7f64499c23e2a9d9269`.
This was actual Ubuntu managed-unit execution; no Linux SDK build or new
Linux-client PostgreSQL/RabbitMQ service pass is claimed.

Affected final builds completed with **zero warnings/errors**. Scoped ordinary
pinned-SDK whitespace formatting and `--verify-no-changes` passed, including the
last changed denial probe. No project, source mapping, dependency lock, SDK/runtime
pin or solution file changed. The shipping dependency and API checks passed.
Independent contract, capture, native consumer, Security, lifecycle and skill
reviews found no remaining material issue after the recorded corrections. The
installed skill validator passed. Local Markdown links and whitespace were
checked; final CIS plan/graph results are recorded at closeout below.

## Retained failures and corrections

The journals preserve every failed or inconclusive invocation and its cost.
None was rewritten as a pass or used to reset attempts.

- The initial capture boundary test exposed an exact-size encoder growth issue.
  Corrected bounded buffer growth admits additional library-owned allocations
  before allocation and checks actual final bytes. Small/large ASCII/Unicode,
  pressure/release and original encoder faults have passing regressions.
- An initial multi-node build failed without a diagnostic; subsequent serial
  affected builds passed. Sandboxed workspace formatting could not open its
  required named pipe; ordinary scoped formatting passed with authorized local
  process access. An invalid formatter argument experiment was retained.
- Initial analyzer/integration source errors and a pre-canceled startup ordering
  issue were corrected and tested before the passing host checkpoint.
- The first public package assertion expected four retained package entries.
  The selected shared framework legitimately pruned the two Microsoft abstraction
  packages. The corrected assertion still requires both exact CIS packages and
  rejects anything outside the same four-entry approved allowlist and pins.
- The first added denial probe observed correct denial but queried the fixture's
  default application identity. The corrected probe queries the actual local
  application/environment and verifies the retained quarantine handoff, exact
  bytes, provenance and same-transaction rejection. Product authorization and
  fixture behavior were not weakened.
- Native CIS refused updates to a started task's executable contract. The small
  WORK-005 support owner therefore completed the already-approved portable
  Security validation scope. WORK-004's integrated defect scope was reconciled
  while Draft. All earlier owners and failed metadata calls remain preserved.

## Bounds, ownership and evidence limits

The shared ceiling remains **24 active hours / 28 logical attempts**, including
**2,700 restoration seconds**, **6 GiB physical counted storage**, **64 MiB
protected reserve** and a **2 GiB Docker allowance inside that ceiling**. Actual
source estimates, agent work, commands and failed attempts remain in
`.cis/local/audit-intake/resources.json`, its metadata journal and the native
cost journal. The settled checkpoint charged **54,615.905 seconds** (about
15 hours 10 minutes) and **11 logical attempts**, including final CIS closure.
No owner's reserve was consumed or allocation exceeded. The direct SDK/source
journals carry these actual charges; native CIS has no batched executions, and
its zero batch counters do not mean zero effort or renewed execution authority.

The settled physical checkpoint counted **5,946,824,168 bytes**, including the entire
Docker allowance, protected reserve, retained cache charge and orchestration
overhead; **495,626,776 bytes** remained. Final read-only Docker/process inventory
found no integration containers, volumes, Testcontainers reapers or
Audit/integration test processes.
No foreign resource or historical fixture was deleted. The user-owned untracked
CountryData launch settings remain unchanged at SHA256
`ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784`.

There is no Audit destination store/schema/migration, archive/query/retention
service, second inbox/outbox, collector, acknowledgement protocol or new retry
owner. Durable receiver same-AuditId byte conflict enforcement is a documented
future obligation. Native source delivery does not establish archive persistence,
compliance, release readiness or full coverage/mutation/performance qualification.
CountryData remains database-free by default and does not enable Audit implicitly.

## CIS closeout

All five canonical tasks are **Complete**. The settled plan is **valid**, with
zero errors, warnings or open decisions in plan validation. Work status reports
no executions, reservations or recovery hold. Final receipts are retained in
`.cis/local/audit-intake/`:

- `complete-integrated-Audit-WORK-004.json`
- `settled-RM06-work-status.json`
- `completed-RM06-plan-validation.json`
- `settled-accounting-checkpoint.json`
- `settled-storage-checkpoint.json`
- `frontmatter-complete-audit-graph-build.json`

The pre-closeout graph refresh has **zero errors, zero Audit warnings and the
54 warnings already present at intake**; its status remains partial for that
existing documentation backlog. Build identity:
`sha256:b2c41ab765c2b1d03e349095198d917dc459d29eb045fe267c7f6a76612325f2`.
The five new Audit documentation records were cataloged and given required
metadata without changing their Draft governance status. No product record was
approved on the user's behalf. Subsequent task/verification metadata edits do
not assert a new graph-currentness fingerprint or rerun completed product tests.

At implementation closeout, source and results remained local on
`codex/rm06-audit-contracts`; no commit, push, merge or release had been performed.
The user subsequently authorized committing and pushing this feature branch
without merging. That publication does not repeat or enlarge product acceptance.
The user-owned untracked launch settings are excluded from this change.

## Preserved native task snapshots

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2250; failed=310; possibleTokenSavings=591; ledgerDigest=sha256:b675f07a79f42bc75fc1896b3c23422e3b81924b90cf1abf01fab83234353ce0 |
| WORK-003 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2258; failed=312; possibleTokenSavings=591; ledgerDigest=sha256:630483067c99a1fe24d467f3f00b864470040787845678a2db3bb31f6a40648d |
| WORK-005 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2260; failed=312; possibleTokenSavings=591; ledgerDigest=sha256:fd030cd651fee7b509e263109ee3bfbf080f09d45833b7a951b416fd38408d66 |
| WORK-002 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2262; failed=312; possibleTokenSavings=591; ledgerDigest=sha256:fec5b80e88d6aabbc02c235e0489976e3eca0aeeb8ebde883aa702b7debe9113 |
| WORK-004 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2271; failed=312; possibleTokenSavings=591; ledgerDigest=sha256:636183fbad8552c57b6566fc63be790ff592e2a747f0472e971e80561b45eeea |
