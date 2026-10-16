# Native console driver migration

16 October 2026, based on `f99ca7a3`. Corrective Batch B changes verification only; shipping Core, dependencies, public behavior and historical vectors are unchanged. Local review applies; no Claude transfer.

Four Python drivers are retired. All 137 physical catalog rows remain: 41 composition, 22 ownership, 36 startup and 38 environment consumers. Native checks require the exact original ID matrices and unique IDs. All 43 former corruptions remain (7 composition, 13 ownership, 10 startup, 13 consumer); positive original Core output is now checked before each unit corruption. Three added rejection controls preserve complete output/captured field sets and canonical successful ticket strings. A separate native cohort verifies unique nonzero instance identities across all 38 fresh consumers.

The existing process helper gains explicit physical-stream capture and minimum stdout/stderr byte checks. These retain original strict UTF-8 bytes, the final JSON report boundary, simultaneous pipe draining, configured pressure minima, 30-second child deadlines and controlled-child empty-stream rules. It adds actual child PID, before/after input/managed hashes, original command/report retention and complete parent environment/CWD preservation to the console checks. Physical startup still preserves application markers; borrowed ownership/serialization, safe errors, old-runtime survival, exact records and independent controlled file progress remain unchanged.

## Verification

Windows integration `ba978a697e7b40c98770c9b52fd6c79d` passed 151, unit `13da53973f364087a02f6862cab2ab31` passed 134 and rendering-helper regressions `c552003779f04754821e30522c9c6710` passed 117, zero failures/skips. Ubuntu integration `f604cbe84088475ea6ad9bf2fa8917ef` passed 151, unit `2119df67ab1f4b7d8e1bcfcd363dcdb0` passed 134 and rendering regressions `3f09082260f24e2d81252138aac445f2` passed 117, zero failures/skips. The native audit confirms matching case identities for all 402 cases across lanes. Linux rebuilds scoped current copies with isolated outputs, not a shared exact package or full lane checkpoint.

Windows unit attempts `955732a3fa964563bf46dee0452295ca` and `a34a32a5010842f5b37046026bb45117` each passed 133 and rejected the new extra-capture control for an earlier reason (byte cap, then full-record equality). The test was corrected to use the actual truncated file record in the 16 KiB vector and a bounded damaged payload. It now rejects specifically for the migrated unknown-field rule. Production code and catalog expectations were not changed to obtain the pass.

The most recent full Windows Developer result remains the earlier 5,948-case value checkpoint. This is focused native verification; full corrected coverage/mutation/compatibility, physical-file/public-factory qualification and release gates remain separate. The original roadmap remains paused while corrective work continues.

## Retirement

The base revision preserves original bytes. Guarded retirement SHA-256 values:

- `verify-console-composition.py`: `17c97cbbe9354b4b39eceaa3596d8a68d84bdb08884f87f95c9e2685c25c8a35`.
- `verify-console-ownership.py`: `0def57fdb175b41b8efe58bd5629715fe421072fa6be2719a768a7f0b8c2180e`.
- `verify-console-startup.py`: `bfc9f23c7795d55217293b137e96596a260ada1f30334aceb54ed3461b8d3775`.
- `verify-console-consumer.py`: `0657ff3b1200ba13b7fbac9ed47a4513b7c818cad097e7580ccd618c40c7cd83`.

No maintained importer remains. Remaining: 42 nonhistorical implementation Python files (33 DiscoveryHost readers, six engineering utilities and three Type Manager fixture utilities), plus one maintained documentation selector. Five historical implementation files and 106 frozen planning/evidence files are preserved.

Final graph refresh: schema 2, 10,691 nodes/51,196 edges, zero warnings/errors, build `7ec0be1ecc39d35c4ae3a6fd6c2cfbc81b418eb019de87bc1be96a03ac712450`. ConsoleCompositionExpectations resolves uniquely. Its depth-one related query is fresh/untruncated (three nodes/three traversals, limit ten, proposed edges excluded). Compiler/semantic extractors remain 6/3. This supports bounded navigation, not semantic completeness.
