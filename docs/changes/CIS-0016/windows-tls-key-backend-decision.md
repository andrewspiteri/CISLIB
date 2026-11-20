---
title: W01 — Windows native TLS key-backend decision
type: decision-proposal
status: Approved
change_id: CIS-0016
---

# Approved W01 execution exception

Permit the Windows-only native TLS bridge below for freshly generated test/setup
leaves and the approved PEM credential source. This is a proposed correction to
the rule confining all private credential material to restricted
`.artifacts/security`. Andrew Spiteri explicitly approved W01 on19November2026; no native import had been executed at that approval checkpoint. Existing shared
30 active hours, 32 logical attempts, 3,600 restoration seconds, physical 6 GiB
ceiling and protected 64 MiB reserve remain unchanged.

The recorded [authorization](authorization.json) currently says:

> Local native test/setup credentials may be generated only as isolated restricted ignored material under.artifacts/security, with no machine trust installation, shared private keys or access to existing user credentials.

Approval appended CIS decision DEC-001 as a W01 user decision for the narrow OS-managed temporary
backing exception. It must preserve the baseline authorization and completed
WORK-001 authority/receipts historically; it must not rewrite them as though this
exception had already applied.

The exception would permit only temporary, newly owned OS user-key containers,
accounted within that same ceiling. It would not permit certificate-store
installation, machine-key storage, existing-credential inspection, cloud access,
or a persistent key pool. PEM remains the public input format; a memory-only
PKCS#12 conversion is a native backend bridge, not support for PFX input files.

## Observed blocker and source evidence

The first WORK-002 native proof built cleanly and passed 15 new unit cases.
Its integration result was five passed and nine failed; several real TLS paths
failed before an HTTP response with a Schannel credential-acquisition error.
These negative results do not establish that the successful TLS baseline works.
The original receipt is retained under
`.artifacts/security/CIS-0016/WORK-002/01-native-proof`.

Fresh `CopyWithPrivateKey` leaves currently have ephemeral Windows keys.
Microsoft documents Schannel's
[ephemeral-key limitation](https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-troubleshooting#handshake-failed-with-ephemeral-keys).
The pinned runtime's
[certificate private-key implementation](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Security.Cryptography/src/System/Security/Cryptography/X509Certificates/CertificatePal.Windows.PrivateKey.cs)
uses an ephemeral CNG copy when a supplied ECDSA or RSA key has no persisted
container. Switching these fixtures to an unnamed legacy CSP therefore does not
establish a confined-key workaround.

## Approved bridge and bounds

1. Reserve transport generation capacity before invoking the credential factory,
   as already implemented. Before a Windows key import, reserve one tracked
   temporary-key slot and its counted storage allowance.
2. Export only the newly owned, already validated leaf/key into a private byte
   buffer. Import from that buffer with
   `X509CertificateLoader.LoadPkcs12(..., X509KeyStorageFlags.UserKeySet)`;
   omit `MachineKeySet`, `PersistKeySet` and `EphemeralKeySet`. Zero the buffer in
   `finally`, dispose the original temporary leaf, and transfer the imported
   certificate to its existing generation/server owner. Never write a PFX file
   or log certificate/private-key/parser inputs.
3. Capture the exact newly created key's provider/container identifier from its
   owned native handle for restoration accounting. Keep that non-secret ownership
   journal under restricted `.artifacts/security`; do not enumerate an OS key
   directory or certificate store. Refuse an unidentifiable backend instead of
   broadening cleanup to unknown files or containers.
4. Start with one isolated native probe importing at most two owned leaves.
   Charge a fixed conservative 8 MiB from the existing counted ceiling for OS
   backing and cap later
   serial native invocations at 16 simultaneously live imported leaves. Stop
   creating keys when the allowance or slot limit cannot be proved available.
   These are approved finite caps, not an additional storage pool or an increase
   to the 6 GiB ceiling. The controller must record actual owned backing and
   residual storage before it extends the probe to the existing test selections.
   The fixed charge includes only newly owned backing; do not inspect existing
   key-file contents or certificate stores to establish a baseline.
5. Dispose only after actual native work and generation/body owners drain.
   Check normal removal using only recorded owned identifiers; cleanup failure
   fails the proof and leaves a restoration record. An abnormal process exit can
   leave backing behind. Subsequent restoration may address only those recorded
   newly owned containers, with no directory-wide or existing-credential cleanup.

The pinned
[loader implementation](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Security.Cryptography/src/System/Security/Cryptography/X509Certificates/X509CertificateLoader.Windows.cs)
maps `UserKeySet` to user storage and requests key-container deletion when neither
persistence nor ephemeral flags are set. This is temporary OS storage rather
than purely in-memory storage, and cleanup must be observed rather than assumed.
Microsoft lists user CNG backing under `%APPDATA%\Microsoft\Crypto\Keys` and
legacy RSA backing under `%APPDATA%\Microsoft\Crypto\RSA\<SID>` in
[key storage and retrieval](https://learn.microsoft.com/en-us/windows/win32/seccng/key-storage-and-retrieval).
Those locations are outside the currently approved credential scratch boundary.

## Certificate stores are a separate boundary

The pinned
[Windows certificate-context constructor](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Net.Security/src/System/Net/Security/SslStreamCertificateContext.Windows.cs)
can add supplied intermediates to the LocalMachine Intermediate CA store, then
fall back to CurrentUser. `offline: true` does not prevent these additions.
W01 requests **no authority for those writes**.

The REST implementation now supplies the owned leaf alone through Windows
`ClientCertificates`; Linux retains its supplied memory-chain context. Windows
intermediates are a deployment/peer-verifier provisioning prerequisite. The
direct-root native fixtures have no intermediate or AIA dependency. Neither the
probe nor the implementation may install a trust anchor or intermediate.
This does not claim control over every pre-existing OS chain-cache behavior.

## Verification and unresolved obligations

Before broader native tests, the isolated probe must prove an actual loopback
TLS handshake, exact newly owned key custody, normal disposal/removal, bounded
accounting and no requested certificate-store installation. Preserve both the
original failure and the new probe result. Failed cleanup stops further key
creation. Do not mask the failure with an accepting TLS callback or skip it.

If this exception is declined, the Windows TLS paths remain unverified under the
current confined-credential rule; unaffected unit and noncredential work can
continue. Linux in-memory ownership and real Windows behavior remain distinct
claims. AC-02 AKS federation, AC-24 cert-manager deployment and release
qualification still require their actual environments and authority.

## Approval history

- 19November2026: Andrew Spiteri selected **Approve W01 exception** in the chat
  against this concrete proposal. CIS-0016 DEC-001 records the same selected
  option and exact execution bounds. The pending question is resolved.
- The original authorization.json and completed WORK-001 scope receipts remain
  unchanged as historical authority. DEC-001 is the later, narrowly overriding
  operational exception for the affected Windows work; it does not retroactively
  authorize anything or alter source/dependency scopes and shared allocations.
- No PKCS#12 import or outside-scratch key write preceded this approval. The first
  executable step remains the two-leaf native custody/TLS/cleanup probe.
