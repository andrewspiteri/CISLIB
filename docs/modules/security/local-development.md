---
title: Isolated local development credentials
type: document
status: draft
cis:
  stable_id: cislib.document.modules.security.local-development
---

# Isolated local development credentials

LocalDevelopment uses the [certificate adapter](certificates.md), the same
complete grants and the existing REST transport. It requires an explicitly
selected LocalDevelopment profile, exact canonical `Development` identity, an
allowed native development host environment and loopback listeners/origins.
Production excludes these roots, mappings and profiles. A development flag or
caller header cannot establish authority.

## Create one workspace's credentials

From the repository root in PowerShell:

```powershell
$workspace = (Get-Location).Path
dotnet run --project implementations/csharp/tools/CisSecurity.LocalDevelopment -- setup --workspace "$workspace"
$securityConfig = Join-Path $workspace '.artifacts/security/local-development/configuration.json'
```

The small nonpackable C# tool uses native RSA-2048/SHA-256 and writes only beneath
that workspace's ignored `.artifacts/security/local-development`. No Entra tenant,
Kubernetes cluster, CLI login, certificate-store installation or machine-wide
trust is required. Setup creates a unique 30-day CA, one server credential and
separate seven-day client credentials for `CountryData` and `CountryData.Caller`.
These are **actual validity intervals**: a five-minute backdate is included in,
not added to, the lifetime. Leaves are clamped to CA expiry. The server has only
server-authentication EKU and localhost/127.0.0.1/::1 SANs. Each client has explicit
client-authentication EKU and one distinct exact URI identity, for example:

```text
https://identity.cislib.local/applications/CountryData.Caller/environments/Development
```

The URI is an opaque mapping value; parsing it does not grant capabilities.
`configuration.json` contains only explicit paths, identity mappings, loopback
origins and the example caller's `country.read` grant targeting
`CountryData`/`Development`. The CA private key is setup-only; runtime applications
use their own leaf/key and public trust files. Never commit or share these files.

Setup may explicitly select two different application names with `--service` and
`--caller`. This tool accepts 1–128 ASCII letters, digits, dots, underscores and
hyphens so its generated names/URI paths stay direct. This subset does not change
the central CIS application identity contract. Selected names persist and cannot
be changed through renew/reset.

Windows setup establishes an inheritance-disabled current-user DACL before
private writes. It changes no owner/group and requests no owner assignment.
Unix directories use 0700 and files 0600. Each certificate/key is separate PEM;
the tool writes no PFX and creates no native TLS key backing merely to export
PEM. Public certificate, key-usage, identity and chain checks use the actual
certificate adapter before publication.

## Start the protected service and caller

In a service terminal at the repository root:

```powershell
$workspace = (Get-Location).Path
$securityConfig = Join-Path $workspace '.artifacts/security/local-development/configuration.json'
$env:CORE_SERVICE_NAME = 'CountryData'
$env:CORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project implementations/csharp/examples/CountryData -- --security=local "--security-config=$securityConfig" --role=service
```

In a second terminal:

```powershell
$workspace = (Get-Location).Path
$securityConfig = Join-Path $workspace '.artifacts/security/local-development/configuration.json'
$env:CORE_SERVICE_NAME = 'CountryData.Caller'
$env:CORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project implementations/csharp/examples/CountryData -- --security=local "--security-config=$securityConfig" --role=caller --target=https://localhost:7443
dotnet run --project implementations/csharp/examples/CountryData -- --security=local "--security-config=$securityConfig" --role=caller --operation=select --country=MT
dotnet run --project implementations/csharp/examples/CountryData -- --security=local "--security-config=$securityConfig" --role=caller --operation=select --country=TTO
dotnet run --project implementations/csharp/examples/CountryData -- --security=local "--security-config=$securityConfig" --role=caller --operation=inspect
```

