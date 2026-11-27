---
title: "Exact Configuration binding editor context"
type: guide
status: Active
owner: Repository maintainers
last_reviewed: 2026-11-26
review_cadence: on binding-editor or native authorization contract change
cis:
  stable_id: cislib:guide:management-configuration-editor-context
---

# Exact Configuration binding editor context

The optional bodyless read resolves one actual application binding and its two
source slots without walking the Configuration catalog:

```http
GET /management/v1/applications/{applicationId}/configuration/{configurationId}/editor-context?schemaVersion=1&source=application
```

Both query parameters are required exactly once. `source` is exactly `common`
or `application`; unknown names, case aliases, malformed percent/UTF-8 encodings
and request bodies are refused. This adds no mutation, storage schema, package,
Scheduler runtime or browser merge authority.

The selected Configuration backend must implement
[`IManagementConfigurationEditorBackend`](../../../implementations/csharp/applications/CIS.Management.Service/Backends/IManagementConfigurationEditorBackend.cs).
The qualified PostgreSQL adapter reads the enabled binding, immutable schema and
at most two actual source slots in one clean native snapshot. The optional
companion does not add members to existing backend contracts. The initialized
service advertises `getConfigurationEditorContext` only when the selected backend
supplies it. An authorized target on a legacy backend returns 501
`management.configuration.editor.unsupported`; hidden targets retain 404 behavior.

## Meaning of the response

`ConfigurationEditorContext` carries the binding/application/environment,
Configuration ID/schema version, canonical application and host names, one
immutable structural shape and optional description. It contains exactly one
Common source slot and one Application source slot, the selected source kind,
and selected-source declared root defaults.

| Source state | Meaning |
| --- | --- |
| `available` | This actual source is enabled and its metadata is currently readable; its source UUID is present. |
| `absent` | No source stream is provisioned in this actual binding snapshot; no UUID is supplied. |
| `redacted` | Its metadata is not currently readable; no UUID is disclosed. |
| `unavailable` | It cannot be selected for current authoring. A metadata-readable disabled retained source may carry its UUID. |

Source-slot absence is different from a deliberate release
`SourceReference {state:"absent"}`, an empty settings object, an omitted override
or a reset instruction. Disabled historical streams remain unavailable here;
existing historical source/publication reads retain their prior semantics.

The `defaults` member has `state` available, redacted or unavailable.
Only available carries exact `defaultsJson`. Available defaults require an
available selected source and both DefinitionsRead and ContentRead on that same
actual source. Selecting a hidden Common source cannot borrow Application
authority. A missing or disabled selected slot gives unavailable defaults, never
`{}` or the other source's data.

These are declared schema defaults, not observed or accepted runtime values.
The response contains no current source payload, publication history, assignment,
ETag, action hint, merged value, nested ObjectDefaults template or manifest.
Editors continue to use existing exact source/publication reads and native
validation/preview; no browser-generated source identity or effective merge
becomes authoritative.

## Native authority, capture and clients

The route first resolves the visible actual application and checks DefinitionsRead
on its actual binding resource. Every disclosed source identity independently
requires DefinitionsRead on that source. Content authority is checked only for
the selected actual source. These read permissions do not grant publication,
release, assignment, result recovery or worker admission.

Malformed optional-backend tuples, duplicate disclosed IDs, unknown source/default
states, partial reads, missing clean rollback or cleanup errors fail closed.
The HTTP mapper captures the finite wire value before checking declared defaults
against its immutable structural shape. It never returns a partial context or
truncates a shape/default payload. Native cancellation reaches actual SQL and
all owned cleanup remains joined.

The existing Configuration wire profile applies: at most 1 MiB for the encoded
response, 256 KiB for decoded exact defaults JSON and depth sixteen, including
any tighter selected limits. Numbers and escaped/Unicode text remain exact JSON
text rather than being converted through JavaScript Number. Additive response
fields remain compatible; unknown access states cannot enable editing.

C# callers may discover the optional
[`IManagementConfigurationEditorClient`](../../../implementations/csharp/src/CIS.Core/Management/Contracts/IManagementConfigurationEditorClient.cs)
on the existing ManagementConfigurationClient. Its
`GetBindingEditorContextAsync(applicationId, configurationId, schemaVersion, source)`
uses the existing Configuration observation alias, native read policy and
entry-based deadline. It checks application/configuration/schema/selected-source
echoes before returning content. Existing IManagementConfigurationClient
implementations acquire no compulsory member.

The TypeScript administration companion's `getBindingEditorContext` uses the
same route and strict response shape. Credentials remain per call, while portal
BFF context and native resource checks remain separate responsibilities.
This read supplies a prerequisite for the approved Configuration editor; it does
not by itself deliver authoring, publication/release, assignment or adoption UI.

Portable vectors are in
[`configuration-cases.json`](../../../conformance/management/v1/configuration-cases.json).
Focused cases cover real source pairing, enablement and absence, selected-source
redaction, unsupported backends, malformed tuples, strict query/body handling,
native cancellation, exact capture and client echo rejection. Execution results
belong to the CIS-0030 verification record; authored cases are not passing proof.

