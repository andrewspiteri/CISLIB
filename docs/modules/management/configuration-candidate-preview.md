---
title: "Unpublished Configuration candidate preview"
type: guide
status: Active
owner: Repository maintainers
last_reviewed: 2026-11-26
review_cadence: on candidate preview or native composition contract change
cis:
  stable_id: cislib:guide:management-configuration-candidate-preview
---

# Unpublished Configuration candidate preview

The optional read-only companion previews one exact draft before publication:

```http
POST /management/v1/configuration/candidate-preview?pageSize=100
Content-Type: application/json
```

Its closed `ConfigurationCandidatePreviewCommand` contains commandVersion one;
the actual application, Configuration ID and schema; selectedSource
(`common|application`) and sourceId; strong sourceETag and assignmentETag;
explicit immutable Common/Application baseline references or deliberate absence;
and exact bounded payloadJson. Optional continuation uses only pageSize/cursor.

The result is candidate-specific. It returns actual binding/environment/host
scope, the same source/preconditions and baseline references, server-computed
draftSha256, structural eligibility, redaction, bounded changes/issues, current
assignment and a recipient page. Baseline publication IDs remain baseline
identities. The draft receives no publication ID and no runtime value or owner
acceptance is claimed.

## Authority and snapshot

The route requires Preview on the resolved actual binding and Validate on the
selected actual source. It adds no compulsory AppRead or DefinitionsRead prefetch
gate to those existing native permissions; editor-context discovery has its own
independent authorization. The selected source must be that binding's enabled
Common or Application slot.

The selected backend must implement the optional
[`IManagementConfigurationCandidatePreviewBackend`](../../../implementations/csharp/applications/CIS.Management.Service/Backends/IManagementConfigurationCandidatePreviewBackend.cs).
After resource authorization, a legacy backend returns 501
`management.configuration.candidate_preview.unsupported`. It is never an empty
successful preview. Initialized discovery advertises
`previewConfigurationCandidate` only for a selected companion.

The qualified PostgreSQL path takes the existing catalog share lock, re-resolves
the actual binding and source pair, locks the selected enabled source for a
read, and checks both strong preconditions in that same native owner. A changed
source or map returns a conflict requiring fresh review. Exact baseline
publications retain existing scope, kind and integrity checks, including references
for the selected layer being replaced.

Disclosure follows the existing all-bound-source ContentRead rule. Without all
required source content grants, the preview is explicitly redacted: no change
paths or issue paths are disclosed, and no hidden/default content becomes
`{}`, absence or an editable mask. Recipient compatibility remains unknown
unless native evidence says otherwise; registration does not prove typed adoption.

## Composition and bounds

One raw in-memory layer replaces the selected baseline layer. The other baseline
layer and the immutable declared defaults are composed by the existing native
Configuration implementation, including its ObjectDefaults, omission, null and
collection rules. No candidate SourceContent, fake publication identity, browser
merge or second validation framework is created. Existing immutable-reference
Preview behavior remains unchanged.

Changes compare against the current default assignment's effective value, or
declared defaults when no default is assigned. They do not silently compare
against a presumed latest publication. Native history remains ordered by
publication UUID; a caller selects an exact history reference or a specifically
labelled assignment-pinned reference.

The ordinary Configuration limits remain: one MiB encoded bodies, 256 KiB exact
decoded source JSON and depth sixteen, bounded structural issues/paths and at
most 500 disclosed recipients. The internal one-extra-row sentinel is captured
before producing a public page. A size-only estimate preserves the existing
worst-case snapshot metadata bound and exact JSON-escaped source-string sizes,
so an escaping-heavy future source pair cannot pass preview merely because its
composed value is small. No candidate domain identity is manufactured for that
estimate.

The cursor binds the exact closed command bytes, including the raw draft,
baseline references and both preconditions, together with the actual binding,
current actor/profile/policy and validated principal facts. A changed draft,
precondition, page size or actor invalidates continuation. Renewed ticket claims
may require restarting the page. Every request still authorizes current native
resources.

A successful read requires actual clean rollback with no commit attempt,
primary failure or cleanup error. Cancellation reaches native SQL and owned
cleanup remains joined. Unknown, partial or malformed backend results cannot
masquerade as absence or return partial protected preview content.

## Effects and clients

Preview creates no publication, release, assignment, quota charge, durable
operation or Audit effect. Structural eligibility is separate from consumer
semantic acceptance, desired assignment, local revision and deployment state.
Publication and release retain their own independent keys, strong preconditions,
authorization, durability and cleanup checks when the operator later confirms.

The existing C# ManagementConfigurationClient implements the optional
`IManagementConfigurationCandidatePreviewClient.PreviewCandidateAsync` without
adding compulsory members to existing ports. TypeScript uses the administration
companion's `previewCandidate`. Both preserve exact draft text and verify the
response's target, source, preconditions, baseline references and computed digest
inside their original request/decode owner. Credentials remain per call. This
POST is a read-only preview and supplies no authorization for automatic mutation
or replacement-key replay.

Canonical Configuration schema/OpenAPI and
[`configuration-cases.json`](../../../conformance/management/v1/configuration-cases.json)
describe the new request/result separately from existing Preview. Focused native
cases cover parity with later explicit publication, exact source text, zero
preview effects, current-default comparison, grant/redaction separation,
preconditions, scoped references, continuation, unsupported/malformed backends,
escaping limits and actual cancellation. Source cases alone are not passing
proof; current native results belong to CIS-0030 verification.

