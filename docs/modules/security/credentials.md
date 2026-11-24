---
title: PEM certificate source and native credential ownership
type: document
status: draft
cis:
  stable_id: cislib.document.modules.security.credentials
---

# PEM certificate source and native credential ownership

The optional certificate package loads paired PEM credentials and adopts them in
the existing REST transport. It does not add another HTTP stack, retry owner,
timer, principal cache or certificate authority. Start with the shared
[Core policy](README.md) and [receiving trust](certificates.md).

`CertificatePemTrustOptions` holds absolute paths to public root and authorized
direct-issuer bundles, an optional intermediate bundle, the issuer-domain mapping
key and the explicit revocation policy. `CertificatePemCredentialFiles` holds
separate leaf-first chain and unencrypted private-key paths. These types and
`CertificatePemSecurityOptions` are immutable references: constructors do not read
files, create a provider, import a native private key or contact a network.

```csharp
services.AddCisCertificatePemSecurity("service-mtls", provider =>
    new CertificatePemSecurityOptions(
        new CertificatePemTrustOptions("production-client-issuer",
            "/run/cis/client-roots.pem", "/run/cis/client-issuers.pem"),
        credential: new CertificatePemCredentialFiles(
            "/run/cis/credentials/tls.crt", "/run/cis/credentials/tls.key"),
        serverTrustBundleFile: "/run/cis/server-roots.pem"),
    authenticationScheme: "service-client-certificates");

services.AddCisCertificateRestClient(new RestClientOptions("country-service")
{
    BaseUrl = "https://country-service.example.test",
    LogCalls = true
}, "service-mtls");
```

First register the matching immutable common `SecurityProfile`, including its
enabled roles, exact local application/environment-to-credential mapping and
nonempty HTTPS origins. The source uses the actual root's `SecurityPolicySnapshot`
and existing `EnvironmentOptions`; it does not override local identity. The
credential subject is the configured trust domain plus exact URI SAN.
`AddCisCertificateRestClient` retrieves the same provider-owned binding and stable
transport for every intentionally sharing alias. Equivalent newly constructed
bindings do not establish borrowing ownership.

The scheme argument is required exactly when the common profile enables inbound
authentication. An inbound-only profile does not require, read or import an
outgoing leaf/key. An outbound-only profile does not install a receiving scheme.
Configuration-backed factories must construct these complete immutable options
inside `IConfigurationReader.Read`; they must not retain a mutable settings root.

## Initial trust and listeners

Existing sequential Startup reads and copies public trust once. Every PEM trust
initializer runs before receiving cross-profile isolation checks. Invalid initial
trust/role configuration fails preparation; a missing or invalid initial outgoing
leaf reports unavailable and can recover on a later source check. Required health
policy controls whether that unavailable credential blocks readiness.

Use `https.UseCisCertificateProfileTrust(kestrelOptions.ApplicationServices,
"service-mtls")` during ordinary Kestrel setup to apply the prepared receiving
profile. The server certificate/key remain application-owned. Before the initial
snapshot is ready, that TLS binding denies authentication. Anonymous probes use
their separate actual listener. Native options can be created before Startup
without reading trust files or accepting a ticket; their empty custom-root
baseline is invalidated once when the initial snapshot is prepared. This keeps
the native sole-scheme behavior from breaking pre-ready operational requests.

Client issuer trust remains separate from outgoing server trust. A null
`ServerTrustBundleFile` explicitly chooses native system server trust; an explicit
bundle captures private server roots. Native hostname and server-authentication
usage checks remain mandatory. The transport's
`CreateCertificateBoundUninitializedTrust` /
`InitializeServerTrustOnce` seam allows this initial preparation before any
material factory or generation exists. It is not a live setter. Ordinary
`CreateSecurityBound` still immediately selects native system trust when its
server-trust argument is null.

Roots, authorized issuers, mappings, origins and grants do not reload. Replacing
their files does not alter a serving instance's snapshot. Replace instances to
adopt trust or grant changes; stop every old serving instance to finish an
emergency removal. Local development reset has the same adoption requirement.

## Bounded capture and adoption

