# Native exception driver migration

16 October 2026, based on `41698978`. Corrective Batch B continues with verification-only changes; shipping Core, public behavior, dependencies and historical conformance inputs are unchanged. Review is local; no Claude transfer.

## Reconciliation

Four Python drivers are retired. Existing C# process cases retain all 128 physical scenarios: six projections, 34 boundaries, 20 content cases and 68 shared qualification rows. The parent now checks complete fixed projection trees, the exact thrown-field allowlist, mutable getter counts and full qualification observations rather than relying only on the child fixture's validation.

A shared test-only traversal of emitted exception JSON independently checks nodes, depth, preorder where present, aggregate loss, per-node true-only markers, allowed fields, bounded Unicode text and cached measurements. It does not traverse caller exception objects or replace Core's capture algorithm. Content scenarios that must omit accessor reports now reject spurious reports. Existing expected-tree/getter/ownership controls remain.

Eight of the nine Python qualification controls already existed in C#. The missing floating-number count control is added using an actual parsed 4.0 token; integer count fields are explicitly guarded. The legacy mutation control maps to the existing immutability control over the same stableAfterMutation field. All nine legacy controls are covered within the 17 native qualification controls; eight further previously existing native controls remain. This strictness concerns observer counts and does not alter the LogValue numeric wire policy.

The common native process helper binds actual PID, exact runtime/OS, fixture/Core/input hashes before/after each run, complete parent environment/CWD and absence of outputs/private sentinels. The original 15-second child bounds remain. Qualification catalog count/unique membership stays 68. No maintained entry point imports the retired readers.

## Verification

Native run identities and final graph navigation are recorded below. The first Windows attempt (`45c06f9cac8042fab58b3b09fcefda43`) stopped at a nullable string cast before native execution; a typed string getter fixes it. No tests are credited from that attempt.

Windows final integration `c7b210adb532454196c7a1ee851052ad` and Ubuntu integration `f0b7b64e0a494b679344f762d6798097` each passed 128 cases, zero failures/skips. The exact native case identities match. Windows unit run `bc7d33d031054dadb84c7daafa2ac8a9` passed 445 cases and architecture run `9075ecfe454a40678437914a49abecc2` passed eight cases. Ubuntu unit `80a074a71f564d7b9aa7a23ab5c1b0b6` passed 445 cases and architecture `71ff2e692d9143e4b9b112162f6e5d47` passed eight, zero failures/skips. Linux uses scoped current copies with isolated outputs, not a shared exact package or complete lane qualification.

The previous complete Windows Developer checkpoint is the 5,948-case value batch. It predates the subsequent scope and exception migrations. These selected results do not claim a new full Developer, coverage/mutation/compatibility or release qualification. The original roadmap remains paused while authorized corrections continue.

## Retirement

The base revision preserves original script bytes. SHA-256 values also appear in `.artifacts/corrective/exception-readers/retirement-audit.json`:

- `verify-exception-projection.py`: `6fcc17885258b7de268489e58d30878c88359605708d2475a09e0cd813bc27f6`.
- `verify-exception-boundaries.py`: `0badb00ac80fecf5e4b6573bb82d2435526df7f506ba429a334dc33093402efc`.
- `verify-exception-content.py`: `0806c3c20f1edfa5d9586a0102303552770a5c13da27383e900783b73673ea7f`.
- `verify-exception-qualification.py`: `21e998a93041697e1cde2f96a10fb3659a1e585b238bbbd6d8df0f31eee4ef63`.

Remaining: **59 nonhistorical implementation Python files** (50 DiscoveryHost readers, six engineering utilities and three Type Manager fixture utilities), plus one maintained documentation selector. Five historical implementation Python files and 106 frozen planning/evidence files remain preserved.

Final schema-2 graph refresh: **10,780 nodes, 51,499 edges**, zero warnings/errors, build `0d3270cd458a0e99cd087a00118818ceb0ce9b3ce8ba73b498c441eed463a455`. The exception qualification helper resolves uniquely; its depth-one related query is fresh/untruncated, three nodes/three traversals, proposed edges excluded. Compiler/semantic extractors remain 6/3. These are bounded navigation facts, not semantic-completeness or release evidence.
