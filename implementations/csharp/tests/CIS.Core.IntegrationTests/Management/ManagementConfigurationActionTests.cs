using System.Net;
using System.Text;
using System.Text.Json;
using CIS.Management;
using CIS.Management.Service.Configuration;
using CIS.Management.Wire;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementConfigurationActionTests
{
    [TestMethod]
    public async Task NativeStoredSourceAndBindingEligibilitySeparatesReadOnlyOperatorsFromRecoverableMutations()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        Assert.IsFalse((await ServiceInfo(actor)).Features.Any(feature => feature.OperationId == "getConfigurationSourceActions"));
        await Apply(actor, await Catalog(actor), "actions-catalog");
        ManagementPage<ManagementConfigurationDefinition> definitions = await Definitions(actor);
        ManagementConfigurationDefinition source = definitions.Items.Single(row => row.Kind == "common");
        ManagementConfigurationDefinition application = definitions.Items.Single(row => row.HostApplicationName == "FirstHost");
        string sourcePath = "configuration/sources/" + source.SourceId + "/actions";
        string bindingPath = BindingPath(application);

        ManagementConfigurationActions sourceActions = await Actions(actor, sourcePath);
        Assert.AreEqual(source.SourceId, sourceActions.ResourceId);
        CollectionAssert.AreEquivalent(new[] { "validateConfiguration", "publishConfigurationSource" },
            sourceActions.Actions.Select(item => item.OperationId).ToArray());
        Assert.IsTrue(sourceActions.Actions.All(item => item.Supported && item.Allowed && item.ReasonCode is null));
        ManagementConfigurationActions bindingActions = await Actions(actor, bindingPath);
        Assert.AreEqual(application.BindingId, bindingActions.ResourceId);
        CollectionAssert.AreEquivalent(new[] { "previewConfiguration", "createConfigurationRelease", "replaceConfigurationAssignments" },
            bindingActions.Actions.Select(item => item.OperationId).ToArray());
        Assert.IsTrue(bindingActions.Actions.All(item => item.Supported && item.Allowed));

        HttpClient reader = fixture.Client("operator-b");
        ManagementConfigurationActions readOnlySource = await Actions(reader, sourcePath);
        Assert.IsTrue(readOnlySource.Actions.All(item => item.Supported && !item.Allowed));
        Assert.IsTrue(readOnlySource.Actions.All(item => item.ReasonCode == "management.action.not_authorized"));
        ManagementConfigurationActions readOnlyBinding = await Actions(reader, bindingPath);
        Assert.IsTrue(readOnlyBinding.Actions.Single(item => item.OperationId == "previewConfiguration").Allowed);
        Assert.IsTrue(readOnlyBinding.Actions.Where(item => item.OperationId != "previewConfiguration").All(item => item.Supported && !item.Allowed));

        ManagementServiceInfo info = await ServiceInfo(actor);
        Assert.AreEqual(1, info.Features.Count(feature => feature.OperationId == "getConfigurationSourceActions"));
        Assert.AreEqual(1, info.Features.Count(feature => feature.OperationId == "getConfigurationBindingActions"));
        foreach ((string operation, string capability) in new[]
        {
            ("publishConfigurationSource", ManagementConfigurationCapabilities.Publish),
            ("createConfigurationRelease", ManagementConfigurationCapabilities.ReleaseCreate),
            ("replaceConfigurationAssignments", ManagementConfigurationCapabilities.AssignmentWrite)
        })
        {
            CollectionAssert.AreEquivalent(new[] { capability, ManagementConfigurationCapabilities.OperationsRead },
                info.Features.Single(feature => feature.OperationId == operation).RequiredCapabilities.ToArray());
        }
        CollectionAssert.AreEqual(new[] { ManagementConfigurationCapabilities.CatalogApply },
            info.Features.Single(feature => feature.OperationId == "applyConfigurationCatalog").RequiredCapabilities.ToArray());
    }

    [TestMethod]
    public async Task HiddenTargetsAndForgedQueryInputsCannotDiscloseActionEligibilityOrSelectAnotherResource()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "actions-hidden-catalog");
        ManagementPage<ManagementConfigurationDefinition> definitions = await Definitions(actor);
        ManagementConfigurationDefinition source = definitions.Items.Single(row => row.Kind == "common");
        ManagementConfigurationDefinition application = definitions.Items.Single(row => row.HostApplicationName == "FirstHost");
        string sourcePath = "configuration/sources/" + source.SourceId + "/actions";
        string bindingPath = BindingPath(application);
        foreach (string path in new[] { sourcePath, bindingPath })
        {
            using HttpResponseMessage hidden = await fixture.Client("reader").GetAsync(path);
            Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
            ManagementConfigurationProblem? problem = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationProblem>(await hidden.Content.ReadAsByteArrayAsync());
            Assert.IsNotNull(problem);
            Assert.AreEqual("management.configuration.not_found", problem.Code);
            string body = await hidden.Content.ReadAsStringAsync();
            Assert.IsFalse(body.Contains(source.SourceId, StringComparison.Ordinal));
            Assert.IsFalse(body.Contains(application.BindingId!, StringComparison.Ordinal));
        }
        foreach (string path in new[]
        {
            sourcePath + "?resourceId=" + application.BindingId,
            sourcePath + "?cursor=forged",
            bindingPath + "&resourceId=" + source.SourceId,
            bindingPath + "&schemaVersion=1",
            bindingPath + "&cursor=forged"
        })
        {
            using HttpResponseMessage rejected = await actor.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode, path);
        }
        using (var request = new HttpRequestMessage(HttpMethod.Get, sourcePath))
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using HttpResponseMessage rejected = await actor.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        using HttpResponseMessage absent = await actor.GetAsync(bindingPath.Replace("schemaVersion=1", "schemaVersion=2", StringComparison.Ordinal));
        Assert.AreEqual(HttpStatusCode.NotFound, absent.StatusCode);
    }

    [TestMethod]
    public async Task NativeHttp2DataBodiesCannotBypassTheBodylessActionReadContract()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "actions-http2-catalog");
        ManagementPage<ManagementConfigurationDefinition> definitions = await Definitions(actor);
        ManagementConfigurationDefinition source = definitions.Items.Single(row => row.Kind == "common");
        ManagementConfigurationDefinition application = definitions.Items.Single(row => row.HostApplicationName == "FirstHost");
        foreach (string path in new[] { "configuration/sources/" + source.SourceId + "/actions", BindingPath(application) })
        {
            using (var bodyless = new HttpRequestMessage(HttpMethod.Get, path)
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact
            })
            {
                using HttpResponseMessage allowed = await actor.SendAsync(bodyless);
                Assert.AreEqual(HttpVersion.Version20, allowed.Version);
                Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode);
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, path)
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                Content = new UnknownLengthActionContent()
            };
            Assert.IsNull(request.Content.Headers.ContentLength);
            Assert.AreEqual(0, request.Headers.TransferEncoding.Count);
            using HttpResponseMessage refused = await actor.SendAsync(request);
            Assert.AreEqual(HttpVersion.Version20, refused.Version);
            Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.IsNull(request.Content.Headers.ContentLength);
            Assert.AreEqual(0, request.Headers.TransferEncoding.Count);
        }
    }

    [TestMethod]
    public async Task UnselectedConfigurationDoesNotAdvertiseOrInventSupportedAdministrativeActions()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1);
        HttpClient actor = fixture.Client("operator-a");
        Assert.IsFalse((await ServiceInfo(actor)).Features.Any(feature => feature.OperationId is "getConfigurationSourceActions" or "getConfigurationBindingActions"));
        foreach (string path in new[]
        {
            "configuration/sources/" + Guid.NewGuid().ToString("D") + "/actions",
            "applications/" + fixture.ApplicationA + "/configuration/shared.settings/actions?schemaVersion=1"
        })
        {
            using HttpResponseMessage response = await actor.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private static string BindingPath(ManagementConfigurationDefinition definition)
        => "applications/" + definition.ApplicationId + "/configuration/" + definition.ConfigurationId + "/actions?schemaVersion=" + definition.SchemaVersion;

    private static async Task<ManagementConfigurationActions> Actions(HttpClient actor, string path)
    {
        using HttpResponseMessage response = await actor.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.IsTrue(response.Headers.CacheControl?.NoStore == true);
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        ManagementConfigurationActions? result = ManagementSchedulerWire.DecodeResponse<ManagementConfigurationActions>(bytes);
        Assert.IsNotNull(result);
        using JsonDocument document = JsonDocument.Parse(bytes);
        CollectionAssert.AreEquivalent(new[] { "resourceId", "actions" }, document.RootElement.EnumerateObject().Select(item => item.Name).ToArray());
        return result;
    }

    private static async Task<ManagementServiceInfo> ServiceInfo(HttpClient actor)
    {
        using HttpResponseMessage response = await actor.GetAsync("service-info");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        ManagementServiceInfo? info = ManagementWire.DecodeResponse<ManagementServiceInfo>(await response.Content.ReadAsByteArrayAsync());
        Assert.IsNotNull(info);
        return info;
    }

    private sealed class UnknownLengthActionContent : HttpContent
    {
        private static readonly byte[] Payload = Encoding.UTF8.GetBytes("{}");

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            => stream.WriteAsync(Payload, cancellationToken).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
