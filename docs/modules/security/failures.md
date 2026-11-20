---
title: Security failure evidence and actual effects
type: document
status: draft
cis:
  stable_id: cislib.document.modules.security.failures
---

# Security result interpretation

`SecurityFailure` carries a bounded known category, safe message, evidence source
and optional actual HTTP/provider/transport codes. It contains no exception,
credential material or claims dump. Authentication, identity mapping, permission,
business work and delivery remain separate facts.

| Observation | Interpretation |
| --- | --- |
| Local source has no usable credential | Local `CredentialUnavailable`. |
| Established credential expiry passed | `CredentialExpired`; no renewal grace. |
| Required revocation/chain evidence missing | `ValidationUnavailable`; fail closed. |
| Validated identity has no exact trusted mapping | `IdentityUnmapped`. |
| Final composite permission denied | `CapabilityDenied` for that target/set. |
| Arbitrary remote 401/403 | Preserve HTTP and `CauseUnknown`; no invented grant/expiry diagnosis. |
| TLS failed before a response | Preserve transport failure and absent HTTP status. |

The shared `ICapabilityAuthorizer` and protected endpoints use the same native
complete-grant handler. Every capability/native requirement must pass against
the actual target; fragments of grants cannot create wider permission. Join
actual evaluation before its canceled scope is disposed.

Callbacks declare known local acquisition failure through
`RestAuthenticationContext.FailWithSecurity`. The outer result stays
`Authentication` / `client.authentication_failed`, with typed
`RestResultContext.SecurityFailure`. Deadline, cancellation and stopping retain
precedence; superseded failures carry no stale detail. Programming, invariant and
cleanup failures remain faults. See [transport](transport.md).

Renewal selects credentials for future exchanges, without replaying an operation
after 401/403 or a denied mutation. Existing REST policy alone owns retry; a later
retry selects the current generation after its delay within the original deadline.
Actual body/response owners retain their old generation through settlement.

CountryData separately proves that a denied TTO update leaves the MT selection
and revision unchanged. Conversely, an operation-audit hook fault after commit
does not imply rollback. See [consumer](consumer.md) and [audit](audit.md).
Local TLS/JWT/SDK evidence cannot establish actual AKS federation, cert-manager
deployment, external error semantics or release readiness.