For authenticated configuration loading before the application's provider exists,
capture an independent policy with `SecurityPolicySnapshot.Create(options)`, then
call `CertificatePemBootstrapPreparation.PrepareAsync(policy, profile, pemOptions)`.
This explicit asynchronous operation validates fixed trust, captures a coherent
PEM pair and prepares the actual native TLS leaf. It fails when no usable initial
credential exists. Expected file/material failures expose a safe authentication
error; actual cancellation and unexpected faults retain their own outcomes.

The application owns the preparation through failed preparation/Build cleanup.
Construct standalone `HttpRestClient` instances with `preparation.Binding` and
borrow `preparation.Transport`. Dispose those clients and await
`RetireBootstrapAsync()` before activating final Startup. Retirement closes new
bootstrap dispatch and joins actual response/body/native-key cleanup; cancellation
of a caller's wait cannot replace that barrier. No temporary service provider is
needed. An explicitly supplied native key scope remains caller-owned.

Register the exact policy object using `services.AddCisSecurity(preparation.Policy)`
and `services.AddCisCertificatePemSecurity(preparation)`, followed by ordinary
`AddCisCertificateRestClient` registrations. When preparation uses an explicit
clock, register that exact `TimeProvider` before common Security registration.
Passive construction may precede
bootstrap retirement, but activation refuses it until retirement has settled.
Only one final provider can claim the preparation; a rejected provider does not
acquire disposal ownership. The final native transport uses that provider's actual
`IMeterFactory`, the same binding and captured fixed trust. It reads a fresh current
leaf at activation and uses its existing Heartbeat source owner to rotate into
that final transport. The retired bootstrap transport is never registered as a
hosted borrowed transport. A newly owned key scope transfers once to the final
runtime; failed Build leaves it with the preparation. Dispose both through their
actual owners. This local handoff proves no AKS or cert-manager deployment.

The default file-check interval is thirty seconds, using existing Heartbeat.
An explicitly selected interval is bounded from one second to one hour. No
source work runs from a metric scrape. One paired capture permits at most
256 KiB of chain PEM, 64 KiB of private-key PEM and eight chain certificates.
Only supported complete certificate/private-key PEM blocks and whitespace are
accepted. The initial implementation has no PFX/password input.

Both files are read twice within the same bounded capture; changed bytes reject
that capture. Native parsing verifies the matching private key and leaf-first
chain. Native custom-root/direct-issuer validation, exact configured local
credential identity, usages, validity and selected revocation must all pass.
Private byte/character buffers are cleared when the operation settles. A file
replacement must expose a coherent pair; inconsistent intermediate states are
rejected and a later check can adopt the complete replacement.

The stable transport reserves capacity **before** the material factory parses a
private key or prepares its native TLS backend. At most one current and one
draining generation exist. A later actual exchange, including a retry after its
existing delay, leases the then-current generation. Existing response/body owners
retain the previous generation until actual cleanup finishes.

When both slots are occupied, adoption is deferred without constructing another
key. Checks coalesce the newest candidate; there is no candidate history or
unbounded queue. An unchanged rejected/deferred material transition is reported
once. Deferred material is reconsidered when capacity is available. A candidate
with a future `NotBefore` is reconsidered when that time arrives without requiring
a file rewrite. Required online validation that was unavailable is retried at the
configured cadence. A bad replacement keeps a still-valid current credential;
no grace extends its actual expiry.

`ISecurityCredentialControl.RequestSourceRecheckAsync` is an explicit coalesced
signal for the next existing check, including reconsidering unchanged rejected
material. It does not mint/renew a certificate, invalidate remote copies, replay a
denied operation or promise immediate adoption. Stopping rejects new signals.

## Readiness, status and shutdown

`RequiredForReadiness` is the common profile's explicit opt-in, default false.
One profile status combines only its enabled inbound/outbound roles: usable
receiving trust cannot overwrite an unavailable outgoing leaf, and inbound-only
readiness never invents a private-key dependency. A critical existing Healthcheck
condition reflects required availability without changing liveness. Outgoing
readiness also observes fixed client-authority and any explicit server-root expiry;
the certificate gauge continues to report the actual leaf's separate expiry.

