---
title: Certificate receiving and client issuer trust
type: document
status: draft
cis:
  stable_id: cislib.document.modules.security.certificates
---

# Certificate receiving and client issuer trust

`CIS.Security.Certificates` is an optional package for native certificate
authentication. Installing it selects nothing and certificate-only applications
do not acquire Azure dependencies. Shared caller, capability, audit and failure
contracts remain in [Core](README.md). The receiving integration needs public
client trust and mappings, not an outgoing private key or certificate source.

Use direct mutual TLS. This adapter does not consume certificate-forwarding
headers, a claimed application header, a certificate display name or a serialized
caller context as authentication. A caller certificate identifies an application;
it does not establish a human actor or a verified replica. Instance evidence is
`NotProvided` unless a separate supported integration establishes it.

## Explicit trust and the native scheme

Create one immutable `CertificateTrustPolicy` with:

- `trustDomain`: the configured issuer-domain part of `SecuritySubject`.
- `roots`: explicitly trusted public client CA certificates.
- `issuingAuthorities`: the exact authorized **direct issuers** of client leaves.
  A root can explicitly be its own issuing authority for directly issued leaves.
- Optional `intermediates`, which assist native chain construction and do not
  authorize an additional issuer.
- The explicit revocation policy and, for ShortLived, its actual lifetime ceiling.

The constructor copies public DER and holds no private keys. Caller-owned input
collections/certificates remain caller-owned. Roots and issuers must have CA
constraints and certificate-signing key usage. Startup verifies their current
validity and that each authorized issuer actually chains to the selected roots.
No trust network retrieval occurs in registration or constructors. Host-owned
native options retain their own imported public certificates until DI disposal.

The runtime validates a non-CA leaf, explicit digital-signature key usage,
explicit client-authentication EKU, current validity, native chain signatures and
constraints, exact direct issuer and exactly one URI SAN. URI text is compared
as the original IA5String, without decoding, normalization, trimming or case
folding. Duplicate or conflicting URI SANs deny. Configure an organization-owned
URI namespace and restrict issuance of each exact application/environment URI.
The receiver's trusted mapping remains authoritative; URI fragments do not
become privileges. Public system roots do not confer application identity.

```csharp
using CIS.Security;
using CIS.Security.Certificates;
using CIS.Security.Certificates.Hosting;
using CIS.Security.Hosting;

// Public CA material is deployment-owned. Keep server trust separate.
var clientTrust = new CertificateTrustPolicy(
    "production-client-issuer",
    roots: [clientRoot],
    issuingAuthorities: [clientIssuingCa]); // Online is the default.

var caller = new SecuritySubject("production-client-issuer",
    "https://identity.example.test/applications/country-worker/environments/production");
var policy = new SecurityOptions();
policy.Profiles.Add(new SecurityProfile("service-mtls",
    SecurityProfileKind.ClientCertificate, SecurityProfileRoles.Inbound,
    authenticationScheme: "service-client-certificates",
    identityMappings: [new(caller, "country-worker", "Production")]));
policy.Capabilities.Add("country.read");
policy.Grants.Add(new("service-mtls", "country.read", "country-service", "Production",
    subject: caller));

services.AddCisSecurity(policy);
services.AddCisCertificateAuthentication("service-mtls", "service-client-certificates",
    new CertificateInboundOptions(clientTrust));
```

The scheme name is explicit at registration and must equal the prepared common
profile's scheme. `AddCisCertificateAuthentication` also accepts a deferred
`Func<IServiceProvider, CertificateInboundOptions>`. It is evaluated after common
policy preparation. When using Configuration, construct the complete immutable
trust/options inside `IConfigurationReader.Read`; do not retain a mutable root
and copy it after leaving the reader.

Native `AuthenticationBuilder.AddCertificate` supplies authentication. Common
services use the minimal authentication services; the adapter does not add Data
Protection, a principal cache or another service provider. Do not add
`AddCertificateCache`. Native options validation rejects replacing the mandatory
strict event or forwarding authentication to another scheme. A failed selected
profile never selects another profile.

The mandatory `OnCertificateValidated` event completes the selected native trust
validation and exact mapping check **before** issuing a native ticket. Its private
request evidence is required by the Core projector. A native `NoCheck` baseline
alone cannot establish identity. Additional native authorization requirements
and complete CIS grants must still pass before protected work.

## Actual listeners and operational access

Configure Kestrel's business TLS listener with
`https.UseCisClientCertificateTrust(clientTrust)` and an application-owned server
certificate. The helper requires a client certificate and performs the same
strict native validation during the actual handshake. It exposes no arbitrary
accepting callback. Configure a separate operational listener where probes need
anonymous access; native client-certificate negotiation applies to a whole TLS
connection and cannot be enabled by one later path.

`CertificateListenerPolicy` takes exact `CertificateListenerBinding` values:
local IP, port and business profile; operational bindings have no profile. Use
`listeners.SelectProfile` in `AddCisSecurityWeb`. Host and forwarded headers never
select a profile. Unknown listener bindings deny through the isolation middleware.
Business listeners require HTTPS. On operational listeners, isolation permits
only the configured exact reserved paths and rejects native-authorized endpoints,
even if one is mistakenly assigned a reserved path.

