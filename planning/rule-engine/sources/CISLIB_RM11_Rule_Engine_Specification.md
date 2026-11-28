# CISLIB Rule Engine Specification

RM11 Embedded evaluation and managed deployment

Specification version 1.0 | 27 November 2026

## 1 Purpose and scope

Implement a table-based rule engine in CISLIB. Applications declare the available columns and supply values for evaluation. Operators manage rule grids centrally, save immutable versions and explicitly deploy a selected version. The engine evaluates the deployed definition inside each application and returns the matching rule rows, their metadata and applicable weights.

This specification defines the common runtime contract, configuration lifecycle, management UI, counter extension and execution audit. Business applications interpret returned metadata and perform any resulting business action through their own services and transactions.

The initial implementation targets the existing .NET 10 platform and introduces no external runtime NuGet dependencies. Reuse the existing Configuration, Central Management, Audit and management portal facilities. Public contracts must use explicit, portable values and stable identifiers so another language can implement the same behaviour later.

The first release covers strings, decimals, Booleans and string-list presets, the two agreed execution modes, and the three rule states. Arbitrary scripts, application code uploaded through the portal, inference, rule-triggered business writes and scheduling are outside this module's scope.

| Concern | Required behaviour |
| --- | --- |
| Rule model | One grid row is one rule; condition columns combine with AND. |
| Evaluation order | Ascending ordinal in both execution modes. |
| First match | Stop only at a matching Enabled rule. |
| All matching | Return matching Enabled rows in descending weight order. |
| Version control | Save creates a version; Deploy selects a saved version for a target. |
| Counters | Informative extension data, independently resettable through the UI. |
| Audit | Capture actual supplied values, version, execution progress and results. |

## 2 Ownership and integration

Applications own their ruleset registrations, column definitions, input facts and metadata meaning. They supply values to the local evaluator and decide what to do with the returned rows. The engine does not fetch customer or ledger facts itself.

CISLIB owns validation, comparisons, evaluation ordering, state handling, result selection, version capture and the common audit and counter contracts. Execution must be available through a mockable application-facing interface.

Central Management owns saved definitions, deployment requests, deployment visibility and the management UI. Capability registration describes what an application supports; an application restart or registration refresh must preserve operator-maintained definitions and deployment selections.

Use RM08 configuration publication and distribution for delivery and application adoption, RM07 for application identity and management availability behaviour, RM06 for durable audit delivery, and RM09 for portal integration. These are integration requirements; this specification does not introduce another broker, database provider or independent control service.

Ruleset management and execution must use the authenticated application and environment scope supplied by the host. Reuse management permission enforcement for viewing, editing, deployment, counter resets and audit inspection. Permission names and role administration belong to the existing permission facilities.

## 3 Ruleset and row definitions

A ruleset has a stable identifier, application scope, display name, registered column schema and execution mode. Each saved version contains a complete, immutable definition, including the schema reference and all configured rows. The schema reference identifies an immutable revision, or the version embeds the schema snapshot, including column order, types, permitted presets and constraints. A later registration update cannot change those semantics for an earlier version. Schema changes that affect evaluation belong to a new saved version.

Each application condition column has a stable identifier, display name, expected input type, compatible preset types and any application-declared constraints. For example, ResidenceCountry may require a three-character country string, while Pensioner expects a native Boolean. Identifiers are unique within the schema and remain stable when display labels change.

A condition cell identifies its column, preset kind, operator and typed preset. The column's schema supplies the common name and type information; cells must still be self-describing through that schema. Each row has one explicit cell for every declared condition column. An incomplete editor cell is a validation error; it must never silently become ANY.

| Row field | Definition |
| --- | --- |
| RuleId | Stable internal identity, independent of ordinal and version. |
| Ordinal | Mandatory integer controlling ascending evaluation order. |
| State | Enabled, Test or Disabled. |
| Conditions | One configured condition for each application column. |
| Weight | Numeric value used to order matches in AllMatching mode. |
| Metadata | Application-owned data returned with the matching row. |
| Comment | Operator description stored as part of the version. |
| Hit counter | Joined from the counter extension; excluded from the immutable definition. |

Ordinal, state, weight, metadata, comment and hit count are not condition columns. All application-defined condition cells combine with AND. A hit occurs once when an evaluated row matches all its conditions, regardless of the number of matching cells or duplicate items in a list preset.

