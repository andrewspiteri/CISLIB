using System.Diagnostics.Metrics;
using System.Net;
using CIS.Metrics.OpenTelemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Metrics;

[TestClass]
public sealed class HttpRequestTests
{
    [TestMethod]
    public async Task NativeTemplateAndFinalStatusIncludeBusinessErrorsAndAuthorizationFailure()
    {
        await using var host = HttpTestHost.Create();
        host.App.MapGet("/status/{code}", (int code) => Results.StatusCode(code));
        host.App.MapGet("/private/{id}", () => Results.Ok()).RequireAuthorization();
        await host.StartAsync();
        foreach (int code in new[] { 200, 400, 404, 429, 500 })
        {
            using HttpResponseMessage response = await host.Client.GetAsync($"/status/{code}");
            Assert.AreEqual(code, (int)response.StatusCode);
        }
        using HttpResponseMessage denied = await host.Client.GetAsync("/private/secret-id");
        Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
        string text = await host.ScrapeAsync();
        foreach (int code in new[] { 200, 400, 404, 429, 500 })
        { Assert.AreEqual(1d, HttpTestHost.Count(text, "/status/{code}", code), text); }
        Assert.AreEqual(1d, HttpTestHost.Count(text, "/private/{id}", 401), text);
        Assert.IsFalse(text.Contains("secret-id", StringComparison.Ordinal), text);
    }

    [TestMethod]
    [DataRow("/error", 500)]
    [DataRow("/_health/error", 404)]
    public async Task HandledReexecutionKeepsOneOriginalRouteEvenWhenHandlerUsesManagementPath(string handler, int status)
    {
        await using var host = HttpTestHost.Create(errorPath: handler);
        host.App.MapGet("/throws/{id}", (HttpContext _) => throw new InvalidOperationException("Controlled business failure."));
        await host.StartAsync();
        using HttpResponseMessage response = await host.Client.GetAsync("/throws/original-id");
        Assert.AreEqual(status, (int)response.StatusCode);
        string text = await host.ScrapeAsync();
        Assert.AreEqual(1d, HttpTestHost.Count(text, "/throws/{id}", status), text);
        Assert.AreEqual(1d, HttpTestHost.DurationCounts(text).Sum(CollectionText.Value), text);
        Assert.IsFalse(text.Contains("original-id", StringComparison.Ordinal), text);
        Assert.IsFalse(HttpTestHost.DurationCounts(text).Any(line => line.Contains("http_route=\"/error\"", StringComparison.Ordinal)), text);
    }

    [TestMethod]
    public async Task EmptyDeclaredRootRouteReexecutionKeepsNativeSlashInsteadOfUnknownOrHandlerRoute()
    {
        await using var host = HttpTestHost.Create();
        host.App.MapGet("", (HttpContext _) => throw new InvalidOperationException("Controlled root-route failure."));
        await host.StartAsync();
        using HttpResponseMessage response = await host.Client.GetAsync("/");
        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        string text = await host.ScrapeAsync();
        Assert.AreEqual(1d, HttpTestHost.Count(text, "/", 500), text);
        Assert.AreEqual(1d, HttpTestHost.DurationCounts(text).Sum(CollectionText.Value), text);
        Assert.IsFalse(HttpTestHost.DurationCounts(text).Any(line => line.Contains("http_route=\"/error\"", StringComparison.Ordinal)), text);
    }

    [TestMethod]
    [DataRow("/private-unmatched", false)]
    [DataRow("/_health/private-unmatched", true)]
    public async Task ErrorBeforePolicyUsesOriginalManagementDecisionAndOmitsUnknownRoute(string originalPath, bool management)
    {
        await using var host = HttpTestHost.Create(beforeMetrics: app =>
        {
            app.Use(async (HttpContext context, RequestDelegate next) =>
            {
                if (context.Request.Path == originalPath)
                { throw new InvalidOperationException("Controlled failure before the Metrics policy."); }
                await next(context);
            });
        });
        await host.StartAsync();
        using HttpResponseMessage response = await host.Client.GetAsync(originalPath);
        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        string text = await host.ScrapeAsync();
        string[] counts = HttpTestHost.DurationCounts(text);
        Assert.AreEqual(management ? 0d : 1d, counts.Sum(CollectionText.Value), text);
        Assert.IsFalse(counts.Any(line => line.Contains("http_route=", StringComparison.Ordinal)), text);
        Assert.IsFalse(text.Contains(originalPath, StringComparison.Ordinal), text);
        Assert.IsFalse(HttpTestHost.DurationCounts(text).Any(line => line.Contains("http_route=\"/error\"", StringComparison.Ordinal)), text);
    }