Existing Heartbeat also observes already retained expiry metadata at one second
when available. This callback reads the clock, immutable dates and its owned
transport's admission state; it does not parse files, acquire keys, validate
chains or call a credential provider. The remaining
certificate gauge uses the actual active outgoing leaf expiry. Missing leaves are
omitted and expired remaining time is clamped to zero. A failed replacement does
not manufacture a later expiry. The source publishes the existing bounded
adopted/rejected/deferred reload outcomes; it adds no metric streams or labels.
Diagnostics retain a per-profile finite lifetime budget and exclude PEM, private
keys, parser input and unrestricted identities.

ApplicationStopping closes source admission and signals its owned cancellation
before waiting for Heartbeat. Disposal joins accepted source work and callback
cleanup, then the transport's actual response drains, then native key owners.
Native chain work is joined with pre/post cancellation checks; URL timeout control
does not pretend that a synchronous native call can be interrupted midway.
Cancellation, stopping, faults and actual credential rejection remain distinct.

On Windows, the explicitly owned native TLS backend uses a memory PKCS12 roundtrip
with temporary software **user** key backing. It does not use PersistKeySet,
machine keys or install certificate trust. The export buffer is cleared. Production
sources use a finite two-key scope; native qualification uses its one shared
sixteen-key scope and restricted custody journal. The owner retains captured exact
provider/container identity until handler/body drain, disposes the certificate,
removes any remaining exact newly owned container and verifies its backing absence.
It never enumerates or deletes unrelated user keys. This is temporary OS backing,
not a claim that Windows TLS keys stay entirely in process memory. Unexpected
cleanup failures close admission and retain bounded unresolved ownership.

Adoption awaits retirement cleanup that is already eligible. Actual held
responses retain their old generation and do not delay adoption until they
settle. If either cleanup path permanently closes the transport, the profile
becomes `Unavailable` and its required readiness condition is flagged. The
existing one-second metadata Heartbeat observes later held-response failures;
source checks also observe closure before reading or accepting unchanged PEM.
Rechecking the same files cannot reopen that transport. Replace the owned
transport/profile instance after resolving the cleanup fault; its original
exception remains observable from the operation and owned disposal paths.

## Intermediate-issued clients on Windows

Windows sends the owned client leaf through native TLS. Supplying intermediate
certificates in the PEM chain validates the source credential, but does not
promise that Windows transmits those intermediates. Linux uses the supplied
intermediates in its native `SslStreamCertificateContext`. This platform
difference is documented by [Microsoft's SslStream troubleshooting guide](https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-troubleshooting#intermediate-certificates-are-not-sent).

A Windows caller supports a directly root-issued leaf, or an intermediate-issued
leaf whose peer already has the required issuing chain. For a CIS receiving
profile, configure the actual direct issuer in its immutable issuing-authority
bundle and any further required intermediates in its intermediate bundle. A
generic peer that knows only the root and cannot obtain the intermediate cannot
authenticate the leaf-only presentation. Deployment-owned Windows intermediate
provisioning is another arrangement; CIS does not perform that provisioning or
inspect certificate stores.

The transport deliberately avoids constructing a supplied-chain context on
Windows: the [pinned .NET runtime implementation](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Net.Security/src/System/Net/Security/SslStreamCertificateContext.Windows.cs)
can add supplied certificates to machine or user intermediate stores. Source
validation and successful adoption establish the local credential's validity,
not a remote peer's issuer inventory or a successful TLS exchange. Qualify each
deployment's actual peer trust and platform arrangement; adding more certificates
to the caller's PEM file alone does not remove the Windows prerequisite.

`CertificatePemBootstrapPreparation.ActivateFinalAsync(provider, token)` supports
the facade's explicit sequential handoff. It requires completed bootstrap drain,
the same captured policy and clock, and its one actual keyed native runtime.
Ordinary later Startup joins that activation; renewal continues into the final
transport created with the actual provider's meter factory.

See the [cert-manager recipe](cert-manager.md) for deployment responsibilities.
Local native adoption tests cannot establish real cert-manager issuance/mount
renewal or AKS federation. Their roadmap acceptance gaps remain explicit.
