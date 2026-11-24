# RM-08 acceptance observations

Candidate: the current `codex/rm08-central-configuration` working tree on baseline
`abfbcafaa6ef2f3c3c18304244e4dafc1dd5c852`. These are focused implementation
observations, not release or external deployment qualification. The imported
specification remains byte-for-byte unchanged. See [verification](verification.md)
for original failures, corrections and native receipt locations.

Receipt abbreviations below are relative to
`.artifacts/configuration-publication/CIS-0027/`:

- **U**: `WORK-006/current-host-order-affected-unit-tests/results/current-host-units.trx`,
  1,200 Core Management/Configuration/Security cases passed.
- **P**: `WORK-009/adoption-retention-provider-tests-corrected/results/adoption-retention.trx`,
  79 actual PostgreSQL/HTTPS cases passed, including original transaction/Audit
  participants, two-replica races, adoption and retention.
- **B**: `WORK-006/native-bootstrap-live-authority-final-tests/results/native-bootstrap.trx`,
  five actual SDK/TLS/PostgreSQL bootstrap, reconnect, empty/missing and failed-Build cases passed.
- **N**: `WORK-006/explicit-native-preparation-activation-tests/results/prepared-activation.trx`,
  15 actual credential preparation/activation/cancellation/drain cases passed.
- **C**: `WORK-011/countrydata-authoritative-flow-and-regressions/results/countrydata.trx`,
  28 of 30 passed, including the complete new CountryData workflow and public
  compiled CountryData consumer. The two failures passed unchanged in
  `WORK-011/consumer/results/consumer.trx`; both receipts remain retained.
- **E**: `WORK-011/country-unit-examples/results/country-examples.trx`, all 61
  maintained example unit cases passed, including 13 new public opt-in cases.
- **L**: `WORK-011/linux-focus-current/results/linux-units.trx` and
  `linux-contracts.trx`, 1,128 Configuration/Management units and 151 selected
  contract/architecture cases passed using current Linux-compiled source.
- **A**: `WORK-011/api/results/api.trx`, the original binary/source consumers and
  public API comparison passed (three cases).
- **T**: WORK-008's [task evidence](agent-tasks/WORK-008.md): 181 native Node cases,
  pinned TypeScript/browser compilation and 12 real HTTPS transport scenarios passed.

