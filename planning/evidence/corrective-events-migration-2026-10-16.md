# Native event driver migration

16 October 2026, based on `32b285b2`. Corrective Batch B changes verification only. Shipping Core, dependencies, public behavior and historical vectors are unchanged. Review is local, with no Claude transfer.

## Reconciliation

Five Python drivers are retired after retaining all 105 physical cases: 32 projection, 28 precedence, 14 correlation, eight encoding and 23 complete-event qualification rows. Native catalog getters require exact counts, schema and unique identities. All 23 former corruption controls already execute in native tests: six projection, eight precedence, eight correlation and the null-wire qualification control. Existing additional native qualification controls remain.

Missing encoding observations are added to the existing C# comparer: protected resource/build identity, lowercase hexadecimal current span, exact array contents, ordinal map-key output and exact retained prior text. Five positive-first Core-output corruption cases qualify those assertions. Numeric wire comparison policy, entry caps and truncation behavior are unchanged.

Existing native expectation readers preserve field precedence, protected identities, Activity source binding, timestamp precision, typed values, UTF-8 framing/counts, atomic omissions, optional fields, later mutation/owned-state observations and worker/restoration behavior. Complete-event process cases retain independent schema/producer-contract validation. The parent process helper adds actual PID binding, before/after managed/input hashes, complete environment/CWD preservation, no outputs and private-sentinel exclusion. Original 30-second child deadlines remain; command/PID/original reports are retained in TRX output.

## Verification

Windows integration `07ea6fae9d8e4c3982aefe58673604aa` and Ubuntu integration `3b97705ebd8b451bba27e4ba61da0b62` each passed 105 cases, zero failures/skips and identical native case identities. Windows unit `994f8eda271b40678ba37b58e703801a` passed 306 cases, and architecture `953d47c7899c48d2827f14e53e48d81c` passed 193. Ubuntu unit `24152059422e4020b35cc3425cb5942b` passed 306 cases and architecture `5bfa19a7b11c4e42af5e7631a69c9e66` passed 193, zero failures/skips. Linux uses scoped current copies and separate outputs; no shared exact-package or complete lane qualification is claimed.

The latest full Windows Developer checkpoint remains the preceding 5,948-case value batch; subsequent scope, exception and event migrations use explicitly scoped native checks. Full current coverage/mutation/compatibility and release work remain open, and the original roadmap remains paused.

## Retirement

Original SHA-256 values are retained in `.artifacts/corrective/event-readers/retirement-audit.json` and below; the base revision retains all script bytes.

- `verify-event-projection.py`: `07d24f0c69d1a0fdbe14d4ae0e1d9ee00e23f1c1931613e04c9ded0c1e533135`.
- `verify-event-precedence.py`: `90949541783db566d06a78c28ad7ae91fc1f0b93c1ff2d3e543b0b69a2bc5c8c`.
- `verify-event-correlation.py`: `fa60be253525868273703345524f260b578a82cab9935ea0b72cb6e7267d0d0e`.
- `verify-event-encoding.py`: `6e6f133a2889b26bfea7b2355c72b445c6612843209ff3c931ebcc4cfbf7172e`.
- `verify-event-qualification.py`: `547bc3910d31bbefceb1270e0aa7318e3a110abd2b612fda72e31105a9c13d81`.

Remaining: **54 nonhistorical implementation Python files** (45 DiscoveryHost readers, six engineering utilities and three Type Manager fixture utilities), plus one maintained documentation selector. Five historical implementation files and 106 frozen planning/evidence Python files remain unchanged.

Final schema-2 graph refresh: **10,743 nodes, 51,379 edges**, zero warnings/errors, build `97b47f4dcc6e2db6663a07c47b055d47945fcd779d60474fcba807890476cfaf`. The event qualification process class resolves uniquely; its depth-one related query is fresh/untruncated, four nodes/four traversals, proposed edges excluded. Compiler/semantic extractors remain 6/3. These are bounded navigation observations, not completeness or release evidence.
