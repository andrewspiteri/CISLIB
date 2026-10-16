# Corrective-work Claude review — partial

16 October 2026. The user explicitly requested that all changes so far be submitted to Claude. This authorizes this read-only submission despite the older stop-review instruction in AGENTS.md.

**Status: incomplete.** Claude Opus 5.5 completed three of six original passes and returned findings. Another submission returned HTTP 429, `usage_limit_reached`: “You've hit your session limit · resets 11pm (Europe/Malta).” That is 23:00 Malta on 16 October. No approval review blocked these transfers. Remaining requests were not sent after the service limit became known. No implementation fixes or new verification runs were performed for this review.

Current correction status: all eleven supported findings have been fixed and targeted checks pass on Windows and Ubuntu. See [corrections and verification](corrections.md). The final Windows Developer checkpoint passes all 6,438 native cases with zero failures/skips/issues; see the [checkpoint](../../evidence/corrective-claude-findings-checkpoint-2026-10-16.md). This does not complete the three unreviewed scopes or claim a Claude review of the fixes.

## Scope and coverage

The snapshot is `c1f37a2e` through `54e01194e2281bcd254e6321753fe191f86ed316`: 47 corrective commits, 472 committed changed/deleted paths, plus five explicitly identified uncommitted files, for 477 assigned paths. Current Core, decisions, selected fixture/vector context, original deleted Python readers and relevant diffs were supplied. The human's amended scope and the difference between development evidence and release qualification were explicit.

| Original pass | Assigned paths | Result |
| --- | ---: | --- |
| 01 Runtime and processing | 51 | [FINDINGS](01-runtime.review.md), completed |
| 02 Values, scopes, events and rendering | 120 | Prepared; not submitted after quota block |
| 03 Provider, queues and lifecycle | 121 | [Service rejection](03-provider-lifecycle.response.json); no review inference |
| 04 Files, recovery and timers | 67 | [FINDINGS](04-files-timers.review.md), completed |
| 05 Type Manager and utilities | 34 | [FINDINGS](05-type-manager.review.md), completed |
| 06 Verification and governance | 84 | Prepared; not submitted after quota block |

Thus 152 assigned paths received their scoped review; 325 remain. Context supplied to another pass does not count as deep review of its assigned scope. Three completed passes are not a whole-change PASS.

The CLI was Claude Code 2.1.295 using `claude-opus-5-5`. Print/JSON mode, a custom review-only system prompt, safe/restricted mode, an empty tool list, empty strict MCP configuration, disabled slash commands/Chrome, no session persistence and noninteractive denied permissions were used. Completed responses report one turn, zero subagents and no web tool use. No credentials or binaries were bundled. These are static reviews, not execution or release qualification.

## Findings at the reviewed snapshot

Eleven Claude-reported actionable items are retained below. The raw reviews preserve their own severity and reasoning. Source inspection corroborated the central observations; this review did not execute reproductions, and some details still need a focused acceptance decision or context check. No finding is silently treated as fixed.

