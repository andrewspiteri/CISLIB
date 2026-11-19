---
title: Security authorization and operation audit boundaries
type: document
status: draft
cis:
  stable_id: cislib.document.modules.security.audit
---

# Security audit boundaries

`ISecurityAuditSink` has two awaited integration hooks. Register a sink before
`AddCisSecurity`; otherwise Core supplies a no-op. There is no queue, durable
receipt, background retry or persistence guarantee.

`AuthorizationCompletedAsync(SecurityAuthorizationAudit, token)` observes the
final composite native decision, including additional policy requirements. It
contains the actual established caller when available, complete capability set,
resolved target and bounded correlation. Failed authentication invents neither
caller nor resource. Passing one requirement is not a successful composite result.

`OperationCompletedAsync(SecurityOperationAudit, token)` is separate. The business
owner supplies its actual completed, failed or canceled outcome. Authorization
success alone proves neither mutation commit, delivery nor downstream completion.
A denied request produces no fictional business-completion record.

CountryData authorizes its actual cached country, commits its single selection/
revision, then awaits the operation sink with the actual caller and target. A
hook fault remains observable while the committed selection remains committed.
There is no implicit rollback or successful-delivery claim. Stronger transactional
delivery requires explicit application-owned policy and infrastructure.

Preserve hook faults and cancellation settlement. The authorization hook completes
before protected work proceeds; the operation hook runs at its real business
boundary. Records omit raw tokens, keys, arbitrary claims and HTTP-context
retention. Constructing, serializing or retaining a record grants no background
request authority. Reported instance metadata remains unverified unless a real
integration established stronger evidence.

Metrics are observations, not audit receipts; aliases/outcomes are finite and
raw subject/URI/correlation histories are excluded. The native Windows custody
journal records only exact newly owned backing metadata for cleanup/restoration,
not security decisions. See [transport](transport.md), [consumer](consumer.md)
and [failures](failures.md).
