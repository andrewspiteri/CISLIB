# Messaging measurements and evidence

Use the existing Metrics facade and optional exporter. The
[fixed Messaging catalog](../../../contracts/metrics/messaging.md) reserves seven
conditional streams and 435 finite combinations while preserving central identity
and the current global limits. Instrumentation attaches to the exact registered
module owner before collection activation. Core adds no collector dependency.

Durable staging, known outbox commit, actual native confirmation, committed
business processing and caller response are different facts. Publication timing
ends at native settlement; delivery timing includes transaction/disposition/scope
cleanup and source settlement. An unknown outcome stays unknown even when a later
attempt succeeds. Duplicate suppression and rejection are distinct from a new
business effect. Metrics failures do not change those outcomes or owned cleanup.

No raw MessageId, business key, trusted scope/resource, reply endpoint, payload,
credential or native exception text is a label. The module uses registered
route/subscription/request/store aliases and fixed outcomes. The native reply
callback is not a logical business subscription and adds no delivery label.

Store gauges are retained bounded observations with an explicit freshness limit.
Scraping performs no database or broker I/O. Missing data is omitted rather than
reported as an empty backlog. Routine steady-state polling logs nothing; repeated
failure diagnostics retain a finite lifetime budget and monotonic spacing.

Unit observations establish finite BCL instrument behavior and lifetime rules.
The existing upstream exporter/Kestrel fixtures establish actual exported names,
mandatory central identity, finite views and conditional reservations. Actual SQL,
TLS, broker confirmation, runtime shutdown and package-consumer evidence are
qualified separately under [CIS-0019](../../changes/CIS-0019/verification.md).
The implemented request coordinator records finite RPC outcomes through actual
owned cleanup. The [collection cases](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Metrics/MessagingCollectionTests.cs)
exercise the optional exporter; their actual Part B execution is qualified in
that verification record. Part A collection alone does not establish Part B RPC
or exported-request evidence.