The same maintained CountryData application owns both roles. It uses its existing
prepared Configuration reader for detached policy/options and the existing
canonical environment identity. Each caller invocation performs one operation;
the default is a harmless read of MT. Explicit selection of MT succeeds, selection
of TTO is denied and exits with status 1, and inspection returns the current
selected value/revision. Compare it with the successful selection to observe the
unchanged denied effect. The native integration test owns that prescribed
verification sequence and its assertions. Product output contains only safe
detached business values, actual HTTP status and a safe error code.
See [CountryData](../../../implementations/csharp/examples/CountryData/README.md)
for its operation/result details. Keep the operational listener separate from
the mTLS business listener; it must not expose a business-route bypass.

## Renew leaves and deliberately replace trust

```powershell
dotnet run --project implementations/csharp/tools/CisSecurity.LocalDevelopment -- renew --workspace "$workspace"
```

Renew retains the exact CA and workspace identity, rotates all three leaf/key
pairs and preserves the explicit application mappings. Existing certificate
source checks adopt a valid outgoing replacement under their normal Heartbeat
ownership; a request arriving during pair replacement may observe a mismatched
pair and retain its valid current generation until the next check. No protected
operation is replayed. Native server TLS credential adoption belongs to its
existing host owner; restart the reference service after server renewal.
Renewal never extends CA validity. A nearly expired/expired CA requires reset.

Read the non-secret workspace ID printed by setup or in `configuration.json`:

```powershell
$workspaceId = (Get-Content -LiteralPath $securityConfig -Raw | ConvertFrom-Json).workspaceId
dotnet run --project implementations/csharp/tools/CisSecurity.LocalDevelopment -- reset --workspace "$workspace" --confirm "$workspaceId"
```

Reset rotates the CA and every leaf/key while preserving the same workspace ID,
application names and grant scope. **Stop and restart all running callers and
services to adopt the new immutable client/server roots.** Leaf source reload
does not replace trust policy. An old trust snapshot rejects the new leaves;
the new trust snapshot rejects the old leaves. There is no universal development
CA and no fallback to development after production authentication fails.

`workspace.json` is a bounded current-user/workspace custody marker with exact
owned filenames and hashes; it grants no runtime authority. The tool refuses an
unmarked nonempty directory, unexpected entries, changed material/configuration,
different owner/workspace, symbolic links and reparse points. It replaces or
restores only exact verified owned files. It does not recursively delete a
directory, search a key directory or open a certificate store. Keep a directory
left by an abnormal process exit for explicit inspection rather than deleting
unknown entries or editing the marker to force adoption.

## Native integration boundary

The public adapter shapes used by the example are:

```csharp
var trust = new CertificatePemTrustOptions(trustDomain, caFile, caFile,
    revocation: CertificateRevocationPolicy.Development);
var gate = new CertificateDevelopmentGate("Development", ["Development"]);
var outgoing = new CertificatePemSecurityOptions(trust,
    new CertificatePemCredentialFiles(callerCertificateFile, callerPrivateKeyFile),
    serverTrustBundleFile: caFile, serverRevocationMode: X509RevocationMode.NoCheck,
    development: gate);
services.AddCisCertificatePemSecurity("local-development", _ => outgoing);
services.AddCisCertificateRestClient(restOptions, "local-development");
```

Prepare the actual common outbound profile first, with its exact local credential
subject and allowed loopback origin. Receiver-only profiles need public trust and
their native scheme, with no outgoing key. Kestrel uses
`https.UseCisCertificateProfileTrust(provider, "local-development")` and the
application-owned server credential. Client issuer trust remains separate from
outgoing server chain/hostname/EKU validation. Development selects explicit
NoCheck for its isolated server root; this does not loosen production Online or
the 24-hour actual ShortLived limit.

On Windows, running TLS owners use the approved temporary native user-key bridge
only when actual TLS starts, retain exact custody through host/response drain and
verify owned backing removal. The setup command never creates that backing.
There is no machine-key storage or trust installation. Local native evidence
does not establish live AKS federation (AC-02) or cert-manager deployment (AC-24);
those require their documented external prerequisites and actual environments.
