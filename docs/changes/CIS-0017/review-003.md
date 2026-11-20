# WORK-003 independent source review

No required findings or material optional corrections were identified in the
three PostgreSQL consumer/fixture/test sources and provider guide. Independent
read-only effort: eight active minutes, separate from WORK-004 authorship.

The intended eleven cases use exactly three ordinary logged tables in a fresh
GUID schema, the supplied native pair and explicit command.Transaction, and an
independent live observer session for visibility. They cover declined-result-only
commit, later/caught failures, rollback/disposal, three server isolation settings,
independent owners and continued application-source use. At most three test
leases are live, within the four-connection fixture ceiling.

The cancellation case confirms the owned blocker through pg_blocking_pids before
canceling actual work. Bounded observer polling only spaces observations; a sleep
is not the evidence. Finally paths unlock the owned session and join work before
lexical owner/source cleanup. Fixture cleanup attempts provider, schema and source
release while retaining failures.

This is a small named real-provider acceptance consumer, not production result or
outgoing storage, a second runtime or a test framework. No broader machinery is
justified. EF and real lost-acknowledgement qualification remain unexecuted recipes
or limits. The reviewer read no credential/operator files and ran no native
commands or SQL. Native PostgreSQL acceptance is still pending restore/execution;
the source review is not a passing database result.

## Actual PostgreSQL evidence follow-up — 21 November 2026

The subsequent independent evidence review found no remaining required source
or executed-lane concern. The earlier source-only conclusion above is preserved
as history; native SQL acceptance has now executed on both selected operating
systems. Additional read-only evidence review: approximately four active minutes.

The ignored `integrated-receipt.json` and both referenced PostgreSQL TRXs agree:
Windows and actual Ubuntu each executed and passed all eleven cases, with zero
failed, skipped, inconclusive, aborted or timed-out cases. The actual active-query
cancellation case, `ActiveNativeQueryCancellationIsConfirmedByAnIndependentObserverAndActuallySettles`,
is present and passed in both lanes, alongside all three server isolation rows.
The corresponding native logs independently report eleven passing cases each.

| Evidence under `.artifacts/transactions/CIS-0017/` | Independently confirmed SHA-256 |
| --- | --- |
| `WORK-003/postgres-windows/results/postgres-windows.trx` | `b68c82de3a66d6fcff53cdc83f996e4959bbad23887c67aab2fb046ff01f5009` |
| `WORK-003/postgres-linux/results/linux-native.trx` | `465680595d90aec38f8535e8056b021de066ae8961285a73280cb2534f7df866` |

Current source-built Core and the integration output's executed Core copy both
hash to `ea1eef108d6efcc07350388e9493d5bb0ca0a81b4309f8bfb7dcb3a7b29eb69b`.
The current integration assembly hashes to
`e5a2d29f79326251e8c418e46916abf375ebf837fe09236dac45f7169be68064`;
its Npgsql asset hashes to
`8e8e3ab73fbb8a424be58647427f82172d7894a75b7ea12e374cde5c38d71beb`.
All match the integrated receipt. SDK 10.0.401 and Core/ASP.NET runtime 10.0.12
with `rollForward=Disable` remain selected. This identifies the frozen working
candidate; it does not claim a clean commit or release qualification.

Npgsql 10.0.4 is an explicitly private integration-test dependency. Neither Core
nor CommonTesting's current lock file adds Npgsql, OpenTelemetry or EF. The
nonsecret owned-resource/image receipts identify the fresh official Linux/amd64
PostgreSQL image
`postgres@sha256:68e33adb18d9962952f00d5e3b8644305e10e375f98dd652bf6ee6a1836314ba`,
loopback port 50495 and actual server 18.7. The provisioning query's ordered
settings show `fsync=on`, `synchronous_commit=on`, `full_page_writes=on` and
`max_connections=16`. These retained receipts support the selected ordinary
durability fixture; the reviewer did not issue fresh database/container queries.

Final independent owned-schema/session/data inspection and removal of the exact
labelled container/volume are still pending root restoration evidence. Passing
case-local cleanup does not establish that final fixture restoration has already
occurred. EF participation, a real lost-acknowledgement/network fault, broad
provider/platform compatibility and release/coverage/mutation/performance remain
unqualified. No credential or operator configuration file was read; this
follow-up launched no native process or SQL and introduced no additional runtime,
provider, test harness or implementation responsibility.
