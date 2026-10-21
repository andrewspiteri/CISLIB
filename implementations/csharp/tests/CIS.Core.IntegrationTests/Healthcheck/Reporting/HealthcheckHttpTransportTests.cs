using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

[TestClass]
public sealed class HealthcheckHttpTransportTests
{
    private sealed class Host : IAsyncDisposable
    {
        private readonly Process child;
        private readonly Task<string> stderr;
        internal HttpClient Client { get; } = new() { Timeout = TransportTestSupport.Limit };
        internal string? PipeName { get; private set; }
        private Host(string mode)
        {
            child = Process.Start(TransportTestSupport.StartInfo("healthcheck-http-host", "CIS.Healthcheck.HttpFixture", [mode]))!;
            stderr = child.StandardError.ReadToEndAsync();
        }
        internal static async Task<Host> Start(string mode = "normal")
        {
            var host = new Host(mode);
            try
            {
                JsonObject ready = await host.Read();
                Assert.AreEqual("ready", (string?)ready["status"]);
                Assert.AreEqual(".NET 10.0.12", (string?)ready["runtime"]);
                Assert.AreEqual(host.child.Id, (int?)ready["pid"]);
                host.Client.BaseAddress = new Uri((string)ready["address"]!);
                host.PipeName = (string?)ready["pipeName"];
                return host;
            }
            catch { await host.DisposeAsync(); throw; }
        }
        internal async Task<JsonObject> Command(string command = "state")
        {
            await child.StandardInput.WriteLineAsync(command);
            await child.StandardInput.FlushAsync();
            return await Read();
        }
        private async Task<JsonObject> Read()
        {
            string? line = await child.StandardOutput.ReadLineAsync().WaitAsync(TransportTestSupport.Limit);
            Assert.IsNotNull(line, "HTTP host ended before its observation.");
            return JsonNode.Parse(line)!.AsObject();
        }
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            try
            {
                if (!child.HasExited)
                {
                    await child.StandardInput.WriteLineAsync("exit"); await child.StandardInput.FlushAsync();
                    await child.WaitForExitAsync().WaitAsync(TransportTestSupport.Limit);
                }
                Assert.AreEqual(0, child.ExitCode);
                Assert.AreEqual("", await stderr.WaitAsync(TransportTestSupport.Limit));
            }
            finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); } child.Dispose(); }
        }
    }

    [TestMethod]
    [DataRow("/_health", "health")]
    [DataRow("/_HeAlTh/", "health")]
    [DataRow("/_health/ready", "readiness")]
    [DataRow("/_HEALTH/READY/", "readiness")]
    [DataRow("/_health/details", "details")]
    [DataRow("/_health/DETAILS/?view=readiness", "details")]
    public async Task RealKestrelServesKnownViewsWithCachePreventionAndNoBusinessEffects(string path, string view)
    {
        await using var host = await Host.Start();
        JsonObject before = await host.Command();
        using HttpResponseMessage response = await host.Client.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonObject snapshot = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        TransportTestSupport.ValidateSnapshot(snapshot, view, true, true);
        Headers(response);
        JsonObject after = await host.Command();
        Assert.AreEqual(1, (int?)after["captures"]);
        Assert.AreEqual(0, (int?)after["businessCalls"]);
        Assert.AreEqual((string?)before["revision"], (string?)after["revision"], "Reading must not mutate health.");
    }

    [TestMethod]
    [DataRow("unhealthy", "/_health", false, false)]
    [DataRow("unhealthy", "/_health/ready", false, false)]
    [DataRow("unhealthy", "/_health/details", false, false)]
    [DataRow("not-ready", "/_health/ready", true, false)]
    [DataRow("stopping", "/_health/ready", true, false)]
    public async Task SelectedHealthOrReadinessMapsTo500(string mode, string path, bool healthy, bool ready)
    {
        await using var host = await Host.Start(mode);
        using HttpResponseMessage response = await host.Client.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        TransportTestSupport.ValidateSnapshot(JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject(), path.EndsWith("details", StringComparison.Ordinal) ? "details" : "health", healthy, ready);
        Headers(response);
        Assert.AreEqual(0, (int?)(await host.Command())["businessCalls"]);
    }

    [TestMethod]
    [DataRow("normal", "/_health", 200)]
    [DataRow("normal", "/_health/ready", 200)]
    [DataRow("unhealthy", "/_health/details", 500)]
    [DataRow("not-ready", "/_health/ready", 500)]
    [DataRow("overflow", "/_health/details", 500)]
    [DataRow("source-throw", "/_health", 500)]
    [DataRow("source-cancel", "/_health", 500)]
    public async Task HeadCapturesOnceAndMakesCompleteStatusDecisionWithoutBody(string mode, string path, int status)
    {
        await using var host = await Host.Start(mode);
        using var request = new HttpRequestMessage(HttpMethod.Head, path);
        using HttpResponseMessage response = await host.Client.SendAsync(request);
        Assert.AreEqual(status, (int)response.StatusCode);
        Assert.AreEqual(0, (await response.Content.ReadAsByteArrayAsync()).Length);
        Headers(response);
        Assert.AreEqual(1, (int?)(await host.Command())["captures"]);
    }

    [TestMethod]
    [DataRow("/_health/unknown", "GET", 404, "NotFound")]
    [DataRow("/_health//", "GET", 404, "NotFound")]
    [DataRow("/_health/ready//", "POST", 404, "NotFound")]
    [DataRow("/_health/details/more", "DELETE", 404, "NotFound")]
    [DataRow("/_health", "POST", 405, "MethodNotAllowed")]
    [DataRow("/_health/ready", "OPTIONS", 405, "MethodNotAllowed")]
    [DataRow("/_health/details", "PATCH", 405, "MethodNotAllowed")]
    public async Task ReservedErrorsDoNotCaptureAuthorizeOrInvokeBusiness(string path, string method, int status, string code)
    {
        await using var host = await Host.Start();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Auth", "throw");
        using HttpResponseMessage response = await host.Client.SendAsync(request);
        Assert.AreEqual(status, (int)response.StatusCode);
        TransportTestSupport.ValidateError(JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject(), code);
        Headers(response);
        if (status == 405) { CollectionAssert.AreEquivalent(new[] { "GET", "HEAD" }, response.Content.Headers.Allow.ToArray()); }
        JsonObject state = await host.Command();
        Assert.AreEqual(0, (int?)state["captures"]);
        Assert.AreEqual(0, (int?)state["authorizationCalls"]);
        Assert.AreEqual(0, (int?)state["businessCalls"]);
    }

    [TestMethod]
    [DataRow("GET")]
    [DataRow("POST")]
    [DataRow("HEAD")]
    public async Task DisabledDetailsStays404AndCannotBeEnabledByQueryOrAuthorization(string method)
    {
        await using var host = await Host.Start("details-disabled");
        using var request = new HttpRequestMessage(new HttpMethod(method), "/_health/details?details=true&view=details");
        request.Headers.Add("X-Auth", "throw");
        using HttpResponseMessage response = await host.Client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        if (method == "HEAD") { Assert.AreEqual("", await response.Content.ReadAsStringAsync()); }
        else { TransportTestSupport.ValidateError(JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject(), "NotFound"); }
        JsonObject state = await host.Command();
        Assert.AreEqual(0, (int?)state["authorizationCalls"]);
        Assert.AreEqual(0, (int?)state["captures"]);
        Assert.AreEqual(0, (int?)state["businessCalls"]);
    }

    [TestMethod]
    [DataRow("deny", "GET", 403, "Forbidden")]
    [DataRow("throw", "GET", 500, "ReportingUnavailable")]
    [DataRow("deny", "HEAD", 403, "Forbidden")]
    [DataRow("throw", "HEAD", 500, "ReportingUnavailable")]
    public async Task DetailsAuthorizationIsIsolatedAndBounded(string authorization, string method, int status, string code)
    {
        await using var host = await Host.Start();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/_health/details");
        request.Headers.Add("X-Auth", authorization);
        using HttpResponseMessage response = await host.Client.SendAsync(request);
        Assert.AreEqual(status, (int)response.StatusCode);
        if (method == "HEAD") { Assert.AreEqual("", await response.Content.ReadAsStringAsync()); }
        else { TransportTestSupport.ValidateError(JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject(), code); }
        Headers(response);
        JsonObject state = await host.Command();
        Assert.AreEqual(1, (int?)state["authorizationCalls"]);
        Assert.AreEqual(0, (int?)state["captures"]);
        Assert.AreEqual(0, (int?)state["businessCalls"]);
    }

    [TestMethod]
    [DataRow("overflow", "/_health/details", "ReportTooLarge")]
    [DataRow("source-throw", "/_health", "ReportingUnavailable")]
    [DataRow("source-cancel", "/_health", "ReportingUnavailable")]
    public async Task ReportingFailuresReturnCompleteBoundedJsonWithoutMonitorFailures(string mode, string path, string code)
    {
        await using var host = await Host.Start(mode);
        using HttpResponseMessage response = await host.Client.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.IsTrue(bytes.Length <= 1024);
        TransportTestSupport.ValidateError(JsonNode.Parse(bytes)!.AsObject(), code);
        Headers(response);
    }

    [TestMethod]
    [DataRow("/_healthcare")]
    [DataRow("/_healthcare/ready")]
    [DataRow("/business")]
    public async Task SimilarPrefixContinuesNormalApplicationPipeline(string path)
    {
        await using var host = await Host.Start();
        Assert.AreEqual("business:" + path, await host.Client.GetStringAsync(path));
        JsonObject state = await host.Command();
        Assert.AreEqual(1, (int?)state["businessCalls"]);
        Assert.AreEqual(0, (int?)state["captures"]);
    }

    [TestMethod]
    public async Task StatusAndPayloadComeFromOneCapturedVersionEvenWhenSourceChangesAfterCapture()
    {
        await using var host = await Host.Start("alternating");
        for (int i = 0; i < 8; i++)
        {
            using HttpResponseMessage response = await host.Client.GetAsync("/_health");
            JsonObject snapshot = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
            bool healthy = (string?)snapshot["healthStatus"] == "Healthy";
            Assert.AreEqual(healthy ? HttpStatusCode.OK : HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.AreEqual((i & 1) == 0, healthy);
        }
        Assert.AreEqual(8, (int?)(await host.Command())["captures"]);
    }

    [TestMethod]
    public async Task ClientCancellationOfAuthorizationDoesNotAffectLaterHealthOrBusiness()
    {
        await using var host = await Host.Start();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_health/details"); request.Headers.Add("X-Auth", "block");
        using var cancellation = new CancellationTokenSource();
        Task<HttpResponseMessage> blocked = host.Client.SendAsync(request, cancellation.Token);
        await WaitForAuthorization(host);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => { using HttpResponseMessage unexpected = await blocked; });
        using HttpResponseMessage healthy = await host.Client.GetAsync("/_health");
        Assert.AreEqual(HttpStatusCode.OK, healthy.StatusCode);
        JsonObject state = await host.Command();
        Assert.AreEqual(1, (int?)state["captures"]); Assert.AreEqual(0, (int?)state["businessCalls"]);
    }

    [TestMethod]
    public async Task AuthorizationCancellationAbortsOnlyItsRequestAndLaterHealthStillWorks()
    {
        await using var host = await Host.Start();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_health/details"); request.Headers.Add("X-Auth", "cancel");
        try { using HttpResponseMessage unexpected = await host.Client.SendAsync(request); Assert.Fail("A canceled authorization must not produce a health response."); }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { }
        using HttpResponseMessage healthy = await host.Client.GetAsync("/_health"); Assert.AreEqual(HttpStatusCode.OK, healthy.StatusCode);
        JsonObject state = await host.Command(); Assert.AreEqual(1, (int?)state["captures"]); Assert.AreEqual(0, (int?)state["businessCalls"]);
    }

    [TestMethod]
    public async Task ExplicitStartupAndStoppingAffectReadinessWhileHealthRemainsHealthy()
    {
        await using var host = await Host.Start("not-ready");
        using (HttpResponseMessage health = await host.Client.GetAsync("/_health")) { Assert.AreEqual(HttpStatusCode.OK, health.StatusCode); }
        using (HttpResponseMessage readiness = await host.Client.GetAsync("/_health/ready")) { Assert.AreEqual(HttpStatusCode.InternalServerError, readiness.StatusCode); }
        await host.Command("startup");
        using (HttpResponseMessage readiness = await host.Client.GetAsync("/_health/ready")) { Assert.AreEqual(HttpStatusCode.OK, readiness.StatusCode); }
        await host.Command("begin-stopping");
        using (HttpResponseMessage readiness = await host.Client.GetAsync("/_health/ready")) { Assert.AreEqual(HttpStatusCode.InternalServerError, readiness.StatusCode); }
        using (HttpResponseMessage health = await host.Client.GetAsync("/_health")) { Assert.AreEqual(HttpStatusCode.OK, health.StatusCode); }
        Assert.AreEqual(0, (int?)(await host.Command())["businessCalls"]);
    }

    [TestMethod]
    [DataRow("health", "/_health")]
    [DataRow("readiness", "/_health/ready")]
    [DataRow("details", "/_health/details")]
    public async Task ActualHttpAndPipePublishTheExactSameDetachedSnapshot(string view, string path)
    {
        await using var host = await Host.Start("parity");
        Assert.IsNotNull(host.PipeName);
        using HttpResponseMessage response = await host.Client.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string httpJson = await response.Content.ReadAsStringAsync();
        JsonObject envelope = await TransportTestSupport.RequestAsync(host.PipeName, "{\"protocolVersion\":1,\"view\":\"" + view + "\"}");
        JsonObject pipeSnapshot = TransportTestSupport.ValidateSnapshotEnvelope(envelope, view, true, true);
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(httpJson), pipeSnapshot), "The same detached source must produce the same complete selected view on both real transports.");
        JsonObject state = await host.Command(); Assert.AreEqual(2, (int?)state["captures"]); Assert.AreEqual(0, (int?)state["businessCalls"]);
    }

    private static async Task WaitForAuthorization(Host host)
    {
        using var timeout = new CancellationTokenSource(TransportTestSupport.Limit);
        while ((int?)(await host.Command())["authorizationCalls"] == 0) { await Task.Delay(10, timeout.Token); }
    }

    private static void Headers(HttpResponseMessage response)
    {
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.IsTrue(response.Headers.CacheControl?.NoStore == true);
        Assert.IsTrue(response.Headers.CacheControl?.NoCache == true);
    }
}