    [TestMethod]
    public async Task NativeFieldsRemainAuthoritativeAndChangingIdsAndUnmatchedPathsNeverBecomeLabels()
    {
        await using var host = HttpTestHost.Create();
        host.App.MapGet("/orders/{id}", (HttpContext context) =>
        {
            IHttpMetricsTagsFeature feature = context.Features.Get<IHttpMetricsTagsFeature>()!;
            feature.Tags.Add(new("http.route", context.Request.Path.Value));
            feature.Tags.Add(new("http.request.method", "raw-override"));
            feature.Tags.Add(new("http.response.status_code", 999));
            feature.Tags.Add(new("url.scheme", "raw-override"));
            feature.Tags.Add(new("safe.application.tag", "unselected"));
            return Results.Ok();
        });
        await host.StartAsync();
        foreach (string path in new[] { "/orders/private-one", "/orders/private-two", "/unmatched-private-one", "/unmatched-private-two" })
        { using HttpResponseMessage response = await host.Client.GetAsync(path); }
        string text = await host.ScrapeAsync();
        Assert.AreEqual(2d, HttpTestHost.Count(text, "/orders/{id}", 200), text);
        string actual = HttpTestHost.DurationCounts(text).Single(line => line.Contains("http_route=\"/orders/{id}\"", StringComparison.Ordinal));
        Assert.IsTrue(actual.Contains("http_request_method=\"GET\"", StringComparison.Ordinal), actual);
        Assert.AreEqual(4d, HttpTestHost.DurationCounts(text).Sum(CollectionText.Value), text);
        foreach (string forbidden in new[] { "private-one", "private-two", "raw-override", "http_response_status_code=\"999\"", "unselected" })
        { Assert.IsFalse(text.Contains(forbidden, StringComparison.Ordinal), text); }
    }

