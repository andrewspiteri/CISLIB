# RM-01 Application identity and security: intake

The exact supplied [specification](sources/CISLIB_RM01_Application_Identity_and_Security_Specification_v1.0.md)
is preserved byte for byte, SHA-256
`d4966a3ad3d1a870d91422a35cc8467cf780d01a9f91cbc3313e5b3fe812ca36`.
Its handoff, agent instructions and proposed APIs are requirements/reference
material; they do not grant execution, dependency, credential or deployment
authority. The initial request started intake. Andrew Spiteri then explicitly
approved P01–P16, seven tasks, the optional dependency profile and one shared
30-hour/32-attempt allocation including 3,600 restoration seconds on19November2026.

CIS registered [CIS-0016](../../docs/changes/CIS-0016/proposal.md) on
`codex/application-identity-security`, baseline
`b53b4eaef6b3374e7387e9bbb065b57533248445`. The specification reviewed
`31739a247efba609f27dd50caab1312bb775bd79`; subsequent changes add the separate
CountryData unit-example project, guides and CIS records. They do not change the
runtime integration points. Preserve that example project under the solution's
examples folder and retain the user's untracked launch settings unchanged.

The [bounded implementation plan](../../docs/changes/CIS-0016/implementation-plan.md)
contains the approved P01–P16 profile, seven coherent implementation outcomes,
acceptance ownership, dependency decision and shared resource limits. At intake,
no native restore, build, test, credential generation or infrastructure operation
had run. Subsequent observations belong to their owning implementation tasks.
All earlier work owners remain complete with their original costs and evidence.

## Reconciliation with current contracts

| Concern | Proposed mapping |
| --- | --- |
| Identity | Project getter-only EnvironmentOptions values without a second resolver. Preserve existing defaults and 255-Unicode-scalar application/environment bounds; compare trusted values ordinally and case sensitively. EnvironmentKind is not a development gate. |
| Trust/configuration | Deep-copy the entire policy under IConfigurationReader.Read into one validated immutable startup snapshot. Trust, mappings, grants and profile selection change by instance replacement; leaf material alone reloads. |
| Caller authority | A scoped read-only accessor is established/restored only by native authentication/host integration. Constructed or serialized CallerContext remains data. No writable consumer setter, unrestricted principal in portable contracts or ambient request authority for background work. |
| Authorization | One native requirement/handler implements complete-grant matching for endpoints and imperative calls. The authoritative target and every required capability must pass, together with extra native requirements. Observe the final composite decision once. |
| Cancellation | Native AuthorizeAsync has no cancellation argument. Check before/after and pass the token to CIS handlers through the operation resource; await actual settlement. Existing REST remains the deadline/retry owner. |
| Host order | Current ConfigureWeb runs before Healthcheck/readiness but cannot stage security around every integration. Add narrow explicit authentication/projection and post-readiness authorization stages, preserving existing integrations with no-op defaults. No new middleware framework or provider. |
| HTTP transport | Keep RestHttpTransport as the stable owner; add bounded internal handler generations. Every actual exchange selects/leases a usable current generation, including after retry delay. Release follows actual response/body cleanup. |
| Binding safety | Bind profile identity and exact HTTPS origin policy together. Security clients reject per-call authentication suppression/replacement and TRACE. Validate borrowed bindings and enforce final origins at the transport seam. Unbound behavior remains unchanged. |
| Shutdown | The facade drains REST and Heartbeat before native hosted Stop. Security must cut off admission and signal source cancellation on ApplicationStopping, then join source work, generation drains and owned credentials; a late hosted Stop alone would be unsafe. |
| Entra | Optional Azure.Identity WorkloadIdentityCredential and native JWT bearer handler. Explicit workload credential only, SDK cache ownership, one tenant/audience per scheme, exact app-only mapping/roles, no delegation or CLI fallback. |
| Certificates | Optional native certificate handler; custom client trust, explicit EKU/key usage/non-CA leaf, exact issuer-authorized URI SAN and per-operation expiry. Keep server trust separate; no principal cache or accept-any callback. |
| Development | Reuse the certificate adapter. Explicit native/canonical environment gates and isolated per-workspace trust; one small native C# setup/reset tool, process-specific trust, no machine trust installation. PEM-only initial scope; optional PFX is omitted. |
| Metrics | Three bounded security instruments with central application/environment labels, final-composite authorization counts and state-transition reload counts. Conditional catalog selection preserves hosts that do not select security. |
| Consumer | Extend the one CountryData tutorial with explicit protected-service/caller profiles and permit/deny effects. Reuse existing ordinary native/public/package test facilities; no new tutorial application or test harness. |
| Evidence | Local native JWT/TLS and real file replacement can prove those boundaries. They cannot establish real AKS federation or cert-manager issuance/renewal. AC-02/AC-24 remain unverified without authorized infrastructure; roadmap RM-01 remains incomplete. |

The native authorization boundary follows
[policy composition](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0),
[resource-based checks](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resource-based?view=aspnetcore-10.0)
and the actual
[AuthorizeAsync signature](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.iauthorizationservice.authorizeasync?view=aspnetcore-10.0).
Native certificate authentication can fail with HTTP 403, while TLS may fail before
HTTP; the handler also needs stricter explicit EKU validation for this contract.
See [native certificate authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/certauth?view=aspnetcore-10.0).
These references explain platform behavior, not approval of new dependencies.

## External acceptance prerequisites

- AC-02 needs an authorized AKS workload-identity environment: OIDC federation,
  service account/projected assertion, tenant/client identity, API audience,
  app-role assignment and reachable receiving service.
- AC-24 needs an authorized Kubernetes namespace, cert-manager/issuer and
  issuance controls, mounted Secret delivery and observed application adoption
  followed by a new authenticated connection.

No such environment or deployment authority has been supplied. Preserve these
acceptance requirements and document reproducible steps. Local signed-token,
SDK-transport and PEM-replacement tests must retain their narrower meaning.
