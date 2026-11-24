# CountryData central configuration opt-in

The maintained application stays unmanaged by default. An application host can
explicitly select the existing same-session native bootstrap from its ordinary
`CountryApplication.CreateService` callback:

```csharp
using CIS.Examples.CountryData;
using CIS.Hosting;
using CIS.TypeManagement;

static CISApplication CreateCountry(string[] args,
    CISApplicationCentralConfigurationOptions central)
    => CountryApplication.CreateService(args, builder =>
    {
        builder.TypeManager = new(typeof(CountryApplication).Assembly);
        CountryCentralConfiguration.Configure(builder, central,
            TimeSpan.FromSeconds(30)); // Explicit example choice, not a module default.
    });
```

Deploy the normal generated `CIS.CountryData.types.json` inventory with the actual
application. The helper fixes Configuration's host to that assembly, enrolls only
the existing `CountrySettings` as `country.settings`, schema `"1"`, with
`RestartRequired`, and preserves prior factories, validators and the noncentral
store. `CountrySecuritySettings` remains outside central enrollment. Conflicting
enrollments, an already selected central profile, a supplied response target or
an unsupported polling interval fail visibly. No CLI switch, credential root or
business endpoint is added.

Supply the `central` options independently of centrally loaded content. Its
credential factory must prepare an actual native PEM or Entra owner and return
the public `CISApplicationCentralConfigurationPreparation`. For example, this
outbound Entra composition uses existing APIs; `policy` and `credentials` come
from trusted bootstrap inputs, including the projected-file reference:

```csharp
using CIS.Management;
using CIS.Management.Hosting;
using CIS.Hosting;
using CIS.RestClient;
using CIS.Security;
using CIS.Security.Entra;
using CIS.Security.Hosting;
using Microsoft.Extensions.DependencyInjection;

static async Task<CISApplicationCentralConfigurationPreparation> PrepareEntra(
    SecurityPolicySnapshot policy, string profile, EntraSecurityOptions credentials,
    ManagementClientOptions management, ManagementConfigurationClientOptions configuration,
    CancellationToken token)
{
    EntraPreparedProfile prepared = await EntraPreparedProfile.PrepareAsync(
        policy, profile, credentials, cancellationToken: token);
    try
    {
        RestClientOptions Bootstrap(string name) => new(name)
        {
            SecurityBinding = prepared.Binding,
            Authenticate = prepared.AuthenticateAsync,
            LogCalls = false
        };
        return new(prepared, Bootstrap(management.RestClientName),
            Bootstrap(configuration.RestClientName), services =>
            {
                services.AddSingleton<TimeProvider>(prepared.TimeProvider);
                services.AddCisSecurity(prepared.Policy);
                services.AddCisEntraSecurity(prepared);
                services.AddCisEntraRestClient(new RestClientOptions(management.RestClientName)
                    { LogCalls = false }.ConfigureCisManagementRestClient(management), profile);
                services.AddCisManagementClient(management);
                services.AddCisEntraRestClient(new RestClientOptions(configuration.RestClientName)
                    { LogCalls = false }.ConfigureCisManagementConfigurationRestClient(configuration), profile);
                services.AddCisManagementConfigurationClient(configuration);
            }, prepared.ActivateFinalAsync, prepared.RetireBootstrapAsync);
    }
    catch
    {
        await prepared.DisposeAsync();
        throw;
    }
}
```

