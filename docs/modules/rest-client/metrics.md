---
title: RestClient Metrics integration
type: document
status: draft
cis:
  stable_id: cislib.document.modules.rest-client.metrics
---

# RestClient Metrics integration

REST observes its logical operations through the ordinary CIS Metrics facade.
The optional `CIS.Metrics.OpenTelemetry` collector owns the existing provider,
views and exporter. See the [fixed catalog](../../../contracts/metrics/rest-client.md)
for names, finite aliases, outcomes and byte boundaries. Implementation/native
checkpoints belong to [CIS-0010 verification](../../changes/CIS-0010/verification.md).
Actual public-operation, payload, retry, two-root isolation and shutdown checks
passed in the joined 93-case checkpoint.

```csharp
using CIS.Metrics.OpenTelemetry;

builder.Services.AddCisMetrics(options =>
{
    options.CollectCoreModules = true;
    options.CollectOutgoingHttp = null; // Auto for configured CIS RestClient.
});
```

Auto is the default. Install the actual REST registry before or after this call;
the collector inspects final passive registration metadata before creating its
effective catalog. Presence detection does not execute a user factory or create
transport. Runtime attachment occurs during provider activation and validates
the exact registration identity. Repeated Metrics installation compares captured
options rather than the order-dependent effective catalog.

Explicit `CollectOutgoingHttp=false` omits native outgoing streams while keeping
automatic logical REST observations. True enables native selection without
requiring REST registration. `CollectCoreModules=false` disables automatic REST
handles while leaving explicitly selected outgoing collection available.
Without Metrics, or with collection disabled, REST behavior remains unchanged;
there is no delayed event replay.

With no REST and Auto, existing explicit `MaxDefinitions=19` and corresponding
twenty-one-stream profiles remain valid. Default configured REST requires room
for twenty-three Core definitions and twenty-seven total streams, capacity sum
4,416. The four logical handles belong to the registry, not each client. Stream
overrides may tune their capacities; global limits and cardinality overflow
behavior are unchanged. Known catalog exhaustion fails before producers run.

The native transport must receive the same root `IMeterFactory` before first
use. The selector accepts only the exact factory-created `System.Net.Http`
meter, the two supported .NET 10 instruments and method dimension; process-global
and foreign-host meters are dropped. A bare borrowed `HttpClient` does not prove
its hidden handler or factory provenance. No private inspection, independent
provider or per-call pool is used to certify it.

Logical duration starts at public entry and ends after actual cleanup, including
retry delay and body/mapping work. Native outgoing duration ends at headers.
Do not compare either histogram count with packets or assume that native count
is the exact number of owned dispatches. Payload counters observe actual
completed chunks, including retries; signing-buffer writes and Content-Length
are not uploaded bytes.

The owner captures a complete Metrics binding once at entry. Late attachment,
capacity overflow or recording failure cannot alter HTTP results or split active
entry/exit across runtimes. The hosting cutoff signals REST cancellation before
awaiting Heartbeat/Configuration and joins real operation cleanup before
dependency teardown. An expired stop wait is not settlement. Startup's
coordinator remains independent of REST and collector SDKs.

Use the existing [Metrics hosting guidance](../metrics/hosting.md) for the
reserved endpoint, authoritative identity and actual callback drain. The old
Metrics footprint observation is not evidence for the new REST composition;
the focused profile/isolation/operation checks establish only their recorded
runtime and workload.
