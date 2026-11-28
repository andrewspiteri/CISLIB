---
title: RM11 backoffice wireframes
type: textual-wireframes
status: Approved
change_id: CIS-0031
approval_status: Approved
authority: human-reviewed
frontend_type: backoffice
---

# RM11 backoffice wireframes

Keep the existing Applications rail, Management header and application context. Use human-first labels, exact authorized target binding and a collapsed Technical details area. The frames use synthetic data at 1600 × 1000 and a representative narrower desktop state. Keyboard order follows reading order. Text labels accompany every status. The rules grid owns its horizontal overflow and keeps primary actions, scope and the selected-cell inspector reachable at desktop zoom.

The browser routes below are proposed. Management behavior binds the owned intents in the [portable handoff](../../../contracts/rule-engine/v1/README.md). Current grants are checked for actual resources on every read, mutation and original-result lookup. Draft stays in memory in the same authorized session and target. The UI uses the existing portal recovery behavior. A denied or changed session clears protected data, while transient same-target loss preserves a dated safe view and disables mutations.

## Screen inventory

| Screen ID | Frontend type | Name | Route/path | Platform | Actors/access | Entry points | Purpose |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM11-RULESETS | backoffice | Rule engines | /applications/{applicationId}/rule-engines | Desktop web | Scoped application and ruleset discovery | Application Capabilities | Open an application and environment’s registered ruleset. |
| RM11-GRID | backoffice | Rules grid | /applications/{applicationId}/rule-engines/{rulesetId} | Desktop web | Scoped schema and version read with independent edit/result permissions | Rulesets, edit version, or return from review | Edit explicit typed conditions, row order, state, metadata and weight. |
| RM11-VERSIONS | backoffice | Saved versions | /applications/{applicationId}/rule-engines/{rulesetId}/versions | Desktop web | Version inspection and separately granted editing or deployment | Grid or successfully saved version | Inspect immutable snapshots and choose the version to deploy. |
| RM11-DEPLOY | backoffice | Confirm deployment | /applications/{applicationId}/rule-engines/{rulesetId}/deploy/{versionId} | Desktop web | Verified separate deployment and exact original-result rights | Deploy selected version | Deploy or roll back the exact selected saved version with visible scope. |
| RM11-ADOPTION | backoffice | Deployment and adoption | /applications/{applicationId}/rule-engines/{rulesetId}/deployment | Desktop web | Exact assignment and instance report reads | After Deploy or Deployment tab | Separate the latest desired version from individual instance observations. |
| RM11-COUNTERS | backoffice | Hit counters | /applications/{applicationId}/rule-engines/{rulesetId}/counters | Desktop web | Scoped counter reads and separate current reset/result permission | Counters tab | Inspect exact scoped counter freshness and confirm audited resets. |
| RM11-EXECUTIONS | backoffice | Executions | /applications/{applicationId}/rule-engines/{rulesetId}/executions | Desktop web | Separate execution search and independent input/detail authorization | Executions tab or return from execution detail | Find authorized evaluation attempts by ID or bounded filters. |
| RM11-EXECUTION | backoffice | Execution detail | /applications/{applicationId}/rule-engines/{rulesetId}/executions/{executionId} | Desktop web | Independent authorization for execution detail and retained facts | Exact execution result | Read actual supplied facts, rule trace and final selection. |
| RM11-CONFLICT | backoffice | Resolve save conflict | /applications/{applicationId}/rule-engines/{rulesetId}/reconcile | Desktop web | Current version read, permitted draft access and scoped editing | Save returned a stale edit revision | Keep the authorized unsaved draft and review the latest edit revision. |

## Screen definitions

### RM11-RULESETS: Rule engines

#### Description

The existing Applications rail stays selected. Capabilities opens a bounded list of registered rulesets under the selected application and environment. Readable names lead. Assignment and capability are independently observed. Technical details holds exact IDs.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM11-RULESETS-OPEN | Open ruleset | Authorized ruleset and schema reads | Open Residence eligibility | Load the scoped Grid with selected immutable version and independently current deployment. | /applications/{applicationId}/rule-engines/{rulesetId} | RM11-GRID | A denied or unavailable ruleset shows only the authorized recovery state. |
| RM11-RULESETS-ENV | Change environment | Authorized scope selection | Select another environment | Cancel old reads, warn on dirty state, and bind new exact scope. | /applications/{applicationId}/rule-engines | RM11-RULESETS | Unavailable environments never borrow data from another scope. |

#### States

- Loading criteria, no rulesets, unsupported service or schema, denied membership, and a dated stale response are distinct.

### RM11-GRID: Rules grid

#### Description

