# Portable Audit v1

The message contract is `cis.audit.record`, schema version `1`, kind `event`.
[contract.schema.json](contract.schema.json) defines the common envelope;
[contract.json](contract.json) records its additional wire and bound rules.
The required common fields are auditVersion, auditId, eventType, eventVersion,
category, occurredAtUtc, recordedAtUtc, producer, actor, outcome and details.
The six optional common fields are target, reason, operationReference,
correlationId, causationAuditId and domainEventId. Omit absent optional fields.

A generic receiver validates the common envelope and bounded JSON structure.
It accepts unknown application event types and versions and retains their
details opaquely. Producers separately select and validate their explicit event
detail schema. No schema registry or network lookup is required. Amounts needing
decimal precision and large identifiers use lossless strings where their event
definition requires them. Unknown numeric detail tokens are retained without
binary floating-point conversion.

The complete payload uses the selected positive byte/depth limits, defaulting to
256 KiB and depth 16 and supporting at most 1 MiB and depth 32. It must fit the actual
Messaging payload/depth limits. Messaging's separate outer metadata allowance is
not extra Audit detail capacity. Native Security scalar bounds are retained;
operation scope is instead bounded to 256 UTF-8 bytes. domainEventId uses the
safe identifier bound of 1024 Unicode scalars. No identity is truncated.

Actor subject is required for user/application and absent for system/anonymous/
unknown. An optional actor application/environment pair is complete or absent.
An application actor's humanSubject requires verifiedDelegation; a user's provider
subject and separately validated humanSubject need not be equal. Source capture
only projects available trusted facts. Missing caller means unknown; anonymous
requires an actual anonymous context. Instance kinds are notProvided, reported
and verified. Reported process metadata never becomes verified identity.

Schema validity is one layer. The wire binding additionally rejects malformed
UTF-8/Unicode, decoded duplicate property names at every depth, control-bearing or
blank safe text, invalid calendar timestamps, unsupported envelope versions,
comments, trailing input and complete-payload byte/depth violations. Versions use
Int32 JSON integer tokens without decimal points or exponents. UTC timestamps
contain seconds, a Z suffix and up to seven fractional digits; source capture
emits seven. Timestamps do not prove commit or global ordering.

The codec retains and forwards the exact original Audit UTF-8 bytes, including
whitespace, property order and escapes. Same auditId with identical original
payload bytes is a repeated occurrence; any differing bytes are a conflict even
when parsed values are equivalent. Outer MessageIds, timestamps and transport
metadata do not participate. Durable duplicate/conflict enforcement across
MessageIds belongs to the future receiving application, together with secure
current authority, durable acceptance and ACK ownership.

[Independent vectors](../../../conformance/audit/v1/record-cases.json) separate
structural schema expectations from complete wire expectations. The
[raw-wire vectors](../../../conformance/audit/v1/wire-cases.json) retain exact
strings and invalid UTF-8, and document repeated/conflicting byte pairs. These
fixtures establish portable input obligations; they do not implement destination
storage, retention, archive durability or a collector.
