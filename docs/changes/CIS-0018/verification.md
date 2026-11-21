---
title: RM-03 intake and verification record
type: verification-record
status: Draft
cis:
  stable_id: cislib:change:cis-0018:verification
---

# Intake checkpoint — 21 November 2026

Actual main/remote baseline:
`8ee1858ce9d84dc4210bbfda8690f03ecb6e9fbc` (RM-02 merge PR11).
Planning branch: `codex/durable-operations`.
CIS created CIS-0018 as Proposed at that exact baseline. Creation inputs preserve
the one existing untracked CountryData launch settings file; it is not staged.

Imported source SHA-256:
`c39086d03379b2110b8408c1e02798a93c6c2690de6491ee9ffff5e9b1ce9430`.
Protected untracked launch settings SHA-256:
`ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784`.
The source copy is byte-preserved with a local -text attribute.

Root read the supplied handoff, root guidance, current decisions/dependency policy,
Transactions/Security/Metrics skills and actual integration APIs. Independent
read-only intakes examined protocol/normalization (8 estimated source minutes),
production PostgreSQL/concurrency (10), and authorization/observations (10).
No required existing-contract blocker or new runtime dependency was found.
Recommended routine implementation choices and all14 acceptance groups are mapped
in the [plan](implementation-plan.md) and [reconciliation](reconciliation.md).

Current physical observation:
2,960,996,728 filesystem/native bytes, plus conservatively retained1GiB Docker
allowance and64MiB reserve, leaving2,340,603,528 bytes within6GiB.
Ignored `.cis/local/operations-intake/baseline.json` retains exact roots/counts.
No cleanup or capacity increase occurred.

Preparation only: no implementation code, native build/test/pack/restore,
Docker/SQL operation, installed dependency or public helper/skill scaffold.
No runtime/provider/package/exporter/coverage/mutation/performance/release proof.
RM-02 results remain historical evidence for that module, not RM-03 passes.

The six draft contracts and proposed allocation are review inputs. No human
approval, outcome authorization or eligible native work owner is manufactured.
Canonical task organization/validation/allocation follows the one actual fresh
decision. Existing completed/closed owners stay unchanged.


## Draft readiness

[Independent draft reviews](review-intake.md) used4+4 additional source minutes
and found no material required correction. Read-only structural checks confirm
six medium implementation contracts, six declared scopes, acyclic dependencies,
positive finite ordinary allocations and exact sums115200seconds/36attempts/
3600restorationseconds, with the original6GiB/64MiB storage values unchanged.
Local Markdown links resolve. No authorization.json, canonical plan.json or
executable agent task was created before an actual user decision.

Conservative external intake source estimate: root60minutes plus28focused-agent
minutes plus8draft-review minutes =96minutes (1.6hours), proposed against WORK-001
within the shared allocation. This is an estimate, not automatic CIS consumption;
no product-native duration or implementation attempt is claimed. Ignored
.cis/local/operations-intake/resources.json retains that breakdown. All future
implementation/tests/documentation/review/restoration share the same proposed
limits, including this already-performed intake.

## WORK-001 portable foundation — actual native checkpoint

The approved outcome and shared allocation are applied; the canonical six-task
plan is valid. WORK-001 delivered eleven Core files, three focused Unit files,
twelve exact portable byte/digest vectors and the portable Operations contract.
Definition registration is passive, capped at16 and does not register a throwing
runtime service. Actual Execute/GetResult orchestration remains WORK-002.

Pinned ordinary formatting and verify-no-changes passed. The latest affected
Release build passed with0warnings/errors. Exactly28new cases across
OperationCanonicalTests/OperationContractTests/OperationRegistrationTests passed
on actual Windows and Ubuntu24.04, zero failures/skips. Both execute the same
current Core bytes, SHA256
6bcf070211acae8ee20299a5de605d8b78e0f76df50fb00d50f110538b8a7d9d.
SDK10.0.401/runtime10.0.12/rollForwardDisable remain unchanged. Receipt:
.artifacts/operations/CIS-0018/WORK-001/foundation-receipt.json, with source,
TRX and vector hashes, exact class scope and runtime config.

Early source review found a real passive-DI descriptor bypass; full pre-mutation
validation now rejects opaque/keyed/duplicate/oversized or mismatched metadata,
with focused regression cases. The first build exposed a nullable test assertion;
Assert.IsNotNull now expresses its precondition. Initial Windows execution then
caught a genuine absent-payload getter defect: the conditional null arm selected
ReadOnlyMemory's implicit array conversion, producing present empty memory for
rejection. The explicit nullable arm fixes that boundary; the existing mutable-
bytes/rejection regression now passes. The other nullable-byte consumers were
inspected; no duplicated affected getter remained.

That initial case-insensitive namespace filter also selected115retained
OperationScope cases, all passing, plus27newpasses/1newfailure. Its143-case TRX,
the failed build and all actual costs are preserved. Latest filters include the
namespace's trailing dot and run exactly28new cases on each OS. Incidental older
passes are not advertised as latest-candidate qualification.

Independent source/correction/necessity review is in review-001.md. The small
closed frame directly supplies the required portable encoding; no rules engine,
DI/runtime framework or test harness was added. Vector preimage hex was manually
specified; root computed SHA256 constants from those bytes without invoking Core
encoding. No database, durable coordinator, telemetry or package-consumer pass
is inferred from this foundation.
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1909; failed=270; possibleTokenSavings=591; ledgerDigest=sha256:0d3bd10c2e23f7ea158d297d97861e5b714d1617d726b5352e9b82d8d6342bdf |