    [TestMethod]
    public async Task CanceledClientKeepsFrameworkOriginalRouteAndActualAbortedStatusWithoutInventingResponseSuccess()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unwound = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        await using var host = HttpTestHost.Create();
        host.Release.Add(cancellation.Cancel);
        host.App.MapGet("/cancel/{id}", async (HttpContext context) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
            finally { unwound.TrySetResult(); }
        });
        await host.StartAsync();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (ReferenceEquals(instrument.Meter, host.Context.HostingMeter) && instrument.Name == "http.server.request.duration")
            { current.EnableMeasurementEvents(instrument); }
        };
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            foreach (KeyValuePair<string, object?> tag in tags)
            { if (tag.Key == "http.route" && Equals(tag.Value, "/cancel/{id}")) { recorded.TrySetResult(); } }
        });
        listener.Start();
        Task<HttpResponseMessage> request = host.Client.GetAsync("/cancel/private-abort", cancellation.Token);
        await entered.Task.WaitAsync(HttpTestHost.Deadline);
        cancellation.Cancel();
        try { using HttpResponseMessage unexpected = await request.WaitAsync(HttpTestHost.Deadline); Assert.Fail("The controlled client must cancel."); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        await unwound.Task.WaitAsync(HttpTestHost.Deadline);
        await recorded.Task.WaitAsync(HttpTestHost.Deadline);
        string text = await host.ScrapeAsync();
        // The pinned exception handler sets 499 for an aborted request; this is native final status, not an inferred outcome.
        Assert.AreEqual(1d, HttpTestHost.Count(text, "/cancel/{id}", 499), text);
        Assert.AreEqual(1d, HttpTestHost.DurationCounts(text).Sum(CollectionText.Value), text);
        Assert.IsFalse(text.Contains("private-abort", StringComparison.Ordinal), text);
    }

    [TestMethod]
    public async Task MetricsAndHealthStayOutsideBusinessGateAndDurationWhileActiveRequestsRemainHostWide()
    {
        int business = 0;
        await using var host = HttpTestHost.Create(businessGate: true);
        host.App.MapGet("/business", () => { Interlocked.Increment(ref business); return Results.Ok(); });
        host.App.MapGet("/_healthcare", () => Results.Ok());
        await host.StartAsync();
        using (HttpResponseMessage ready = await host.Client.GetAsync("/_health/ready"))
        { Assert.AreEqual(HttpStatusCode.InternalServerError, ready.StatusCode); }
        using (HttpResponseMessage blocked = await host.Client.GetAsync("/business"))
        { Assert.AreEqual(HttpStatusCode.ServiceUnavailable, blocked.StatusCode); }
        Assert.AreEqual(0, business);
        string early = await host.ScrapeAsync();
        Assert.IsTrue(CollectionText.Samples(early).Any(line =>
            line.StartsWith("http_server_active_requests{", StringComparison.Ordinal) && CollectionText.Value(line) >= 1), early);
        host.Health.MarkStartupCompleted();
        foreach (string path in new[] { "/_health", "/_health/ready", "/_health/details", "/_health/unknown" })
        { using HttpResponseMessage management = await host.Client.GetAsync(path); }
        using (HttpResponseMessage live = await host.Client.GetAsync("/business")) { live.EnsureSuccessStatusCode(); }
        using (HttpResponseMessage similar = await host.Client.GetAsync("/_healthcare")) { similar.EnsureSuccessStatusCode(); }
        string text = await host.ScrapeAsync();
        Assert.AreEqual(1, business);
        Assert.AreEqual(1d, HttpTestHost.Count(text, "/business", 503), text);
        Assert.AreEqual(1d, HttpTestHost.Count(text, "/business", 200), text);
        Assert.AreEqual(1d, HttpTestHost.Count(text, "/_healthcare", 200), text);
        Assert.AreEqual(3d, HttpTestHost.DurationCounts(text).Sum(CollectionText.Value), text);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("/base")]
    public async Task ReservedBranchUsesExactPathBaseAndGetOnlyAndStopsAdmission(string pathBase)
    {
        await using var host = HttpTestHost.Create(pathBase: pathBase);
        await host.StartAsync();
        await host.ScrapeAsync();
        foreach (HttpMethod method in new[] { HttpMethod.Post, HttpMethod.Head })
        {
            using var request = new HttpRequestMessage(method, pathBase + "/_metrics");
            using HttpResponseMessage response = await host.Client.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
            Assert.IsTrue(response.Content.Headers.Allow.Contains("GET"));
        }
        foreach (string path in new[] { "/metrics", "/_metrics/child" })
        { using HttpResponseMessage missing = await host.Client.GetAsync(pathBase + path); Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode); }
        host.Context.BeginStopping();
        using HttpResponseMessage stopped = await host.Client.GetAsync(pathBase + "/_metrics");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, stopped.StatusCode);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FixedRouteDiagnosticsMustFitUtf8OnlyWhenNativeHttpCollectionIsSelected(bool collectHttp)
    {
        await using var host = HttpTestHost.Create(options => options.CollectIncomingHttp = collectHttp);
        host.App.MapGet("/long", () => Results.Ok()).WithMetadata(new Diagnostics(new string('\u00e9', 129)));
        if (collectHttp)
        { await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.App.StartAsync().WaitAsync(HttpTestHost.Deadline)); }
        else
        {
            await host.StartAsync();
            using HttpResponseMessage response = await host.Client.GetAsync("/long");
            response.EnsureSuccessStatusCode();
            Assert.IsFalse(CollectionText.Samples(await host.ScrapeAsync()).Any(line => line.StartsWith("http_", StringComparison.Ordinal)));
        }
    }

    [TestMethod]
    [DataRow("/_metrics", true)]
    [DataRow("_metrics", false)]
    public async Task ConflictingBusinessRouteFailsBeforeListenerEvenWithoutNativeHttp(string route, bool collectHttp)
    {
        await using var host = HttpTestHost.Create(options => options.CollectIncomingHttp = collectHttp);
        host.App.MapGet(route, () => Results.Ok());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.App.StartAsync().WaitAsync(HttpTestHost.Deadline));
    }

    [TestMethod]
    public async Task DisabledCompositionLeavesCanonicalPathUninstalledAndOrdinaryApplicationUsable()
    {
        await using var host = HttpTestHost.Create(options => options.Enabled = false);
        host.Metrics.RegisterObservableGauge(new("app.http.disabled"), () => throw new InvalidOperationException("Must not collect."));
        host.App.MapGet("/work", () => Results.Ok());
        await host.StartAsync();
        Assert.IsNull(host.App.Services.GetService<MetricsCollectionContext>());
        using HttpResponseMessage metrics = await host.Client.GetAsync("/_metrics");
        Assert.AreEqual(HttpStatusCode.NotFound, metrics.StatusCode);
        using HttpResponseMessage work = await host.Client.GetAsync("/work");
        work.EnsureSuccessStatusCode();
    }

    private sealed record Diagnostics(string Route) : IRouteDiagnosticsMetadata;
}
