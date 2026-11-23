---
name: cislib-management
description: Implement, consume or review CISLIB Central Management service APIs, portable clients, optional process reporting and Jobs inventory. Apply to CIS.Management and the independent management application; portal UI and central configuration publication remain separate.
---

# CISLIB Central Management

Read [repository guidance](../../../AGENTS.md), the
[module guide](../../../docs/modules/management/README.md) and
[portable v1 contract](../../../contracts/management/v1/README.md).
Use the checked-in OpenAPI, canonical schema and exact-text conformance cases;
CLR or TypeScript reflection does not define the protocol.

- Keep the service an independent `CISApplication` consumer with its own data
  source and explicit SQL migration/broker provisioning. Core contains portable
  contracts, the existing REST-backed client and optional reporting, with no
  database/broker dependency. CountryData remains the tutorial. Add no portal,
  central Configuration publisher, scheduler, generic command engine or Audit
  archive.
- Native credential validation and trusted mappings establish workload or
  independently validated human/delegation identity. Authorize the actual
  management service application/environment and resolved
  `management.application/<applicationId>` resource. Filter before pages/cursors;
  reauthorize mutations and recovery. Reported inventory, instance labels,
  permission hints and copied actor data confer no authority.
- Preserve strict UTF-8/Unicode, closed command objects, compatible response
  additions, decimal Int64 strings and seven-fraction UTC timestamps. Keep native
  Jobs byte bounds and scoped identities, including observation mode. Admit whole
  bounded inventories and refuse a replacement rather than truncating it.
- Metadata replacement uses the original RM-02 owner and existing RM-03
  authorization/arbitration. Replay precedes the old-ETag handler. Conditional
  update, original decision and required RM-06 outgoing Audit share one original
  transaction and one completion. Replay repeats neither effect nor Audit; use
  existing Messaging delivery without a second journal or queue.
- Keep native rejected errors distinct from typed values. Prepared requires
  known commit; Found/Replayed retain original commit despite current read
  rollback/cleanup. Preserve Unknown and the original caller key. NotFound or
  expired results do not prove rollback or authorize a new-key rerun. See the
  [metadata guide](../../../implementations/csharp/applications/CIS.Management.Service/Metadata/README.md).
- C# borrows the selected keyed native REST/security owner. TypeScript uses
  per-call credentials and an explicit server entry; browser imports acquire no
  Node/React/Next dependency. One entry deadline includes encoding, credentials,
  transport, decode, permitted read retries and actual cleanup. Writes do not
  retry automatically; cancellation signals and joins owned work.
- Reporting is explicitly selected, with a quiet dedicated REST alias. Perform
  initial registration directly in sequential Startup; distinguish required
  receipt from optional disconnected reporting. Use one existing Heartbeat owner,
  one latest observation and one pending complete inventory. Preserve uncertainty
  after an unconfirmed upload even when local content reverts to an earlier
  confirmed value. Jobs projection inspects all pages without resolving handlers.
- Signal reporting cancellation early while preserving admitted Jobs/Audit
  barriers. Close/drain REST before awaiting reporter/Heartbeat settlement;
  capture admitted factory ownership on failed Build. A remote close is best
  effort, never a teardown prerequisite. Startup's coordinator stays independent.
  Read the [reporting guide](../../../docs/modules/management/registration.md).

Reuse bounded public CommonTesting for consumer facts and the existing native
PostgreSQL/RabbitMQ/HTTPS/package fixtures for their distinct boundaries. Use
ordinary MSTest and the TypeScript workspace's pinned compiler/native Node
commands. Source, scripts and local identity proofs do not establish external
Entra federation, cert-manager issuance, destination Audit persistence or release
qualification. Keep actual execution and remaining limits in the current change.
