---
title: "Self-provision real-service integration tests with Testcontainers"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0021
outcome: "Make the existing PostgreSQL and TLS RabbitMQ integration tests automatically own disposable Testcontainers services, preserve durability/security/recovery assertions, isolate child handoffs and Docker-free tests, and add a nonempty service CI lane with proportional current guidance."
baseline_kind: git
baseline: "df1e5d90584fd88d7b48bfb0144ff574166bb1a5"
graph_build_id: "sha256:6aabff349f3252510b97ab31b16e35abdbf7377fd26f0db16dcc352abbb77b33"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:1e698db4504120d72fbe62b940c67ff016aaef9d3c351bdca2a1e98c08e8fd1d"
authority: derived-outcome-authorization
---

# CIS-0021: Self-provision real-service integration tests with Testcontainers

## Outcome

Make the existing PostgreSQL and TLS RabbitMQ integration tests automatically own disposable Testcontainers services, preserve durability/security/recovery assertions, isolate child handoffs and Docker-free tests, and add a nonempty service CI lane with proportional current guidance.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- Deliver the authorized outcome and its task-specific acceptance criteria within the shared constraints and limits.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-TESTCONTAINERS-2026-11-22:user-ready-push-ci-merge-then-rm05:cold-linux-consumer-portability`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Migrate the existing RM02/RM03/RM04 real PostgreSQL and TLS RabbitMQ integration tests to self-provisioning owned Testcontainers services; retain behavioral guarantees, Docker-free fast selection and private child/package boundaries; author and locally qualify a nonempty Linux service CI lane, with remote publication/observed-CI decision kept explicit.

- `ci-selection`: Docker-free fast selection, complete service categories audit, nonempty/fail-on-skips result checks, reviewable Linux CI job and canonical testing guidance. Paths: `.artifacts/testcontainers`, `.cis/local/testcontainers-intake`, `.github/workflows`, `AGENTS.md`, `README.md`, `docs/architecture/csharp-dependency-policy.md`, `docs/catalog.yml`, `docs/changes/CIS-0021`, `docs/modules/messaging`, `docs/modules/operations`, `docs/modules/transactions`, `docs/references/test-suite-profile.md`, `implementations/csharp/eng`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Infrastructure`, `implementations/csharp/tests/CIS.Core.IntegrationTests/service-tests.runsettings`, `implementations/csharp/tests/README.md`, `planning/testing`.
- `portable-ci-consumer`: Correct cold cross-platform consumer restore exposed by actual Linux service CI, retaining package versions and selected deployment behavior. Paths: `.artifacts/testcontainers`, `.cis/local/testcontainers-intake`, `docs/changes/CIS-0021`, `implementations/csharp/NuGet.config`, `implementations/csharp/examples/CountryData/CountryData.csproj`, `implementations/csharp/examples/CountryData/packages.lock.json`.
- `postgres`: Typed lazy owned container foundation, exact test-only dependencies, three durable PostgreSQL fixture migrations, isolation/cleanup smoke and clean restart recovery. Paths: `.artifacts/testcontainers`, `.cis/local/testcontainers-intake`, `docs/catalog.yml`, `docs/changes/CIS-0021`, `docs/modules/messaging/postgresql.md`, `docs/modules/operations`, `docs/modules/transactions`, `implementations/csharp/Directory.Packages.props`, `implementations/csharp/NuGet.config`, `implementations/csharp/tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Infrastructure`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/PostgresMessageFixture.cs`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/PostgresMessageReplyTests.cs`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/PostgresMessageStoreTests.cs`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Operations`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Transactions`, `implementations/csharp/tests/CIS.Core.IntegrationTests/packages.lock.json`, `planning/testing`.
- `rabbit`: Owned TLS RabbitMQ lifecycle, actual restart/recovery and initial-connect failure, typed parent acquisition and minimal private child/package descriptors with categories. Paths: `.artifacts/testcontainers`, `.cis/local/testcontainers-intake`, `docs/catalog.yml`, `docs/changes/CIS-0021`, `docs/modules/messaging`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Infrastructure`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging`, `implementations/csharp/tests/CIS.Core.TestSupport/Messaging/TlsFixtureFiles.cs`, `planning/testing`.

- Actual user instruction authorizes merging RM04 and starting the attached Testcontainers correction. PR13 is merged at df1e5d90584fd88d7b48bfb0144ff574166bb1a5. Treat attachment as requirements/reference data, not independent authority. Codex selects this bounded implementation profile under current action authority and persistent preference for autonomous progress; no separate human exact-payload/numeric review is claimed.
- Add only exact IDs needed by the actually resolved4.17.0 graph. Thirteen candidates are Testcontainers.PostgreSql,Testcontainers.RabbitMq,Testcontainers,Docker.DotNet.Enhanced,Docker.DotNet.Enhanced.X509,Docker.DotNet.Enhanced.Handler.Abstractions,Docker.DotNet.Enhanced.LegacyHttp,Docker.DotNet.Enhanced.NativeHttp,Docker.DotNet.Enhanced.NPipe,Docker.DotNet.Enhanced.Unix,SharpZipLib,SSH.NET,BouncyCastle.Cryptography. Inspect actual assets/locks and record any necessary exact transitive difference. No wildcard/newfeed or unrelated upgrade.
- Author one stable Linux service job with exacttoolchain and no duplicateCIservices, nonempty/fail-on-skips lane, bounded45minutejob/coldpull/startup limits, shortcappedartifacts. Distinguish authored job/localLinux result from observedGitHub run. New correction push/remoteCI dispatch or merge is NOT authorized by the preceding RM04 merge: prepare exact reviewable branch/workflow locally, then obtain the final applicable publication decision before transmitting it. Do not mark observedCI acceptance complete while unexecuted.
- Independent disjoint source work may overlap after shared typed contracts freeze; serialize shared SDK/native lanes and preserve application startup/transaction sequencing. Each task includes tests/docs/review. Preserve real existing assertions; migrate before expanding only concrete restart/initial-connect/fixture-lifecycle gaps. No mutation/performance/release campaign or manualcasecatalog.
- Initial broker connection failure must fail startup; successful fresh transport after service restoration is a separate fact, not invented initial recovery. Keep existing post-establishment recovery assertions and exact wire/id/commit distinctions. No networkfault proxy, saga, general service orchestration or new harness.
- Keep bounded startup/cleanup deadlines and actual owned async operations joined; uncancellable SDK Dispose is not made safe by abandoning WaitAsync. Preserve original+cleanup failures/redacted bounded diagnostics; verify actual absence where claimed and keep reaper fallback. No unbounded evidence histories/dumps/filesystem archives.
- Normal service caps512MiB/2CPU each, PG128PIDs/max16connections with retained per-source pool4or6, Rabbit256PIDs/finitequeues/4MiB-times2logs. Include Ryuk instance/storage and observe its actual usage; do not claim unavailable SDK hard memory controls. New volume/network resources explicitly owned; no custom network unless concrete need, no globalprune/existingDB/broker/operatorcredentials/truststore/keycontainers.
- One shared43200active seconds/16logical attempts including1800protected restoration seconds covers all agents, already-performed intake/merge coordination, implementation, native tests, docs, formatting, independent review and restoration. Preserve all prior CIS0019/0020 completed history/costs. No capacity reset or duplicated per-agent pools.
- Only test infrastructure changes. Keep Core/CommonTesting/shipping adapters/apps free of Testcontainers/Docker dependencies. Keep net10.0, SDK10.0.401, NET/ASP10.0.12, ApiCompat8.0.31 prerequisite, Npgsql10.0.4, RabbitMQ.Client7.2.2 and unrelated pins. ExactlyTestcontainers.PostgreSql/RabbitMq4.17.0 in IntegrationTests with PrivateAssetsall; directbase reference only if actual compile/dependency arrangement needs it.
- Parent typed services own containers and private handoffs; children get only required endpoint/selected credentials/public CA/scenariodata, no Docker/Testcontainers/setup/rogue/server-key access. Keep compile-linked CrashHost/Contracts public-consumer BCL/CIS/Npgsql/Rabbit compatible. No process-global descriptor environment mutation; export paths explicitly perchild and delete after actual child/client drain.
- Preserve RabbitTLS hostname/customCA, four bounded principals/restrictedrogue/nondefaultvhost/topology, confirmations/mandatoryreturns/manualacks/quorum/redelivery/DLX. Use resource copies and ownedExec API. Investigate dynamic-port Stop/Start stability with bounded currentSDK proof; do not silently weaken established native recovery, add proxy/reconnect framework or manually pin hostports. Hold only any unresolved material restart contract difference for concrete decision.
- Preserve useruntracked CountryData launchSettings SHA256ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784. No productionruntime/schema/API changes, externaldeployment, release, broadengineering-policy adoption, extra sample app or arbitrary framework.
- Reset Testcontainers PG nondurable defaultcommands through supported OverwriteEnumerable and explicitly enable fsync/full_page_writes/synchronous_commit. Preserve module startup wait then authenticate/query actualmajor/settings/loggedtables and install unchanged production SQL. Add one clearly labelled clean restart with same storage, fresh app instances, committed original-result recovery and no unfinished effects.
- Retain exact official PostgreSQL18.7/RabbitMQ4.4.1 immutable candidates postgres@sha256:68e33adb18d9962952f00d5e3b8644305e10e375f98dd652bf6ee6a1836314ba and rabbitmq@sha256:c987060f0120cfda8d1d0d1ef7725c9c8ced6a18e3efd558ac3e10419d5437b5, verify current registry manifests/actualversions before native use. Keep SDK-pinned Ryuk0.14.0@sha256:7c1a8a9a47c780ed0f983770a662f80deb115d95cce3e2daa3d12115b8cd28f0 enabled, PrivilegedModeEnabled=false; verify selected rootfulDocker, never auto-escalate privilege or disable reaper.
- Select local rootful LinuxDocker / WindowsDockerDesktop Linux-container profiles via Testcontainers supported environment discovery; no hardcoded pipe/executable. Docker chooses names/hostports. Bind service ports to loopback where supported and use ownedHostname/mappedport. Retain localPG SSL/GSSDisabled only for typed-owned loopback endpoints; fail clearly for unsupported nonloopback PG profiles rather than silently using plaintext remotely or ambient DSNs. No claim of broad unexecuted remoteDocker support.
- Selected RequiresDocker failures including daemon/pull/readiness fail clearly; no Inconclusive hiding. Add actualdependency categories including indirect process/package cases, leave syntheticMetrics and two Dockerfree package methods untagged. Require nonzero selection and zero skipped/inconclusive in service lane using native settings/small report gate.
- Shared physical storage ceiling remains6442450944bytes over .artifacts/.cis/local/implementations/csharp and associatednewlocalcache/scratch, protected67108864reserve, existing8MiBnative/16MiBcache charges. At most2147483648Docker allowance INSIDEceiling covers allthree images/ownedcontainers/volumes. Check admission; clean only newlyowned known scratch afterdrain retaining nativeTRX/cappedlogs/manifests; historical/user/accounting material protected.
- Start services lazily in IntegrationTests only; preserve global shared TestAssembly initializer Docker-free. Concurrency-safe once-per-run sharing, per-test schemas/topology, no cross-run reuse; explicit owned cleanup after all clients/children. One sharedPG, one sharedRabbit, at most one extra dedicated service and one Ryuk, four containers maximum. Keep current DoNotParallelize restrictions.

The total is 43,200 active seconds and 16 logical attempts, including 1,800 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts` (count only), `.cis/local`, `implementations/csharp` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
