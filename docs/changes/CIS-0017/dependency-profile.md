---
title: RM-02 proposed dependency and database fixture profile
type: document
status: Proposed
---

# Dependency and infrastructure profile

## Npgsql outside Core

Propose **Npgsql 10.0.4** only in the reference/native integration test project.
Its official [NuGet manifest](https://api.nuget.org/v3-flatcontainer/npgsql/10.0.4/npgsql.nuspec)
was inspected during intake: the `net10.0` group contains only
`Microsoft.Extensions.Logging.Abstractions` with minimum version `10.0.0`.
Existing Logging/DI abstractions 10.0.12 satisfy that graph. The new exact source
mapping is **Npgsql**; do not add wildcard mappings. Confirm the actual locked
restore/assets after approval. Any unexpected package ID or changed direct pin
is a concrete dependency decision, not permission to expand the graph.

Retain SDK 10.0.401 and .NET/ASP.NET 10.0.12. Npgsql 11 is not selected: it would
widen the Microsoft abstraction closure. No EF, Testcontainers, mocking framework
or Npgsql telemetry plugin is needed. Core/CommonTesting package consumers remain
provider-free; the existing package mechanism can prove public native capability
or portable scripted behavior without embedding Npgsql into those packages.

The compiling three-table consumer belongs in
`tests/CIS.Core.IntegrationTests/Transactions`. Its provider reference is test-only.
Use existing package tests for actual Core/CommonTesting NuGets, private offline
feed/cache and exact hashes. Do not fork that mechanism or pass a third-party
assembly to a helper that assumes every assembly is a CIS source project.

## Isolated PostgreSQL operational boundary

Docker Desktop was initially unavailable; the user started it during intake.
Its inspected engine is **29.6.1, Linux amd64**, local context `desktop-linux`.
No PostgreSQL service/executable or cached official PostgreSQL image was found.
No database connection, image pull, container creation or package restore ran.

Propose the official PostgreSQL 18 Alpine image, immutable Linux/amd64 reference:

`postgres@sha256:68e33adb18d9962952f00d5e3b8644305e10e375f98dd652bf6ee6a1836314ba`

The public [18-alpine tag metadata](https://hub.docker.com/v2/repositories/library/postgres/tags/18-alpine)
was inspected on 20 November 2026. That platform manifest reports 120,245,127
compressed bytes; the index digest is
`sha256:ff64c9e3bdf8e2120107d3559de8db314e8305f7e38c5105903c6295876738ff`.
Pin the platform manifest, inspect the pulled image, and record actual server
minor version/settings during execution. An image reference is not a database
execution result or a promise that the tag will remain unchanged.

One generated, labelled owned container and fresh named data volume serve the
fixture; do not discover/reuse existing databases or operator credentials. Bind
only the required local test listener, generate private ignored credentials,
and pass them through private test configuration/password-file input. Never
print DSNs/passwords, commit credentials or change global Docker configuration.
Use a task-private Docker client configuration and the inspected local engine
endpoint for the public image; do not look up operator registry credentials.
Allow only exact created resource IDs/ownership markers for stop/removal.
Keep the selected image as a counted dependency or remove it only when proven
newly owned and unshared. No engine/image/volume prune.

Proposed fixture bounds: two CPUs, 512 MiB container memory, 128 PIDs, at most
16 server connections and a reused application-owned data source with pool size
4. Use ordinary logged tables with normal durability, fresh schema identifiers,
at most three fixture tables, 128 rows per table and 1 KiB per application payload.
Keep logs bounded. Native test/build lanes are coordinated rather than sharing
mutable outputs concurrently.

Reserve one counted GiB inside the proposed shared eight-GiB ceiling:

| Newly owned Docker material | Admission/accounting bound |
| --- | --- |
| Unpacked selected image | 512 MiB; inspect actual image size before starting it |
| Compressed image cache | 128 MiB; current public manifest is below this |
| PostgreSQL data | 256 MiB; measure exact owned volume growth at bounded checkpoints and close new test admission if it exceeds the allowance |
| Retained container logs | 8 MiB, configured rotation |
| Metadata/other owned overhead | 120 MiB |

This is conservative counted storage, not an invented hard filesystem quota.
Do not weaken durability or substitute TEMP/UNLOGGED tables to reduce storage.
If ordinary init/qualification cannot fit these declared bounds, stop native
expansion and present the measured difference. Preserve the existing 64 MiB
physical restoration reserve and all historical bytes/costs.

Application fixture configuration explicitly uses `Timeout=15`,
`Command Timeout=30`, `Cancellation Timeout=2000` (milliseconds), `Enlist=false`,
and `No Reset On Close=false`. These are fixture choices, not Core rewriting
connection strings or universal production SLAs. See the native
[usage](https://www.npgsql.org/doc/basic-usage.html) and
[parameter](https://www.npgsql.org/doc/connection-string-parameters.html) contracts.

Observe results from independent connections. Coordinate real query cancellation
using an owned advisory-lock blocker and an observer of the owned backend, then
cancel/await the actual query and release all participants. No timing-only sleep,
backend termination privilege, artificial network failure or lost-ack claim.
The real lost-acknowledgement guarantee is intentionally not claimed: the
conservative Unknown classification is proven with recording provider doubles.

Plan actual Windows and Ubuntu checks using the existing pinned lanes. Record
the actual endpoint/access arrangement; do not expose a listener broadly merely
to bypass a platform connectivity limitation. A lane or real database check not
executed remains unverified. Restore exact owned resources in normal/failure
paths. Existing user/other-project resources remain outside operational scope.