Use two distinct aliases with the same independent HTTPS endpoint and immutable
policy/binding. Configure the central options with the actual expected
environment and exact expected cohort, including null for no cohort. The server
freezes the trusted native subject's cohort; the expectation cannot select it.
This recipe uses the system clock. Custom clocks must be the same actual clock
through preparation, options and final native DI. A PEM factory instead supplies
its prepared borrowed transport and the existing PEM final registration and
`ActivateFinalAsync` callbacks. See [facade ownership](../application/core.md#preparation-and-readiness)
and [native credentials](../security/credentials.md).

## Provision and publish

Load one reviewed local Management deployment manifest with these immutable
definition facts, then apply its loaded digest using the authorized catalog
status/apply APIs. Do not send the manifest itself over HTTP.

| Field | Declaration / default |
| --- | --- |
| `SourceUrl` | Required nonnull string; `https://countriesnow.space/api/v0.1/countries/iso` |
| `RefreshInterval` | Required nonnull string; `01:00:00` |
| `RequestTimeout` | Required nonnull string; `00:00:30` |
| `RetryPolicyName` | Required nullable string; `transient-http` |

The finite root shape is `object` with those exact case-sensitive members.
Durations use invariant TimeSpan `c` text, not ISO duration text. Bind the
definition to the deployment's actual native application/environment and host
`CIS.CountryData`; explicitly permit its common source. Trusted issuer/subject
cohort mappings belong in that local manifest. Select the deployment quota and
retention explicitly. The helper chooses neither a backend nor a quota.

The operator needs the actual catalog/source/binding/release capabilities;
metadata-only discovery does not grant content or mutation access. Read
`GetDefinitionsAsync` and follow its bounded pages to obtain the authorized
common and application `SourceId` rows. Filter by `country.settings`, schema
`"1"`, exact environment and actual application ID. A display name is not a
resource ID. Read each source's current ETag before publication.

With an authenticated operator `IManagementConfigurationClient client`, this
method publishes exact partial common/application source text and pins a release
without activating it. The source IDs and application ID are the discovered
authorized resources. Supply a reviewed unique operation-key prefix once and
retain it with the original commands for recovery:

```csharp
using CIS.Management;

static async Task<string> PublishCountryRelease(IManagementConfigurationClient client,
    string applicationId, string commonSourceId, string applicationSourceId, string prefix)
{
    string common = await Publish(commonSourceId, prefix + ".common",
        "{\"RefreshInterval\":\"01:00:00\",\"RequestTimeout\":\"00:00:10\"}");
    string application = await Publish(applicationSourceId, prefix + ".application",
        "{\"SourceUrl\":\"https://countriesnow.space/api/v0.1/countries/iso\",\"RetryPolicyName\":null}");
    ManagementConfigurationAssignmentMap map = Value(await client.GetAssignmentsAsync(
        applicationId, "country.settings", "1"));
    ManagementConfigurationDecision created = Decision(await client.CreateReleaseAsync(
        applicationId, "country.settings", "1", prefix + ".release", map.ETag,
        new(1, new("present", common), new("present", application), "Reviewed CountryData pair")));
    return created.ReleaseId!;

    async Task<string> Publish(string sourceId, string key, string payload)
    {
        ManagementConfigurationValidationResult validation = Value(await client.ValidateAsync(new(1, sourceId, payload)));
        if (!validation.Valid) { throw new InvalidOperationException("Structural validation refused this draft."); }
        ManagementConfigurationSource source = Value(await client.GetSourceAsync(sourceId));
        return Decision(await client.PublishSourceAsync(sourceId, key, source.ETag,
            new(1, payload, "Reviewed CountryData source"))).PublicationId!;
    }
}

static T Value<T>(ManagementConfigurationCallResult<T> call) where T : class
    => call.Exchange.Success && call.Exchange.Value is { } value ? value
        : throw new InvalidOperationException("Stop and inspect the original exchange/recovery facts.");

static ManagementConfigurationDecision Decision(ManagementConfigurationCallResult<ManagementConfigurationOperationResult> call)
{
    ManagementConfigurationOperationResult result = Value(call);
    return result.OriginalCompletion == "committed" && result.KnownBusinessDecision is { IsKnown: true, Value: "applied" }
        && result.Result is { } decision ? decision
        : throw new InvalidOperationException("The original durable decision is not known applied.");
}
```

On uncertainty, use the matching catalog/publication/release/assignment recovery
API with the unchanged original key. Found/Replayed, original commit and current
read cleanup remain separate. NotFound does not prove rollback or permit a
new-key rerun. The methods above stop on refusal/uncertainty; they add no retries.

Preview the pinned pair with `PreviewAsync(new(1, applicationId,
"country.settings", "1", new("present", commonPublicationId),
new("present", applicationPublicationId)))`. It reports structural facts and
known recipients, not proof that every consumer's typed validator will accept.
Read the current whole map and call `ReplaceAssignmentsAsync` with its ETag,
original key and `new(1, "replace", releaseId, map.Overrides, reason)`. This
retains every existing cohort override explicitly. A selected cohort still uses
its pinned override, so promoting a release to all targets requires a reviewed
complete map that intentionally removes or updates those overrides. Empty
defaults require an actual release with two `new("absent")` references.

## Observe staging, rejection, restart and rollback

The merge order is captured defaults, common publication, application
publication. Explicit nullable `RetryPolicyName: null` selects None. A URL must
be absolute HTTP(S) without user information; both durations must be positive
and supported by Heartbeat; this central tutorial permits only None or its
installed `transient-http` preset. The composed typed validator checks the
detached complete candidate **before** RestartRequired staging. The catalog uses
the same rules against its actual registered policy set when it captures values.

1. Assign release R1 and start CountryData using the opt-in. All initial roots
   must be accepted before the catalog constructor captures settings and primes
   the controlled or real countries source. Inspect the separate registration
   and adoption freshness through `GetStatusAsync(applicationId)`.
2. Publish/pin/assign valid R2, such as a different request timeout. The running
   process reports `PendingRestart`: its root, captured catalog settings and
   accepted local revision remain active. Publication or assignment alone is
   never consumer acceptance.
3. A structurally valid candidate such as `SourceUrl: "file:///countries.json"`
   fails the typed validator and reports identified `Rejected`, preserving
   intact active values. Unknown fetch/transport failures cannot invent that
   rejection. Restore a valid release using a new reviewed complete-map intent
   before restarting; a new process with an invalid assigned candidate fails
   bootstrap instead of silently using the old process's values.
4. Restart the actual CountryApplication. Its new native registration and live
   snapshot establish the currently assigned release as its initial baseline;
   the old process's pending marker is not persisted as runtime authority.
5. Roll back by reading the current ETag and calling `ReplaceAssignmentsAsync`
   with `new(1, "rollback", r1, retainedOverrides, reason)`. This creates a NEW
   higher assignment generation pointing at R1's old immutable content. It
   never lowers a generation. The running R2 process stages the changed values;
   restart again to establish R1 content at that higher generation. Returning
   to still-active values clears pending restart without a runtime-change event.

The [example unit cases](../../../implementations/csharp/examples/CountryData.UnitTests/Configuration/CountryCentralConfigurationExamples.cs)
use public composition and CommonTesting substitution. They prove passive
selection and typed candidate rules, not authentication, durable publication or
deployment interoperability. The separately executed native CountryData proof
uses the existing real TLS/PostgreSQL fixture and actual maintained application;
consult the owning change's verification record for results and platform limits.
