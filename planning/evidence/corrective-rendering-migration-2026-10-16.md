# Native rendering driver migration

16 October 2026, based on `09e03e0b`. Corrective Batch B changes verification only. Shipping Core, dependencies, public behavior and historical vectors are unchanged. Local review applies; no Claude transfer.

Four Python drivers are retired after matching their assertions against native C# readers. All 117 physical rows remain: 20 JSONL reuse, 12 human rendering, 64 presentation and 21 combined rendering. Native catalog checks require those exact counts and unique IDs; the presentation unit check additionally requires the complete profile/format/detail/stream Cartesian matrix. The catalog format remains unchanged.

All 37 former rejection controls already exist in C# (9/11/6/11 by family) and are retained. All six formatting positives remain: JSON object member order/spacing, human field order/spacing and the combined JSON/human layout plus equivalent numeric notation cases. Human assertions now bind captured severity and case identity to the vector. Presentation assertions bind the original literal payload independently of the observed capture. Existing raw UTF-8/LF, byte caps, BOM/control rejection, exact types/arrays, loss and capture/memory reuse checks remain.

The process helper adds actual child PID, before/after input and managed hashes, complete parent environment/CWD preservation, original command/report retention and an unchanged 30-second child limit. Controlled rendering creates no files; presentation observes redirected physical streams and the absence of a debugger. This fixture boundary does not itself qualify the later physical console/file composition or a release package.

## Verification

Windows integration `f55572024279404ebe3befd65a322a24` passed all 117 intended cases. The passing Windows unit selection `9e26d55f5c30497dab648ac7c39409e6` contains all 170 intended unit cases within 190 total passes; architecture selection `c2172396671f41eaa00a78aba1bdfa9d` contains all seven intended architecture cases within 59 total passes. No failures/skips occurred in those selections. Ubuntu exact integration `207f65062f6048f89504bb8870dcb13c` passed 117, unit `0a1dedacdd1d4b3cb88cd55ce2497df0` passed 170 and architecture `feb0dbd238014dde95b4b598e73b24f6` passed seven, with zero failures/skips. The native-case audit records matching identities for all 294 intended cases.

Initial broad integration filters also selected four package presentation consumers: Windows `5b5a673c3cc34bf8a06f153eb262accf` and Ubuntu `88d9cb0e5cf0408595c0ab1d026dd3df` each passed 154 and rejected four stale package/Core bindings. These are not credited as successful package checks. Exact class-prefix selections then verified this migration without changing assertions or rebuilding unrelated packages. Ubuntu builds scoped current source copies with isolated outputs and updated source timestamps, not one shared exact package or a full lane checkpoint.

The most recent full Windows Developer run remains the earlier 5,948-case value checkpoint. These later migrations use focused native checks; full corrected coverage/mutation/compatibility and release qualification remain open. The original roadmap remains paused while authorized corrective work continues.

## Retirement

The base revision preserves original bytes; the guarded retirement audit records SHA-256:

- `verify-jsonl-reuse.py`: `0b672c606a702b43ab46c488ecf111a477a9a52caae0fe05bc2fb6562dc94437`.
- `verify-human-rendering.py`: `fe6eacb0679aae44a8ca4ce145c7faf6b4e70339fd3640dc04baa853906f27e7`.
- `verify-presentation.py`: `075fb27189b7e2d448d1c141513d4b162eaccdc24940486887afaf33a856d1be`.
- `verify-rendering-story.py`: `973de7354ec45b6dd0cd343b2d4a9f11976200aa1873625b1968f8b73370ae0f`.

No maintained importer remains. Remaining: 46 nonhistorical implementation Python files (37 DiscoveryHost readers, six engineering utilities and three Type Manager fixture utilities), plus one maintained documentation selector. Five historical implementation files and 106 frozen planning/evidence files are preserved.

Final graph refresh/status: schema 2, 10,708 nodes/51,258 edges, fresh with zero warnings/errors, build `22ddbf84ec4666997d9d23e508df8fa6ab9a00b54842fc1ae5170fa682d9ce14`. RenderingStoryExpectations resolves uniquely. Its depth-one related query is untruncated (three nodes/three traversals, limit ten, proposed edges excluded), with compiler/semantic extractors 6/3. This is bounded navigation evidence, not semantic completeness.
