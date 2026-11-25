# Management operator and workload identity

`Security.Mode=entra-management` is the Management application's explicit mixed
reference. The same HTTPS listener validates app-only workload traffic and
delegated human operators through one native JWT scheme and the existing CIS
Security mappings/grants. It is selectable through the normal service settings
file; no code-only callback or additional listener is required. Existing
`Mode=entra` remains the strict app-only optional Entra adapter, and certificate,
local and compiled-native selections retain their existing contracts.

Use the [example settings](../../../implementations/csharp/applications/CIS.Management.Service/deployment/settings.entra-management.example.json)
with the [existing deployment recipe](../../../implementations/csharp/applications/CIS.Management.Service/deployment/README.md).
The example is deliberately non-secret and is not loaded automatically. Replace
every example directory/client/object/resource ID, service environment, IP,
certificate reference and broker selection. Start the ordinary service with
`--settings` naming that private absolute settings path and its existing secret
environment inputs. No existing database, Entra tenant or deployment is accessed
by the checked-in example.

## Fixed trusted inputs

Set `EntraTenantId`, the exact Management access-token `EntraAudience`, one HTTPS
`EntraAuthorityHost`, `EntraPortalClientId`, and `EntraOperatorScope`. For ordinary
Entra v2 access tokens the audience is the Management API's client ID. The short
delegated scope example is `management.operator`; the portal requests its full
resource scope, such as `api://<management-api-client-id>/management.operator`,
along with OIDC `openid profile`. It does not use a Graph or portal ID token as
Management's credential. Token signature, fixed v2 issuer/tenant, exact audience,
expiry, RS256 and actual selected scheme are validated natively. Clock tolerance
defaults to thirty seconds and cannot exceed two minutes.

The delegated scope permits this client/token flow; it does not itself confer
read, write or recovery permission. Existing native grants authorize each actual
service/environment and resolved application/source/binding resource. This
desktop increment gives its example operator an application-read grant only.
Additional Configuration definition metadata still requires its actual source
grant; application visibility does not grant defaults/content access.

Each trusted mapping names the exact issuer, directory object ID (`oid`), client
ID (`azp`), actor kind and calling application/environment. User mappings must
select the configured portal client and explicitly name the portal's trusted
application/environment. Workload mappings retain their own application/client;
their subject grants continue to require an actual provider application role.
Mapped roles still require the intersection with the signed token's roles.
An object cannot be mapped as both actor kinds in this mode.

## Distinct native branches

Workloads require `ver=2.0`, `idtyp=app`, no `scp` claim, their exact mapped client
and bounded actual roles. Their registration/control/observation/inventory/close
requests continue to use the trusted self mapping. A User actor cannot use those
workload endpoints even if a registration grant is accidentally configured.

Operators require a bounded `scp` list containing the exact configured scope,
the configured portal `azp` and their exact User mapping. User `idtyp` may be
absent (the normal Entra optional-claim behavior) or `user`; an app/device/unknown
type cannot select that branch. Singular identity claims, bounded distinct roles
and scopes are validated without truncation. The verified human subject is
`(issuer, oid)`; trusted mapping separately identifies the calling portal. Pairwise
OIDC `sub`, email, display names, serialized actor/delegation claims and request
headers cannot replace either identity. This direct delegated User path invents
no application-as-human or `VerifiedDelegation` assertion.

Entra documents [stable object versus pairwise subject identity](https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference),
[actor/scope validation](https://learn.microsoft.com/en-us/entra/identity-platform/claims-validation)
and [optional user idtyp](https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims-reference).
Configure the actual single tenant/API/portal registrations, API delegated scope,
consent and subject mappings independently of a browser's selected target. Replace
the affected service instance to change trust, mappings, grants or policy version.

## Ownership and evidence

Native validation proof lives on the successful authentication ticket and is
bound to this provider and principal. The provider's fixed native options/events
are checked after post-configuration; no forwarded scheme or custom validator
can silently bypass signature, issuer, audience or lifetime checks.

The native metadata manager uses this provider's owned HTTP document retriever,
which passes its linked cancellation token into the native HTTP request and body
read rather than relying on the platform retriever's cancellation behavior.
At most four metadata requests are admitted, each with a ten-second bound through
native document/body settlement and a one-MiB HTTP response buffer. Metadata
addresses must be bounded absolute HTTPS URLs without credentials or fragments.
The owner adds no request retries. Host stopping
closes and cancels that owner; DI disposal waits for admitted requests before
disposing its HTTP resources. The native OIDC configuration manager owns normal
discovery/key-refresh behavior; no token cache, background loop or new lifecycle
framework is added. A trusted code-only `EntraMetadataManager` remains explicitly
borrowed for an application/fixture that owns it; JSON cannot create a metadata
manager, CLR provider or static signing-key override.

Focused service tests cover configuration capture, claim boundaries, native
signed-token validation, same-host workload reporting and operator inspection,
denial, held response-body cancellation/settlement, oversized documents and HTTPS
address validation. Their controlled signing metadata and
owned HTTPS/container fixtures do not prove an actual Entra deployment, AKS
federation, consent setup or production portal session-store operation. Check the
current change's verification record for executed results; source alone is not
passing evidence.