Deleting a row removes it from the newly saved version. Previous definitions and audit references retain that row. Rule identifiers must not be reassigned to unrelated rules. Copying a row creates a new rule identifier.

## 4 Datatypes and comparison operators

The table below defines the supported ordinary comparisons. ANY and MISSING are explicit special presets handled before ordinary comparisons.

| Cell value type | Operators | Operand behaviour |
| --- | --- | --- |
| String | Equals, NotEquals, WildcardMatch | Compare one supplied string with a configured string or wildcard pattern. |
| Decimal | Equals, NotEquals, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual | Compare numerical decimal values exactly. |
| Boolean | Equals, NotEquals | Compare native true or false values. |
| List of strings | IsIn, IsNotIn | Test one supplied string against a configured list of strings. |

String-list membership describes the preset side of the comparison. A string input column may allow scalar string presets for equality or wildcard matching and string-list presets for membership. The schema must express this compatibility. This release does not assign list-to-list or list-valued input matching semantics.

For example, ResidenceCountry IsIn ["MLT", "ITA"] matches a supplied string of "MLT". IsNotIn returns the opposite membership result only when a valid, present string is available. Duplicate list entries do not change matching. Membership uses literal string entries; wildcard interpretation belongs to WildcardMatch.

Decimal values must retain exact decimal meaning through the runtime, storage, portal and audit. The .NET adapter uses decimal semantics. Transport and UI editing must use a lossless representation, such as decimal text where a JSON number could lose precision. Values outside the supported decimal range are rejected explicitly. The engine performs comparisons without business rounding or an epsilon tolerance.

Booleans remain native values. The string "False" is not the Boolean false, and a missing Boolean must never be replaced with false. No implicit conversion of supplied strings into decimals or Booleans occurs during comparison.

String comparisons and list membership use case-sensitive, culture-independent matching by default. Inputs are not automatically trimmed or case-folded. Applications may normalize values before supplying them; the audit records the exact normalized values actually evaluated.

WildcardMatch applies to the entire supplied string. An asterisk (*) matches zero or more Unicode scalar characters, and a question mark (?) matches exactly one. A backslash escapes an asterisk, question mark or backslash. Invalid escape sequences and a trailing escape are rejected when saving. A literal asterisk used with Equals remains an ordinary character. The matcher must have bounded resource usage and must not execute arbitrary code.

## 5 ANY and MISSING

ANY makes its condition cell unconditionally true, including when the application omits the value. MISSING matches the missing input state. These are dedicated preset markers, distinct from ordinary string literals such as "ANY" or "MISSING". Selecting either marker makes the ordinary operator and value editor inapplicable.

For this specification, both an absent value and an explicitly supplied null count as missing for matching. The audit preserves whether the application omitted the field or supplied null. False, zero, an empty string and an empty list remain distinct supplied values; they are not automatically converted to missing.

| Condition kind | Valid present input | Absent or null input |
| --- | --- | --- |
| Ordinary comparison | Apply its declared operator. | No match, including NotEquals and IsNotIn. |
| ANY | Match. | Match. |
| MISSING | No match. | Match. |

The evaluator checks special markers and missing status before ordinary type and constraint checks. ANY therefore imposes no requirement to supply or validate that column for this cell. MISSING tests absence rather than validating a present value. If an ordinary comparison needs a present value of the wrong type or outside its declared constraints, that cell produces an evaluation error.

Validation of supplied values must respect actual execution. An unused value must not cause an error in a Disabled row or a row never reached. A Test-only comparison error must retain the agreed Test error behaviour.

## 6 States and execution modes

Rows are evaluated in ascending ordinal order in both modes. Within a reached row, conditions are evaluated in the schema's declared column order. AND short-circuits on the first non-match; cells after that point are marked NotEvaluated. A cell error stops evaluation of that row. Its state determines whether execution continues.

| State | If the row matches | If the row does not match | If row evaluation fails |
| --- | --- | --- | --- |
| Enabled | Record one hit and make the row eligible for return. | Continue without a hit. | Abort the invocation with a technical failure. |
| Test | Record one hit, exclude the row from results and continue. | Continue without a hit. | Audit the error and continue with the next row. |
| Disabled | Skip evaluation and record no hit. | Skip evaluation. | No cell evaluation is attempted. |

### 6 1 FirstMatch

