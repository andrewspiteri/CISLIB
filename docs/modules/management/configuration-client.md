# C# configuration client and store

`IManagementConfigurationClient` is a separate substitutable port for the nineteen
RM-08 configuration APIs. `IManagementClient`, ordinary Management wire limits
and existing metadata result contracts keep their behavior. The
[canonical OpenAPI and schema](../../../contracts/management/v1/README.md) define
the fixed routes, exact schema query and original-key command families.

## Borrow the existing native owner

Configure a dedicated REST alias before selecting its ordinary or security-bound
native registration:

```csharp
using CIS.Management;
using CIS.Management.Hosting;
using CIS.RestClient;
using CIS.Security.Certificates.Hosting;

var configurationClient = new ManagementConfigurationClientOptions("central-configuration")
{
    BaseUrl = "https://management.example/",
    Timeout = TimeSpan.FromSeconds(30)
};
var native = new RestClientOptions(configurationClient.RestClientName)
{
    LogCalls = false
};
native.ConfigureCisManagementConfigurationRestClient(configurationClient);
services.AddCisCertificateRestClient(native, "management-certificate");
services.AddCisManagementConfigurationClient(configurationClient);
```

The selected certificate profile, exact immutable Security policy and its native
credential runtime must already be registered through the supported
[credential ownership](../security/credentials.md). Entra or ordinary REST
selection uses its existing equivalent registration. The typed client borrows
the keyed `IRestClient`; it creates no transport, provider, reporter, timer or
Startup participant. The first typed alias supplies the default port. The same
public `ManagementConfigurationClient` constructor supports a deliberately owned
standalone native client before DI; authenticate that bootstrap independently.

The dedicated profile supplies the configuration codec, at most one MiB encoded
requests/responses and 256 KiB decoded source text. Tighter native processing
limits stay tighter. Zero error-body capture is rejected for this dedicated
profile because a conditional read must observe actual empty response content.
Ordinary Management's 64 KiB request limit is not widened. Authentication, native
transport, final meter factory, trace forwarding and `LogCalls` selection retain
their existing owners.

All calls use the fixed `configuration` observation alias and native `request`
fallback. They do not add nineteen aliases or place portable configuration,
registration, release or publication IDs in metric dimensions. Two Management
profiles and two configuration profiles fit the existing thirty-two pair limit.

## Actual results, integrity and conditional reads

Encoding, bounded response reads, target-aware source validation, exact UTF-8
SHA-256 and actual cleanup run inside the existing native REST entry deadline.
Each snapshot call has its own immutable expected target and mapper; concurrent
calls share no mutable codec target. Source text is never normalized or
reserialized to calculate its digest. Malformed/unidentified responses retain no
desired release. A content failure after independently matching the complete
target can surface a payload-free `ContentRejection`, including with actual HTTP
200. That fact is exposed only if the final native result still represents that
decode rejection; timeout, cancellation and cleanup faults retain their real
outcomes.

`GetRegistrationConfigurationAsync(expectedTarget, retainedSnapshot)` derives
`If-None-Match` only from a complete valid retained snapshot for that exact
authority/application/environment/registration/cohort/definition/schema/host.
The native owner validates retained integrity before dispatch, and validates a
304's matching strong ETag and observed empty body before settlement. Missing,
foreign, unsolicited, unobserved or nonempty 304 responses are invalid responses.

For a valid conditional result, **C# preserves native HTTP 304 as an HTTP failure
with no successful value**: `Exchange.Success` is false, `FailureKind` is `Http`,
`ErrorCode` is `http.304`, and `HasValue` is false. The caller already owns the
validated retained snapshot. The Configuration store interprets this exact
conditional fact as whole-pair unchanged; it fabricates no successful REST value
and performs no integrity work after the native operation returns. Neither a
snapshot nor a 304 renews Management authority or grants admission.

