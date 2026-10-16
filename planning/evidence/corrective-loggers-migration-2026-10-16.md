# Native logger driver migration

16 October 2026, based on `9c2aab57`. Corrective Batch B changes verification only. Shipping Core, dependencies, public behavior and historical vectors are unchanged. Local review applies; no Claude transfer.

## Reconciliation

Four Python drivers are retired. All 256 physical cases remain: 23 foundation, 67 decision, 41 capture and 125 outcome rows. The complete outcome table retains all 98 ordered seven-state pairs for direct/lazy calls. C# catalog checks require exact counts and unique IDs. The three catalogs without a caseCount field retain that shape; existing catalogs still require their declarations, and any supplied optional declaration must agree.

| Legacy family | Controls retained | Native disposition |
| --- | ---: | --- |
| Foundation | 5 | UTF-8/LF checks already existed; three missing/wrong/invented-ticket cases are added. Existing BOM/size cases remain. |
| Decision ordering | 6 | Ticket/UTF-8 already existed; exact order, premature factory, unavailable count and closed admission controls are added. Two existing native controls remain. |
| Capture | 8 | Five existing controls retained; invented invalid ticket, lost truncation and replaced caller exception controls added. |
| Outcomes | 9 | All nine already existed and remain unchanged. |

The result is all 28 legacy controls within 32 native comparator cases, ten newly added. Each added/expanded control validates unchanged Core output before corruption. Foundation assertions now preserve schema version, safe error identity, the literal truncated prefix, complete union/stopped output shapes and identical fanout bytes. Capture/outcome readers reject extra wire groups. Existing strict UTF-8, byte bounds, traces, ownership, counters and safe errors remain.

The shared fixture helper adds actual child PID, before/after managed/input hashes and complete parent environment/CWD checks. Child limits remain 30 seconds. Commands/PIDs/original reports appear in native TRX output. No maintained entry point imports these retired drivers.

These fixtures exercise controlled internal capture/output-admission seams. Their output tickets and partial outcomes are not a replacement for the public incoming-queue acceptance contract and do not prove physical writer delivery or final release qualification.

## Verification

Windows unit `aa05d1f47e174b7490c58c8016f3b3a2` passed 349 cases, integration `848c182ce0174697b0d7d59189349075` passed 256 and architecture `9210623fe3c4491c8904766dc0d87a9b` passed seven, zero failures/skips. Ubuntu integration `71992561e5464e39919ceb3f4579abe6` passed 256. Ubuntu unit `b9472fd77f734a66ac04c207e6893ed0` passed 349 cases and final architecture `cbfacf6c55504ddea4a1c51e5223ef2e` passed seven, zero failures/skips. Linux rebuilds scoped current copies with isolated outputs, not one shared exact package or a full lane checkpoint.

The most recent full Windows Developer run remains the earlier 5,948-case value checkpoint; subsequent migrations use scoped checks. Current full coverage/mutation/compatibility and release qualification remain open. The original roadmap remains paused while authorized corrective work continues.

## Retirement

The base revision preserves original script bytes. SHA-256 values also appear in `.artifacts/corrective/logger-readers/retirement-audit.json`:

- `verify-logger-foundation.py`: `1466b97883724590483fd802958e8b95b2e81b0f86b79bc125eb8361d5917fb1`.
- `verify-logger-decision.py`: `5a188a660776ed5fb04865aa9eff26b0966b0f656e99fbe12f2a373569bba69a`.
- `verify-logger-capture.py`: `b83e051228adbcb14de6a3623b2a7231714573e890a4922bac67ffac93951645`.
- `verify-logger-outcome.py`: `a2bc9870439337233c08a42d2b9ad1b0a1764faa7a455f700717922fa2e93528`.

Remaining: **50 nonhistorical implementation Python files** (41 DiscoveryHost readers, six engineering utilities and three Type Manager fixture utilities), plus one maintained documentation selector. Five historical implementation Python files and 106 frozen planning/evidence files remain preserved.

Ubuntu architecture attempts `378bd39954d14f138e75bee1da331e89` and `e3aac107b89f4f97b4c0e95b7fbe12ea` each rejected two old pre-correction API expectations from an incrementally reused DLL. The current canonical architecture source was copied byte-for-byte (SHA-256 `4358bf76d7c75d47f12a3c23465cc90b8be476b3e55b9bc2e0effee001db8ebe`), but its preserved source timestamp did not force compilation. Explicit Rebuild in the isolated checkout followed by the native focused run resolved both failures. No canonical test expectations or shipping code were changed to obtain that pass. Other scoped copies in this slice have their new source timestamps; future backfilled copies must be rebuilt/touched rather than trusted by timestamp alone.

The integration audit records identical 256 native case identities. Final schema-2 graph refresh: **10,728 nodes, 51,329 edges**, zero warnings/errors, build `6a32debba127b7edda3d1241fd2d30705cd23553f04935199b8f6065618cc67f`. The outcome expectation helper resolves uniquely; its depth-one related query is fresh/untruncated, three nodes/three traversals, proposed edges excluded. Compiler/semantic extractors remain 6/3. This supports bounded navigation, not semantic completeness or release qualification.
