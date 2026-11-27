using System.Text.Json.Nodes;
using CIS.Management;
using CIS.Management.Wire;
using CIS.RestClient;
using CIS.RestClient.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.UnitTests.Management.ManagementConfigurationClientTestSupport;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ManagementConfigurationCreationClientTests
{
    private const string Application = "11111111-1111-4111-8111-111111111111";

    [TestMethod]
    public void AdditiveCreationFieldsPreserveThePublishedPositionalConstructorAndDeconstructor()
    {
        var original = new ManagementConfigurationDecision(1, "sourcePublished", "applied", "published", Time,
            null, null, Source, Publication, new string('a', 64), null, null, null, null, null, "\"source.2\"", null, null);
        var (version, kind, decision, code, decidedAt, catalog, manifest, source, publication, sha, app, configuration,
            schema, release, generation, etag, intent, assignment) = original;
        Assert.AreEqual(1, version);
        Assert.AreEqual("sourcePublished", kind);
        Assert.AreEqual(Source, source);
        Assert.AreEqual(Publication, publication);
        Assert.AreEqual("\"source.2\"", etag);
        Assert.IsNotNull(typeof(ManagementConfigurationDecision).GetConstructor(
            [typeof(int), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
                typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
                typeof(string), typeof(string), typeof(string), typeof(ManagementConfigurationAssignmentMap)]));
        ManagementConfigurationDecision extended = original with
        {
            BindingId = Registration,
            HostApplicationName = "ExampleHost",
            SourceETag = "\"source.1\""
        };
        Assert.AreEqual(original.SourceId, extended.SourceId);
        Assert.AreEqual(Registration, extended.BindingId);
        _ = (decision, code, decidedAt, catalog, manifest, sha, app, configuration, schema, release, generation, intent, assignment);
    }

    [TestMethod]
    public async Task OptionalPreparationClientUsesFourFiniteRoutesAndClosedCommandWithoutAutomaticPostReplay()
    {
        int calls = 0;
        ManagementConfigurationCreationCommand command = Command<ManagementConfigurationCreationCommand>("configuration-creation-command-valid");
        ManagementConfigurationClientOptions options = Options();
        await using var rest = new HttpRestClient(Native(options, new Handler((request, _) =>
        {
            string suffix = calls++ switch
            {
                0 => "configuration-creations/context",
                1 => "configuration-creations",
                2 => "configuration-creation-operations/original-create-key",
                _ => "configuration-summaries"
            };
            Assert.IsTrue(new Uri(request.Url).AbsolutePath.EndsWith("/applications/" + Application + "/" + suffix, StringComparison.Ordinal));
            string json = suffix switch
            {
                "configuration-creations/context" => Case("configuration-creation-context-valid"),
                "configuration-summaries" => Case("configuration-summary-unknown-origin"),
                _ => Case("configuration-created-decision-valid")
            };
            return ValueTask.FromResult<IRestResponse>(new Response(200, new MemoryStream(Bytes(json))));
        })));
        IManagementConfigurationCreationClient client = new ManagementConfigurationClient(rest, options);
        Assert.IsTrue((await client.GetCreationContextAsync(Application)).Exchange.Success);
        ManagementConfigurationCallResult<ManagementConfigurationOperationResult> created = await client.CreateConfigurationAsync(Application,
            "original-create-key", "\"creation-context\"", command);
        Assert.IsTrue(created.Exchange.Success, created.Exchange.ErrorMessage);
        Assert.AreEqual("original-create-key", created.OperationKey);
        Assert.IsTrue((await client.GetCreationOperationAsync(Application, "original-create-key")).Exchange.Success);
        ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationSummary>> page = await client.GetConfigurationSummariesAsync(Application, new(1));
        Assert.IsTrue(page.Exchange.Success);
        Assert.AreEqual("future-origin", page.Exchange.Value!.Items.Single().Origin);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ManagementConfigurationSummary>)page.Exchange.Value.Items).Clear());
        Assert.AreEqual(4, calls);
    }

    [TestMethod]
    [DataRow("applicationId")]
    [DataRow("configurationId")]
    [DataRow("scope")]
    public async Task ForeignCreationFactsCannotBecomeAcceptedOriginalsEvenWhenTheWireIsValid(string field)
    {
        JsonObject response = JsonNode.Parse(Case("configuration-created-decision-valid"))!.AsObject();
        if (field == "scope")
        {
            response["operation"]!["scope"] = "management.configuration.v1/management.configuration.create/" + AppId + "/management.actor.v1." + new string('a', 64);
        }
        else
        {
            response["result"]![field] = field == "applicationId" ? AppId : "foreign.settings";
        }
        Assert.IsNotNull(ManagementConfigurationWire.DecodeResponse<ManagementConfigurationOperationResult>(Bytes(response.ToJsonString())));
        int calls = 0;
        ManagementConfigurationClientOptions options = Options();
        await using var rest = new HttpRestClient(Native(options, new Handler((_, _) =>
        {
            calls++;
            return ValueTask.FromResult<IRestResponse>(new Response(200, new MemoryStream(Bytes(response.ToJsonString()))));
        })));
        var client = new ManagementConfigurationClient(rest, options);
        ManagementConfigurationCallResult<ManagementConfigurationOperationResult> result = await client.CreateConfigurationAsync(Application,
            "original-create-key", "\"creation-context\"", Command<ManagementConfigurationCreationCommand>("configuration-creation-command-valid"));
        Assert.IsFalse(result.Exchange.Success);
        Assert.IsNull(result.Exchange.Value);
        Assert.AreEqual(RestFailureKind.InvalidResponse, result.Exchange.Context.FailureKind);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task SummaryResponseCannotExceedTheRequestedPageOrCrossTheRequestedApplication()
    {
        ManagementPage<ManagementConfigurationSummary> original = Value<ManagementPage<ManagementConfigurationSummary>>("configuration-summary-unknown-origin");
        ManagementConfigurationSummary row = original.Items.Single();
        var oversized = new ManagementPage<ManagementConfigurationSummary>([row, row with
        {
            SourceId = "22222222-2222-4222-8222-222222222222", BindingId = "55555555-5555-4555-8555-555555555555", ConfigurationId = "second.settings"
        }]);
        foreach (ManagementPage<ManagementConfigurationSummary> response in new[] { oversized,
            new ManagementPage<ManagementConfigurationSummary>([row with { ApplicationId = AppId }]) })
        {
            ManagementConfigurationClientOptions options = Options();
            await using var rest = new HttpRestClient(Native(options, new Handler((_, _) =>
                ValueTask.FromResult<IRestResponse>(new Response(200, new MemoryStream(ManagementConfigurationWire.EncodeResponse(response)))))));
            var client = new ManagementConfigurationClient(rest, options);
            ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationSummary>> result = await client.GetConfigurationSummariesAsync(Application, new(1));
            Assert.IsFalse(result.Exchange.Success);
            Assert.IsNull(result.Exchange.Value);
            Assert.AreEqual(RestFailureKind.InvalidResponse, result.Exchange.Context.FailureKind);
        }
    }
}