Skip Disabled rows. Evaluate each reached Enabled or Test row. A matching Test row records a hit and execution continues. A matching Enabled row records a hit, becomes the sole returned row and stops rule evaluation immediately. Weight has no effect on that selection.

All rows after the stopping row are NotReached, including Test rows. They receive no hits. If the grid contains only Test matches or no matching Enabled row, evaluation completes with an empty result.

### 6 2 AllMatching

Evaluate every Enabled or Test row in ordinal order unless cancellation or an Enabled error stops execution. Collect matching Enabled rows. Matching Test rows record hits but do not enter the returned collection.

After successful evaluation, sort the returned rows by descending weight. Equal weights use ascending ordinal as a deterministic tie-breaker. Evaluation order and returned order must remain separately identifiable in the audit.

An Enabled error makes the invocation fail; previously collected rows must not be released as a successful partial result. Observed rule matches remain audit facts and retain their counter contributions.

## 7 Worked ruleset

The following version uses the application columns ResidenceCountry and Pensioner. The country is a string; Pensioner is a native Boolean. Metadata labels are illustrative application values.

| Ordinal | State | ResidenceCountry | Pensioner | Weight | Metadata |
| --- | --- | --- | --- | --- | --- |
| 10 | Test | Equals MLT | Equals false | 100 | Trial |
| 20 | Enabled | Equals MLT | Equals false | 20 | Local |
| 30 | Enabled | ANY | ANY | 80 | Fallback |
| 40 | Disabled | ANY | ANY | 200 | Inactive |

With ResidenceCountry = "MLT" and Pensioner = false, FirstMatch records hits for rows 10 and 20, returns row 20 and does not reach rows 30 or 40. AllMatching records hits for rows 10, 20 and 30, skips row 40 and returns rows 30 then 20 because their weights are 80 and 20.

The originally discussed conditions remain valid: ResidenceCountry Equals MLT AND Pensioner Equals false; and ResidenceCountry NotEquals MLT AND Pensioner ANY. The second condition accepts any pensioner value or its absence, but an absent residence country does not satisfy NotEquals MLT.

## 8 Save Deploy and version retention

The management lifecycle is Edit, Save, select a saved version, then Deploy. A save containing changed configuration creates a new immutable version. Changes to state, ordering, conditions, weight, metadata, comment, execution mode or the associated schema create a version. Merely opening or resaving an unchanged definition need not create one.

Saving does not activate a version. The user must explicitly Deploy a selected saved version to the intended application and environment scope through the existing configuration deployment facilities. A saved but undeployed revision must not become executable through an unrestricted version-selection parameter.

An application validates a deployment against its registered schema and supported execution semantics, prepares the complete version, and adopts it atomically. A single invocation captures one accepted deployed version and uses it throughout. Deployment during evaluation affects subsequent version selection, not the invocation already running.

Multiple application instances may adopt a deployment at different times. The portal must show the requested target version, each instance's applied version and any adoption rejection. Report a completed rollout only when the existing deployment policy's target conditions have been satisfied; do not imply that selecting Deploy changes every instance instantaneously.

A normal invocation uses the instance's accepted deployed version. If a consumer requires an exact version, the runtime may evaluate a retained version only when deployment history records that it was deployed to that same application and environment scope. Selecting a newer active version does not remove this eligibility from a retained, supported older version. The request must fail explicitly when that reference is unavailable or unsupported. It must never silently select another version.

Deploying an earlier saved version provides rollback to that exact definition. Editing an earlier version and saving creates a new version. Rollback and selection changes are audited. Running invocations keep their captured definition.

Retain definitions while they are referenced by live bindings or retained audit records. An application deployment must preserve the semantics and schema support needed by versions it is expected to execute. Record the application build and engine contract version used by every invocation. A configuration version alone does not promise exact historical replay when executable behaviour or original facts are unavailable.

During a management outage, a bootstrapped application keeps its accepted definitions and follows RM07's existing admission and recovery rules. It must not infer an undeployed default, relax bootstrap requirements or replace a requested missing version. Local execution may complete when its admitted version and durable audit path remain available.

Concurrent edits must use the existing management revision/concurrency mechanism so one editor cannot silently overwrite another editor's saved changes. Deployment and registration must preserve all earlier immutable versions.

## 9 Counter extension and resets