```csharp
var listeners = new CertificateListenerPolicy(
    [new(IPAddress.Loopback, 8443, "service-mtls")],
    [new(IPAddress.Loopback, 8080)]);
services.AddCisSecurityWeb(options => options.SelectProfile = listeners.SelectProfile);

// Direct native hosts: after UseRouting, before security and operational branches.
app.UseCisCertificateListenerIsolation(listeners);
app.UseCisSecurityAuthentication();
// Install existing Healthcheck/collector branches and the existing readiness gate here.
app.UseCisSecurityAuthorization();
app.MapGet("/countries", ReadCountries)
    .RequireCisCapabilities("service-mtls", new("country-service", "Production"), "country.read");
```

The default operational paths match existing module defaults: `/_health`,
`/_health/ready` and `/_metrics`. This policy does not install these modules or
invent successful probes. Supply explicit paths for configured alternatives.
Detailed health remains opt-in with its existing authenticated predicate; adding
an anonymous operational path is not authorization for details. Keep any branch
specific access rules from the existing integrations.

The [facade](../application/core.md) already installs authentication before health
and readiness, and business authorization afterward. Select actual listeners in
its services callback, configure Kestrel through `builder.WebHost`, and apply
listener isolation in the web mapping callback for business endpoints. Direct
native hosts also enroll the existing sequential `AddCisStartup` after dependent
hosted services. The integration does not add another lifecycle owner.

## Revocation, expiry and failures

| Policy | Actual behavior |
| --- | --- |
| `Online` | Default. Native online revocation, including platform-valid cached information; unknown or unavailable required status denies. |
| `ShortLived` | Explicit production choice. No online revocation; actual `NotAfter - NotBefore` must be at most 24 hours, or the smaller configured positive maximum. |
| `Development` | No online revocation, only with LocalDevelopment and all development gates. The local seven-day leaf default does not loosen production ShortLived. |

There is no fallback between policies. The native Certificate handler's baseline
uses custom roots and `NoCheck` because its options expose no URL timeout. The
mandatory strict event performs the **selected** revocation check using native
`X509Chain`, a five-second `UrlRetrievalTimeout`, no ignored verification errors
and disabled AIA issuer downloads. Disabling AIA does not disable selected online
CRL/OCSP processing. The timeout is the native URL-retrieval control, not a hard
five-second wall limit for the whole chain operation. Native synchronous work is
joined; cancellation is checked before and after it, without abandoning a task
or pretending the native call can be interrupted midway. The handshake uses the
same policy. See the pinned [native Windows implementation](https://raw.githubusercontent.com/dotnet/runtime/v10.0.12/src/libraries/System.Security.Cryptography/src/System/Security/Cryptography/X509Certificates/ChainPal.Windows.BuildChain.cs)
and [native timeout contract](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chainpolicy.urlretrievaltimeout?view=net-10.0).

Every request authenticates without a principal cache. Actual leaf expiry is
also enforced by Core at each protected operation, including later operations
on an already established connection. No grace extends certificate validity.
An invalid remote leaf does not mark the receiver's own trust policy unavailable.
Receiving-only availability does not invent an outgoing expiry. Combined-role
credential availability belongs to the outgoing source owner.

Native certificate rejection may be HTTP 403. A TLS failure may produce no HTTP
response. Neither observation alone proves capability denial; preserve the actual
native result and any separately established local category. The reusable
`CertificateTrustValidator.Validate(leaf, trust, utcNow)` returns bounded safe
`CertificateValidationResult` facts for owned source integration. Its leaf digest
is diagnostic, never the stable subject/grant key.

ShortLived cannot revoke copies immediately. Online validation also has cache and
propagation limits. Record the chosen policy and the issuer's real revocation
support in deployment configuration. Trust, mappings and grants are immutable
startup policy: replace instances to adopt changes. Emergency removal is complete
only when every serving instance with superseded trust/grants stops admitting
work; already admitted operations retain their existing ownership/deadline.
Mounted-secret replacement or deletion does not revoke a remote copy.

## Isolated development

Choose `SecurityProfileKind.LocalDevelopment`, a Development trust policy and
`CertificateDevelopmentGate`. It requires the exact non-unknown canonical
`EnvironmentOptions.EnvironmentName` and an explicitly permitted actual native
host environment; native Production is rejected. `EnvironmentKind` is not a
development gate. Both conditions are necessary; neither headers nor a profile
flag replace them.

Loopback is the receiving default. A container scenario can explicitly list at
most sixteen exact private listener IPs; public addresses are rejected. Incoming
requests are checked against actual `Connection.LocalIpAddress`. Production and
development profiles in one host cannot share a root. Deployment must exclude
development CAs/mappings/grants from production hosts and keep workspace trust
isolated. Outgoing development origins and server trust remain separately
constrained by the outgoing binding; receiving trust is never a substitute.

This guide covers receiving trust. PEM acquisition/adoption, the local setup/reset
tool and deployment recipes have separate owning outcomes. Generated local trust
must not be installed into machine-wide stores. Native local TLS tests do not
prove AKS federation, cert-manager issuance or a cluster deployment.
