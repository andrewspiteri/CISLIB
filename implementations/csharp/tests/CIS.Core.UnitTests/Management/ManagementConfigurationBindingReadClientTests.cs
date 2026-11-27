using System.Text.Json.Nodes;
using CIS.Management;
using CIS.Management.Wire;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.Tests.UnitTests.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.UnitTests.Management.ManagementConfigurationClientTestSupport;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ManagementConfigurationBindingReadClientTests
{
    [TestMethod]
    public async Task OptionalBindingReadsUseExistingNativeConfigurationProfileAndFrozenRoutes()
    {
        ManagementPage<ManagementConfigurationRelease> releases = Value<ManagementPage<ManagementConfigurationRelease>>("configuration-releases-valid");
        var status = new ManagementPage<ManagementConfigurationAdoptionEntry>(
            [new(AppId, "Development", "shared.settings", "1", "noInstances", "noInstances", "noInstances")]);
        int calls = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        {
            string suffix = calls++ == 0 ? "releases" : "status";
            Assert.AreEqual("GET", context.Request.Method);
            Assert.IsTrue(context.Request.Path.Value!.EndsWith("/applications/" + AppId + "/configuration/shared.settings/" + suffix,
                StringComparison.Ordinal));
            Assert.AreEqual("1", context.Request.Query["schemaVersion"].Single());
            Assert.AreEqual("2", context.Request.Query["pageSize"].Single());
            Assert.HasCount(2, context.Request.Query);
            context.Response.ContentType = "application/json";
            byte[] bytes = suffix == "releases" ? ManagementConfigurationWire.EncodeResponse(releases)
                : ManagementConfigurationWire.EncodeResponse(status);
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
        });
        ManagementConfigurationClientOptions options = Options();
        RestClientOptions native = Native(options);
        native.AllowedOrigins.Add(new Uri(server.Address).GetLeftPart(UriPartial.Authority));
        native.Handlers.Add(new LocalTransport(server.Address));
        await using var rest = new HttpRestClient(native);
        IManagementConfigurationBindingReadClient client = new ManagementConfigurationClient(rest, options);
        ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationRelease>> first = await client.GetReleasesAsync(
            AppId, "shared.settings", "1", new(2));
        Assert.IsTrue(first.Exchange.Success, first.Exchange.ErrorCode);
        Assert.HasCount(2, first.Exchange.Value!.Items);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ManagementConfigurationRelease>)first.Exchange.Value.Items).Clear());
        ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationAdoptionEntry>> second = await client.GetBindingStatusAsync(
            AppId, "shared.settings", "1", new(2));
        Assert.IsTrue(second.Exchange.Success, second.Exchange.ErrorCode);
        Assert.AreEqual("noInstances", second.Exchange.Value!.Items.Single().Convergence);
        Assert.AreEqual(2, calls);
        Assert.AreEqual("configuration", first.Exchange.Context.OperationName);
        Assert.IsNull(first.OperationKey);
    }

    [TestMethod]
    [DataRow("releases", "applicationId", "22222222-2222-2222-2222-222222222222")]
    [DataRow("releases", "configurationId", "foreign.settings")]
    [DataRow("releases", "schemaVersion", "2")]
    [DataRow("status", "applicationId", "22222222-2222-2222-2222-222222222222")]
    [DataRow("status", "configurationId", "foreign.settings")]
    [DataRow("status", "schemaVersion", "2")]
    public async Task EveryReturnedRowMustMatchTheExactRequestedBindingSelectors(string kind, string field, string foreign)
    {
        JsonObject response = kind == "releases" ? JsonNode.Parse(Case("configuration-releases-valid"))!.AsObject()
            : JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(new
            {
                items = new[] { new ManagementConfigurationAdoptionEntry(AppId, "Development", "shared.settings", "1",
                    "noInstances", "noInstances", "noInstances"), new ManagementConfigurationAdoptionEntry(AppId, "Development",
                    "shared.settings", "1", "unknown", "unknown", "unknown") }
            }, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            }))!.AsObject();
        response["items"]!.AsArray()[1]![field] = foreign;
        ManagementConfigurationClientOptions options = Options();
        await using var rest = new HttpRestClient(Native(options, new Handler((_, _) =>
            ValueTask.FromResult<IRestResponse>(new Response(200, new MemoryStream(Bytes(response.ToJsonString())))))));
        var client = new ManagementConfigurationClient(rest, options);
        if (kind == "releases")
        {
            ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationRelease>> result = await client.GetReleasesAsync(AppId, "shared.settings", "1");
            Assert.IsFalse(result.Exchange.Success);
            Assert.IsNull(result.Exchange.Value);
            Assert.AreEqual(RestFailureKind.InvalidResponse, result.Exchange.Context.FailureKind);
        }
        else
        {
            ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationAdoptionEntry>> result = await client.GetBindingStatusAsync(AppId, "shared.settings", "1");
            Assert.IsFalse(result.Exchange.Success);
            Assert.IsNull(result.Exchange.Value);
            Assert.AreEqual(RestFailureKind.InvalidResponse, result.Exchange.Context.FailureKind);
        }
    }

    private sealed class LocalTransport(string address) : IRestExchangeHandler
    {
        public ValueTask<IRestResponse> HandleAsync(RestRequestContext request, RestExchange next, CancellationToken cancellationToken)
        {
            Uri source = new(request.Url);
            request.Url = new Uri(new Uri(address), source.PathAndQuery).AbsoluteUri;
            return next(request, cancellationToken);
        }
    }
}

