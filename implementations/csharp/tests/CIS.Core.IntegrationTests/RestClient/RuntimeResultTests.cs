using System.Diagnostics;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.Tests.UnitTests.RestClient;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
public sealed class RuntimeResultTests
{
    [TestMethod]
    [DataRow("", false)]
    [DataRow("null", true)]
    [DataRow("\"\"", true)]
    public async Task PublicJsonResultDistinguishesAbsentNullAndEmptyString(string wire, bool present)
    {
        await using var server = await RestHttpLocalServer.StartAsync(context => context.Response.WriteAsync(wire));
        await using var client = new HttpRestClient(new("orders") { BaseUrl = server.Address });
        RestResult<string?> optional = await client.Get<string?>("/").WaitAsync(RestHttpTestSupport.Deadline);
        Assert.IsTrue(optional.Success); Assert.AreEqual(present, optional.Context.HasValue);
        Assert.AreEqual(wire == "\"\"" ? "" : null, optional.Value);
        RestResult<string?> required = await client.Get<string?>("/", new() { RequireValue = true }).WaitAsync(RestHttpTestSupport.Deadline);
        Assert.AreEqual(present, required.Success);
        Assert.AreEqual(present ? RestFailureKind.None : RestFailureKind.InvalidResponse, required.Context.FailureKind);
        RestResult<RestNoValue> head = await client.Head("/", new() { RequireValue = true }).WaitAsync(RestHttpTestSupport.Deadline);
        Assert.IsTrue(head.Success); Assert.IsFalse(head.Context.HasValue); Assert.AreEqual(1, head.Context.AttemptCount);
    }

    [TestMethod]
    public async Task CorrelationCapturesOnlyTheExistingEntryTraceBeforeAnAuthenticationCallbackReplacesAmbientActivity()
    {
        await using var server = await RestHttpLocalServer.StartAsync(context => context.Response.WriteAsync("1"));
        Activity? original = Activity.Current;
        Activity.Current = null;
        try
        {
            await using var client = new HttpRestClient(new("orders") { BaseUrl = server.Address });
            RestResult<int> absent = await client.Get<int>("/").WaitAsync(RestHttpTestSupport.Deadline);
            Assert.IsNull(absent.Context.CorrelationId);
            using var entry = new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).Start();
            string expected = entry.TraceId.ToHexString();
            RestResult<int> result = await client.Get<int>("/", new()
            {
                Authenticate = async (_, _) =>
                {
                    using var nested = new Activity("auth").SetParentId("00-11111111111111111111111111111111-2222222222222222-01").Start();
                    await Task.Yield();
                }
            }).WaitAsync(RestHttpTestSupport.Deadline);
            Assert.IsTrue(result.Success); Assert.AreEqual(expected, result.Context.CorrelationId);
        }
        finally { Activity.Current = original; }
    }

    [TestMethod]
    public async Task HandlerShortCircuitUsesTheNormalReaderAndDisposesItsResponseWithZeroPrimaryDispatches()
    {
        var response = new RestHttpTestResponse(200, new MemoryStream(RestHttpTestSupport.Utf8("23")), "application/json");
        var options = new RestClientOptions("orders") { BaseUrl = "http://127.0.0.1:1/" };
        options.Handlers.Add(new ShortCircuit(response));
        await using var client = new HttpRestClient(options);
        RestResult<int> result = await client.Get<int>("/").WaitAsync(RestHttpTestSupport.Deadline);
        Assert.IsTrue(result.Success); Assert.IsTrue(result.Context.HasValue); Assert.AreEqual(23, result.Value);
        Assert.AreEqual(0, result.Context.AttemptCount); Assert.AreEqual(200, result.Context.HttpStatusCode);
        Assert.IsFalse(response.Body.CanRead);
    }

    private sealed class ShortCircuit(IRestResponse response) : IRestExchangeHandler
    {
        public ValueTask<IRestResponse> HandleAsync(RestRequestContext request, RestExchange next, CancellationToken cancellationToken)
            => ValueTask.FromResult(response);
    }
}
