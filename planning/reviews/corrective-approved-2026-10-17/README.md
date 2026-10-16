# Approved corrective review continuation — 17 October 2026

**Partial: 17 of 24 scoped Claude reviews completed.** Two requests returned HTTP 429 `usage_limit_reached`, reporting a reset at **05:00 Europe/Malta on 17 October** (03:00 UTC). Five further bundles were not attempted after that refusal. The last already-running request completed. No processes remain running and no automatic retry is scheduled. This is a service quota limit; the approved payloads do not need another user approval.

The human explicitly answered **“Approve this exact 24-bundle review set.”** That authorizes this exact manifest and destination despite the older stop-review entry in AGENTS.md. All attempted prompts matched the approved bytes. The previous automatic-approval rejection remains a historical observation; it did not block these attempts.

Approved manifest SHA-256: `66bea29723b9aa2a039df2934c69d3dc0c0b25f189c95ee557eb1d8a4972a46a`. The set contains **24 prompts / 4,923,824 UTF-8 bytes**, destined for Anthropic Claude, with no credentials or binaries. [Approved manifest](approved-manifest.json), [execution index](index.json).

## What completed

- Bundles **1–8**: configuration, console, events, exceptions, logger, rendering, scopes and values.
- Bundles **9–14**: flush, lifecycle, provider and queue/diagnostics work.
- Bundles **15–16**: repository/graph guidance and the required-case inventory.
- Bundle **24**: the native C# source-inventory migration.

The earlier 23 prompts assign **352 unique paths**. Completed bundles cover **244**; **108 remain**, comprising **81 original pending paths and all 27 additional corrective paths**. [Assigned input identities](assigned-inputs.json). The separately completed source-inventory delta overlaps this assignment and is not added as an invented unique-path total. In particular, the dedicated rereviews of the eleven earlier fixes remain pending, although completed bundles inspect some overlapping context.

The 23 earlier prompts retain snapshot `396e87d6` plus five declared working-tree inputs. The source-inventory delta retains `a4df38e0` plus its task changes. Neither is relabelled as current HEAD. Local triage used canonical `ba6eb62d`. Later checkpoint/review metadata is outside those immutable prompts. The five user-owned files remain untouched and unstaged.

## Findings and local checks

[Prioritized findings and dispositions](findings.md) distinguish supported issues from missing-context concerns. [Detailed local triage](triage-notes.md) preserves each completed review's dispositions. Raw Claude results remain unchanged under `results/`; [index.json](index.json) pins every response, extracted Markdown and process observation by bytes/hash.

Isolated C# probes reproduced a Git-root alias inventory error, an integer-kind exception-reader gap, a provider nested-map budget inconsistency and loss of a leading UTF-8 BOM in ProcessRunner's text capture. A relative inventory-root claim did not reproduce, and the actual normalized temporary-root helper resolves the reported Windows short-path-oracle concern. Sources and observations are retained here. These are narrow observations against existing built code, not a new Developer checkpoint or full public-consumer qualification.

No shipping implementation, maintained test or contract was changed during this review. Current status and AGENTS guidance record this exact approval and clarify the Change Impact Studio dependency boundary. Current Windows/Ubuntu Developer results remain the [source-inventory checkpoint](../../evidence/corrective-source-inventory-2026-10-16/README.md): 6,443 / 6,445 passed, zero failures/skips/issues. The review identifies limits in some checks; those run counts do not by themselves establish reader completeness or release qualification.

## Still pending in the approved set

| Bundle | Scope | Observation |
| --- | --- | --- |
| 17 | Verification/governance 3 | [Actual quota refusal](results/17-06-verification-governance-governance-3.response.json); no review |
| 18 | Verification/governance 4 | [Actual quota refusal](results/18-06-verification-governance-governance-4.response.json); no review |
| 19 | Verification/governance 5 | Not attempted after quota refusal |
| 20 | Verification/governance 6 | Not attempted after quota refusal |
| 21 | File corrective fixes | Not attempted; dedicated F-series fix review remains open |
| 22 | Runtime/contract corrective fixes | Not attempted; dedicated R1/R2 fix review remains open |
| 23 | Type Manager corrective fixes | Not attempted; dedicated T-series fix review remains open |

Continue these exact immutable payloads after service availability returns, using a fresh output directory for refused attempts so the original responses are preserved. Verify the approved hashes before submission; do not silently extend this authorization to changed inputs or another destination. Exact prompts and original runner adapters remain in ignored `.artifacts/claude-review-2026-10-16-resume/inputs/`, `.artifacts/claude-review-2026-10-16-review-set/` and `.artifacts/claude-review-2026-10-17-approved/`.

## Execution limits

Claude Code 2.1.295 resolved `opus/high` to **`claude-opus-5-5`**. Print/JSON mode, safe/restricted operation, an empty tool list, strict empty MCP configuration, disabled slash commands/Chrome, denied noninteractive permissions and no session persistence were used. Completed responses report one turn, no tool denials and no subagents. Claude executed no tests or repository actions. [RetainReview.cs](RetainReview.cs) verified all 24 payloads, actual results and assigned-path membership before retaining evidence; it refuses to overwrite this receipt.

The original logging roadmap goal remains **paused**. This partial static review does not constitute whole-change PASS, passing coverage/mutation, exact-package compatibility, offline replay or release qualification. The next bounded correction should address the supported runtime/reader gaps before continuing the engineering migration or roadmap.