`ManagementConfigurationCallResult<T>` separately retains the original mutation
key and safely decoded configuration Problem Details. Error truncation, native
security evidence, status and cleanup facts remain native. Writes, validation
POST and preview POST never retry automatically. GET retries are off by default;
an explicitly selected conservative policy must be the actual registered native
instance. Runtime snapshot calls always select None, preserving no-retry live
bootstrap and the same owned polling path; administrative GET/recovery calls
retain the explicit read policy. Operation success and Problem data must match
the original key, fixed family and known source/application/definition/schema;
scoped read values and status receipts must echo their requested binding and
exact sequences. A foreign typed response is InvalidResponse, while the caller's
original key remains available for recovery. Future decision kinds remain
explicitly unknown. After uncertainty, keep the unchanged command and original key and use
the matching catalog/publication/release/assignment recovery method. NotFound or
expired recovery does not prove rollback or permit a new-key rerun.

## Retained-root adapter and routing

`ManagementConfigurationStore` implements the existing `IConfigurationStore` and
the optional paired `IConfigurationSnapshotStore`. Supply it the borrowed typed
client and, when ordinary roots coexist, their existing noncentral store. The
Configuration catalog selects central reads only for explicitly enrolled roots;
ordinary roots keep independent-record reads and only their keys reach legacy
watch acquisition. A Reliable legacy store becomes RequiresReconciliation so its
watch remains active alongside required central polling. Pure central mode is
None. Select a positive polling interval explicitly; no default is installed.

Use `ConfigurationOptions.Enrollments` and independently established
`SnapshotTarget` before `ConfigurationRuntime.PrepareAsync`. Discovery or a
response's own labels cannot establish central enrollment/session authority.
The [central snapshot guide](../configuration/central-snapshots.md) describes
retained-root acceptance, synchronized readers, hot/restart policy, identified
rejection, generation/session fences and readiness epochs. The hosting handoff
must join bootstrap REST retirement before adopting the final native typed
client and activating Configuration; the adapter does not dispose borrowed ports.

There is one paired cache owner in Configuration. It retains parsed JSON and the
same exact decoded source strings, including leading/trailing whitespace, and
charges both representations against its shared sixteen-MiB logical allowance.
Conditional requests borrow that immutable pair and reconstruct only transient
wire DTOs. The adapter maintains no second persistent snapshot cache. Replacement
and actual stop release the owned source allowance. No per-POCO network, polling,
acknowledgement loop or authority owner is added.

Identified content codes use a finite local mapping: integrity →
`Configuration.SourceIntegrity`, schema/version → `Configuration.SourceSchema`,
scope → `Configuration.SourceScope`, format → `Configuration.SourceFormat`,
size → `Configuration.SourceSize`, and declared fetch failure →
`Configuration.SourceFetch`. Unknown future codes use
`Configuration.ContentRejected`. Local typed/cross-property validation retains
its separate Configuration validation code. No arbitrary substitute-provided
code becomes an adoption reason or metric label, and an unidentified failure
never invents a desired identity.

## Consumer substitution

`CIS.CommonTesting.ScriptedManagementConfigurationClient` provides finite queued
typed results, original faults, controlled actual waits and an expected-target
snapshot response factory. Queues and safe call records have an explicit bounded
capacity. Records contain operation kind and bounded IDs/schema/key only, with a
Boolean for retained-state presence; they retain no payload, ETag, credentials or
caller context. `Clear` does not abandon an already admitted held call.

This helper tests consumer behavior. It does not execute HTTP, verify digests,
authenticate a target, commit SQL or prove deployment interoperability. Actual
source/publication provider stories and the native codec/body/cleanup tests keep
their separate evidence limits. See the owning change's native verification
record for executed results; authored examples alone are not passing evidence.

The [facade handoff](../application/core.md#preparation-and-readiness) provides
optional same-session pre-Build native composition. It retains the original
registration and closed gate, retires bootstrap transport/security before Build,
adopts the final client with the real meter factory, and performs fresh control
after native credential and Configuration activation. The standalone client and
store remain independent of that facade.

`ReportStatusAsync` and `GetStatusAsync` use the
[adoption stream and current-target projection](configuration-adoption.md).
Self reporting retains native registration ownership; operator reads require the
separate app-scoped `management.configuration.adoption.read` capability.
