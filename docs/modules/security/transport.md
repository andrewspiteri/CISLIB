---
title: Security-bound REST and native certificate ownership
type: document
status: draft
cis:
  stable_id: cislib.document.modules.security.transport
---

# Security-bound REST

Use a selected security adapter to bind a named REST client to its actual
workload profile. The existing REST runtime continues to own final preparation,
authentication, deadlines, retries, primary dispatch, response consumption and
cleanup. Security does not add an HTTP sender or replay an operation after a
credential change. Read the [shared security contract](README.md) and the
[native HTTP ownership guide](../rest-client/http.md) alongside this boundary.

## Immutable binding and registration

`CIS.RestClient.RestSecurityBinding` identifies one credential profile, its
issuer-qualified credential identity, policy version, credential kind and exact
HTTPS origin set. `RequestAuthentication` uses the final authentication callback;
`ClientCertificate` authenticates at the native TLS boundary. The binding object
itself is the borrowing owner. Equal names, subjects and versions do not permit
a different binding object to borrow a certificate-bearing transport.

Real CIS adapters construct the binding from the exact root
`SecurityPolicySnapshot` and profile alias. This constructor requires the enabled
outbound role and derives its trusted values. `ValidateForPolicy` rejects an
unrelated root snapshot or the low-level metadata constructor. The latter is a
native transport primitive, not profile activation, local identity validation or
caller authentication.

`RestClientOptions.SecurityBinding` supports an explicitly owned standalone
client. Hosted adapters can defer binding and callback creation through
`AddCisSecurityBoundRestClient`; neither factory runs during registration. This
adapter registration example uses only the implemented hosting seam:

```csharp
using CIS.RestClient;
using CIS.RestClient.Hosting;
using CIS.Security;
using Microsoft.Extensions.DependencyInjection;

static IServiceCollection AddWorkloadClient(IServiceCollection services,
    RestClientOptions options, string profile,
    Func<IServiceProvider, Func<RestAuthenticationContext, CancellationToken, Task>> authenticationFactory)
{
    return services.AddCisSecurityBoundRestClient(options,
        root => new RestSecurityBinding(root.GetRequiredService<SecurityPolicySnapshot>(),
            profile, RestSecurityCredentialKind.RequestAuthentication),
        authenticationFactory);
}
```

The selected adapter supplies `authenticationFactory` and owns its credential
lifecycle. Runtime activation invokes each factory once from the final provider,
after Configuration preparation. Inputs are captured during registration;
later mutation of the options object does not change the client. A supplied
authentication factory cannot replace an existing default callback.

Allowed origins are nonempty HTTPS origins without credentials, paths, query or
fragment. Omitted client origins inherit the binding's exact set; an explicitly
supplied client set must equal it. Keep REST-bound profiles within the existing
32-origin REST limit. Core's larger general profile input ceiling does not expand
that REST limit. Registration rejects a base URL outside the bound policy.

Every bound call retains the selected authentication. Per-request
`DisableAuthentication`, replacement `Authenticate`, TRACE and final method
replacement are rejected. Final destination checks also cover absolute URLs and
customization performed during content acquisition. The native transport checks
the destination and disallowed methods again before dispatch. Ordinary unbound
REST clients retain their existing callback override and TRACE behavior.

## Native TLS and separate server trust

`RestHttpTransport.CreateSecurityBound` captures the exact binding, existing root
`IMeterFactory`, finite native pooling options and optional `RestHttpServerTrust`.
The default two-minute connection lifetime remains; an infinite lifetime is
rejected only for bound transports. Native timeouts remain infinite because the
existing REST operation supplies cancellation and its total deadline. Redirects,
cookies, automatic credentials and environment proxies remain disabled.

Server trust is independent of the roots that issue client application identities.
Without an explicit server bundle, native system server trust remains in effect.
`RestHttpServerTrust` copies one to eight public roots and the chosen native
revocation mode. Its native chain policy retains hostname, signature, validity
and server-usage validation. It exposes no accepting callback. Development uses
its isolated server roots and explicit development revocation choice; it does
not install machine-wide trust.

Server trust is captured when the transport is created. Changing the source
list or rotating a client leaf cannot alter it. Trust changes require replacement
of the affected owner under the startup-snapshot rule.

On Linux, client material uses native
[ClientCertificateContext](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslclientauthenticationoptions.clientcertificatecontext?view=net-10.0).
The generation retains the supplied leaf and chain through the native
[certificate context lifetime](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstreamcertificatecontext.create?view=net-10.0).
On Windows it supplies only the owned leaf through native `ClientCertificates`.
Supplying intermediates to a Windows certificate context can add them to OS
certificate stores, even when context creation uses `offline: true`.
CIS does not provision these stores. A Windows deployment must arrange the
required intermediates through its own provisioning or the peer's explicit
issuer bundle. See the [pinned native context implementation](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Net.Security/src/System/Net/Security/SslStreamCertificateContext.Windows.cs).

