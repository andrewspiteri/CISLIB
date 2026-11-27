using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using CIS.Management;
using CIS.Management.Service.Configuration;
using CIS.Management.Wire;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementPortalHostTests
{
    private const string ManualSharedConfiguration = "shared.features";
    private const string ManualSharedPayload = "{\"Enabled\":true,\"Features\":{\"CountrySearch\":true,\"Export\":false}}";
    private const string ManualBillingConfiguration = "billing.processing";
    private const string ManualCountryCommonPayload = "{\"Greeting\":\"Global country catalogue\",\"Enabled\":true,\"Count\":10}";
    private const string ManualCountryApplicationPayload = "{\"Greeting\":\"European country catalogue\",\"Enabled\":true,\"Count\":25}";
    private const string ManualBillingPayload = "{\"BatchSize\":250,\"Currency\":\"EUR\",\"Retry\":{\"MaxAttempts\":3,\"DelaySeconds\":15},\"InvoiceExport\":true}";

    private static ManagementConfigurationManifest WithManualSharedConfiguration(ManagementConfigurationManifest manifest)
        => manifest with
        {
            Definitions = manifest.Definitions.Append(new(ManualSharedConfiguration, "1", new("object", false, []), "{}",
                "Shared feature settings for the local review fixture")).ToArray(),
            Bindings = manifest.Bindings.Append(new("managed-a", "Development", "FeatureFlags", ManualSharedConfiguration, "1")).ToArray(),
            CommonSources = manifest.CommonSources.Append(new(ManualSharedConfiguration, "1", "Development")).ToArray()
        };

    // Only the explicit human review branch adds this definition; the automated Shared-copy example stays unchanged.
    private static ManagementConfigurationManifest WithManualReviewExamples(ManagementConfigurationManifest manifest)
        => manifest with
        {
            Definitions = manifest.Definitions.Append(new(ManualBillingConfiguration, "1", new("object", false, []), "{}",
                "Invoice batching, currency and retry settings for Billing Worker")).ToArray(),
            Bindings = manifest.Bindings.Append(new("managed-b", "Development", "InvoiceProcessor", ManualBillingConfiguration, "1")).ToArray(),
            CommonSources = manifest.CommonSources.Append(new(ManualBillingConfiguration, "1", "Development")).ToArray()
        };

    private static async Task<string> SeedManualInventoryAsync(ManagementHostFixture fixture)
    {
        HttpClient country = fixture.Client("workload-a");
        HttpClient billing = fixture.Client("workload-b");
        string countrySecond = await RegisterManualInstanceAsync(country, "country-api-eu-02", "2.4.2", reported: true);
        await RegisterManualInstanceAsync(country, "country-api-us-canary", "2.5.0-rc.1", reported: false);
        await RegisterManualInstanceAsync(country, "country-api-eu-retired", "2.3.9", reported: true, closed: true);
        await RegisterManualInstanceAsync(billing, "billing-worker-eu-02", "1.8.3", reported: true);
        await RegisterManualInstanceAsync(billing, "billing-worker-eu-retired", "1.7.6", reported: true, closed: true);
        string observed = ManualObservedAt();
        using (HttpResponseMessage observation = await ManagementServiceTests.Put(country,
            "registrations/" + fixture.RegistrationA + "/observation", new ManagementObservation("1", observed, "ready", "healthy")))
        {
            Assert.AreEqual(HttpStatusCode.OK, observation.StatusCode);
        }
        using (HttpResponseMessage observation = await ManagementServiceTests.Put(billing,
            "registrations/" + fixture.RegistrationB + "/observation", new ManagementObservation("1", observed, "ready", "healthy")))
        {
            Assert.AreEqual(HttpStatusCode.OK, observation.StatusCode);
        }
        var inventory = new ManagementInventorySnapshot("1", observed, [new("country-data", "2.4.2")],
            [new("country-catalogue", "maintenance", "Catalogue maintenance")],
            [new("country-catalogue", "countries.refresh", "maintenance", "Refresh country cache", ["scheduled"],
                EffectiveConfiguration: new("0", true), Observations: [new("europe", "scheduled", "released")])]);
        using HttpResponseMessage reportedInventory = await ManagementServiceTests.Put(country,
            "registrations/" + fixture.RegistrationA + "/inventory", inventory);
        Assert.AreEqual(HttpStatusCode.OK, reportedInventory.StatusCode);
        return countrySecond;
    }

    private static async Task<string> RegisterManualInstanceAsync(HttpClient workload, string name, string version, bool reported, bool closed = false)
    {
        string id = Guid.NewGuid().ToString("D");
        string observed = ManualObservedAt();
        using (HttpResponseMessage registration = await ManagementServiceTests.Put(workload,
            "registrations/" + id, new ManagementRegistrationRequest(1, name, observed, version)))
        {
            Assert.AreEqual(HttpStatusCode.OK, registration.StatusCode);
        }
        if (reported)
        {
            using HttpResponseMessage observation = await ManagementServiceTests.Put(workload,
                "registrations/" + id + "/observation", new ManagementObservation("1", observed, "ready", "healthy"));
            Assert.AreEqual(HttpStatusCode.OK, observation.StatusCode);
        }
        if (closed)
        {
            using HttpResponseMessage close = await workload.DeleteAsync("registrations/" + id);
            Assert.AreEqual(HttpStatusCode.OK, close.StatusCode);
        }
        return id;
    }

    // Runs under the temporary native seed identity. Its grants are removed before the A/B chooser opens.
    private static async Task<string> SeedManualReviewStateAsync(ManagementHostFixture fixture, RSA signing, string issuer,
        ManagementConfigurationDefinition countryApplication, ManagementConfigurationDefinition countryCommon,
        ManagementConfigurationDefinition billingApplication, ManagementConfigurationDefinition billingCommon)
    {
        Assert.AreEqual(fixture.ApplicationA, countryApplication.ApplicationId);
        Assert.AreEqual("shared.settings", countryApplication.ConfigurationId);
        Assert.AreEqual("common", countryCommon.Kind);
        Assert.AreEqual(countryApplication.ConfigurationId, countryCommon.ConfigurationId);
        Assert.AreEqual(fixture.ApplicationB, billingApplication.ApplicationId);
        Assert.AreEqual(ManualBillingConfiguration, billingApplication.ConfigurationId);
        Assert.AreEqual("common", billingCommon.Kind);
        Assert.AreEqual(ManualBillingConfiguration, billingCommon.ConfigurationId);
        HttpClient actor = fixture.Client(subject: null, replica: fixture.Origins.Count - 1, explicitToken: LookupToken(signing, issuer));
        await SetManualMetadataAsync(actor, fixture.ApplicationA, "Country Services",
            "Country catalogue and regional lookup APIs. Compare inherited feature flags with the European application override.");
        await SetManualMetadataAsync(actor, fixture.ApplicationB, "Billing Worker",
            "Background invoice processing for the European billing queue. Inspect batching, retry settings and prior worker versions.");
        string commonId = await ManagementConfigurationDistributionTests.PublishValue(actor, countryCommon.SourceId,
            Guid.NewGuid().ToString("D"), ManualCountryCommonPayload);
        string countryId = await ManagementConfigurationDistributionTests.PublishValue(actor, countryApplication.SourceId,
            Guid.NewGuid().ToString("D"), ManualCountryApplicationPayload);
        await AssignManualLayersAsync(actor, fixture.ApplicationA, countryApplication.ConfigurationId,
            new("present", commonId), new("present", countryId), preserveCohorts: true);
        string billingId = await ManagementConfigurationDistributionTests.PublishValue(actor, billingApplication.SourceId,
            Guid.NewGuid().ToString("D"), ManualBillingPayload);
        string billingReleaseId = await AssignManualLayersAsync(actor, fixture.ApplicationB, billingApplication.ConfigurationId,
            new("absent"), new("present", billingId), preserveCohorts: false);

        // The independent Shared-only seed already verified its origin/content before the security-mode switch.
        await VerifyManualSummaryAsync(actor, fixture.ApplicationA, countryApplication.ConfigurationId, "applicationOverride");
        await VerifyManualSummaryAsync(actor, fixture.ApplicationB, billingApplication.ConfigurationId, "applicationOnly");
        foreach ((string publicationId, string payload) in new[] { (commonId, ManualCountryCommonPayload), (countryId, ManualCountryApplicationPayload), (billingId, ManualBillingPayload) })
        {
            using HttpResponseMessage content = await actor.GetAsync("configuration/publications/" + publicationId);
            Assert.AreEqual(HttpStatusCode.OK, content.StatusCode);
            Assert.AreEqual(payload, (await ManagementConfigurationCatalogTests.Decode<ManagementConfigurationPublication>(content)).PayloadJson);
        }
        await VerifyManualInstanceExamplesAsync(actor, fixture.ApplicationA, "country-api-eu-retired", "2.3.9", "country-api-us-canary");
        await VerifyManualInstanceExamplesAsync(actor, fixture.ApplicationB, "billing-worker-eu-retired", "1.7.6");
        return billingReleaseId;
    }

    private static async Task SetManualMetadataAsync(HttpClient actor, string applicationId, string name, string description)
    {
        using HttpResponseMessage currentResponse = await actor.GetAsync("applications/" + applicationId);
        Assert.AreEqual(HttpStatusCode.OK, currentResponse.StatusCode);
        ManagementApplication current = await ManagementServiceTests.Decode<ManagementApplication>(currentResponse);
        using var change = new HttpRequestMessage(HttpMethod.Put, "applications/" + applicationId + "/metadata")
        {
            Content = new ByteArrayContent(ManagementWire.EncodeRequest(new ManagementMetadataCommand(1, name, description)))
        };
        change.Content.Headers.ContentType = new("application/json");
        change.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        change.Headers.Add("If-Match", current.MetadataETag);
        using HttpResponseMessage changed = await actor.SendAsync(change);
        Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode);
        ManagementMetadataResult result = await ManagementServiceTests.Decode<ManagementMetadataResult>(changed);
        Assert.AreEqual("committed", result.OriginalCompletion);
        ManagementMetadataDecision decision = result.Result ?? throw new AssertFailedException("The real metadata operation must retain its applied decision.");
        Assert.AreEqual("applied", decision.Decision);
        Assert.AreEqual(name, decision.Metadata.DisplayName);
        Assert.AreEqual(description, decision.Metadata.Description);
    }

    private static async Task<string> AssignManualLayersAsync(HttpClient actor, string applicationId, string configurationId,
        ManagementConfigurationSourceReference common, ManagementConfigurationSourceReference application, bool preserveCohorts)
    {
        string path = "applications/" + applicationId + "/configuration/" + configurationId;
        using HttpResponseMessage mapResponse = await actor.GetAsync(path + "/assignments?schemaVersion=1");
        Assert.AreEqual(HttpStatusCode.OK, mapResponse.StatusCode);
        ManagementConfigurationAssignmentMap map = await ManagementConfigurationCatalogTests.Decode<ManagementConfigurationAssignmentMap>(mapResponse);
        using HttpResponseMessage released = await ManagementConfigurationCatalogTests.Send(actor, path + "/releases?schemaVersion=1",
            Guid.NewGuid().ToString("D"), map.ETag, new ManagementConfigurationReleaseCommand(1, common, application, "Prepare the named manual-review example"));
        Assert.AreEqual(HttpStatusCode.OK, released.StatusCode);
        string releaseId = (await ManagementConfigurationCatalogTests.Decode<ManagementConfigurationOperationResult>(released)).Result!.ReleaseId!;
        ManagementConfigurationOverride[] overrides = preserveCohorts ? map.Overrides.Select(item => new ManagementConfigurationOverride(item.CohortId, releaseId)).ToArray() : [];
        using var assignment = new HttpRequestMessage(HttpMethod.Put, path + "/assignments?schemaVersion=1")
        {
            Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(new ManagementConfigurationAssignmentCommand(1, "replace", releaseId,
                overrides, "Explicitly assign the named manual-review example")))
        };
        assignment.Content.Headers.ContentType = new("application/json");
        assignment.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        assignment.Headers.Add("If-Match", map.ETag);
        using HttpResponseMessage assigned = await actor.SendAsync(assignment);
        Assert.AreEqual(HttpStatusCode.OK, assigned.StatusCode);
        ManagementConfigurationOperationResult result = await ManagementConfigurationCatalogTests.Decode<ManagementConfigurationOperationResult>(assigned);
        Assert.AreEqual("committed", result.OriginalCompletion);
        ManagementConfigurationAssignmentMap assignedMap = result.Result?.Assignment ?? throw new AssertFailedException("The real assignment must retain its complete committed map.");
        Assert.AreEqual(releaseId, assignedMap.DefaultReleaseId);
        CollectionAssert.AreEquivalent(overrides, assignedMap.Overrides.ToArray());
        return releaseId;
    }

    private static async Task VerifyManualSummaryAsync(HttpClient actor, string applicationId, string configurationId, string origin)
    {
        using HttpResponseMessage response = await actor.GetAsync("applications/" + applicationId + "/configuration-summaries?pageSize=100");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        ManagementConfigurationSummary summary = (await ManagementConfigurationCatalogTests.Decode<ManagementPage<ManagementConfigurationSummary>>(response)).Items
            .Single(item => item.ConfigurationId == configurationId);
        Assert.AreEqual(origin, summary.Origin);
    }

    private static async Task VerifyManualInstanceExamplesAsync(HttpClient actor, string applicationId, string retiredName, string retiredVersion, string? unreportedName = null)
    {
        using HttpResponseMessage response = await actor.GetAsync("applications/" + applicationId + "/instances?pageSize=100");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        ManagementPage<ManagementInstance> instances = await ManagementServiceTests.Decode<ManagementPage<ManagementInstance>>(response);
        ManagementInstance retired = instances.Items.Single(item => item.ReportedInstanceId == retiredName);
        Assert.AreEqual(retiredVersion, retired.ApplicationVersion);
        Assert.AreEqual("closed", retired.Presence);
        Assert.IsNotNull(retired.ClosedAtUtc);
        if (unreportedName is not null)
        {
            ManagementInstance unreported = instances.Items.Single(item => item.ReportedInstanceId == unreportedName);
            Assert.AreEqual(unreported.RegisteredAtUtc, unreported.LastSeenAtUtc, "An unreported registration has only its actual registration receipt as presence evidence.");
            Assert.IsNull(unreported.Observation);
            Assert.IsNull(unreported.ObservationReceivedAtUtc);
            Assert.IsNull(unreported.ClosedAtUtc);
        }
    }

    private static string ManualObservedAt() => DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static async Task SeedManualSharedConfigurationAsync(ManagementHostFixture fixture, HttpClient actor,
        ManagementConfigurationDefinition application, ManagementConfigurationDefinition common)
    {
        ManagementConfigurationSource source = await ManagementConfigurationCatalogTests.Source(actor, common.SourceId);
        using HttpResponseMessage published = await ManagementConfigurationCatalogTests.Publish(actor, source,
            Guid.NewGuid().ToString("D"), ManualSharedPayload);
        Assert.AreEqual(HttpStatusCode.OK, published.StatusCode);
        string publicationId = (await ManagementConfigurationCatalogTests.Decode<ManagementConfigurationOperationResult>(published)).Result!.PublicationId!;
        string bindingPath = "applications/" + fixture.ApplicationA + "/configuration/" + ManualSharedConfiguration;
        using HttpResponseMessage mapResponse = await actor.GetAsync(bindingPath + "/assignments?schemaVersion=1");
        Assert.AreEqual(HttpStatusCode.OK, mapResponse.StatusCode);
        ManagementConfigurationAssignmentMap map = await ManagementConfigurationCatalogTests.Decode<ManagementConfigurationAssignmentMap>(mapResponse);
        Assert.IsNull(map.Generation);
        using HttpResponseMessage released = await ManagementConfigurationCatalogTests.Send(actor, bindingPath + "/releases?schemaVersion=1",
            Guid.NewGuid().ToString("D"), map.ETag, new ManagementConfigurationReleaseCommand(1, new("present", publicationId), new("absent"),
                "Pin the local review fixture's shared feature settings"));
        Assert.AreEqual(HttpStatusCode.OK, released.StatusCode);
        string releaseId = (await ManagementConfigurationCatalogTests.Decode<ManagementConfigurationOperationResult>(released)).Result!.ReleaseId!;
        using var assignment = new HttpRequestMessage(HttpMethod.Put, bindingPath + "/assignments?schemaVersion=1")
        {
            Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(new ManagementConfigurationAssignmentCommand(1, "replace", releaseId, [],
                "Assign the shared-only local review fixture explicitly")))
        };
        assignment.Content.Headers.ContentType = new("application/json");
        assignment.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        assignment.Headers.Add("If-Match", map.ETag);
        using HttpResponseMessage assigned = await actor.SendAsync(assignment);
        Assert.AreEqual(HttpStatusCode.OK, assigned.StatusCode);
        ManagementConfigurationOperationResult decision = await ManagementConfigurationCatalogTests.Decode<ManagementConfigurationOperationResult>(assigned);
        Assert.AreEqual("1", decision.Result!.Generation);
        Assert.IsEmpty(decision.Result.Assignment!.Overrides);
        using HttpResponseMessage summaryResponse = await actor.GetAsync("applications/" + fixture.ApplicationA + "/configuration-summaries");
        Assert.AreEqual(HttpStatusCode.OK, summaryResponse.StatusCode);
        ManagementConfigurationSummary summary = (await ManagementConfigurationCatalogTests.Decode<ManagementPage<ManagementConfigurationSummary>>(summaryResponse)).Items
            .Single(row => row.ConfigurationId == ManualSharedConfiguration);
        Assert.AreEqual(application.SourceId, summary.SourceId);
        Assert.AreEqual(application.BindingId, summary.BindingId);
        Assert.AreEqual("shared", summary.Origin);
        Assert.IsFalse(summary.HasCohortOverrides);
        using HttpResponseMessage content = await actor.GetAsync("configuration/publications/" + publicationId);
        Assert.AreEqual(HttpStatusCode.OK, content.StatusCode);
        Assert.AreEqual(ManualSharedPayload, (await ManagementConfigurationCatalogTests.Decode<ManagementConfigurationPublication>(content)).PayloadJson);
    }
}
