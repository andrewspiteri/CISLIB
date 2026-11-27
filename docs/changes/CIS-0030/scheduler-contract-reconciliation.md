# Scheduler BRD reconciliation — 26 November 2026

The newly supplied download is exactly the already imported RM10 v1.1 source:
113,225 bytes; SHA-256
`cbb75ceec89564cefeebb003ab51eed565c2bbcad4e18235954accad97c36510`.
The preserved source is
[RM10 v1.1](../../../planning/scheduler/sources/CISLIB_RM10_Application_Scheduler_and_Triggers_Specification_v1.1.md).
There is no source revision delta and no duplicate CIS import is needed. Existing
RM10 requirement IDs, the forty prefixed acceptance IDs and their original
unverified implementation obligations remain intact.

Andrew supplied this attachment for better contract identification, explicitly
excluding implementation of the scheduler itself from this BRD review. The
document's implementation/handoff wording is requirements material, not new
execution authority. This reconciliation and WORK-152 client/codec checks start
no calendar producer, durable scheduler store, worker adapter or recurrence loop.
Those runtime tasks remain unstarted; their authored plans are not delivery proof.
The existing portal work and approved R07 design remain distinguishable from
delivery of a scheduler backend.

| BRD concept | Existing / selected boundary | Contract consequence |
| --- | --- | --- |
| Registered Job | Existing Jobs handler binding, exact execution scope/definition/version and passive execution-support declaration | A Task UUID, category, endpoint or deployment version cannot select a different handler. |
| Trigger provider | Trusted application capability definition, separate instance participation | Inventory observations cannot install schemas or grant source authority. RuleEngine entries describe registered instances without creating an engine. |
| Configured Trigger | Source application + Trigger UUID, immutable revision and native owner | Event identity and history links retain that source scope, including cross-application task targets. |
| Scheduled Task | Named association of one Trigger, exact Job, resource mapping and input mappings | It is the operator's configuration object. Trigger and Job are contextual editor tabs; history is separate. |
| Business configuration | Existing RM08 configuration ID/schema, immutable release and assignment generation | Scheduler contracts reference the existing owner; they do not create a second Configuration authority. |
| Framework execution settings | Detached exact tick snapshot, distinct from business settings | An old run cannot silently inherit a worker's new live policy/settings. Passive representation does not execute a callback. |
| Event / Run / Attempt | Original source occurrence, selected logical work, and distinct execution/recovery attempts | Attempts do not inflate logical run totals; an event receipt hides both target IDs and count when disclosure is denied. |
| Original operation | Existing Messaging business reference versus administrative Management reference | Preserve their different key grammars and exact original recovery keys; no automatic write/event/report transport replay. |
| Version and recovery | Actual worker/build attribution and explicit start/recovery contracts | Similar names or newer version labels cannot imply compatibility. Native runtime admission remains a later independent boundary. |
| Creator / producer / requester / executor | Separate issuer-qualified actor data and actual workload authority | Human scheduling intent is not a worker credential or execution grant. |
| Preview | Complete Task draft, exact source preconditions and existing-object ETags; separate pure Calendar preview | A successful validation response is not publication, assignment, activation or permission. |
| Human-facing identity | Display names, environment, version and source context first | Technical UUIDs remain available under Technical details; raw IDs are not the primary explanation. |
| Sensitive history | Visible metadata versus independently authorized captured input/settings/results | Redacted, unavailable, expired and unknown access never disclose protected content or become success. |

The reviewed contract work also binds filtered history responses to the requested
Task, Job, source Event, resource and state wherever those fields are present.
Build/version/contract filters still require authoritative backend evaluation;
a client cannot fabricate validation from fields absent from its Run summary.

The current C# and TypeScript codec/client checks establish only their observed
wire, request, recovery and lifetime behavior. R07 remains design approval.
Configuration authoring, portal wiring and real scheduler history are not marked
complete by this document. No mock-only or inventory-only result establishes
full RM09 acceptance.

The [contract follow-ups](scheduler-contract-followups.md) record the remaining
attempt outcome/freshness, captured instance, active-versus-desired revision,
Event-to-Run and inherited-identity gaps. The approved R07 states already support
truthful unavailable or Not reported presentation; these findings add no new UI
approval and grant no Scheduler runtime authority.

## Compatibility follow-up before Scheduler activation

The current v1 ServiceInfo and application API-capability representations each
allow at most 64 entries. At the original contract checkpoint the fully composed
future inventory had 69 feature entries and 68 distinct HTTP operations; two
existing application-query feature IDs describe the same GET route. The
37-entry service at WORK-171 grew to38 with the cohort read,39 with exact editor
context,40 with unpublished candidate preview and42 with the two binding reads.
The current canonical OpenAPI contains72 distinct HTTP operations. Combining
that contract count with the one existing feature alias gives an inferred
future inventory of73, not a claim that those handlers are installed or tested.
A bounded discovery extension or explicit selection must be agreed before
installing every Scheduler handler. Preserve the
legacy default representations, keep HTTP discovery separate from application
features, and expose no uninstalled handlers. Increasing only a new SDK limit or
silently truncating the inventory would break compatibility. This is a recorded
contract gap, with no Scheduler implementation or limit change in this review.

