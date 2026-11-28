---
title: RM11 Rule Engine feature
type: feature-specification
status: Proposed
feature_id: RM11
targets: [cislib]
stack: [CSharp, TypeScript, Next.js]
references:
  - planning/rule-engine/sources/CISLIB_RM11_Rule_Engine_Specification.md
---

# RM11 Rule Engine feature

The supplied v1.0 specification is the complete requirement source. The table transcribes its acceptance rows for CIS, with a proposed story owner and backoffice classification where the criterion includes a browser surface. The implementation plan maps the supplementary producer, consumer and evidence owners.

## Requirements

| ID | Story ID | Frontend type | Requirement | Acceptance criteria |
| --- | --- | --- | --- | --- |
| RM11-AC01 | RM11-R03 | backoffice | Schema exposure and restart | Application registration exposes the declared column names, datatypes and constraints in the editor; a restart preserves saved rules and deployment selection. |
| RM11-AC02 | RM11-R02 | not-applicable | AND and cell trace | A row with one false condition does not match. All true conditions produce exactly one hit. Audit marks short-circuited cells as NotEvaluated. |
| RM11-AC03 | RM11-R02 | not-applicable | Ordinal ordering | Storage/display order differs from ordinal order. Evaluation follows ascending ordinal; duplicate ordinals are rejected on save. |
| RM11-AC04 | RM11-R02 | not-applicable | FirstMatch Test hit | A matching Test row precedes a matching Enabled row in FirstMatch. Both receive hits, only the Enabled row is returned and evaluation stops there. |
| RM11-AC05 | RM11-R02 | not-applicable | FirstMatch stopping boundary | Test and Enabled rows follow a FirstMatch stopping row. Neither is evaluated or receives hits; both are NotReached in audit. |
| RM11-AC06 | RM11-R02 | not-applicable | Disabled row | Disabled rows contain otherwise matching or invalid supplied-value comparisons. They are skipped without hits or comparison errors. |
| RM11-AC07 | RM11-R02 | not-applicable | AllMatching order | AllMatching includes Enabled and Test matches with differing weights. Audit preserves ordinal evaluation; only Enabled rows return in descending weight order, with ordinal tie-breaking. |
| RM11-AC08 | RM11-R02 | not-applicable | Native Boolean | Native Boolean false matches Equals false; string "False" is not silently converted. Missing Boolean never becomes false. |
| RM11-AC09 | RM11-R02 | not-applicable | Missing and null | Absent and null inputs are used with Equals, NotEquals and IsNotIn. All ordinary comparisons produce no match. ANY matches them; MISSING matches only missing state. |
| RM11-AC10 | RM11-R02 | not-applicable | Present boundary values and markers | False, zero and an empty string are supplied. They remain present values. Literal "ANY"/"MISSING" strings remain distinct from marker presets. |
| RM11-AC11 | RM11-R02 | not-applicable | Scalar strings and wildcard | Exercise string equality/case, wildcard *, wildcard ?, escaping and whole-string matching. Results follow section 4; malformed patterns fail save validation. |
| RM11-AC12 | RM11-R11 | backoffice | Lossless decimals | Exercise all six decimal comparisons with values requiring exact precision. Portal, transport, execution and audit preserve the values without binary floating-point rounding. |
| RM11-AC13 | RM11-R02 | not-applicable | Literal string membership | Exercise IsIn/IsNotIn with matches, misses, duplicate and empty lists. Membership direction, missing behaviour and one-hit-per-row semantics are preserved. |
| RM11-AC14 | RM11-R02 | not-applicable | Reached invalid input | A reached ordinary condition receives an invalid value. An Enabled row aborts; a Test row records an error and continues. ANY and unexecuted rows are not defeated by eager global comparison validation. |
| RM11-AC15 | RM11-R02 | not-applicable | Completed result shape | Empty ruleset, no Enabled matches and only Test matches each produce a Completed empty result. Returned matching rows preserve metadata and applicable weights. |
| RM11-AC16 | RM11-R03 | not-applicable | Immutable save | Save changed conditions, state, order, metadata, weight or comment. A new immutable version is created and previous versions remain unchanged. |
| RM11-AC17 | RM11-R04 | not-applicable | Explicit deployment | Save without Deploy. The application keeps its deployed version; a raw request for the saved-only version cannot bypass deployment. |
| RM11-AC18 | RM11-R05 | backoffice | Captured version and adoption | Deploy while an invocation is running. That invocation keeps its captured version; later eligible invocations use the adopted version. A rejection or lag is visible per instance. |
| RM11-AC19 | RM11-R04 | not-applicable | Rollback and exact coverage | Deploy an older saved version. It becomes the selected definition through the normal deployment path; an unavailable/unsupported exact version produces an explicit error. |
| RM11-AC20 | RM11-R05 | not-applicable | Concurrent input isolation | Simultaneous invocations on multiple instances keep inputs, captured versions, results and execution identities isolated. Mutation of caller-owned inputs after capture does not alter evaluation/audit. |
| RM11-AC21 | RM11-R08 | not-applicable | Counter aggregation and idempotency | Counter contributions aggregate for the same rule scope, including Test hits. Save, deploy, rollback and state changes do not automatically reset them. Delivery retries do not double-count a hit. |
| RM11-AC22 | RM11-R08 | backoffice | Counter reset ordering | Reset an individual, selected and all counters through the UI. No rule version or deployment is created. Late pre-reset contributions do not restore cleared totals; later contributions count normally. |
| RM11-AC23 | RM11-R08 | backoffice | Counter degradation | Counter storage is delayed or unavailable. Successful evaluation still follows its audit requirement; the UI reports counter freshness/unavailability rather than treating missing statistics as zero. |
| RM11-AC24 | RM11-R07 | backoffice | Complete execution audit | Full execution audit exposes actual typed inputs, absent/null distinction, immutable rule definition, reached/non-reached rows, Test hits and returned result order. |
| RM11-AC25 | RM11-R06 | not-applicable | Failure and cancellation | An Enabled error or cancellation occurs after earlier matches. No successful partial business result is returned; accepted audit retains progress and prior hits. Late cancellation follows the terminal-status boundary, and invocation cancellation is never swallowed as a Test error. |
| RM11-AC26 | RM11-R06 | not-applicable | Mandatory durable audit | Audit start cannot be durably accepted. No rule is evaluated and AuditUnavailable is returned. Completion audit failure also prevents successful result release. |
| RM11-AC27 | RM11-R07 | not-applicable | Durable delivery and deduplication | Audit completion is durably queued while the reporting store is unavailable. Execution can complete; eventual delivery preserves the same execution identity without duplicate logical records. |
| RM11-AC28 | RM11-R07 | not-applicable | Interrupted execution | Process interruption occurs after a durable start. The attempt remains identifiable as incomplete/interrupted; no terminal result or unrecorded comparison is fabricated. |
| RM11-AC29 | RM11-R12 | backoffice | Browser operator journeys | Browser journeys edit typed cells and markers, save, inspect history, Deploy, observe adoption, reset counters and inspect an execution audit. Required cases have actual executed results. |
| RM11-AC30 | RM11-R03 | not-applicable | Concurrent editor conflict | Two operators save against the same starting revision. The existing concurrency mechanism prevents silent overwrite and preserves both operator intent and immutable version history. |

## UX scope

The backoffice scope uses the existing Management portal for typed grid editing, saved versions, deployment and per-instance adoption, independent counter operations, and authorized execution lookup and details. The new interaction surfaces use the existing CIS wireframe and rendered review.

## Integration and verification scope

Use .NET 10, the existing CIS.Core and Managed Configuration composition, the existing Management authority/provider, RM06 Audit over durable Messaging, and typed C# and TypeScript Management clients. Use the existing native provider, consumer and browser fixtures. The CountryData example demonstrates the two execution modes, explicit deployment and audit correlation.

## Boundaries

Applications own facts, metadata meaning and business actions. The rule engine evaluates only deployed and locally supported versions. The first release excludes scripting, recurrence and general audit archiving.
