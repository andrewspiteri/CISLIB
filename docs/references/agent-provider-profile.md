---
title: "Agent Provider Profile"
type: agent-provider-profile
status: Active
owner: "Repository maintainers"
last_reviewed: "2026-11-09"
review_cadence: "on provider, permission, isolation, or retention-policy change"
---

# Agent provider profile

No direct provider is selected by default. A controller must select Codex, Claude,
or another discovered provider until this profile is reviewed. Credentials remain
provider-native and must never be copied into this document.

| Setting | Value | Rationale |
|---|---|---|
| default-provider | none | Avoid silent vendor selection. |
| default-transport | provider-preferred | Capability negotiation remains explicit. |
| implementation-isolation | git-worktree | Preserve the user's current working tree while seeding the isolated worktree from its exact tracked and non-ignored untracked baseline. |
| direct-dirty-working-tree | denied | Requires an explicit reviewed policy change. |
| maximum-run-seconds | 3600 | Bound local foreground execution. |
| maximum-startup-seconds | 30 | Fail closed when a provider produces no startup evidence. |
| maximum-idle-seconds | 300 | Classify a silent stalled provider separately from total expiry. |
| approve-within-ceiling | denied | Permission requests require explicit controller authorization per run. |

## Providers

| Provider | Enabled | Preferred transport | Allowed modes | Maximum permission | Notes |
|---|---|---|---|---|---|
| codex | yes | app-server | plan, implement, review | workspace-write | `exec-json` is an explicit non-interactive fallback. Resolution checks `CIS_CODEX_EXECUTABLE`, process PATH, then the current Windows Codex Desktop installation. Inconclusive login status is advisory because ambient Desktop/App Server authentication can still execute. Explicit setup uses provider-native browser or device authentication through `cis agent provider authenticate codex`; CIS never receives credentials. |
| claude | yes | stream-json | plan, implement, review | workspace-write | Headless execution does not relay interactive approvals. Implementation-only `--allow-command` adds literal native rules per attempt; denied commands fail. Native rules are not an OS sandbox. |
| portable | yes | envelope | plan, implement, review | read-only | Preparation only; no direct execution. |

Applicable adopted engineering defaults govern supported direct tasks and story stages through
one canonical change/task allocation. Story work-status and bind-work inspect or establish an
existing approved association; they grant no capacity. Implementation, review, correction and
retries share retained cost, reserves and durable correction history across owned repositories.
An exact registered owned participant's controls permit allocation at its authority. Retained
story correction history also permits explicit finite allocation against existing approved
canonical work. Establishing that allocation adopts work accounting across the authority:
every supported direct-task and story route, including siblings without correction history,
then needs its canonical owner and applicable finite allocation. One task's allocation grants
no capacity to another. This does not adopt engineering quality policy or grant plan, design
or completion approval. Reconcile displayed historical effort before reuse or a specific
bounded correction grant.
Two automatic corrections do not reset on a new invocation; relevant delegated disposition may
permit one bounded next step. Generic agent resume redirects story stages to governed continuation.

When story reflection is due, story work-status reports `controller-assessment-required`.
The authorized orchestrating agent inspects retained implementation, failed/valid review
evidence, measured cost, reservations, remaining capacity and current revision. It records
Outcome, nonempty Evidence, Approach, Decision and the next bounded action through
`cis agent work progress <change-id> <task-id> --input <assessment.json> --actor <controller>`
at the existing canonical owner; CIS supplies measured Cost. Keep `closeAttempt: false`
unless the logical attempt has ended, then resume the governed story. No routine human
acknowledgment or extra reviewer is needed. Automatic `story-execution-summary` and legacy
`story-controller-progress` records never qualify, and candidate reuse gives no reflection
credit. Explicit controller progress remains the owner-wide assessment route; incidental
story summaries cannot satisfy direct-task reflection. Status reads remain non-mutating;
independent hard/recovery/permission/approval holds and counters remain binding.

Granted correction and review requests carry the authorized hypothesis, specific action
and disposition provenance. Recovery and extensions retain prior direction, compatible
stages, round identity and spent work; missing legacy direction requires exact-disposition
recovery or the reported bounded `simplify` reconciliation. Nested stages created by the
governed story or correction controller may use the parent's remaining whole seconds below 30,
rounded down within parent/correction deadlines; preparation stays charged. Direct `cis agent run`
requests, including `--parent-execution`, and new root requests retain the 30-second minimum;
direct requests must fit the parent's remainder without automatic shortening. An early retry failure
preserves legacy findings and still-current response provenance; material changes require
assessment and a valid newer empty structured review supersedes obsolete findings.
Responses never waive required defects or approve completion.

Compatible retained implementations survive model-selection changes when the approved task,
repository baseline and native run evidence still match. Fresh review must be independent of
every actual retained implementer model; unknown model identity holds review without discarding work.

Task findings state required/optional disposition and supporting basis independently of severity.
Optional advice does not manufacture corrections or answers; inconsistent verdicts are invalid.
Apply the canonical code-quality standard's proportionate implementation policy. The existing
reviewer explicitly concludes whether the actual diff materially expands product responsibilities
or verification machinery. Applicable reasoning states the accepted obligation, simpler existing
alternative, measured/estimated/unknown cost, maintenance/verification effect and continue,
simplify or remove decision. Ordinary helpers need only a concise not-applicable conclusion,
without another reviewer, benchmark or planning artifact. Unsupported responsibilities can be
corrected by removal or a narrower promise.
Native mutation/incremental work inherits a verified live CIS parent or uses explicit
`--change`, `--task`, `--actor` and applicable `--authority`. Preparation, native launch,
result admission and recovery share the canonical allowance and logical attempt. Missing
allocation or finite numeric owned-storage policy holds before costly preparation; profile
timeouts and byte caps never allocate capacity. A current representative pilot retains healthy
baseline, kill, survivor, infrastructure rejection, restored health and any claimed child
transport. Inspect its actual outcome and cost, then record substantive controller progress
with the displayed `native-pilot:<basis>` before broadening. Validity, threshold and qualification
remain distinct; accounting-only changes preserve compatible material evidence.

Total owned storage includes declared outputs/scratch and associated CIS agent/archive/retrieval
data. Overlapping physical paths count once; separate copies remain counted. Existing retention
plans, delegation and confirmation protect current/recovery/accounting and user material;
counting a path grants no deletion authority. Supported native/artifact enforcement uses bounded
sampling. `--recovery-of` performs only the original pinned restoration/postflight within its
existing reserve and attempt. Cancellation preserves uncertain reservations.

Existing provider permissions and approval gates remain binding. Coverage excludes raw external
agent activity, independent hosts and arbitrary provider writes. Authorities without
adopted accounting or applicable engineering defaults retain their non-adopted execution
boundaries; product-document authoring and import operations retain their dedicated flows.