| ID | Finding | Next bounded action |
| --- | --- | --- |
| R1 | Incoming flush/stop observations substitute later downstream counters for the caller's registration counters; coalesced incoming boundaries share QueueFull facts. | Preserve caller snapshots and separately account for covered worker loss; add actual backlog/coalescing regressions. Resolve against the existing immutable-boundary contract. |
| R2 | Portable write-result rules and error-code catalogs lag incoming acceptance and the emitted IncomingQueueFull/RecursiveProcessing codes. | Reconcile the current development contract/reference catalogs with the public pipeline. Check the Accepted XML wording against the public/internal stage distinction. |
| F1 | The 23-mode JSONL boundary matrix has no required-case binding even though acceptance cites its retention-full mode. | Add the matrix binding/minimum of 23 for each lane; keep the distinct twelve-control binding. All modes did run in the latest full checkpoint. |
| F2 | The inventory-bound selector corruption consists only of spaces, so invalid JSON would still reject without the size guard. | Extend the valid padded metadata document one byte beyond the bound; retain the exact-bound positive. |
| F3 | Several migrated file-reader corruptions check only an exception type, losing the former intended-reason meta-check. | Restore stable expected-reason checks in the named readers, without creating another validation framework. Claude found no current wrong-reason pass. |
| F5 | Delete-directory evidence labels describe deletion failure although intent-first preflight can fail before File.Delete. | Clarify those pre-intent modes and cite the separate post-intent denial controls for actual deletion-failure claims. |
| F6 | The two-process inventory holder's ready report is checked less fully than the former Python acquired report. | Add the acquired/created/format/length/segment/hash assertions to that actual holder observation. |
| T1 | The maintained Type Manager acceptance map still points at two retired Python drivers. | Point current instructions at native runtime/inventory commands; preserve historical receipts. |
| T2 | The examples guide omits the seven exact SDK-pack cache prerequisites; the native list duplicates toolchain policy. | State the prerequisites/link the native guide and read the existing frameworkDownloads policy. |
| T3 | processWallMilliseconds now includes verifier-side logging/hashing. | Document the changed observation boundary or preserve the previous timing boundary; it remains unqualified with no threshold. |
| T4 | Independently pinned producer reports are hashed and parsed on separate reads. | Hash and parse one owned byte array; review the same issue in the compatibility reader. Local trusted-input race, not a claimed exploit. |

See the three raw reviews for exact methods, evidence and remaining context limits. Their positive observations are also retained: the Type Manager port preserves the original check sets, and the file/timer review found no confirmed shipping implementation defect in the runtime paths it inspected.

## Questions resolved locally

- Runtime reviewer: `FixtureProcessChecks.RunAsync` still checks exit zero, empty stderr for flat reports, OS, exact .NET 10.0.12, actual PID, fixture/Core hashes and unchanged bytes (lines 38–78). The removed inline assertions were centralized, not lost.
- File reviewer F4: `FileRuntimeStartupExpectations` calls `HumanRenderingExpectations.Read`, whose lines 14–19 reject raw control/format/line/paragraph separator runes. The suggested lost raw-format guard is already present.
- File reviewer Q1: the full Developer report truthfully records source commit `1de526ed`. The subsequent `54e01194` commit changes four Markdown files only. All five uncommitted input hashes match entries in that Developer source inventory; [audit](uncommitted-input-audit.json). No claim is made that later review documentation was tested by that run, and no redundant full run was performed.
- Type Manager reviewer: the two named PowerShell entry points have no Python invocation; current documentation links remain the separate T1 issue.
- The user-owned launch profile is explicitly recorded in the examples migration and full source inventory, with SHA-256 `1509170e5a2d67d067d16e8a3c69db9e67755f5761bc0b5413fe11558054b6be`. It is not reproducible from Git alone, is not staged, and remains a declared working-tree input.
- Strict UTF-8 is deliberate fail-closed behavior. The review raises portability context, not a false-pass defect.

## Evidence and continuation

[input-manifest.json](input-manifest.json) records assigned/context source identities; [submission.json](submission.json) pins each original prompt. Raw JSON responses, Markdown results and process observations preserve both completed reviews and the service refusal.

Exact prompt files and runner remain under the ignored local directory `.artifacts/claude-review-2026-10-16`. To avoid oversized future requests, the 325 pending assigned paths are also divided into six smaller, whole-file continuation bundles under its `continuation` directory. [Continuation manifest](continuation-submission.json) records every pending path exactly once. Changed/deleted files remain complete; omitted unchanged large JSON context is explicitly identified by hash. No continuation request has been submitted.

After Claude's session limit resets, use the prepared continuation runner with the original snapshot. Changes made before continuation need a new/delta snapshot rather than an assertion that these prompts cover them. The first runtime request used 889,535 input tokens, so the original larger bundles should not simply be resent unchanged.

The original logging roadmap goal remains paused. The earlier 6,434-case Windows Developer result still stands within its documented scope; these findings and the incomplete review do not close pending coverage, mutation, full Ubuntu, released compatibility or publication gates.
