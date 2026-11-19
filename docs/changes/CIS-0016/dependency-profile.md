---
title: RM-01 proposed optional dependency profile
type: architecture
status: Approved
change_id: CIS-0016
---

# Approved optional dependency profile

Andrew Spiteri approved this direct-pin and finite-mapping decision with P01–P16
and the shared allocation on 19 November 2026. Central pins/source mappings are
now authored; actual optional package restore/deployment evidence remains pending
its owning provider/consumer tasks. This approval is not a restore or live-provider
proof.

Core retains its approved two-NuGet closure and Microsoft.AspNetCore.App reference.
SDK 10.0.401 and qualification runtime 10.0.12 remain unchanged. Optional provider
registration is inert until a profile/role/binding is selected. Certificate-only
consumers must not acquire Azure SDKs.

| Shipping adapter | Direct package | Proposed central pin |
| --- | --- | --- |
| CIS.Security.Entra | Azure.Identity | 1.21.0 |
| CIS.Security.Entra | Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.12 |
| CIS.Security.Certificates | Microsoft.AspNetCore.Authentication.Certificate | 10.0.12 |

These versions are verified from official metadata and cached manifests:
[Azure.Identity](https://www.nuget.org/packages/Azure.Identity/1.21.0),
[JWT bearer](https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.JwtBearer/10.0.12)
and [certificate authentication](https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.Certificate/10.0.12).
The two handlers are separate NuGets; the existing framework reference alone does
not supply them. Matching the selected runtime takes precedence over newer patch
availability. Do not change Core or the toolchain to use a newer optional package.

## Finite source-mapping candidates

The following 27 exact IDs are the proposed NuGet.config allowlist expansion.
Versions describe the inspected graph's selected direct versions or declared
transitive lower bounds; this table is not a generated restore lock. The first
approved restore must establish and review the actual net10.0 graph and deployed
assets. No wildcard source mapping is proposed.

| New package IDs | Version / inspected lower bound |
| --- | --- |
| Azure.Identity | 1.21.0 |
| Azure.Core | 1.53.0 |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.12 |
| Microsoft.AspNetCore.Authentication.Certificate | 10.0.12 |
| Microsoft.Identity.Client; Microsoft.Identity.Client.Extensions.Msal | 4.83.1 |
| Microsoft.IdentityModel.Abstractions; Microsoft.IdentityModel.JsonWebTokens; Microsoft.IdentityModel.Logging; Microsoft.IdentityModel.Protocols; Microsoft.IdentityModel.Protocols.OpenIdConnect; Microsoft.IdentityModel.Tokens; System.IdentityModel.Tokens.Jwt | 8.19.2 |
| System.ClientModel | 1.10.0 |
| System.Memory.Data | 10.0.3 |
| Microsoft.Bcl.AsyncInterfaces | 10.0.3 |
| Microsoft.Bcl.Cryptography | 10.0.2 |
| System.Security.Cryptography.ProtectedData | 4.5.0 |
| Microsoft.Extensions.Configuration.Abstractions; Microsoft.Extensions.Hosting.Abstractions; Microsoft.Extensions.Diagnostics.Abstractions; Microsoft.Extensions.FileProviders.Abstractions | 10.0.3 |
| Microsoft.Extensions.Caching.Memory; Microsoft.Extensions.Caching.Abstractions | 10.0.12 |
| System.Memory | 4.5.0; expected framework-prunable |
| System.Threading.Tasks.Extensions | 4.6.3; expected framework-prunable |
| System.ValueTuple | 4.5.0; expected framework-prunable |

Existing mappings already cover Logging/DI/Options/Primitives and DiagnosticSource.
The native reference-pack overrides list Microsoft.Extensions at 10.0.0, so do not
assume newly required 10.0.3/10.0.12 packages disappear during pruning. The existing
Core-only package exception and actual closure remain authoritative. Additional
IDs, changed direct pins or a material graph incompatibility require a specific
decision before expanding the approved profile.

Supporting primary manifests are
[Azure.Core](https://www.nuget.org/packages/Azure.Core/1.53.0),
[System.ClientModel](https://www.nuget.org/packages/System.ClientModel/1.10.0),
[OpenIdConnect](https://www.nuget.org/packages/Microsoft.IdentityModel.Protocols.OpenIdConnect/8.19.2),
[Tokens](https://www.nuget.org/packages/Microsoft.IdentityModel.Tokens/8.19.2)
and [Caching.Memory](https://www.nuget.org/packages/Microsoft.Extensions.Caching.Memory/10.0.12).
The caching dependency does not select a certificate/principal cache in CIS.

## First approved project restore

The first net10.0 restore uses the approved pins without another mapping. The
Entra lock resolves17package nodes: the two direct packages; Azure.Core1.53.0;
Microsoft.Bcl.AsyncInterfaces10.0.3; Microsoft.Bcl.Cryptography10.0.2; MSAL and
its Extensions.Msal4.83.1; the seven IdentityModel/System.IdentityModel packages
at8.19.2; System.ClientModel1.10.0; System.Memory.Data10.0.3; and
System.Security.Cryptography.ProtectedData4.5.0. Runtime assets select net10.0
where supplied, native MSAL net8.0, AsyncInterfaces netstandard2.1 and
ProtectedData netstandard2.0. No unexpected package ID or changed direct pin is
required. The remaining finite mapping candidates are unused by this graph.

The certificate project's lock contains only its direct certificate handler;
it acquires no Azure, MSAL or IdentityModel package. Core and CommonTesting locks
have no semantic changes. The ordinary public MSTest provider project restores
both explicitly referenced adapters. These are project/asset observations,
retained under .artifacts/security/CIS-0016/providers/01-restore; WORK-007 still
owns actual packaged-consumer separation proof. No build, JWT, TLS or deployment
success is inferred from this restore.

## Deployment recipe pin

Propose cert-manager v1.21.3 for documentation, with explicit private-key rotation
and issuer/issuance policy. The official
[supported-release table](https://cert-manager.io/docs/releases/) lists the 1.21
line and Kubernetes 1.33–1.36; the
[1.21 release notes](https://cert-manager.io/docs/releases/release-notes/release-notes-1.21/)
identify v1.21.3. This selects a reproducible guide, not an installation or live
deployment. The actual target cluster must satisfy that support relationship.
