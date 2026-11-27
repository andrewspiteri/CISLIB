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
public sealed class ManagementConfigurationEditorClientTests
{
    [TestMethod]
    public void ResponseCaptureKeepsExactDefaultsAndFreezesTheSingleStructuralDeclaration()
    {
        byte[] bytes = Bytes(Case("configuration-editor-available-valid"));
        ManagementConfigurationEditorContext value = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationEditorContext>(bytes)!;
        Assert.IsNotNull(value);
        Assert.IsTrue(value.Defaults.DefaultsJson!.Contains("9007199254740993", StringComparison.Ordinal));
        Assert.IsTrue(value.Defaults.DefaultsJson.Contains("Café", StringComparison.Ordinal));
        ManagementConfigurationEditorContext roundTrip = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationEditorContext>(
            ManagementConfigurationWire.EncodeResponse(value))!;
        Assert.AreEqual(value.Defaults.DefaultsJson, roundTrip.Defaults.DefaultsJson);
        IList<ManagementConfigurationMember> members = (IList<ManagementConfigurationMember>)value.Shape.Members!;
        Assert.ThrowsExactly<NotSupportedException>(() => members.Clear());
        Assert.ThrowsExactly<NotSupportedException>(() => ManagementConfigurationWire.EncodeRequest(value));
    }

    [TestMethod]
    public async Task OptionalClientUsesTheExistingConfigurationProfileAndEchoesExactTargetAndSourceInsideNativeDecode()
    {
        ManagementConfigurationEditorContext value = Value<ManagementConfigurationEditorContext>("configuration-editor-available-valid");
        int calls = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        {
            calls++;
            Assert.AreEqual("GET", context.Request.Method);
            Assert.IsTrue(context.Request.Path.Value!.EndsWith("/applications/" + value.ApplicationId
                + "/configuration/shared.settings/editor-context", StringComparison.Ordinal));
            Assert.AreEqual("1", context.Request.Query["schemaVersion"].Single());
            Assert.AreEqual("application", context.Request.Query["source"].Single());
            Assert.HasCount(2, context.Request.Query);
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(ManagementConfigurationWire.EncodeResponse(value), context.RequestAborted);
        });
        ManagementConfigurationClientOptions options = Options();
        RestClientOptions native = Native(options);
        native.AllowedOrigins.Add(new Uri(server.Address).GetLeftPart(UriPartial.Authority));
        native.Handlers.Add(new LocalTransport(server.Address));
        await using var rest = new HttpRestClient(native);
        IManagementConfigurationEditorClient client = new ManagementConfigurationClient(rest, options);
        ManagementConfigurationCallResult<ManagementConfigurationEditorContext> result = await client.GetBindingEditorContextAsync(
            value.ApplicationId, "shared.settings", "1", "application");
        Assert.IsTrue(result.Exchange.Success, result.Exchange.ErrorCode);
        Assert.AreEqual(value.Defaults.DefaultsJson, result.Exchange.Value!.Defaults.DefaultsJson);
        Assert.AreEqual(1, calls);
        Assert.AreEqual("configuration", result.Exchange.Context.OperationName);
    }

    [TestMethod]
    [DataRow("applicationId", "22222222-2222-2222-2222-222222222222")]
    [DataRow("configurationId", "foreign.settings")]
    [DataRow("schemaVersion", "2")]
    [DataRow("selectedSource", "common")]
    public async Task ForeignEchoesAreRefusedBeforeAnyProtectedValueIsReturned(string field, string foreign)
    {
        JsonObject value = JsonNode.Parse(Case("configuration-editor-available-valid"))!.AsObject();
        value[field] = foreign;
        if (field == "selectedSource")
        {
            value["defaults"] = new JsonObject { ["state"] = "redacted" };
        }
        ManagementConfigurationClientOptions options = Options();
        await using var rest = new HttpRestClient(Native(options, new Handler((_, _) =>
            ValueTask.FromResult<IRestResponse>(new Response(200, new MemoryStream(Bytes(value.ToJsonString())))))));
        var client = new ManagementConfigurationClient(rest, options);
        ManagementConfigurationCallResult<ManagementConfigurationEditorContext> result = await client.GetBindingEditorContextAsync(
            AppId, "shared.settings", "1", "application");
        Assert.IsFalse(result.Exchange.Success);
        Assert.IsNull(result.Exchange.Value);
        Assert.AreEqual(RestFailureKind.InvalidResponse, result.Exchange.Context.FailureKind);
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

