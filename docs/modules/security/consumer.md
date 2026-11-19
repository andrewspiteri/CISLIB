---
title: CountryData security service and caller
type: document
status: draft
cis:
  stable_id: cislib.document.modules.security.consumer
---

# CountryData security service and caller

[CountryData](../../../implementations/csharp/examples/CountryData/README.md) is
the maintained application. `CountryApplication.CreateService` retains its catalog,
provider, Workflows and refresh owner. The default `--security=none` keeps the
ordinary HTTP lookup and countries client's `LogCalls = false`.

Explicitly select `--security=local|certificate|entra`. Service is the default
role; `--role=caller` runs the same binary's console caller using one named real
`IRestClient`. It adds no sender, retry loop or identity resolver.

## Runnable local profile

Follow [local setup](local-development.md). Pass its absolute non-secret
`configuration.json` with `--security-config=<path>`. This explicitly selected
native JSON provider uses `reloadOnChange: false`. Its passive defaults become
`CountrySecuritySettings`; the complete policy/provider snapshot is copied once
under the existing CIS `IConfigurationReader`. Factories perform no I/O. An
application-supplied CIS store/default factories remain authoritative; the example
does not overwrite them. Trust/grant/settings changes require restart.

Use central `CORE_SERVICE_NAME=CountryData` for the service and
`CORE_SERVICE_NAME=CountryData.Caller` for the caller, both with
`CORE_ENVIRONMENT=Development`. Set native `ASPNETCORE_ENVIRONMENT` and
`DOTNET_ENVIRONMENT` to Development as well. No new resolver replaces these
existing canonical/native identities.

Business TLS defaults to `https://localhost:7443`; the separate loopback operational
listener is `http://127.0.0.1:7080`. The caller selects that exact allowed origin
using `--target=https://localhost:7443`. Sequential Startup loads server PEM and
the selected credential source before Kestrel accepts work. Native certificate
owners remain alive through actual host/REST/response drain. Runtime never reads
the setup CA private key. Server certificate replacement requires restart.
Server Startup captures at most 256 KiB of actual certificate/trust PEM and
64 KiB of actual key PEM, rejecting excess before native key preparation. Private
byte/character buffers are cleared on success, cancellation and failure.

| Request | Required target/capability | Effect |
| --- | --- | --- |
| `GET /country/MT` | Actual cached country MT, `country.read` | Existing lookup response. |
| `GET /selection` | Service/environment, `country.read` | One selected value and revision. |
| `PUT /selection/MT` | Exact country MT, `country.select` | Select Malta and advance revision. |
| `PUT /selection/TTO` | Actual country TT, `country.select` | Deny; value and revision unchanged. |

The server resolves aliases through its actual catalog before authorization.
The complete grant uses canonical ISO2, not submitted route text. There is one
selected value with no history or persistence promise. The normal caller performs one operation:
`--operation=read|select|inspect`, defaulting to a harmless read with `--country=MT`.
`CountryCallerResult` prints only success, actual HTTP status, safe error code and
the business value. It emits no headers, raw tokens or REST diagnostic context.
The native fixture independently executes read, explicit MT selection, denied TTO
selection and inspection, asserting unchanged effect/revision after denial. That
demonstration dataset contains Malta and Trinidad and Tobago.

`/_health`, `/_health/ready` and the actual exporter `/_metrics` retain existing
operational behavior. The operational listener offers no business bypass,
including with a spoofed Host header. Management access beyond loopback needs the
application's deliberate access policy.

## Other selected profiles

`certificate` uses explicit client CA/direct-issuer files, exact caller URI/
application/environment mapping and separate server TLS certificate/key.
`IssuingAuthorityFile` can narrow the direct issuer. `ServerTrustCertificateFile`
selects separate private server roots; otherwise production uses native system
server trust. Client issuers do not implicitly become server trust. Production
uses Online revocation and needs its actual evidence. Windows intermediates are
deployment prerequisites; see [certificates](certificates.md) and [transport](transport.md).

`entra` explicitly supplies `TenantId`, `Audience`, `CallerClientId`,
`CallerObjectId`, `FederatedSubject`, `FederatedTokenFile`, `Scope`, allowed HTTPS
origins and server TLS inputs. The receiving mapping accepts the intended app-only
identity and `Country.Read`/`Country.Select` application roles. The selection grant
still covers only MT. WorkloadIdentityCredential has no operator/CLI fallback.
See [Entra deployment](entra.md) for actual registration/federation prerequisites.

Optional `NativeKeyJournalDirectory` selects explicit operator custody beneath
workspace `.artifacts/security`, outside setup-owned `local-development`.
Only native preparation creates its restricted GUID subdirectory and durable
non-secret Created/Removed metadata. Links/escapes/journal failures fail closed;
no key contents or store inventory are written. Each process role owns two slots.
Windows qualification must choose its private journal under the [W01](../../changes/CIS-0016/windows-tls-key-backend-decision.md)
temporary user-key exception; production can omit it. Other platforms retain
native memory ownership. No trust store is provisioned.

The selection awaits a separate operation audit after committing its value; hook
faults stay observable and do not roll it back. See [audit](audit.md) and
[failure interpretation](failures.md). Metrics use bounded aliases/outcomes and
central application/environment labels; native HTTP retains its own timing.
Tests cover CommonTesting unit examples, actual facade/caller/effect/scrape and
three isolated package shapes. Authored source is not passing evidence. Local
proof does not establish AC-02 AKS, AC-24 cert-manager, roadmap completion or release.
