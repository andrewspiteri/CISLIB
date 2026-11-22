# Native C# tests

The ordinary [CisLib.slnx](../CisLib.slnx) contains the five Core MSTest/VSTest
families, native Security tests, CountryData unit examples and shared test
support. Use the same projects in the IDE or through `dotnet test`; no
preparation checkpoint, package campaign or custom runner is required.

| Project | Retained purpose |
| --- | --- |
| UnitTests | Core module behavior, bounds, ownership, errors and deterministic concurrency |
| IntegrationTests | Real consumers, streams, files, process/transport boundaries and lifecycle |
| ArchitectureTests | Actual shipping dependency/API/ownership boundaries and Type Manager MSBuild task regressions |
| CompatibilityTests | Accepted development API/configuration/wire behavior and frozen source/binary consumers; no released baseline is invented |
| BusinessCases | Meaningful application/developer/operator stories |
| Security.IntegrationTests | Native certificate, transport and identity boundaries |
| CountryData.UnitTests | Example consumer unit tests reusing CommonTesting |
| TestSupport | Deterministic clocks, signals, owned temporary directories and bounded child execution with drained output/cleanup |

Run from `implementations/csharp`, with SDK **10.0.401** and both selected
**.NET/ASP.NET Core 10.0.12** shared frameworks installed. Keep the runtime pin and
disabled roll-forward; installing only a newer patch does not satisfy it.

```powershell
dotnet restore CisLib.slnx --locked-mode
dotnet build CisLib.slnx -c Release --no-restore
```

## Fast regression

This selection uses no Docker, including discovery and assembly cleanup. It also
excludes performance observations and extended stress tests:

```powershell
dotnet test CisLib.slnx -c Release --no-build --no-restore --filter "TestCategory!=RequiresDocker&TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress"
```

The whole-solution command includes Compatibility, so its ApiCompat tool and
8.0.31 tool-host prerequisite below still apply. HTTP, file, scripted consumer and
passive package tests remain available without Docker. An unfiltered run or IDE
Run All also selects service, performance and extended stress cases.

## PostgreSQL and RabbitMQ integration

Selected external-service tests provision their own isolated services through
Testcontainers. Install a reachable local rootful Linux Docker engine, or Docker
Desktop in Linux-container mode on Windows. Default test execution creates new
owned services; it does not read an ambient developer database or require manual
provisioning, connection files or setup credentials. Unsupported Docker, image
pull and authenticated-readiness failures fail selected tests clearly.

```powershell
dotnet test tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj -c Release --no-build --no-restore --settings tests/CIS.Core.IntegrationTests/service-tests.runsettings --filter "TestCategory=RequiresDocker&TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress" --logger "trx;LogFileName=service-tests.trx" --results-directory ../../.artifacts/testcontainers/service-results
$serviceExit = $LASTEXITCODE
dotnet run --file eng/VerifyServiceTestReport.cs --artifacts-path ../../.artifacts/testcontainers/service-guard -- ../../.artifacts/testcontainers/service-results/service-tests.trx $serviceExit
```

The native run settings reject an empty selection, inconclusive cases and cleanup
warnings. The small BCL result check also requires a successful native exit/run
summary, a nonzero selection and every selected result Passed; skipped results
and inconsistent counters fail. It reads at most 16 MiB of native TRX and prints
counts and elapsed time. It does not run tests, provision services or certify
release readiness. In Bash, capture `$?` immediately after `dotnet test` and pass
that value to the same check.

The integration project's ordinary identity-only launch profile supplies its
known test application/environment before the host captures identity, in both
CLI and IDE runs. It starts no services and preserves ordinary empty-filter and
long-test behavior. The explicitly selected service profile adds the CI timeout,
empty-selection and cleanup gates. Neither profile contains credentials or
external endpoints; production unknown-identity guards remain unchanged.

For PostgreSQL-only work, exclude tests that also need RabbitMQ:

```powershell
dotnet test tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj -c Release --no-build --no-restore --settings tests/CIS.Core.IntegrationTests/service-tests.runsettings --filter "TestCategory=RequiresDocker&TestCategory=PostgreSql&TestCategory!=RabbitMq&TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress" --logger "trx;LogFileName=postgresql-tests.trx"
```

Categories describe actual dependencies. Combined delivery, host, RPC, process and
actual packaged-RPC cases carry both service categories. Synthetic Metrics and
the public-helper/passive Messaging package cases stay Docker-free. Keep the
existing `DoNotParallelize` restrictions; parallel service execution is not a goal.

### Ownership and real-provider guarantees

Services start lazily in the integration assembly and can be shared within one
test run. Each case retains independent PostgreSQL schemas and broker namespaces.
Restart/interruption cases own a dedicated or otherwise exclusive service and
keep its data through the interruption. The bound is one shared PostgreSQL, one shared RabbitMQ,
at most one additional dedicated service and one Resource Reaper. Cross-run reuse
is disabled. Startup/failure/disposal joins actual owned operations and clients;
an elapsed timeout alone does not establish cleanup.

RabbitMQ live-client recovery uses a real application stop/start, preserving the
Erlang VM, container network and original assigned endpoint. A separate full
container restart verifies retained persistent unacknowledged work with a fresh
transport at the current inspected endpoint. The observed Docker Desktop engine
reassigns random ports even on atomic container restart; that cannot prove live
recovery at the old endpoint. The fixtures never retarget a live transport or pin
a fixed host port. These observations have distinct evidence boundaries.

