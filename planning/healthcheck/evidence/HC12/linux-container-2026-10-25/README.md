# Linux container transport check — 25 October 2026

The existing Healthcheck pipe implementation works in the executed Linux container. No shipping transport code was changed. `HealthPipeEndpoint.Create` selects its Windows native ACL branch only on Windows; Linux uses the .NET named-pipe APIs backed by a Unix domain socket. This was already the intended Linux contract in Technical Intent section 9 and HC-PIPE-02.

The Ubuntu server `192.168.88.161` ran the retained original host fixture and public matching probe from the complete191 snapshot. Both used original Core SHA256 `94e5d5e075423b846c603086b7f772e5611a6491965dcb168a9ac29586b9dd5f`. Read-only host and probe closures were mounted into the same container, with UID/GID 10001, a read-only root filesystem, writable temporary filesystem, no network, dropped capabilities and no-new-privileges. The cached Microsoft ASP.NET runtime image was pinned to `mcr.microsoft.com/dotnet/aspnet@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4`; the real host reported .NET 10.0.12.

| Actual public probe observation | Exit |
| --- | --- |
| Healthy, UID 10001 | 0 |
| Different UID 65534 denied | 2 |
| Critical condition flagged | 1 |
| Condition cleared, owner recovered | 0 |
| Readiness after startup | 0 |
| Missing endpoint | 2 |

Success and unhealthy outputs were parsed as actual snapshots; transport errors produced no snapshot and nonempty stderr. The denied peer left the host capture count unchanged at one. The actual endpoint was `/tmp/CoreFxPipe_cis-fixture-1f4283a55277410eab8234f165024d52`, observed as a socket owned by 10001:10001 with mode 0755. This is not a 0600 promise. The host exited naturally, Docker reported stopped/exit0/not-OOM-killed, all CLI outputs were drained and processes reaped, and only the exact task-owned container ID was removed. There were no holds in run02 and the input closures remained unchanged.

[receipt.json](receipt.json) SHA256 `208f422a8e3e68c3bb22351ab1f949993f7939ac8af0ff5f6c65bcdd66faed98` records the six outcomes. [raw-container-controls.tar.gz](raw-container-controls.tar.gz), 223,397 bytes, SHA256 `59f5da7142635c34989190ccf0521e051c619461736a3802d7e26619055adc7a`, retains both attempts, every actual Docker argv/output/exit/timeout/reap/drain record, host streams, source, project and compiled controls. [control-source-bindings.json](control-source-bindings.json) SHA256 `b89a0c35cf463fe59a51f8c08382db6460413784e609dbe689b5799defd1091a` binds the executed control PE/PDB and all physical source documents. The private utility built with zero warnings/errors. Independent source and actual-result review are retained alongside this record.

The first attempt was a setup failure: a copied image digest had 65 hexadecimal characters. Docker rejected it before creating a container or starting the app. Its timeout, logs and source preimage remain retained. The second attempt changed only that digest to the actual stored image digest and selected a fresh output directory; no timeout or transport rule was relaxed.

The independent [source peer](source-peer.md) and [actual-result peer](actual-peer.md) distinguish source checks from execution. [control-proof.tar.gz](control-proof.tar.gz), 67,201 bytes, SHA256 `7f00b6adfb890044dca309f8b1ab21e6b367c52ebf1c8c90efdd9e880a1f461f`, preserves the C# proof helper, its full compiled/source closure, exact reused validator source, build/entry records and reviews. Native archive create/extract and the C# whole-file-set receiving check completed zero; [control-proof-readback.json](control-proof-readback.json) retains that result.

This is bounded Linux Docker exec evidence, using a retained development fixture and matching probe. The receipt's `qualified` field refers only to that private control. There was no kubelet or Kubernetes cluster execution, published image/package qualification, root-adversary proof, container concurrency campaign or mutation credit. Final deployment qualification must test the actual application image, UID, endpoint filesystem, installed probe path and Kubernetes probe configuration. [Kubernetes supports exec and HTTP probes](https://kubernetes.io/docs/concepts/workloads/pods/probes/); it has no native named-pipe probe field. The HC12 mutation baseline and all complete191 holds remain unchanged.
