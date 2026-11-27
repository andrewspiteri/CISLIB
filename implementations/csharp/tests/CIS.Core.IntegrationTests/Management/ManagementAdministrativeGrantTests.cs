using System.Net;
using System.Net.Http.Headers;
using CIS.Management;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Security;
using CIS.Management.Wire;
using CIS.Security;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationDistributionTests;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("RabbitMq")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementAdministrativeGrantTests
{
    [TestMethod]
    public async Task MetadataWriteAndReplayRequireCurrentLookupWhileLookupSurvivesWriteRevocation()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1);
        HttpClient actor = fixture.Client("operator-a");
        ManagementApplication before = await Application(actor, fixture.ApplicationA);
        var command = new ManagementMetadataCommand(1, "Reviewed display", "An ordinary display change");
        using HttpResponseMessage accepted = await MetadataWrite(actor, fixture.ApplicationA, "original", before.MetadataETag, command);
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        ManagementMetadataResult original = ManagementWire.DecodeResponse<ManagementMetadataResult>(await accepted.Content.ReadAsByteArrayAsync())!;

        await fixture.StartReplicaAsync(selectSecurity: options => NativeGrants(options,
            options.Grants.Where(grant => !IsOperator(grant, "management.operations.read")).ToArray()));
        HttpClient writeOnly = fixture.Client("operator-a", 1);
        ManagementApplication current = await Application(writeOnly, fixture.ApplicationA);
        using HttpResponseMessage refused = await MetadataWrite(writeOnly, fixture.ApplicationA, "fresh", current.MetadataETag, command);
        Assert.AreEqual(HttpStatusCode.NotFound, refused.StatusCode);
        using HttpResponseMessage replay = await MetadataWrite(writeOnly, fixture.ApplicationA, "original", before.MetadataETag, command);
        Assert.AreEqual(HttpStatusCode.NotFound, replay.StatusCode, "An old key cannot bypass current result-disclosure permission.");
        using HttpResponseMessage hidden = await writeOnly.GetAsync(MetadataLookup(fixture.ApplicationA, "original"));
        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));

        await fixture.StartReplicaAsync(selectSecurity: options => NativeGrants(options,
            options.Grants.Where(grant => !IsOperator(grant, "management.applications.metadata.write")).ToArray()));
        HttpClient lookupOnly = fixture.Client("operator-a", 2);
        using HttpResponseMessage found = await lookupOnly.GetAsync(MetadataLookup(fixture.ApplicationA, "original"));
        Assert.AreEqual(HttpStatusCode.OK, found.StatusCode);
        ManagementMetadataResult recovered = ManagementWire.DecodeResponse<ManagementMetadataResult>(await found.Content.ReadAsByteArrayAsync())!;
        Assert.AreEqual("found", recovered.ResultSource);
        Assert.AreEqual(original.Operation, recovered.Operation);
        Assert.AreEqual(original.Result, recovered.Result);
        using HttpResponseMessage noWrite = await MetadataWrite(lookupOnly, fixture.ApplicationA, "cannot-write", current.MetadataETag, command);
        Assert.AreEqual(HttpStatusCode.NotFound, noWrite.StatusCode);
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
    }

    [TestMethod]
    [DataRow("publication")]
    [DataRow("release")]
    [DataRow("assignment")]
    public async Task ConfigurationBundlesCannotBorrowAnotherResourcesLookupAndOriginalRecoveryReauthorizes(string family)
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "catalog");
        ManagementConfigurationDefinition common = (await Definitions(actor)).Items.Single(item => item.Kind == "common");
        ManagementConfigurationSource source = await Source(actor, common.SourceId);
        ManagementConfigurationAssignmentMap map = await Map(actor, fixture.ApplicationA);
        string path;
        string originalETag;
        string resourceType;
        string resourceId;
        string writeCapability;
        object command;
        string lookup;
        ManagementConfigurationOperationResult original;
        if (family == "publication")
        {
            path = "configuration/sources/" + source.SourceId + "/publications";
            originalETag = source.ETag;
            command = new ManagementConfigurationPublicationCommand(1, "{\"Greeting\":\"Reviewed\"}", "Publish exact source");
            resourceType = ManagementConfigurationCapabilities.SourceResource;
            resourceId = source.SourceId;
            writeCapability = ManagementConfigurationCapabilities.Publish;
            lookup = "configuration/sources/" + source.SourceId + "/publication-operations/original";
            using HttpResponseMessage accepted = await ConfigurationWrite(actor, path, "original", originalETag, command);
            Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
            original = await Decode<ManagementConfigurationOperationResult>(accepted);
        }
        else
        {
            resourceType = ManagementConfigurationCapabilities.BindingResource;
            resourceId = map.BindingId;
            if (family == "release")
            {
                path = BindingPath(fixture.ApplicationA, "releases");
                originalETag = map.ETag;
                command = new ManagementConfigurationReleaseCommand(1, new("absent"), new("absent"), "Pin reviewed defaults");
                writeCapability = ManagementConfigurationCapabilities.ReleaseCreate;
                lookup = BindingPath(fixture.ApplicationA, "release-operations/original");
                using HttpResponseMessage accepted = await ConfigurationWrite(actor, path, "original", originalETag, command);
                Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
                original = await Decode<ManagementConfigurationOperationResult>(accepted);
            }
            else
            {
                string release = (await CreateRelease(actor, fixture.ApplicationA, map.ETag, "setup-release",
                    new(1, new("absent"), new("absent"), "Pin reviewed defaults"))).Result!.ReleaseId!;
                path = BindingPath(fixture.ApplicationA, "assignments");
                originalETag = map.ETag;
                command = new ManagementConfigurationAssignmentCommand(1, "replace", release, [], "Assign reviewed defaults");
                writeCapability = ManagementConfigurationCapabilities.AssignmentWrite;
                lookup = BindingPath(fixture.ApplicationA, "assignment-operations/original");
                using HttpResponseMessage accepted = await ConfigurationWrite(actor, path, "original", originalETag, command);
                Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
                original = await Decode<ManagementConfigurationOperationResult>(accepted);
            }
        }
        string currentETag = family == "publication" ? (await Source(actor, source.SourceId)).ETag
            : (await Map(actor, fixture.ApplicationA)).ETag;
        long operations = await Count(fixture, fixture.Options.OperationsSchema, "operation_journal");
        long audit = await Count(fixture, fixture.Options.MessagingSchema, "outbox");
        string unrelatedResource = Guid.NewGuid().ToString("D");
        await fixture.StartReplicaAsync(selectSecurity: options => NativeGrants(options,
            options.Grants.Where(grant => !(IsOperator(grant, ManagementConfigurationCapabilities.OperationsRead)
                && grant.ResourceType == resourceType)).Append(new CapabilityGrant(options.Profile,
                    ManagementConfigurationCapabilities.OperationsRead, EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
                    subject: new(ManagementHostFixture.Issuer, "operator-a"), resourceSelection: CapabilityResourceSelection.ExactResource,
                    resourceType: resourceType, resourceIdentifier: unrelatedResource)).ToArray()));
        HttpClient wrongLookupTarget = fixture.Client("operator-a", 1);
        string actionPath = family == "publication" ? "configuration/sources/" + source.SourceId + "/actions"
            : BindingPath(fixture.ApplicationA, "actions");
        using (HttpResponseMessage eligibility = await wrongLookupTarget.GetAsync(actionPath))
        {
            Assert.AreEqual(HttpStatusCode.OK, eligibility.StatusCode);
            ManagementConfigurationActions actions = ManagementSchedulerWire.DecodeResponse<ManagementConfigurationActions>(
                await eligibility.Content.ReadAsByteArrayAsync())!;
            Assert.AreEqual(resourceId, actions.ResourceId);
            string operationId = family == "publication" ? "publishConfigurationSource"
                : family == "release" ? "createConfigurationRelease" : "replaceConfigurationAssignments";
            ManagementAdministrativeAction action = actions.Actions.Single(item => item.OperationId == operationId);
            Assert.IsTrue(action.Supported);
            Assert.IsFalse(action.Allowed);
        }
        using (HttpResponseMessage refused = await ConfigurationWrite(wrongLookupTarget, path, "fresh", currentETag, command))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, refused.StatusCode);
        }
        using (HttpResponseMessage replay = await ConfigurationWrite(wrongLookupTarget, path, "original", originalETag, command))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, replay.StatusCode);
        }
        using (HttpResponseMessage hidden = await wrongLookupTarget.GetAsync(lookup))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        }
        Assert.AreEqual(operations, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(audit, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));

        await fixture.StartReplicaAsync(selectSecurity: options => NativeGrants(options,
            options.Grants.Where(grant => !IsOperator(grant, writeCapability)).ToArray()));
        HttpClient lookupOnly = fixture.Client("operator-a", 2);
        using HttpResponseMessage found = await lookupOnly.GetAsync(lookup);
        Assert.AreEqual(HttpStatusCode.OK, found.StatusCode);
        ManagementConfigurationOperationResult recovered = await Decode<ManagementConfigurationOperationResult>(found);
        Assert.AreEqual("found", recovered.ResultSource);
        Assert.AreEqual(original.Operation, recovered.Operation);
        Assert.AreEqual(original.Result, recovered.Result);
        Assert.AreEqual(operations, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(audit, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
    }

    private static bool IsOperator(CapabilityGrant grant, string capability)
        => grant.Subject?.Issuer == ManagementHostFixture.Issuer && grant.Subject.Identifier == "operator-a"
            && grant.Capability == capability;

    // Copies this existing fixture's native bootstrap; it changes only its captured grants.
    private static ManagementSecurityOptions NativeGrants(ManagementSecurityOptions original, IReadOnlyList<CapabilityGrant> grants)
        => new()
        {
            Mode = original.Mode,
            Profile = original.Profile,
            AuthenticationScheme = original.AuthenticationScheme,
            BusinessAddress = original.BusinessAddress,
            BusinessPort = original.BusinessPort,
            OperationalPort = original.OperationalPort,
            ServerCertificateFile = original.ServerCertificateFile,
            ServerPrivateKeyFile = original.ServerPrivateKeyFile,
            ServerTrustBundleFile = original.ServerTrustBundleFile,
            CertificateRevocation = original.CertificateRevocation,
            BorrowedNativeKeyScope = original.BorrowedNativeKeyScope,
            PolicyVersion = original.PolicyVersion,
            IdentityMappings = original.IdentityMappings,
            NativeProvider = original.NativeProvider,
            Grants = grants
        };

    private static async Task<ManagementApplication> Application(HttpClient actor, string application)
    {
        using HttpResponseMessage response = await actor.GetAsync("applications/" + application);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return ManagementWire.DecodeResponse<ManagementApplication>(await response.Content.ReadAsByteArrayAsync())!;
    }

    private static string MetadataLookup(string application, string key)
        => "applications/" + application + "/metadata-operations/" + key;

    private static async Task<HttpResponseMessage> MetadataWrite(HttpClient actor, string application, string key, string etag,
        ManagementMetadataCommand command)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "applications/" + application + "/metadata");
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Content = new ByteArrayContent(ManagementWire.EncodeRequest(command));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await actor.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> ConfigurationWrite(HttpClient actor, string path, string key, string etag, object command)
    {
        using var request = new HttpRequestMessage(command is ManagementConfigurationAssignmentCommand ? HttpMethod.Put : HttpMethod.Post, path);
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        byte[] bytes = command switch
        {
            ManagementConfigurationPublicationCommand value => ManagementConfigurationWire.EncodeRequest(value),
            ManagementConfigurationReleaseCommand value => ManagementConfigurationWire.EncodeRequest(value),
            ManagementConfigurationAssignmentCommand value => ManagementConfigurationWire.EncodeRequest(value),
            _ => throw new ArgumentException("A fixed configuration command is required.", nameof(command))
        };
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await actor.SendAsync(request);
    }
}

