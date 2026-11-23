# Audit contracts

Audit uses the existing Messaging event contract `cis.audit.record`, version `1`.
The [v1 profile](v1/README.md), [common JSON Schema](v1/contract.schema.json) and
[wire bounds](v1/contract.json) define the portable common envelope.

The independent [record cases](../../conformance/audit/v1/record-cases.json) and
[exact wire cases](../../conformance/audit/v1/wire-cases.json) distinguish schema,
wire and future receiver byte-identity obligations. They do not implement an
Audit destination, archive, collector or durable AuditId store.

The [consumer guide](../../docs/modules/audit/README.md) covers source capture,
transactional staging, standalone submission and existing Messaging integration.
The explicit Security bridge uses the
[authorization detail schema](v1/security-authorization-details.schema.json) and
[reported-operation detail schema](v1/security-operation-details.schema.json),
with [independent vectors](../../conformance/audit/v1/security-details-cases.json).
The common receiving codec preserves structurally valid unknown application details.
