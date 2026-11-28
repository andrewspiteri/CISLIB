---
title: RM11 portable Rule Engine contract v1
type: contract
status: Draft
change_id: CIS-0031
---

# RM11 portable Rule Engine contract v1

This contract freezes the design handoff for the embedded evaluator and Management clients. The retained RM11 specification defines operator semantics, failure policy and acceptance. The operation names below identify behaviors for the screen, service and adapters. Management's companion route and permission identifiers are assigned with its implementation.

## Identity and values

- Application and environment come from the trusted host or resolved Management resource. Instances, builds and engine contract versions identify observed execution or adoption. Registration reports participation without replacing a trusted schema, saved definition or assigned release.
- Ruleset, schema revision, version and RuleId are opaque stable IDs. IDs use nonempty ordinal strings at most 128 UTF-8 bytes and exclude control scalars. A version contains its complete ordered schema or an immutable schema revision and complete rule rows. RuleId survives edits, reordering and deployment. Copy creates a new RuleId. An ordinal is a unique nonnegative Int32 in a version; rule order is ascending ordinal.
- Portable values are tagged objects with a distinguished decimal kind. A decimal wire value is invariant text with a minus sign when negative, a base-ten integer (zero or a nonzero leading digit), and optional decimal fraction. Exponents, plus signs, grouping, and whitespace are invalid. Trim trailing fractional zeroes for representability checks and compare normalized numeric values. The remaining coefficient must fit 96 bits and scale 0–28, with no parse rounding. Weight uses the same scalar and defaults to zero. Transport, portal input and audit never coerce this text to a JavaScript number. Boolean transport uses native true or false.
- Input entries list only supplied keys. Supplied null is an explicit null kind. Omitted keys have no entry. The captured input snapshot binds exact unknown keys and their supplied type and value too. JSON values outside the engine's ordinary types are opaque preserved input and fail only when a reached comparison actually requires a conflicting type. A string list is a membership preset for a scalar string input. Metadata is bounded opaque JSON; validate its declared shape if present and preserve its value and numeric lexemes across a read, untouched edit and result.
- JSON is strict UTF-8 with unique decoded member names and valid Unicode scalar strings. Case-sensitive ordinal text and literal membership have no implicit normalization. A wildcard compares the entire input using scalar units; asterisk matches zero or more, question mark matches one, and backslash escapes only those two characters or itself. Reject invalid escape syntax on save. Runtime work is bounded by the documented string/pattern limits and an explicit operation budget.

## Evaluator and auditable result

- Validate the complete ordered schema and one explicit typed cell per column on save and adoption. Each condition is ordinary, Any or Missing. Any always matches. Missing matches omitted or supplied null. Ordinary comparison against either missing state is false. Ordinary type/constraint validation occurs only when a present input reaches that cell. An empty editor cell is invalid. Evaluate columns in declared order, stop the row on the first nonmatch or error, and retain NotEvaluated for later cells.
- An invocation captures one accepted deployed definition and one detached input snapshot. Enabled matches enter the selection, Test matches count without selection, and Disabled rows skip. An Enabled error ends evaluation, and a Test error remains visible as a diagnostic while eligible work continues. FirstMatch stops on the first matching Enabled row. AllMatching evaluates in ordinal order and sorts returned matches by descending weight and ascending ordinal. A row contributes one observed hit regardless of later terminal outcome. Cancelled or failed invocations withhold usable selections. Completed with zero matches is a successful result.
- Use distinct immutable start and completion events keyed by ExecutionId and phase. Start binds application, environment, instance, build, engine version, exact ruleset version, correlation and captured facts. Completion binds terminal status, observed rows, reached cells and selected order when completed. Include start/finish time and explicit error codes. The compact trace refers to the already bound version by ordered row/cell indices and can recover conditions through the exact definition. Its bounded error codes and observed outcomes carry the facts; input values stay in the start event and definitions stay immutable by reference.
- Confirm durable RM06 acceptance before comparison and before releasing a completed result. Preserve Unknown and NotSubmitted outcomes. Finalization after cancellation is bounded and can retain previously observed hits when durable evidence survives. The receiver deduplicates later delivery by authenticated source and logical event identity, retaining equivalent replays and rejecting a conflicting event. Start without completion is incomplete; an interrupted label needs corroborating evidence. Reporting storage availability is independent of the evaluator's required audit acceptance.