Docker chooses resource names and mapped host ports. Typed parent ownership binds
the discovered endpoint to this run's container. The selected PostgreSQL profile
requires loopback; non-loopback profiles fail rather than silently using plaintext
or an arbitrary existing database. Broader remote-Docker profiles are unqualified.
Service containers use 512 MiB/2 CPU caps and finite process, connection and log
limits. Ryuk stays enabled at the SDK's pinned image with privileged mode disabled;
its actual supported controls and observations remain distinct from service caps.

The selected images are PostgreSQL **18.7** and RabbitMQ **4.4.1**, pinned by
immutable digest in the test infrastructure. Reset the PostgreSQL module's
non-durable test defaults and assert `fsync`, `full_page_writes` and `synchronous_commit` are
all `on` through an authenticated client. Keep ordinary logged tables, independent
connections and the product's real installers. RabbitMQ retains TLS hostname and
custom-root validation, fresh restricted principals/vhost, production topology,
quorum queues, confirms, mandatory returns, manual acknowledgements and redelivery.
A successful confirm is separate from consumer processing.

Parents own containers and short-lived private child handoffs. RPC children and
outside-friend package consumers receive only their required endpoint, selected
credentials, public CA and scenario data. They do not acquire Testcontainers,
setup privileges or the broker server key. Pass descriptor paths explicitly to
each child; do not introduce process-global environment changes or restore the old
manual `CIS_RM02/03/04_*_CONNECTION_FILE` prerequisites.

Solution builds preserve the selected configuration for the RPC child and fail
if its executable metadata is missing. The safe-DLX recovery case permits 390
seconds for two native 180-second confirmation retry rounds under a capacity-one
target; this observation deadline does not promise production delivery latency.

Normal completion and failed startup/test paths attempt exact owned cleanup after
actual client/child drain. Preserve original and cleanup failures, capture capped
redacted fixture diagnostics in the native report, and keep the Resource Reaper as
fallback. Never prune Docker globally or remove unrelated containers, caches or
historical evidence. Temp assembly files mapped into the test host can be removed
only after that host exits; use run-owned scratch when measuring local storage.

Keep the dependency direction test-only: IntegrationTests references shipping
projects, and shipping projects never reference container infrastructure.
`PrivateAssets="all"` supplements project/package closure checks. CommonTesting
continues to support ordinary scripted consumer tests without Docker.

## CI and current evidence

The [service workflow](../../../.github/workflows/service-tests.yml) defines one
stable `PostgreSQL and RabbitMQ integration` check on Ubuntu 24.04 for pull
requests and manual dispatch. It installs/checks the exact SDK/shared frameworks,
uses locked restore and the same build/service selection, and lets Testcontainers
own services. It has a 45-minute job bound and keeps one TRX, including capped
redacted fixture diagnostics, for seven days. Private handoffs and container
filesystems are not artifacts. The service job does not need ApiCompat's older
runtime because it does not execute the whole-solution fast selection.

The workflow definition and a local Linux pass are separate from an observed
successful CI run. Record the actual tested commit, runner, counts, duration,
image identities and cleanup in the current
[CIS-0021 verification](../../../docs/changes/CIS-0021/verification.md). Required
branch-check enforcement is repository configuration, not established by YAML.
A Linux pass does not establish Windows execution. Single-node restart, simulated
acknowledgement loss, application-process death and network faults are distinct
facts; these tests do not establish multi-node failover or deployment qualification.

For a future external-service adapter, add its supported test-only Testcontainers
module (or a small generic fixture where no module exists), pin its image,
authenticate readiness, isolate state, bound resources, categorize the actual
consumers and join owned cleanup. Reuse these conventions and the production
adapter without creating another service-management framework.

## Focused native work and existing fixtures

For focused edits, keep the native incremental build and select the affected behavior:

```powershell
dotnet test tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj -c Release --filter FullyQualifiedName~Severity
dotnet test tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj -c Release --filter FullyQualifiedName~Healthcheck
```

Projects build their real fixture dependencies directly. Shared setup stages the built product and fixture files needed by those tests; it does not create producer reports or claim another verification pass. Product-specific mutation child staging remains necessary when a real child must execute Stryker's instrumented Core and active-mutant selector. It is inactive in ordinary execution.

The Type Manager compatibility fixture tests the executing Core against specific frozen development API/source/binary consumers. Restore the pinned native tools with `dotnet tool restore --configfile eng/Tools.NuGet.config` before its API cases; ApiCompat 10.0.401 requires the pinned .NET 8.0.31 tool-host runtime. `CIS_COMPAT_API_TOOL` may select that exact tool entry DLL. `CIS_COMPAT_API_DOTNET` explicitly selects its host for API calls only. Without an override, the fixture uses a complete `.artifacts/tool-host-8.0.31/` installation under the repository when present, then ordinary `dotnet`. SDK and product execution still use ordinary `dotnet`; the exact API runtime pin and disabled roll-forward apply to every API host. Other producer/report/Core-source overrides are retired. Tests do not download tools internally.

Positive frozen consumers receive a separate generated runtime configuration for Core's approved .NET/ASP.NET shared frameworks, alongside the supplemental NuGet dependency manifest. Frozen source, binaries, dependency manifests and runtime configuration remain unchanged. Separate negative cases omit the NuGet deployment and the shared-framework configuration respectively, preserving complete-or-fail discovery checks. Passing these consumers establishes behavior with current approved dependencies, not unchanged historical deployment prerequisites.

[Native quality checks](../verification/mutation/README.md) describe coverage and mutation commands and the accepted release thresholds. Complete current release qualification and actual external integrations remain open in [current decisions](../../../planning/decisions.md). A focused pass establishes only its executed scope; performance/stress and platform claims require their actual selected runs.
