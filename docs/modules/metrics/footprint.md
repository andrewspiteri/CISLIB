---
title: Metrics bounded footprint observation
type: document
status: draft
cis:
  stable_id: cislib.document.modules.metrics.footprint
---

# Metrics bounded footprint observation

The single [MetricsFootprintTests observation](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Metrics/MetricsFootprintTests.cs)
uses the existing real WebApplication fixture with its Startup, Heartbeat and
Healthcheck owners, actual prepared Type Manager and Configuration, and a public
Core logging runtime borrowed by its existing logger provider. It adds one
application counter and one duration timer, both with a `category` dimension.
It creates no replacement collection pipeline or production backend.

The default policy is captured unchanged: 96 CIS/application definitions, 128
total streams, 128 ordinary combinations, 64 per generic histogram, 1,024 for
inbound HTTP duration and a 16,384 configured capacity sum. Durations have fourteen
finite bounds. The response cap is four MiB per complete encoded document, with
two handlers, no queue, five-second timeout, 300-ms cache and exemplars disabled.
SDK zero-dimension and overflow points are additional to dimensioned capacities.

The actual owner bindings supply nineteen Core definitions. With the two
application instruments and two native HTTP streams, the test captures the
twenty-three policy entries, including their selected/reserved status, dimension
keys, capacities and histogram bounds. This metadata is recorded directly from
the optional context's bounded catalog snapshot.

The workload has one fixed native GET category, without a declared route template.
It warms actual module/app operations and both Prometheus 0.0.4 and OpenMetrics
1.0.0 responses. Two requests for those formats are issued together after each
350-ms cache-expiry wait. It then records two rounds of 4,096 new finite category
values into each application instrument. A recorded W3C activity supplies trace
context while the selected profile continues to emit no exemplars.

Full-GC managed-memory observations are taken before creating the host's owners,
after warm-up and after each round. The JSON test output records those values,
incremental retained delta, second-round growth, per-format response bytes/sample
counts, the complete catalog and actual workload. It reports measurements before
the retained-memory/series assertions so a failed target still leaves evidence.
All samples must retain authoritative identity; overflow must appear; further new
categories must not increase sample count after saturation. The observed retained
delta must stay below 64 MiB and each successful response below four MiB.

This includes the actual host/owners and retained observation responses in its
managed-memory measurement. It does not assign exact bytes to an SDK stream, claim
a four-MiB heap bound, or saturate every native/module dimension combination.
Configured worst-case capacity may exceed the response cap when labels and
buckets are fully populated; the existing oversize-response case separately
requires failure instead of a truncated successful scrape. This test observes a
series plateau and records the actual managed-memory change between rounds; it
does not establish an allocator/RSS bound or platform-wide release qualification.

The case is categorized `PerformanceObservation`, so the normal repository
regression exclusion still applies. Its one authorized native invocation and
actual results belong to [CIS-0009](../../changes/CIS-0009/verification.md).
Source alone is not measurement evidence. The recorded scoped result is
summarized below; the change record retains the native report and currentness.

## Observed Windows result

The one CIS-0009 invocation passed. Incremental retained managed memory was
6,501,960 bytes (6.20 MiB), below 64 MiB. Two rounds of 4,096 new categories retained
the same 1,337 Prometheus / 1,538 OpenMetrics samples and 229,843 / 265,794-byte
responses. Second-round managed growth was 1,294,352 bytes, including retained
observation responses; series plateau and this workload's target pass are the
claims. The exact catalog/workload/GC measurements remain in the change record.
This does not qualify another platform or establish a universal heap bound.