The ruleset header identifies application, environment and desired deployment. Grid, Versions, Deployment, Counters and Executions share that target. Each row is an AND across schema-ordered columns. The grid shows ordinal, state, four example inputs, weight, hit count/freshness and rule label. A clearly named row inspector shows comment and bounded metadata, with indicated keyboard and row actions. A selected condition opens the typed editor on the same page. The footer differentiates unsaved changes from saved version. Mode is a labeled native selector.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM11-GRID-CELL | Edit a condition | Draft editable and column enabled | Open the cell editor | Offer only compatible operators. A dedicated Any/Missing choice hides operator and value. Preserve exact decimal input and native Boolean. Use chips for membership. | /applications/{applicationId}/rule-engines/{rulesetId} | RM11-GRID | Focus and announce the invalid cell; a blank cell must be explicitly set. |
| RM11-GRID-ROWS | Add, copy, remove, move | Draft editable | Use Add rule, Move up/down, Copy or Remove | Reorder by ordinal. Copy allocates a new stable rule ID; deletion removes the row only from the pending version. Preserve keyboard order and inspect comment and metadata. | /applications/{applicationId}/rule-engines/{rulesetId} | RM11-GRID | Keep draft and original version on validation, cancellation, or server refusal. |
| RM11-GRID-MODE | Execution mode | Draft editable | Choose First match or All matching | Update draft mode and explain that weight contributes to All matching result order. | /applications/{applicationId}/rule-engines/{rulesetId} | RM11-GRID | Keep prior draft mode if selection is invalid. |
| RM11-GRID-SAVE | Save version | Fresh scoped edit grant and original-result read; valid complete draft | Save the whole new version | Send current edit revision and original key. Display new immutable version; deployment remains a later explicit action. | /applications/{applicationId}/rule-engines/{rulesetId}/versions | RM11-VERSIONS | Keep allowed draft for stale revision, server validation, or unknown result and show Check result. |
| RM11-GRID-VERSIONS | Versions | Scoped version list/read | Open Versions | Load bounded saved versions and selected immutable grid. | /applications/{applicationId}/rule-engines/{rulesetId}/versions | RM11-VERSIONS | Indicate unsupported, denied or unavailable without merging another scope. |
| RM11-GRID-DEPLOYMENT | Deployment | Scoped assignment/instance reads | Open Deployment | Show requested version and separately observed application states. | /applications/{applicationId}/rule-engines/{rulesetId}/deployment | RM11-ADOPTION | Retain a dated permissible snapshot when stale. |
| RM11-GRID-COUNTERS | Counters | Scoped counter read | Open Counters | Show operational counts independently of draft and selected version. | /applications/{applicationId}/rule-engines/{rulesetId}/counters | RM11-COUNTERS | Distinguish unavailable from zero. |
| RM11-GRID-HISTORY | Executions | Scoped execution search | Open Executions | Load authorized bounded history. | /applications/{applicationId}/rule-engines/{rulesetId}/executions | RM11-EXECUTIONS | Do not leak protected execution rows or counts on denial. |

#### States

- Unsaved valid grid; cell focus and typed Boolean, decimal, list, Any and Missing; failed validation focuses the first invalid cell; disabled reads/editing while loading or unavailable; empty grid; read-only saved version; dirty navigation prompt; session loss clears protected draft.

### RM11-VERSIONS: Saved versions

#### Description

A list and the exact selected read-only snapshot share one page. Version notes and dates lead. A deployed or requested version is labeled independently of selection. Compare with current uniform deployment when its exact values are authorized; show unavailable if current assignment is mixed, absent or stale.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM11-VERSIONS-SELECT | Select saved version | Exact version view authorized | Select a retained version | Show its immutable schema, rules, mode, comment and weight. | /applications/{applicationId}/rule-engines/{rulesetId}/versions | RM11-VERSIONS | Keep last authorized selection during transient failure and date it. |
| RM11-VERSIONS-EDIT | Edit as new version | Exact read and current edit grant | Edit a copy of selected version | Bind source version and latest edit precondition without mutating the source. | /applications/{applicationId}/rule-engines/{rulesetId} | RM11-GRID | Keep changes only within current authorized scope. |
| RM11-VERSIONS-DEPLOY | Deploy | Saved version and independently authorized target/assignment | Review deployment | Open explicit confirmation with current/selected versions and target. | /applications/{applicationId}/rule-engines/{rulesetId}/deploy/{versionId} | RM11-DEPLOY | Disable while preconditions are stale or target is unresolved. |

#### States

- History is immutable. First version, no selected version, no deployment, loading, separate denied reads/actions and unavailable deployed comparison are distinct.

### RM11-DEPLOY: Confirm deployment