Store hit counts in an extension table or equivalent existing operational store. The immutable rule definition carries its stable RuleId; the counter is joined for display. Counter values participate neither in matching nor in result ordering and are excluded from the saved definition and its checksum/version identity.

The default counter key is application, environment, ruleset and RuleId. Counts aggregate matches from all application instances in that scope and accumulate across saved and deployed versions until explicitly reset. Saving, deploying, rolling back, changing state or reordering a rule does not inherently reset its counter. Per-version history remains inspectable through the execution audit.

Each actual matching Enabled or Test row contributes at most one hit per invocation. Counter delivery retries must not duplicate the same execution/rule contribution. A fresh evaluation invocation is a new attempt and can contribute a new hit. Disabled, non-matching, errored and NotReached rows contribute none. A normally finalized failure or cancellation retains all observed earlier matches in its accepted audit and counter contributions. Successfully recorded contributions are never rolled back because of a later failure.

Counters are informative. A counter store delay or failure must not change matching, selected results or the accepted evaluation/audit failure policy. The system must expose stale or unavailable counter data honestly, and recover missed contributions using the existing durable delivery/audit facilities where possible. After a process interruption or failed audit finalization, recovery is limited to surviving durable evidence; unrecorded hits must not be guessed. The counter write does not become a second mandatory commit gate for rule execution.

The UI must reset an individual counter, selected counters or all counters in the current ruleset scope at any time, including for Test or Disabled rows. A reset changes operational counter state and records its actor, scope and time through management auditing. It does not create a ruleset version or a deployment.

Reset and increment operations require a consistent authoritative order per counter. A contribution is assigned its position on first durable acceptance in that counter order; retries retain that position. Contributions before the reset boundary must not restore the cleared total if delivered again later. Newly accepted contributions after the boundary count normally, including those from invocations that began earlier. The boundary is acceptance order, not a wall-clock comparison between hosts. The implementation may use existing store ordering, a reset generation or an ordered audit checkpoint; no particular backend is prescribed. A multi-counter reset reports each affected counter's outcome rather than silently claiming a partial reset was complete.

Display the counter's last reset time and freshness information. Historical execution audit remains intact after reset. Existing rule identities continue to identify counter data even if an older version is later redeployed.

## 10 Evaluation request and returned results

The public evaluation interface accepts a ruleset identifier, typed values keyed by column identifier, correlation context and cancellation. Exact version selection is optional and is subject to the deployed-version eligibility rules in section 8. Application/environment/instance identity comes from trusted host context.

Capture an isolated input snapshot before evaluation. The audit and evaluator must use that same snapshot; later changes to a caller-owned dictionary or object must not change the values evaluated or subsequently recorded. Preserve all supplied values in the audit, including unknown columns, while only registered columns participate in matching.

| Result field | Required meaning |
| --- | --- |
| ExecutionId | Unique invocation identity used by audit and counter delivery. |
| RulesetId and Version | Exact immutable definition captured for the invocation. |
| Execution status | Completed, Failed or Cancelled. |
| Matches | Zero or one row in FirstMatch; ordered rows in AllMatching. |
| Matched row | Captured rule identity, ordinal and definition, with unchanged metadata and applicable weight. |
| Error details | Stable code and readable explanation when execution fails or is cancelled. |

A Completed result with no matches is a successful evaluation with an empty collection. A matched rule is returned as a row with its associated data, rather than being reduced to a generic approval Boolean. Metadata is an opaque, serializable application value; the engine validates its declared shape where supplied and returns it unchanged.

Expose any continued Test-rule errors through execution diagnostics and the audit. They do not enter Matches or turn a matching Test row into an Enabled result. Counter extension values are not authoritative members of the immutable returned rule snapshot.

Failed or Cancelled results do not expose partial matches as a usable business decision. Their audit and diagnostic context may identify rows already evaluated and matched. Callers decide retry behaviour using existing application facilities; the rule engine performs no business side effects itself.

## 11 Full audit and failure behaviour

Every invocation accepted for evaluation must be auditable. Capture actual typed input values and presence information, the exact ruleset version, application/environment/instance identity, correlation, engine contract version, application build and start time. A hash or a count alone is insufficient to satisfy the supplied-value requirement.

Before evaluating rows, durably accept an execution-start record, either directly or through the existing durable audit delivery path. If this cannot be accepted, reject the invocation with AuditUnavailable before evaluating rules. Do not claim that an unavailable audit record was persisted.

