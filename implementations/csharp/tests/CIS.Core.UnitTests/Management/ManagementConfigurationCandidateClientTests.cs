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
public sealed class ManagementConfigurationCandidateClientTests
{
    [TestMethod]
    public async Task OptionalCandidateClientPostsExactDraftWithoutMutationHeadersAndKeepsItsActualDecodeOwner()
    {
        ManagementConfigurationCandidatePreviewCommand command = Command<ManagementConfigurationCandidatePreviewCommand>("configuration-candidate-command-valid");
        ManagementConfigurationCandidatePreview response = Value<ManagementConfigurationCandidatePreview>("configuration-candidate-preview-valid");
        int calls = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        {
            calls++;
            Assert.AreEqual("POST", context.Request.Method);
            Assert.IsTrue(context.Request.Path.Value!.EndsWith("/configuration/candidate-preview", StringComparison.Ordinal));
            Assert.AreEqual("1", context.Request.Query["pageSize"].Single());
            Assert.IsFalse(context.Request.Headers.ContainsKey("Idempotency-Key"));
            Assert.IsFalse(context.Request.Headers.ContainsKey("If-Match"));
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body, context.RequestAborted);
            ManagementConfigurationCandidatePreviewCommand captured = ManagementConfigurationWire.DecodeRequest<ManagementConfigurationCandidatePreviewCommand>(body.ToArray())!;
            Assert.AreEqual(command, captured);
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(ManagementConfigurationWire.EncodeResponse(response), context.RequestAborted);
        });
        ManagementConfigurationClientOptions options = Options();
        RestClientOptions native = Native(options);
        native.AllowedOrigins.Add(new Uri(server.Address).GetLeftPart(UriPartial.Authority));
        native.Handlers.Add(new LocalTransport(server.Address));
        await using var rest = new HttpRestClient(native);
        IManagementConfigurationCandidatePreviewClient client = new ManagementConfigurationClient(rest, options);
        ManagementConfigurationCallResult<ManagementConfigurationCandidatePreview> result = await client.PreviewCandidateAsync(command, new(1));
        Assert.IsTrue(result.Exchange.Success, result.Exchange.ErrorCode);
        Assert.AreEqual(response.DraftSha256, result.Exchange.Value!.DraftSha256);
        Assert.AreEqual(ManagementConfigurationWire.ComputePayloadSha256(command.PayloadJson), result.Exchange.Value.DraftSha256);
        Assert.AreEqual(1, calls);
        Assert.IsNull(result.OperationKey);
        Assert.AreEqual("configuration", result.Exchange.Context.OperationName);
        IList<ManagementConfigurationChange> changes = (IList<ManagementConfigurationChange>)result.Exchange.Value.Changes;
        Assert.ThrowsExactly<NotSupportedException>(() => changes.Clear());
    }

    [TestMethod]
    [DataRow("applicationId", "22222222-2222-2222-2222-222222222222")]
    [DataRow("configurationId", "foreign.settings")]
    [DataRow("schemaVersion", "2")]
    [DataRow("selectedSource", "common")]
    [DataRow("sourceETag", "\"foreign\"")]
    [DataRow("assignmentETag", "\"foreign\"")]
    [DataRow("draftSha256", "0000000000000000000000000000000000000000000000000000000000000000")]
    [DataRow("baseline", "changed")]
    public async Task WireValidForeignEchoesOrDigestCannotBecomeAcceptedCandidateFacts(string field, string foreign)
    {
        ManagementConfigurationCandidatePreviewCommand command = Command<ManagementConfigurationCandidatePreviewCommand>("configuration-candidate-command-valid");
        JsonObject response = JsonNode.Parse(Case("configuration-candidate-preview-valid"))!.AsObject();
        if (field == "baseline")
        {
            response["commonSource"] = new JsonObject { ["state"] = "present", ["publicationId"] = Publication };
        }
        else
        {
            response[field] = foreign;
        }
        if (field is "applicationId" or "configurationId" or "schemaVersion")
        {
            response["currentAssignment"]![field] = foreign;
        }
        if (field == "assignmentETag")
        {
            response["currentAssignment"]!["eTag"] = foreign;
        }
        Assert.IsNotNull(ManagementConfigurationWire.DecodeResponse<ManagementConfigurationCandidatePreview>(Bytes(response.ToJsonString())));
        int calls = 0;
        ManagementConfigurationClientOptions options = Options();
        await using var rest = new HttpRestClient(Native(options, new Handler((_, _) =>
        {
            calls++;
            return ValueTask.FromResult<IRestResponse>(new Response(200, new MemoryStream(Bytes(response.ToJsonString()))));
        })));
        var client = new ManagementConfigurationClient(rest, options);
        ManagementConfigurationCallResult<ManagementConfigurationCandidatePreview> result = await client.PreviewCandidateAsync(command);
        Assert.IsFalse(result.Exchange.Success);
        Assert.IsNull(result.Exchange.Value);
        Assert.AreEqual(RestFailureKind.InvalidResponse, result.Exchange.Context.FailureKind);
        Assert.AreEqual(1, calls, "A read-only preview does not authorize automatic command replay.");
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