#### Description

One confirmation states the application, environment and ruleset, selected and currently requested versions, and the exact target. Show existing targeting before replacing it. For an older version, use the same Deploy operation. Instance adoption is observed separately.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM11-DEPLOY-CONFIRM | Deploy version | Current grant and guarded assignment revision match | Confirm once | Send the saved version with exact target and immutable original operation intent. Open actual rollout. | /applications/{applicationId}/rule-engines/{rulesetId}/deployment | RM11-ADOPTION | A conflicting assignment prompts review again. Unknown results stay tied to the original key. |
| RM11-DEPLOY-CANCEL | Cancel | Confirmation is open | Dismiss without mutation | Restore focus to original Deploy control. | /applications/{applicationId}/rule-engines/{rulesetId}/versions | RM11-VERSIONS | A submitted result remains consultable through original-result lookup. |

#### States

- Pending request, stale precondition, known rejection, unknown result and deployment confirmed while adoption is still pending are distinct.

### RM11-ADOPTION: Deployment and adoption

#### Description

Show latest requested version and timestamp. The table shows displayed instance, current desired version, last accepted version, rollout state, latest observation and exact reason via detail. Use text labels for Applied, Pending, Rejected, Offline and Restart required where supported. Convergence follows policy and requires actual current recipients.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM11-ADOPTION-REFRESH | Refresh status | Scope readable | Refresh current observations | Apply only newer observations to this scope; keep current desired and last accepted separate. | /applications/{applicationId}/rule-engines/{rulesetId}/deployment | RM11-ADOPTION | Show stale timestamp and fetch error; absent recipients are not converged. |
| RM11-ADOPTION-RESULT | Check result | Current authority for original operation | Look up the original deployment receipt | Obtain actual committed/rejected/unknown outcome separately from rollout. | /applications/{applicationId}/rule-engines/{rulesetId}/deployment | RM11-ADOPTION | Unknown/NotFound does not imply rollback or enable a replacement request. |
| RM11-ADOPTION-BACK | Saved versions | Version view authorized | Open Versions | Choose a different immutable version if needed. | /applications/{applicationId}/rule-engines/{rulesetId}/versions | RM11-VERSIONS | Display access error for the actual resource. |

#### States

- Mixed applied/pending/rejected, stale or offline report, no recipients, known outcome with cleanup issue and no desired version.

### RM11-COUNTERS: Hit counters

#### Description

Show hits by RuleId with readable rule labels, state, last reset time and updated freshness. Table selection and the action menu support one row, selected rows, or all counters. Confirmation states exact application/environment/ruleset and selected or entire retained scope. Whole-scope reset includes retained identities and remains independent of the deployed version.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM11-COUNTERS-RESET | Reset selected, a single rule or all | Fresh scoped reset grant and original-result access | Confirm visible target and scope | Submit once with original key; show each actual reset outcome and new observation time. | /applications/{applicationId}/rule-engines/{rulesetId}/counters | RM11-COUNTERS | Keep unresolved original key and expose partial outcomes, delayed data, or denied reset distinctly. |
| RM11-COUNTERS-RECHECK | Refresh counters or check reset result | Current applicable scope and original-result access | Refresh selected scope | Keep count/freshness separate from reset result. | /applications/{applicationId}/rule-engines/{rulesetId}/counters | RM11-COUNTERS | Unavailable count remains unavailable; keep dated authorized values when stale. |

#### States

- Selection, reset confirmation, partial or unknown original result, retained identity, stale/nonzero and unavailable/blank distinct from the actual value zero.

### RM11-EXECUTIONS: Executions

#### Description

An exact Execution ID lookup shares a page with version, instance, date/time and outcome filters, cursor paging, and recent scoped rows. State and returned match count are independent. Failed, Cancelled, incomplete and confirmed interrupted attempts show no usable result count. Completed with zero is a real successful outcome.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM11-EXECUTIONS-FILTER | Find or filter executions | Scoped execution search is authorized | Use filters or paste exact Execution ID | Reset cursor on changed filters and show only authorized current page. | /applications/{applicationId}/rule-engines/{rulesetId}/executions | RM11-EXECUTIONS | Loading, no matches, delayed reporting and access errors remain distinct. |
| RM11-EXECUTIONS-DETAIL | Open execution | Scoped execution result and detail read | Open exact execution with retained version | Load separately authorized audit facts and trace. | /applications/{applicationId}/rule-engines/{rulesetId}/executions/{executionId} | RM11-EXECUTION | Withhold protected fact values and trace if denied. |

#### States

- Completed with and without matches, failed, cancelled, incomplete and interrupted, zero filtered records, stale/unavailable store and protected lookup denial.