The completion record contains the observed rule and cell results, selected rows, selection order, errors, terminal status and completion time. The audit must expose the configured operators and presets through the retained immutable version or an equivalent retained snapshot. Successful result release requires durable acceptance of the complete completion record. An in-memory logging buffer alone does not meet this condition.

| Audit outcome | Meaning |
| --- | --- |
| Selected for completed result | Enabled row matched and belongs to the durably recorded Completed result. |
| Matched in Test | Test row matched and contributed a hit, but was excluded by state. |
| Matched before failure | Row matched, but the invocation did not produce a successful business result. |
| NotMatched | Reached row evaluated to false. |
| Disabled | Row was encountered and skipped because of state. |
| NotReached | Row was not evaluated after stopping, failure or cancellation. |
| Error | Reached row could not complete its required comparisons. |

For reached rows, retain evaluated cell outcomes and the stopping/error reason. Cells skipped by AND short-circuit or a row error are NotEvaluated. A Disabled row after a FirstMatch stopping point is NotReached, because the evaluator never encountered it. Preserve evaluation order separately from final weight ordering.

| Event | Runtime behaviour |
| --- | --- |
| Enabled row comparison or runtime error | Abort, audit observed progress and return a technical failure. |
| Test row comparison or runtime error | Audit the error, record no hit for that row and continue. |
| Cancellation observed before terminal status is chosen | Stop evaluation, audit progress and return Cancelled if final audit acceptance succeeds. |
| Completion audit cannot be durably accepted | Return AuditUnavailable; withhold successful matches. |
| Completion audit durably queued | A successful result may be released without waiting for the reporting store. |
| Counter extension unavailable | Preserve evaluation/audit behaviour; report operational counter degradation. |

Choose the terminal evaluation status once, immediately before audit finalization. Cancellation observed before that point selects Cancelled. Later cancellation does not rewrite the chosen status during finalization. Invocation cancellation is never treated as a recoverable Test-rule error.

Finalization must still attempt durable audit after caller cancellation; do not reuse an already-cancelled token in a way that makes cancellation auditing impossible. Use the existing bounded audit-delivery behaviour. If durable audit acceptance cannot be confirmed, the API returns Failed with AuditUnavailable and preserves any earlier cancellation or evaluation failure as diagnostic context.

A durable Completed record proves the recorded evaluation and selected result, not caller receipt. The process may stop after that record is accepted but before the response is delivered. Preserve the execution identity and recorded outcome without claiming delivery or fabricating a new evaluation.

If the process stops after a durable start but before a durable completion, retain an incomplete/interrupted attempt and any durable progress. Never invent unrecorded row results or relabel an incomplete attempt as a successful no-match. The start record allows the absence of completion to be identified. This specification does not require a new per-cell synchronous persistence mechanism.

Administrative saves, deployments, rollbacks and counter resets use the existing management audit path. Audit access is governed by the management permissions. Retention and storage providers remain audit-infrastructure settings; definitions needed to interpret retained executions must remain accessible.

## 12 Management UI

Provide a rule-engine area within the management portal. Operators select the registered application/ruleset and environment, inspect its declared schema, choose a saved version and see the currently requested and applied deployment versions.

The primary editor is the agreed grid. Keep ordinal, state, counter, comment, metadata and applicable weight visible alongside application-defined condition columns. Use suitable typed editors: string input, decimal input, Boolean selection, string-list editor and explicit ANY/MISSING choices. Only compatible operators are offered for a cell. ANY/MISSING remove the need for an ordinary operator or preset editor.

Support adding, copying, removing and reordering rows; editing conditions, state, metadata and comments; selecting the execution mode; and validating the complete definition before save. A copied row receives a new identity. Changes remain edits until saved, and a saved revision remains undeployed until the operator selects Deploy.

Display older saved versions as immutable. Allow an older version to be selected for deployment, or opened as the basis for a new saved revision. Show save conflicts and validation errors clearly without discarding the operator's edits. A saved state change, including Test to Enabled or Enabled to Disabled, becomes effective through deployment of its version.

Counter reset controls are ordinary management operations available independently of the version editor and deployment actions. Support a row, selected rows and the full ruleset scope. Preserve the historical audit view and show reset time/freshness beside counters.

