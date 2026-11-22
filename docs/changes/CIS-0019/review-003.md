---
title: "RM-04 optional RabbitMQ provider review"
type: document
status: Draft
---

The optional provider passes 21 actual Windows and 21 actual Ubuntu cases, with
zero failures/skips. They cover TLS hostname/chain rejection, actual validated
user-id and mapped cross-application provenance, missing/forged/unmapped identity,
capability isolation, confirms/mandatory routes, durable quorum backpressure,
malformed/oversize safe quarantine, manual settlement, callback drain, native
recovery after owned-container restart and actual broker cancellation. A queued
startup case establishes security availability before dispatch admission.

Independent source review verified private current-ticket lifetime, connection
generation fences and the distinction between channel shutdown and actual
basic.cancel. Native recovery remains the only reconnect/registration owner;
unexpected protocol/programming faults remain visible. All received and
republished bytes remain immutable; Core owns any required encoded-buffer lease.

The first cohorts exposed unsupported conditional quorum deletion in fixture
cleanup, an automatically reassigned Docker port, genuine stale-connection and
Unregistered-event recovery bugs, and incorrect exact-threshold assumptions.
Their logs/results/costs remain. Cleanup now verifies actual drain and exact newly
owned queue names; a fixed loopback port survives restart. Saturation tests first
observe actual publisher rejection and recover every confirmed exact body.
The owned broker reported a 180,000 ms dead-letter confirm retry period; the
single blocked-DLX test uses 210 seconds, without tuning policy. It passes in
about three minutes and preserves every accepted body through target backpressure.

Independent native alignment checked 18 source files, six binaries/runtime
metadata and two TRX files in
`.artifacts/messaging/CIS-0019/WORK-003/provider-receipt.json`. Every hash matched;
Core/provider source-output and IntegrationTests copies match. The receipt then
adds five existing project/dependency/check inputs and the passing existing
ShippingDependencyTests case. That native check verifies approved package and
compiled-reference closure; the affected Release builds have zero warnings/errors
and scoped formatting/verification pass. The full fresh package consumer remains
an explicit Part A/Part B obligation.

These are bounded provider facts at the retained candidate. Parallel new
dispatcher/host source is not represented by those frozen DLLs. This checkpoint
does not establish the complete Part A runtime, RPC recovery, deployment, release
or multi-node quorum failover. The fresh owned PostgreSQL/RabbitMQ resources stay
available for the same module's integration and final restoration.

After the complete Part A runtime compiled, a justified currentness check ran
the same 21 provider cases again on each actual Windows/Ubuntu lane, plus the
existing Core dependency-closure case. All pass with zero failures/skips. The
new `provider-current-part-a-receipt.json` binds current source, those three
results and the immutable `part-a-candidate-2` binaries. The original receipt is
preserved as historical: its shared live output paths were subsequently
overwritten, so it is not presented as current binary custody.
