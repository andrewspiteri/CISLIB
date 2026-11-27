# Preparing Configuration before deployment

An authorized operator can prepare a new Configuration for an existing Management application even when the application has no registered instances. The application must already exist through ordinary registration or the trusted Configuration catalog. This API does not create applications.

`GET /applications/{applicationId}/configuration-creations/context` returns the exact native application/environment, a strong creation ETag and the server's fixed generic schema version `1`. The request requires both `management.configuration.create` and `management.configuration.operations.read` on that exact `management.application` resource. The context is advisory; the effect independently checks current grants and the original ETag.

`POST /applications/{applicationId}/configuration-creations` takes the original `Idempotency-Key`, that `If-Match` and this closed command:

```json
{
  "commandVersion": 1,
  "configurationId": "custom.settings",
  "intent": "createGeneric",
  "reason": "Prepare application settings before deployment"
}
```

The effect creates or explicitly reuses one immutable global definition and creates an application binding, application source and empty assignment map. The definition is a non-null object with no declared members and `{}` defaults. Unknown JSON members are allowed under the existing finite depth, node, collection and source-size limits. The server does not infer types, required properties, defaults or validation rules from the operator's JSON. The host scope is the actual application's name; the reserved `CommonConfiguration` name cannot use this preparation path.

Creation publishes no payload, creates no release, changes no assignment and alters no already admitted Configuration session. The portal's large JSON input is held in its protected draft and published only through the existing source-publication operation after a separate review. A later release and assignment each require their own explicit command, original key and native grants. There is no automatic activation.

`createGeneric` refuses an existing definition. `reuseGeneric` explicitly reuses an enabled definition only when its complete retained body and immutable fingerprint equal this fixed generic contract. A typed definition, altered defaults or another schema version cannot be silently adopted. An existing application binding is a conflict. The same original key and input recover the same decision without repeating effects; another input under that key is a conflict. Creation and its payload-free Audit intent commit through the existing Transactions/Operations owner.

The operation family is `management.configuration.create`. Its target is the real application UUID. `GET /applications/{applicationId}/configuration-creation-operations/{operationKey}` requires only current Configuration OperationsRead on that application; it does not require application discovery, definition reads or the original write grant. Use the same actor and original key. Absence, expiry, denial or uncertainty never authorize automatic resubmission under a new key.

C# callers discover `IManagementConfigurationCreationClient` on the existing
`ManagementConfigurationClient`, including the same instance resolved through
the keyed/default `IManagementConfigurationClient` hosting registration. This
follows the optional Editor/BindingRead companion pattern; the creation interface
has no separate DI alias or transport owner. TypeScript exposes the four methods
on its existing administration client through the additive optional
`ManagementConfigurationCreationClientPort`. Existing substitute interfaces gain
no compulsory members. The corrected focused provider batch passed 15 cases;
one additional required-Audit rollback case and the R08 browser fixture also
passed their report guards. Current candidate and proof limits are recorded in
[CIS-0030 verification](../../changes/CIS-0030/verification.md); these are scoped
local results rather than external deployment/runtime qualification.

## Explicit application authoring policy

Creation grants no other permission and grants no creator privilege. The host may independently configure each required Configuration capability on the exact application resource for **native-created generic** bindings and sources. Exact source/binding grants continue to work.

| Actual native resource | Independently grantable application alternatives |
| --- | --- |
| Application source | DefinitionsRead, ContentRead, Validate, Publish, Configuration OperationsRead |
| Application binding | DefinitionsRead, Preview, ReleaseCreate, AssignmentWrite, Configuration OperationsRead |
| Retained release of that binding | DefinitionsRead |

The native store proves the actual application, permanent native-created provenance and exact generic definition fingerprint before using this alternative. A token claim, creator identity or caller-supplied application cannot provide that proof. Common sources, typed/catalog-only bindings, another application and changed generic fingerprints never receive the alternative. No wildcard authoring grant is introduced.

A mutation's write and original-result lookup capabilities must both be granted on **one** target: the real source/binding, or the explicit actual application alternative. A write grant on the source plus lookup only on the application does not form a valid bundle. Reads and result lookups remain independent. Removing write/read grants while retaining the appropriate OperationsRead still permits recovery of that actor's existing original.

The operation journal family, canonical input, original scope, key and Audit target continue to identify the real source or binding. Selecting the application as the authorization target does not combine all of that application's source originals. This policy is a finite service rule over the existing native capability authorizer.

## Assigned-layer summaries

`GET /applications/{applicationId}/configuration-summaries?pageSize=...&cursor=...` returns a bounded, metadata-only application list. The optional client/backend companion leaves old interfaces intact; a backend without the companion returns explicit `501`. ApplicationRead and actual per-row source/binding DefinitionsRead are required. Release DefinitionsRead is independently checked before disclosing assigned origin.

The summary derives origin from the **assigned immutable release references**, never the newest publication or the existence of a source:

| Origin | Meaning |
| --- | --- |
| `unassigned` | No default release has been assigned |
| `defaultsOnly` | The assigned release explicitly pins both layers absent |
| `shared` | Assigned common publication; application layer absent |
| `applicationOnly` | Assigned application publication; common layer absent |
| `applicationOverride` | Both exact assigned layer publications are present |
| `mixed` | A cohort pins a genuinely different layer-reference pair |
| `protected` | Current grants do not disclose referenced release metadata |
| `unavailable` | The retained assignment cannot establish a valid complete origin |

Rows contain stable source/binding IDs, definition identity, actual host/environment, optional description and a cohort-override indicator. They contain no release/publication IDs, payload, defaults, cohort members or merged effective values. `defaultsOnly` does not mean the empty object has been fabricated as current source content. Future response origin tokens remain unknown to older clients. Pagination binds the exact app/environment, page size and current authenticated read-policy context; hidden rows do not become cursor positions. The finite scan refuses with `503` rather than returning a false empty list when its limit is reached.

An optional `modifiedAtUtc` reports the publication time of the default-assigned selected layer: the Application publication when present, otherwise the Common publication. The value retains the exact native UTC timestamp. It does not follow a newer unassigned publication, assignment time or another cohort's release. Defaults-only/unassigned rows have no publication date. Revealing a Common-layer date requires independent DefinitionsRead on that actual Common source; otherwise the date is omitted even if the assigned origin itself is disclosed. This adds no schema migration or fabricated historical timestamps.

## Deployment reconciliation

Explicit schema migration advances Management versions one through three to version four. Existing definitions/bindings are marked manifest-managed, and existing bindings are never marked native-created. Migration preserves authority/catalog IDs and retained application data; ordinary service startup only validates the selected version.

An applied trusted manifest preserves unclaimed native-created definitions/bindings. It may explicitly claim the same generic version only with the exact immutable generic fingerprint and the actual binding. The native-created marker remains explicit so the configured generic application policy continues to have the same meaning. Once claimed, the binding follows ordinary manifest enable/disable reconciliation. Turning a generic contract into a typed application-owned contract requires another schema version and deliberate migration; no payload-based schema inference or automatic conversion occurs.

The focused provider tests use the existing owned PostgreSQL/Management fixture, including an actual catalog-preallocated application with zero registrations, signed native credentials and the normal durable/Audit owners. Additional cases cover competing current creation preconditions, quota refusal without metadata changes, original-key replay, read-cleanup uncertainty, bounded changing-count companion collections, required assigned-origin authorization references and actor/page-bound cursors. A read with unconfirmed cleanup is unavailable rather than absence. Build, provider and browser results are recorded separately in the change evidence; source review and conformance vectors alone do not prove runtime behavior.