The execution history view must support finding an ExecutionId and filtering by ruleset, version, instance, time and outcome. Its detail view shows supplied values and missing/null distinctions, evaluated conditions, row states/results, Test hits, stop reasons and the returned metadata/weight order. Failures and interrupted attempts must be distinguishable from Completed evaluations with no matches.

Use the existing portal interaction patterns and authorization model. Include a reviewable grid design in the CIS implementation work and follow the established UI review process. Verify the actual operator journeys with browser tests; an empty test body or an unexecuted named case is not evidence of coverage.

## 13 Validation and resource behaviour

Validate unique rule and column identities, unique valid ordinals, known states, supported operators, compatible typed presets, complete condition cells, valid wildcard syntax, metadata shape where declared and weight validity before accepting a saved definition. A deployment must also validate compatibility with its target application's registered schema and engine contract.

Empty rulesets are valid and return a Completed empty result. A row of explicit ANY cells is a valid unconditional rule. The editor must make such conditions visible; a missing editor value must never create an unconditional rule.

Definitions and execution snapshots must be safe for concurrent use. Use an immutable captured version for each invocation and keep input, result and error state isolated. Bound accepted definition, list, string and input/audit sizes using documented host options. Reject oversized or unsupported values explicitly; do not truncate accepted inputs or audit data silently.

Use the existing cancellation and error-result conventions. Keep matching deterministic for the same version, inputs and engine contract. Performance optimizations must preserve ordinal ordering, Test semantics, short-circuit observability, state-sensitive errors and audit completeness. Counter updates must not introduce a global execution lock across unrelated rulesets.

## 14 Specification defaults

The following conventions complete the defined behaviour and make independent implementations agree. They are explicit specification defaults and can be revised deliberately without reopening the agreed grid, state, deployment or audit model.

| Default | Convention |
| --- | --- |
| MISSING | Include the marker; absent and explicit null both satisfy it for matching, with provenance retained in audit. |
| String comparison | Case-sensitive and culture-independent; no automatic trimming or normalization. |
| Wildcards | Whole-string matching; * for zero or more characters, ? for one; backslash escaping of *, ? and backslash. |
| Membership | Supplied scalar string against configured literal string list; empty IsIn is false and empty IsNotIn is true for a valid present string. |
| Ordinal | Unique non-negative integer within a version; gaps allowed. |
| Weight | Decimal, default zero; negative values allowed; ties resolve by ascending ordinal. |
| Counter scope | Stable rule identity within application/environment/ruleset, cumulative across versions and instances until explicit reset. |
| Cell evaluation | Declared schema order with AND short-circuit; skipped cells are NotEvaluated. |
| Metadata | Opaque serializable application value, with an optional application-declared schema. |
| No match | Completed evaluation with an empty Matches collection. |

## 15 Acceptance criteria

Acceptance is based on observable behaviour. Use focused cases mapped to the criteria below, including the real portal journeys and relevant existing provider integration paths. These are required scenarios, not a requirement for a separate test project or one new test file per row.