| Criterion | Actual observation and evidence | Practical limit |
| --- | --- | --- |
| AC-01 | Existing default/custom-store/pipeline/root/merge/event cases in U and L; exact current public package consumer in C plus short consumer rerun; A preserves original consumers/API. | Focused existing contracts; no unrelated whole-repository qualification. |
| AC-02 | B resolves central values in a real constructor, compares the same actual scope factory/singleton, preserves one registration and final meter ownership, and joins held failed-Build disposal. N proves native preparation ownership. | Controlled issuer transport invokes the real Entra SDK and native TLS boundary; external federation is not proved. |
| AC-03 | B refuses missing catalog/unassigned initial state before consumer construction and accepts a live explicitly empty release at revision zero. U rejects stale/session-mismatched initial state. | No disk cache or offline-start fallback is provided. |
| AC-04 | P keeps release creation separate from assignment; C publishes R2 and reconciles before assignment while retaining R1/revision zero. | No automatic assignment or application acknowledgement is inferred from publication. |
| AC-05 | U and L preserve defaults/common/application merge, nested members, collection replacement, null and source-before-overlay validation. C composes actual CountrySettings defaults with common/application partial sources. | Server structural validation does not execute local validators. |
| AC-06 | P fetches complete pinned pairs from one original read context; missing/foreign references refuse. U requires paired capability for central entries while preserving legacy routing. | Atomicity is per enrolled root, not across roots or a fleet. |
| AC-07 | P exercises native actor/application/content/status scopes, foreign references and frozen registration/host binding. U checks the exact independent expected target before retaining desired state. | Reported labels/UUIDs are not credentials. |
| AC-08 | P proves trusted frozen cohort/default resolution and replay without recapture. U fences session rebinding and late completions. C uses a trusted canary and new native host sessions. | Sequential hosts in C run in one OS process; they do not prove distinct physical instances. |
| AC-09 | P whole-map promotion removes only explicit overrides and preserves deliberate pins under CAS. C stages the canary, then promotes and removes its override before fresh-host activation. | No automatic promotion policy. |
| AC-10 | U and L cover unchanged checksums, duplicate/equal content, provenance-only decisions and revision-zero baselines. C verifies zero initial revision after each fresh host. | Assignment generation, release identity and local revision remain distinct. |
| AC-11 | U covers lower/equal-conflicting generations, cancellation, supersession, session and epoch completion fences. P freezes the admitted session. | Cooperative cancellation joins admitted owners; a canceled wait is not settlement. |
| AC-12 | U covers malformed/digest/schema/typed/cross-property/materialization/buffer failures with intact accepted state. P bounds source and full escaped envelopes. C rejects a structurally valid `file:` source through the real local validator and reports rejection. | Local arbitrary validator memory is not a CLR heap guarantee. |
| AC-13 | U preserves retained hot roots, coordinated complete-state reads and subscriber order/isolation, including throwing subscribers. B proves actual hot reconciliation after outage. | CountryData intentionally selects RestartRequired. |
| AC-14 | U covers pending supersession, active/desired distinction and return-to-active clearing without false events. C stages R2, keeps the original live root/URL and reports pendingRestart. | Staged desired values are not used by the current catalog. |
| AC-15 | C creates new actual CountryApplication hosts from current live promotion/rollback maps; fresh roots capture the current authoritative release at local revision zero. | This is real host/provider/native-session restart coverage, not an independent OS-process restart experiment. |
| AC-16 | U verifies throwing live setters produce terminal disabled/Faulted state without false acceptance or intact historical-state claims. | Recovery requires the supported new-host path. |
| AC-17 | B stops/restarts the actual management service at the same selected origin and recovers before an hourly config poll. U preserves accepted configuration while separate authority closes admission. P retains strict backend hold/unavailable behavior. | A configuration snapshot/304 never renews management authority. |
| AC-18 | B proves confirmed reconnect kicks the existing Configuration worker before the next poll; U covers failed completed and still-draining recovery tasks. Polling and latest-state reconciliation do not depend on notifications. | No additional watcher, broker or timer was added. |
| AC-19 | P observes one whole-map winner across replicas, defined conflicts, higher rollback generations and retained unrelated pins. C observes a higher-generation rollback through pendingRestart and fresh activation. | No per-instance execution ordering is implied by a map commit. |
| AC-20 | P exercises original-key publication/release/assignment recovery after native commit acknowledgement and cleanup uncertainty, and original required Audit refusal. One result/effect/charge remains. | Audit staging/committed outbox is proved; an external Audit destination store is outside scope. |
| AC-21 | P uses two real management hosts sharing isolated PostgreSQL, independent native contexts, writer races, original result recovery and authority/cleanup failures. B restarts a real service host; C operates on replica 0 and bootstraps consumers through replica 1. | Persistence and native host restart are observed. Separate OS-process crash/deployment qualification was not run or claimed. |
| AC-22 | P covers native ownership, independent sequences/receipt freshness, replay/conflict, unknown future states, offline/stale/no-instance targets, frozen eligibility and bounded pages. U preserves prior facts after unidentified poll failure. C reports actual rejected/pending/applied relationships. | Registration labels do not prove process identity or typed fleet acceptance. |
| AC-23 | U/L and canonical independent vectors preserve large exact decimal revisions, local zero, timestamps, unknowns, non-ASCII payloads and exact SHA-256. T proves the portable TS/WebCrypto/awaited-cleanup boundary; A and the current public package consumer retain compatibility. | Kotlin signatures are documented; no Kotlin runtime implementation is included. |
| AC-24 | U/P/T enforce source/envelope/materialization/alias/cursor/diagnostic limits. P proves idempotent trusted provisioning, truthful capability discovery, eligible-first latest-N/pin/shared-reference GC, hard quota refusal and actual owner drainage. B/N/C prove host/native cleanup. | Logical process/source and database quotas are not arbitrary heap or physical database size guarantees; explicit external pins can exhaust capacity and refuse publication. |

One representative volume-sensitive native result is P's
`LegalUnicodeMetadataProducesBoundedCompleteContinuationPagesRatherThanAnEncoderFailure`:
64 additional sequential registrations plus the initial recipient, each new label
containing 1,024 astral scalars (4,096 UTF-8 bytes) and version containing 512
astral scalars (2,048 UTF-8 bytes). Against the two-replica PostgreSQL fixture,
requested page size 500 produces more than one and at most four actual pages;
every encoded page is at most 1 MiB, and all 65 recipients appear exactly once.
The retained case duration is 5.078164 seconds. This is one focused correctness
observation with sequential workload, not a throughput or fleet-size promise.

External Entra federation, AKS/cert-manager, independent OS-process crash tests,
remote CI and release qualification remain unverified. No new packages, pins,
source mappings, SDKs, images, framework, test runner or general harness were added.