Windows Schannel can reject certificates backed only by ephemeral private keys.
A material provider must choose a supported native key backend and own its
cleanup; the REST transport does not silently persist or reimport a key.
Temporary OS user-key backing is a separate operational/storage decision from
certificate-store membership. See Microsoft's
[SslStream troubleshooting](https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-troubleshooting#handshake-failed-with-ephemeral-keys).

`RestHttpNativeCertificate` takes ownership of a supplied leaf without I/O.
Explicitly call `PrepareForTls(scope)` inside an admitted certificate factory,
after the transport has reserved generation capacity. On Windows this uses a
zeroed memory PKCS#12 buffer and temporary software user-key backing. It never
opens a certificate store. Other platforms retain the original native key.
Then transfer it with `RestHttpCertificateMaterial.FromNativeCertificate(owner)`;
a failed transfer leaves the owner with the caller. A Kestrel server can borrow
`owner.Certificate` and retain the owner until the actual server has drained.

`RestHttpNativeKeyScope` limits related native owners to an explicit one-to-sixteen
active imports, reservations or unresolved cleanup entries. It is a local owner,
not a global key pool. An optional custody callback receives `Created`/`Removed`
and exact newly owned provider/container/file metadata; it reads no key contents
and inventories no existing keys. Local qualification shares one sixteen-slot
scope and persists its journal under restricted `.artifacts/security`. Its first
isolated probe uses two slots. Each backing is limited to 512 KiB; the controller
charges the conservative 8 MiB allowance within the existing physical ceiling.
Production callers own their finite scopes and custody policy independently.

Disposal verifies absence of only the exact newly owned key file after native
certificate disposal. Unidentified backing, custody callback failure or cleanup
failure closes scope admission; unresolved entries remain bounded. Abnormal
termination can leave an owned container behind and requires explicit restoration
from its journal. No directory-wide key cleanup or implicit persistence flag is
used. The local operational exception and its limits are recorded in
[W01](../../changes/CIS-0016/windows-tls-key-backend-decision.md).

## Certificate generations and source ownership

A certificate-bound transport initially has no handler or usable leaf. The
certificate source calls `TryAdoptCertificateAsync` with its material factory.
The transport reserves capacity before invoking that factory, including before
the source constructs a private key. A `Deferred` result invokes no factory and
retains no candidate queue. The source coalesces a recheck of its newest files.

The factory returns `RestHttpCertificateMaterial`: one owned leaf/private key and
copied public intermediates, at most eight certificates in total. Successful
material construction transfers leaf ownership; constructor failure leaves it
with the caller. The adapter validates the complete expected identity, chain,
usage and revocation policy before returning material. A factory that throws
must clean resources acquired before returning. Returned material is owned by
the transport even when cancellation or publication failure follows.

The transport rejects disposed, not-yet-valid or expired candidates and keeps the
last usable generation. It never extends expiry. Successful adoption creates a
new native pool and atomically publishes it. The previous generation stops
admitting new exchanges. At most one current and one draining generation are
live; the single construction reservation consumes the remaining capacity.

Each actual exchange selects and leases the current generation. A later REST
retry selects again after its delay under the original operation deadline. No
generation reference survives that delay. Actual response/body ownership keeps
the selected pool and key alive until settlement. Old work admitted before
expiry may finish; new selection and the primary boundary check actual expiry
and fail closed when no usable leaf remains.

`CertificateExpiresAtUtc` and `LiveGenerationCount` read bounded owned state
without source I/O. They are diagnostic state, not identity proof. The source
owns availability/recheck policy and the existing Heartbeat registration;
the transport does not create a scheduler or renew certificates.

After publishing a replacement, adoption awaits old-generation cleanup when no
response/body holds remain. A cleanup fault surfaces as its original fault;
adoption cannot report healthy success after that fault has closed admission.
When an old response is still held, adoption does not wait for its body. Its later
release owns retirement cleanup and surfaces any actual cleanup fault.

`IsAdmissionClosed` reads terminal native admission state without waiting for
those retained owners. `ThrowIfAdmissionClosed()` preserves the original retained
cleanup fault, or reports disposal when normal closing began. A known leaf expiry
does not establish that the transport accepts exchanges. The PEM source checks
admission before source work and unchanged-material shortcuts and before recording
successful adoption. A terminally closed transport makes its profile unavailable
and flags a readiness-required profile. The existing one-second expiry-metadata
Heartbeat observes failures that arise later during body retirement; no extra
worker or retry owner is created. Unchanged files and source-recheck requests
cannot reopen that transport. Replace the affected owner/instance deliberately.

## Stop, cleanup and result detail

Stop the logical REST clients and certificate source before disposing a borrowed
transport. Transport disposal closes new exchanges/adoption and joins actual
construction and response owners. It does not cancel another borrower's work.
A canceled external wait is not cleanup completion. An owned transport is disposed
by its runtime; an explicitly borrowed one remains with its application owner.
Borrowing checks require the same binding object, exact origins and root meter
factory, including for a credential-free borrower.

Retired pools and keys are disposed only after their actual holds reach zero.
Unexpected generation-cleanup failure closes admission and is retained for the
shared disposal result. Closing prevents further adoption, so faults remain
bounded by the owned generations/reservation rather than an error history.
Construction/exchange and cleanup faults preserve both causes; programming
faults do not become ordinary permission denials.

Known callback acquisition failures use `RestAuthenticationContext.FailWithSecurity`.
The outer result remains `Authentication` / `client.authentication_failed` with
safe typed `RestResultContext.SecurityFailure`. Reliable local causes remain local;
put a safe provider code in the typed detail instead of inventing a remote-server
diagnosis. Native TLS failures can have no HTTP status. Arbitrary remote 401/403
retain their actual HTTP result and report `CauseUnknown`, rather than fabricate
`CapabilityDenied` or expired-certificate evidence. The original cancellation,
deadline and stopping outcomes retain precedence and do not inherit stale security
detail when they supersede an earlier failure.