### RM11-EXECUTION: Execution detail

#### Description

Header names immutable version, instance, start/finish and terminal status. Separate supplied typed values with omitted, supplied null and unknown keys. Trace lists observed rules by ordinal, reached cells and explicit stop reasons. Selected rows separately list final return order with weights and metadata. Technical details includes execution/correlation IDs, build and engine version.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM11-EXECUTION-TRACE | Expand observed rule | Relevant trace is retained and permitted | Open a row | Show actual compared values and results, including NotEvaluated, NotReached and Test diagnostic states. | /applications/{applicationId}/rule-engines/{rulesetId}/executions/{executionId} | RM11-EXECUTION | If evidence is delayed, show incomplete without inferring completion or interruption. |
| RM11-EXECUTION-BACK | Back to executions | Scoped search remains allowed | Return to prior query | Restore authorized filters, cursor, scroll and focus. | /applications/{applicationId}/rule-engines/{rulesetId}/executions | RM11-EXECUTIONS | Reauthorize current scope, and discard protected data after session changes. |

#### States

- Finished with exact selected order; no usable matches after failure/cancellation; Completed with empty results; incomplete without end evidence; confirmed interrupted when corroborated; exact presence and metadata.

### RM11-CONFLICT: Resolve save conflict

#### Description

The side-by-side comparison shows changed cells from the authorized retained draft and the current saved version. A fresh edit precondition is adopted only after deliberate review. Unknown operation outcomes require Check result first.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM11-CONFLICT-COMPARE | Review current version | Current scope can read both versions and original result is settled | Compare and choose retained changes | Return to the grid with explicitly reviewed intent against current edit revision. | /applications/{applicationId}/rule-engines/{rulesetId} | RM11-GRID | Keep in-memory draft if routes or grants allow. Session change clears it. |
| RM11-CONFLICT-KEEP | Keep draft | Same-session scope is authorized | Close comparison | Resume draft without automatically retrying prior Save. | /applications/{applicationId}/rule-engines/{rulesetId} | RM11-GRID | Show unreadable or unavailable current values without disclosing them. |

#### States

- Conflict, current-version unavailable, and unresolved original operation remain separately actionable.

## Journey and requirement coverage

| Requirement/exclusion | Screen IDs | Action IDs | States | Notes |
| --- | --- | --- | --- | --- |
| RM11-AC01, AC02, AC08–AC14 | RM11-RULESETS, RM11-GRID | RM11-RULESETS-OPEN, RM11-GRID-CELL, RM11-GRID-ROWS, RM11-GRID-MODE | Scoped entry, typed/marked cells, native Boolean, exact decimal, validation | Rows remain complete and the selected app/environment/schema is visible. |
| RM11-AC16, AC17, AC30 | RM11-GRID, RM11-VERSIONS, RM11-CONFLICT | RM11-GRID-SAVE, RM11-VERSIONS-EDIT, RM11-CONFLICT-COMPARE | Immutable history, edit older as new, stale edit retained | Edit revision and operation identity are independent of deployment. |
| RM11-AC18, AC19 | RM11-VERSIONS, RM11-DEPLOY, RM11-ADOPTION | RM11-VERSIONS-DEPLOY, RM11-DEPLOY-CONFIRM, RM11-ADOPTION-REFRESH, RM11-ADOPTION-RESULT | Rollback, pending, stale, rejection, desired/applied | Explicit Deploy of a known saved version, and separate actual observations. |
| RM11-AC21–AC23 | RM11-COUNTERS | RM11-COUNTERS-RESET, RM11-COUNTERS-RECHECK | Single, selected and full scope, partial reset, stale/unavailable | Reset is a separate audited operation and each result is explicit. |
| RM11-AC24–AC29 | RM11-EXECUTIONS, RM11-EXECUTION | RM11-EXECUTIONS-FILTER, RM11-EXECUTIONS-DETAIL, RM11-EXECUTION-TRACE, RM11-EXECUTION-BACK | Authorized trace, exact values/presence, error, incomplete and result order | Browser evidence is collected in portal implementation. Screens show intended UX. |

## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Rationale |
| --- | --- | --- | --- | --- |
| Approved | Andrew Spiteri | 2026-11-27T21:13:44Z | `sha256:3ced61e739ff3e5dc9d0b9b522e2fb03757853aab62b15630b0aaf1540d0bcfc` | Andrew Spiteri selected Approve this exact screen package in response to the CIS-0031 review request for all 13 RM11 previews. The recorded manifest is sha256:d76b1fd3b0bb2ccd785e91d4dd2005fa2d0f255a05df269adc189d846692d1c0. |