| ID | Scenario and required outcome |
| --- | --- |
| AC01 | Application registration exposes the declared column names, datatypes and constraints in the editor; a restart preserves saved rules and deployment selection. |
| AC02 | A row with one false condition does not match. All true conditions produce exactly one hit. Audit marks short-circuited cells as NotEvaluated. |
| AC03 | Storage/display order differs from ordinal order. Evaluation follows ascending ordinal; duplicate ordinals are rejected on save. |
| AC04 | A matching Test row precedes a matching Enabled row in FirstMatch. Both receive hits, only the Enabled row is returned and evaluation stops there. |
| AC05 | Test and Enabled rows follow a FirstMatch stopping row. Neither is evaluated or receives hits; both are NotReached in audit. |
| AC06 | Disabled rows contain otherwise matching or invalid supplied-value comparisons. They are skipped without hits or comparison errors. |
| AC07 | AllMatching includes Enabled and Test matches with differing weights. Audit preserves ordinal evaluation; only Enabled rows return in descending weight order, with ordinal tie-breaking. |
| AC08 | Native Boolean false matches Equals false; string "False" is not silently converted. Missing Boolean never becomes false. |
| AC09 | Absent and null inputs are used with Equals, NotEquals and IsNotIn. All ordinary comparisons produce no match. ANY matches them; MISSING matches only missing state. |
| AC10 | False, zero and an empty string are supplied. They remain present values. Literal "ANY"/"MISSING" strings remain distinct from marker presets. |
| AC11 | Exercise string equality/case, wildcard *, wildcard ?, escaping and whole-string matching. Results follow section 4; malformed patterns fail save validation. |
| AC12 | Exercise all six decimal comparisons with values requiring exact precision. Portal, transport, execution and audit preserve the values without binary floating-point rounding. |
| AC13 | Exercise IsIn/IsNotIn with matches, misses, duplicate and empty lists. Membership direction, missing behaviour and one-hit-per-row semantics are preserved. |
| AC14 | A reached ordinary condition receives an invalid value. An Enabled row aborts; a Test row records an error and continues. ANY and unexecuted rows are not defeated by eager global comparison validation. |
| AC15 | Empty ruleset, no Enabled matches and only Test matches each produce a Completed empty result. Returned matching rows preserve metadata and applicable weights. |
| AC16 | Save changed conditions, state, order, metadata, weight or comment. A new immutable version is created and previous versions remain unchanged. |
| AC17 | Save without Deploy. The application keeps its deployed version; a raw request for the saved-only version cannot bypass deployment. |
| AC18 | Deploy while an invocation is running. That invocation keeps its captured version; later eligible invocations use the adopted version. A rejection or lag is visible per instance. |
| AC19 | Deploy an older saved version. It becomes the selected definition through the normal deployment path; an unavailable/unsupported exact version produces an explicit error. |
| AC20 | Simultaneous invocations on multiple instances keep inputs, captured versions, results and execution identities isolated. Mutation of caller-owned inputs after capture does not alter evaluation/audit. |
| AC21 | Counter contributions aggregate for the same rule scope, including Test hits. Save, deploy, rollback and state changes do not automatically reset them. Delivery retries do not double-count a hit. |
| AC22 | Reset an individual, selected and all counters through the UI. No rule version or deployment is created. Late pre-reset contributions do not restore cleared totals; later contributions count normally. |
| AC23 | Counter storage is delayed or unavailable. Successful evaluation still follows its audit requirement; the UI reports counter freshness/unavailability rather than treating missing statistics as zero. |
| AC24 | Full execution audit exposes actual typed inputs, absent/null distinction, immutable rule definition, reached/non-reached rows, Test hits and returned result order. |
| AC25 | An Enabled error or cancellation occurs after earlier matches. No successful partial business result is returned; accepted audit retains progress and prior hits. Late cancellation follows the terminal-status boundary, and invocation cancellation is never swallowed as a Test error. |
| AC26 | Audit start cannot be durably accepted. No rule is evaluated and AuditUnavailable is returned. Completion audit failure also prevents successful result release. |
| AC27 | Audit completion is durably queued while the reporting store is unavailable. Execution can complete; eventual delivery preserves the same execution identity without duplicate logical records. |
| AC28 | Process interruption occurs after a durable start. The attempt remains identifiable as incomplete/interrupted; no terminal result or unrecorded comparison is fabricated. |
| AC29 | Browser journeys edit typed cells and markers, save, inspect history, Deploy, observe adoption, reset counters and inspect an execution audit. Required cases have actual executed results. |
| AC30 | Two operators save against the same starting revision. The existing concurrency mechanism prevents silent overwrite and preserves both operator intent and immutable version history. |

## 16 Implementation delivery

Import this specification through the existing CIS workflow and derive tasks of manageable size from the acceptance criteria. Parallelize independent runtime, management and portal work once their shared contracts are agreed. Reuse existing services and mocks instead of building a separate harness or duplicate persistence stack.

Provide the common runtime and management contracts, the evaluator and supported operators, deployment integration, counter extension, audit integration and portal flows. Include a small application example using ResidenceCountry and Pensioner, demonstrating both execution modes, explicit deployment and audit correlation.

Use the repository's normal build and impact-selected testing workflow. Add focused unit cases for comparison and selection semantics, integration cases for actual delivery/counter/version boundaries, and browser cases for the required management journeys. Use existing integration infrastructure for any real external service involved. Keep evidence compact and attributable to the executed criteria.

Completion requires all applicable acceptance criteria to pass, documented public consumption and operational behaviour, verified UI journeys, and no unresolved deviation from the confirmed matching, state, deployment, counter or audit rules. Physical storage layout, provider choice and internal optimization remain implementation decisions under the existing platform contracts.