## Management actions and ownership

| Intent | Request and precondition | Observable result |
| --- | --- | --- |
| ListRulesets, GetSchema, ListVersions, GetVersion | Resolve application, environment, ruleset and immutable schema/version. Use bounded query-scoped cursors and applicable read grants. | Trusted schema and complete saved version. Unavailable, denied and empty remain distinct. |
| SaveVersion | Validated complete definition and an advancing edit revision, plus original operation key. Changes based on an older immutable version still require the latest edit precondition. | One immutable saved version and updated edit revision. The desired assignment and every actual instance report remain separate. Stale revision keeps the editable draft available to the authorized operator. |
| RequestDeployment | Select an existing saved version; guarded RM08 assignment-map ETag, target read, current grant and original operation key. The same operation handles rollback. | Desired assignment captured with original transaction and same-scope ever-deployed evidence. Actual install/acceptance, lag, rejection and report freshness remain separate. |
| GetDeploymentStatus | Read the desired assignment and separately authorized, latest factual instance reports for the exact scope. | Operational state per recipient and aggregate convergence only when the policy is satisfied. An invocation already running keeps its captured version. |
| GetCounters, ResetCounters | Read or reset explicit RuleIds or the entire ruleset scope under its own grant and original operation key. | Count, last reset/freshness and per-counter reset outcomes. Reset covers retained identities when full scope is selected and does not mutate a definition or assignment. |
| FindExecutions, GetExecution | Scoped ExecutionId lookup or bounded ruleset/version/instance/time/status query; independently authorize retained input and associated evidence detail. | Durable observed rows and results, app build/version, exact presence and values. Missing result, incomplete delivery and confirmed interruption retain their different meanings. |
| CheckOperation | Original actor, scope, operation identity/key and current read permission. | Committed, rejected or unknown original outcome and separately observed recovery problems. Never retry an unknown mutation by generating a new key. |

Saved, desired and applied versions retain different owners. Exact local version selection requires a retained definition, same-scope authoritative deployment history and compatible engine/local support. A failed selection never falls back. Protect definition references through assignment, durable audit handoff, retained executions and existing quotas. New attempt admission follows RM07 and an already admitted attempt can settle under its captured version and available audit authority.

The counter projection serializes first durable hit acceptance and reset for each application/environment/ruleset/RuleId. It deduplicates by execution and rule through the replay horizon, including pre-reset redelivery. Counter writes and reset/read availability cannot veto an otherwise valid result release.

## Initial envelope profile

| Limit | Initial ceiling |
| --- | ---: |
| Schema columns per version | 24 |
| Rules per version | 48 |
| Items in a membership preset or supplied list | 32 |
| Ordinary string or wildcard pattern | 512 UTF-8 bytes |
| Opaque metadata per row, inside the definition ceiling | 4 KiB |
| Entire serialized definition / input snapshot | 128 KiB / 24 KiB |
| Entire encoded audit record | 128 KiB after envelope serialization |
| JSON depth including the applicable transport envelope | 16 |

Check actual selected Management, Configuration, Audit and Messaging limits as well as these defaults. Smaller selected provider limits still apply. Preflight the bounded input and maximum trace for the captured version before capturing the start. Reject unsupported or oversize input without silently truncating facts. Bind compact trace position to the retained ordered definition and bounded code values. Preserve exact content or surface a technical failure if finalization cannot be accepted.

The existing Management HTTP profile caps the entire command at 1 MiB and decoded Configuration source JSON at 256 KiB. Audit and Messaging default to 256 KiB, with selected limits allowed to be smaller. The portable examples and representative profile check describe the wire design. Provider and runtime proof belongs to the affected implementation owners.

- [RM11 specification](../../../planning/rule-engine/sources/CISLIB_RM11_Rule_Engine_Specification.md)
- [Configuration contract](../../../docs/modules/management/configuration-contract.md)
- [Audit contract and bounds](../../../docs/modules/audit/README.md)
- [Messaging limits](../../messaging/README.md)
