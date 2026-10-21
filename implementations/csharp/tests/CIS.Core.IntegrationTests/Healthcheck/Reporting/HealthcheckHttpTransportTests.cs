using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using CIS.Healthcheck.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MutationChildren = CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildSupport;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

[TestClass]
public sealed class HealthcheckHttpTransportTests
{
    [TestMethod]
    [DataRow("app")]
    [DataRow("source")]
    [DataRow("options")]
    public void RegistrationRejectsNullRequiredArgumentsAtTheReceivingBoundary(string argument)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        IApplicationBuilder app = new ApplicationBuilder(services);
        var source = new ObservingSnapshotSource(() => throw new InvalidOperationException("Registration must not capture a snapshot."));
        var options = new HealthcheckOptions { DetailsEnabled = false };
        ArgumentNullException error = Assert.ThrowsExactly<ArgumentNullException>(() => HealthcheckApplicationBuilderExtensions.UseCisHealthcheck(
            argument == "app" ? null! : app, argument == "source" ? null! : source, argument == "options" ? null! : options));
        Assert.AreEqual(argument, error.ParamName);
        Assert.AreEqual(0, source.Captures);
    }

    [TestMethod]
    public void EnabledDetailsRequiresAuthorizationDuringRegistration()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        IApplicationBuilder app = new ApplicationBuilder(services);
        var source = new ObservingSnapshotSource(() => throw new InvalidOperationException("Rejected registration must not capture a snapshot."));
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => app.UseCisHealthcheck(source,
            new HealthcheckOptions { DetailsEnabled = true }));
        Assert.AreEqual("authorizeDetails", error.ParamName);
        Assert.AreEqual(0, source.Captures);
    }

    [TestMethod]
    public async Task DisabledDetailsAcceptsOmittedAuthorizationAndStillReservesItsRoute()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        IApplicationBuilder app = new ApplicationBuilder(services);
        var source = new ObservingSnapshotSource(() => throw new InvalidOperationException("Disabled details must not capture a snapshot."));
        Assert.AreSame(app, app.UseCisHealthcheck(source, new HealthcheckOptions { DetailsEnabled = false }));
        int businessCalls = 0;
        app.Run(_ => { businessCalls++; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Path = "/_health/details";
        context.Request.Method = HttpMethods.Get;
        using var body = new MemoryStream();
        context.Response.Body = body;
        await app.Build()(context).WaitAsync(TransportTestSupport.Limit);
        Assert.AreEqual(StatusCodes.Status404NotFound, context.Response.StatusCode);
        TransportTestSupport.ValidateError(JsonNode.Parse(body.ToArray())!.AsObject(), "NotFound");
        Assert.AreEqual(0, source.Captures);
        Assert.AreEqual(0, businessCalls);
    }

    [TestMethod]
    [DataRow("GET")]
    [DataRow("HEAD")]
    public async Task CancellationAfterCaptureAbortsBeforeChangingResponseMetadataOrWriting(string method)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var options = new HealthcheckOptions { DetailsEnabled = false };
        await using var service = new HealthcheckService(options,
            new HealthApplicationIdentity { Name = "canceled-http-write", InstanceId = Guid.Parse("f3d654a2-6113-4e92-8c7e-7ea747197131") });
        service.MarkStartupCompleted();
        var source = new ObservingSnapshotSource(() =>
        {
            HealthSnapshot snapshot = service.CaptureSnapshot();
            cancellation.Cancel();
            return snapshot;
        });
        IApplicationBuilder app = new ApplicationBuilder(services);
        app.UseCisHealthcheck(source, options);
        int businessCalls = 0;
        app.Run(_ => { businessCalls++; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        var lifetime = new ObservingRequestLifetime { RequestAborted = cancellation.Token };
        context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        context.Request.Path = "/_health";
        context.Request.Method = method;
        context.Response.StatusCode = StatusCodes.Status202Accepted;
        using var body = new MemoryStream();
        context.Response.Body = body;

        await app.Build()(context).WaitAsync(TransportTestSupport.Limit);

        Assert.AreEqual(1, source.Captures, "Cancellation must occur after the real snapshot read, beyond the entry cancellation guard.");
        Assert.IsTrue(cancellation.IsCancellationRequested);
        Assert.IsTrue(lifetime.Aborted, "The actual middleware must terminate the canceled request.");
        Assert.AreEqual(StatusCodes.Status202Accepted, context.Response.StatusCode);
        Assert.IsNull(context.Response.ContentType);
        Assert.IsNull(context.Response.ContentLength);
        Assert.IsFalse(context.Response.Headers.ContainsKey("Cache-Control"));
        Assert.IsFalse(context.Response.Headers.ContainsKey("Pragma"));
        Assert.IsFalse(context.Response.Headers.ContainsKey("Expires"));
        Assert.AreEqual(0L, body.Length);
        Assert.AreEqual(0, businessCalls);
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly Process child;
        private readonly Task<string> stderr;
        private readonly HttpClient client = new() { Timeout = TransportTestSupport.Limit };
        internal string? PipeName { get; private set; }
        private Host(string mode)
        {
            child = Process.Start(TransportTestSupport.StartInfo("healthcheck-http-host", "CIS.Healthcheck.HttpFixture", [mode]))!;
            MutationChildren.ObserveStarted(child);
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
                host.client.BaseAddress = new Uri((string)ready["address"]!);
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
        internal Task<HttpResponseMessage> GetAsync(string path) => ObserveHttpAsync(() => client.GetAsync(path));
        internal Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
            => ObserveHttpAsync(() => client.SendAsync(request, cancellationToken));
        internal async Task<string> GetStringAsync(string path)
        {
            using HttpResponseMessage response = await GetAsync(path);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        private async Task<HttpResponseMessage> ObserveHttpAsync(Func<Task<HttpResponseMessage>> operation)
        {
            try { return await operation(); }
            catch (OperationCanceledException error) when (error.InnerException is TimeoutException)
            { MutationChildren.MarkTimeout(child, "http-request"); throw; }
        }
        private async Task<JsonObject> Read()
        {
            string? line;
            try { line = await child.StandardOutput.ReadLineAsync().WaitAsync(TransportTestSupport.Limit); }
            catch (TimeoutException) { MutationChildren.MarkTimeout(child, "host-read"); throw; }
            Assert.IsNotNull(line, "HTTP host ended before its observation.");
            return JsonNode.Parse(line)!.AsObject();
        }
        public async ValueTask DisposeAsync()
        {
            client.Dispose();
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
            catch (TimeoutException) { MutationChildren.MarkTimeout(child, "host-stop"); throw; }
            finally
            {
                try
                {
                    if (!child.HasExited)
                    {
                        MutationChildren.MarkForcedStop(child, "cleanup");
                        child.Kill(entireProcessTree: true);
                        await child.WaitForExitAsync();
                    }
                }
                finally { try { MutationChildren.CompleteLifecycle(child); } finally { child.Dispose(); } }
            }
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
        using HttpResponseMessage response = await host.GetAsync(path);
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
        using HttpResponseMessage response = await host.GetAsync(path);
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
        using HttpResponseMessage response = await host.SendAsync(request);
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
        using HttpResponseMessage response = await host.SendAsync(request);
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
        using HttpResponseMessage response = await host.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Headers(response);
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
        using HttpResponseMessage response = await host.SendAsync(request);
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
        using HttpResponseMessage response = await host.GetAsync(path);
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
        Assert.AreEqual("business:" + path, await host.GetStringAsync(path));
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
            using HttpResponseMessage response = await host.GetAsync("/_health");
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
        Task<HttpResponseMessage> blocked = host.SendAsync(request, cancellation.Token);
        await WaitForAuthorization(host);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => { using HttpResponseMessage unexpected = await blocked; });
        using HttpResponseMessage healthy = await host.GetAsync("/_health");
        Assert.AreEqual(HttpStatusCode.OK, healthy.StatusCode);
        JsonObject state = await host.Command();
        Assert.AreEqual(1, (int?)state["captures"]); Assert.AreEqual(0, (int?)state["businessCalls"]);
    }

    [TestMethod]
    public async Task AuthorizationCancellationAbortsOnlyItsRequestAndLaterHealthStillWorks()
    {
        await using var host = await Host.Start();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_health/details"); request.Headers.Add("X-Auth", "cancel");
        try { using HttpResponseMessage unexpected = await host.SendAsync(request); Assert.Fail("A canceled authorization must not produce a health response."); }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { }
        using HttpResponseMessage healthy = await host.GetAsync("/_health"); Assert.AreEqual(HttpStatusCode.OK, healthy.StatusCode);
        JsonObject state = await host.Command(); Assert.AreEqual(1, (int?)state["captures"]); Assert.AreEqual(0, (int?)state["businessCalls"]);
    }

    [TestMethod]
    public async Task ExplicitStartupAndStoppingAffectReadinessWhileHealthRemainsHealthy()
    {
        await using var host = await Host.Start("not-ready");
        using (HttpResponseMessage health = await host.GetAsync("/_health")) { Assert.AreEqual(HttpStatusCode.OK, health.StatusCode); }
        using (HttpResponseMessage readiness = await host.GetAsync("/_health/ready")) { Assert.AreEqual(HttpStatusCode.InternalServerError, readiness.StatusCode); }
        await host.Command("startup");
        using (HttpResponseMessage readiness = await host.GetAsync("/_health/ready")) { Assert.AreEqual(HttpStatusCode.OK, readiness.StatusCode); }
        await host.Command("begin-stopping");
        using (HttpResponseMessage readiness = await host.GetAsync("/_health/ready")) { Assert.AreEqual(HttpStatusCode.InternalServerError, readiness.StatusCode); }
        using (HttpResponseMessage health = await host.GetAsync("/_health")) { Assert.AreEqual(HttpStatusCode.OK, health.StatusCode); }
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
        using HttpResponseMessage response = await host.GetAsync(path);
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
        CollectionAssert.AreEqual(new[] { "no-cache" }, response.Headers.Pragma.Select(value => value.ToString()).ToArray());
        Assert.IsTrue(response.Content.Headers.TryGetValues("Expires", out IEnumerable<string>? expires));
        CollectionAssert.AreEqual(new[] { "0" }, expires!.ToArray());
    }

    private sealed class ObservingSnapshotSource(Func<HealthSnapshot> capture) : IHealthSnapshotSource
    {
        internal int Captures { get; private set; }
        public HealthSnapshot CaptureSnapshot() { Captures++; return capture(); }
    }

    private sealed class ObservingRequestLifetime : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; }
        internal bool Aborted { get; private set; }
        public void Abort() => Aborted = true;
    }
}
