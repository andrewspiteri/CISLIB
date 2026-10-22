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
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task ValidDetailsAndAuthorizationCombinationsInstallBeforeTheReservedBranch(bool detailsEnabled, bool suppliesPredicate)
    {
        var options = new HealthcheckOptions { DetailsEnabled = detailsEnabled };
        HealthSnapshot snapshot = await CaptureSettledHttpSnapshotAsync(options);
        using var services = new ServiceCollection().BuildServiceProvider();
        IApplicationBuilder app = new ApplicationBuilder(services);
        var source = new ObservingSnapshotSource(() => snapshot);
        int authorizationCalls = 0, businessCalls = 0;
        Func<HttpContext, ValueTask<bool>>? predicate = suppliesPredicate
            ? _ => { authorizationCalls++; return ValueTask.FromResult(true); }
        : null;
        IApplicationBuilder? installed = null;
        try { installed = app.UseCisHealthcheck(source, options, predicate); }
        catch (ArgumentException error)
        {
            Assert.Fail($"A valid details/predicate combination must install successfully; actual exception type {error.GetType().Name}, parameter {error.ParamName}.");
        }
        Assert.AreSame(app, installed);
        Assert.AreEqual(0, source.Captures, "Installation must not capture application state.");
        Assert.AreEqual(0, authorizationCalls, "Installation must not run application authorization.");
        app.Run(_ => { businessCalls++; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Path = "/_health/details";
        context.Request.Method = HttpMethods.Get;
        using var body = new MemoryStream();
        context.Response.Body = body;
        await app.Build()(context).WaitAsync(TransportTestSupport.Limit);
        Assert.AreEqual(detailsEnabled ? StatusCodes.Status200OK : StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.AreEqual(detailsEnabled ? 1 : 0, source.Captures);
        Assert.AreEqual(detailsEnabled ? 1 : 0, authorizationCalls);
        Assert.AreEqual(0, businessCalls);
        JsonObject response = JsonNode.Parse(body.ToArray())!.AsObject();
        if (detailsEnabled) { TransportTestSupport.ValidateSnapshot(response, "details", true, true, active: 0); }
        else { TransportTestSupport.ValidateError(response, "NotFound"); }
    }

    [TestMethod]
    [DataRow("GET", "/_health", "health")]
    [DataRow("HEAD", "/_health", "health")]
    [DataRow("GET", "/_health/ready", "readiness")]
    [DataRow("HEAD", "/_health/ready", "readiness")]
    [DataRow("GET", "/_health/details", "details")]
    [DataRow("HEAD", "/_health/details", "details")]
    public async Task CancellationOnEntryAbortsBeforeSourceAuthorizationOrResponseChanges(string method, string path, string view)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var options = new HealthcheckOptions { DetailsEnabled = true };
        await using var service = new HealthcheckService(options,
            new HealthApplicationIdentity { Name = "canceled-http-entry", InstanceId = Guid.Parse("4a81a808-dcb2-4220-9d33-97792c89152c") });
        service.MarkStartupCompleted();
        HealthSnapshot before = service.CaptureSnapshot();
        var source = new ObservingSnapshotSource(service.CaptureSnapshot);
        int authorizationCalls = 0, businessCalls = 0;
        HttpContext? authorizedContext = null;
        IApplicationBuilder app = new ApplicationBuilder(services);
        app.UseCisHealthcheck(source, options, context =>
        {
            authorizationCalls++;
            authorizedContext = context;
            return ValueTask.FromResult(true);
        });
        app.Run(_ => { businessCalls++; return Task.CompletedTask; });
        RequestDelegate middleware = app.Build();
        var canceled = new DefaultHttpContext();
        var canceledLifetime = new ObservingRequestLifetime { RequestAborted = cancellation.Token };
        canceled.Features.Set<IHttpRequestLifetimeFeature>(canceledLifetime);
        canceled.Request.Method = method;
        canceled.Request.Path = path;
        canceled.Response.StatusCode = StatusCodes.Status202Accepted;
        canceled.Response.ContentType = "text/plain; charset=utf-8";
        canceled.Response.ContentLength = 7;
        canceled.Response.Headers.CacheControl = "private";
        canceled.Response.Headers.Pragma = "prior-pragma";
        canceled.Response.Headers.Expires = "prior-expires";
        canceled.Response.Headers.Allow = "POST";
        canceled.Response.Headers["X-Existing"] = "preserved";
        Dictionary<string, string> originalHeaders = canceled.Response.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
        using var canceledBody = new MemoryStream();
        canceled.Response.Body = canceledBody;
        cancellation.Cancel();

        await middleware(canceled).WaitAsync(TransportTestSupport.Limit);

        Assert.IsTrue(cancellation.IsCancellationRequested);
        Assert.IsTrue(canceledLifetime.Aborted);
        Assert.AreEqual(0, source.Captures, "An already canceled request must not call the real snapshot source.");
        Assert.AreEqual(0, authorizationCalls, "An already canceled details request must not invoke application authorization.");
        Assert.IsNull(authorizedContext);
        Assert.AreEqual(0, businessCalls);
        Assert.AreEqual(StatusCodes.Status202Accepted, canceled.Response.StatusCode);
        Assert.AreEqual("text/plain; charset=utf-8", canceled.Response.ContentType);
        Assert.AreEqual(7L, canceled.Response.ContentLength);
        CollectionAssert.AreEquivalent(originalHeaders.Keys.ToArray(), canceled.Response.Headers.Keys.ToArray());
        foreach ((string name, string value) in originalHeaders) { Assert.AreEqual(value, canceled.Response.Headers[name].ToString()); }
        Assert.IsFalse(canceled.Response.HasStarted);
        Assert.AreEqual(0L, canceledBody.Length);

        // The same installed middleware must still perform its real callbacks and
        // GET/HEAD response behavior for an uncanceled request to the same route.
        var positive = new DefaultHttpContext();
        var positiveLifetime = new ObservingRequestLifetime();
        positive.Features.Set<IHttpRequestLifetimeFeature>(positiveLifetime);
        positive.Request.Method = method;
        positive.Request.Path = path;
        using var positiveBody = new MemoryStream();
        positive.Response.Body = positiveBody;

        await middleware(positive).WaitAsync(TransportTestSupport.Limit);

        Assert.IsFalse(positiveLifetime.Aborted);
        Assert.AreEqual(1, source.Captures);
        Assert.AreEqual(view == "details" ? 1 : 0, authorizationCalls);
        if (view == "details") { Assert.AreSame(positive, authorizedContext); }
        else { Assert.IsNull(authorizedContext); }
        Assert.AreEqual(0, businessCalls);
        Assert.AreEqual(StatusCodes.Status200OK, positive.Response.StatusCode);
        Assert.AreEqual("application/json; charset=utf-8", positive.Response.ContentType);
        Assert.IsTrue(positive.Response.ContentLength is > 0);
        Assert.AreEqual("no-store, no-cache", positive.Response.Headers.CacheControl.ToString());
        Assert.AreEqual("no-cache", positive.Response.Headers.Pragma.ToString());
        Assert.AreEqual("0", positive.Response.Headers.Expires.ToString());
        if (method == "HEAD") { Assert.AreEqual(0L, positiveBody.Length); }
        else
        {
            Assert.AreEqual(positive.Response.ContentLength, positiveBody.Length);
            TransportTestSupport.ValidateSnapshot(JsonNode.Parse(positiveBody.ToArray())!.AsObject(), view, true, true, active: 0);
        }
        Assert.AreEqual(before.Revision, service.CaptureSnapshot().Revision);
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

    [TestMethod]
    [DataRow("GET")]
    [DataRow("HEAD")]
    public async Task CanceledAuthorizationReturnsBeforeAnyResponseOrSnapshotWork(string method)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var source = new ObservingSnapshotSource(() => throw new InvalidOperationException("Canceled authorization must not read health."));
        var context = new DefaultHttpContext();
        var lifetime = new ObservingRequestLifetime { RequestAborted = cancellation.Token };
        context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        context.Request.Path = "/_health/details";
        context.Request.Method = method;
        Dictionary<string, string> originalHeaders = PreserveResponseBeforeCancellation(context);
        using var body = new MemoryStream();
        context.Response.Body = body;
        int authorizationCalls = 0, businessCalls = 0;
        IApplicationBuilder app = new ApplicationBuilder(services);
        app.UseCisHealthcheck(source, new HealthcheckOptions { DetailsEnabled = true }, received =>
        {
            Assert.AreSame(context, received);
            authorizationCalls++;
            return ValueTask.FromException<bool>(new OperationCanceledException("secret authorization cancellation"));
        });
        app.Run(_ => { businessCalls++; return Task.CompletedTask; });

        await app.Build()(context).WaitAsync(TransportTestSupport.Limit);

        Assert.IsFalse(cancellation.IsCancellationRequested, "A predicate cancellation terminates its request even when RequestAborted has not been signaled.");
        Assert.IsTrue(lifetime.Aborted);
        Assert.AreEqual(1, authorizationCalls);
        Assert.AreEqual(0, source.Captures);
        Assert.AreEqual(0, businessCalls);
        AssertCanceledResponseWasUntouched(context, body, originalHeaders);
    }

    [TestMethod]
    [DataRow("GET")]
    [DataRow("HEAD")]
    public async Task SuccessfulAuthorizationRechecksRequestCancellationBeforeSnapshotCapture(string method)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var options = new HealthcheckOptions { DetailsEnabled = true };
        await using var service = new HealthcheckService(options,
            new HealthApplicationIdentity { Name = "canceled-http-authorization", InstanceId = Guid.Parse("dd143719-cbc6-40f2-a404-61af20318e08") });
        service.MarkStartupCompleted();
        HealthSnapshot before = service.CaptureSnapshot();
        var source = new ObservingSnapshotSource(service.CaptureSnapshot);
        var context = new DefaultHttpContext();
        var lifetime = new ObservingRequestLifetime { RequestAborted = cancellation.Token };
        context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        context.Request.Path = "/_health/details";
        context.Request.Method = method;
        Dictionary<string, string> originalHeaders = PreserveResponseBeforeCancellation(context);
        using var body = new MemoryStream();
        context.Response.Body = body;
        var authorization = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int authorizationCalls = 0, businessCalls = 0;
        IApplicationBuilder app = new ApplicationBuilder(services);
        app.UseCisHealthcheck(source, options, received =>
        {
            Assert.AreSame(context, received);
            authorizationCalls++;
            return new ValueTask<bool>(authorization.Task);
        });
        app.Run(_ => { businessCalls++; return Task.CompletedTask; });
        Task request = app.Build()(context);
        try
        {
            Assert.AreEqual(1, authorizationCalls);
            Assert.IsFalse(request.IsCompleted);
            Assert.AreEqual(0, source.Captures);
            cancellation.Cancel();
            authorization.TrySetResult(true);
            await request.WaitAsync(TransportTestSupport.Limit);

            Assert.IsTrue(lifetime.Aborted);
            Assert.AreEqual(1, authorizationCalls);
            Assert.AreEqual(0, source.Captures, "A successful authorization must not permit capture after this request was canceled.");
            Assert.AreEqual(0, businessCalls);
            AssertCanceledResponseWasUntouched(context, body, originalHeaders);
            Assert.AreEqual(before.Revision, service.CaptureSnapshot().Revision);
        }
        finally { authorization.TrySetResult(true); }
    }

    [TestMethod]
    [DataRow("GET")]
    [DataRow("HEAD")]
    public async Task CanceledSnapshotCaptureTerminatesOnlyItsRequestWithoutPublishingAnError(string method)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var options = new HealthcheckOptions { DetailsEnabled = false };
        HealthSnapshot snapshot = await CaptureSettledHttpSnapshotAsync(options);
        int reads = 0, businessCalls = 0;
        var source = new ObservingSnapshotSource(() =>
        {
            if (++reads == 1)
            {
                cancellation.Cancel();
                throw new OperationCanceledException("secret snapshot cancellation", cancellation.Token);
            }
            return snapshot;
        });
        IApplicationBuilder app = new ApplicationBuilder(services);
        app.UseCisHealthcheck(source, options);
        app.Run(_ => { businessCalls++; return Task.CompletedTask; });
        RequestDelegate middleware = app.Build();
        var context = new DefaultHttpContext();
        var lifetime = new ObservingRequestLifetime { RequestAborted = cancellation.Token };
        context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        context.Request.Path = "/_health";
        context.Request.Method = method;
        Dictionary<string, string> originalHeaders = PreserveResponseBeforeCancellation(context);
        using var body = new MemoryStream();
        context.Response.Body = body;

        try { await middleware(context).WaitAsync(TransportTestSupport.Limit); }
        catch (NullReferenceException error)
        {
            Assert.Fail($"Matching request cancellation during capture must complete without escaping {error.GetType().Name}.");
        }

        Assert.IsTrue(cancellation.IsCancellationRequested);
        Assert.IsTrue(lifetime.Aborted);
        Assert.AreEqual(1, source.Captures);
        AssertCanceledResponseWasUntouched(context, body, originalHeaders);

        var positive = new DefaultHttpContext();
        var positiveLifetime = new ObservingRequestLifetime();
        positive.Features.Set<IHttpRequestLifetimeFeature>(positiveLifetime);
        positive.Request.Path = "/_health";
        positive.Request.Method = HttpMethods.Get;
        using var positiveBody = new MemoryStream();
        positive.Response.Body = positiveBody;
        await middleware(positive).WaitAsync(TransportTestSupport.Limit);

        Assert.IsFalse(positiveLifetime.Aborted);
        Assert.AreEqual(StatusCodes.Status200OK, positive.Response.StatusCode);
        Assert.AreEqual(positiveBody.Length, positive.Response.ContentLength);
        JsonObject positiveSnapshot = JsonNode.Parse(positiveBody.ToArray())!.AsObject();
        TransportTestSupport.ValidateSnapshot(positiveSnapshot, "health", true, true, active: 0);
        Assert.AreEqual(snapshot.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), (string?)positiveSnapshot["revision"]);
        Assert.AreEqual(2, source.Captures);
        Assert.AreEqual(0, businessCalls);
    }

    [TestMethod]
    [DataRow("authorization")]
    [DataRow("source")]
    [DataRow("unexpected-source-cancellation")]
    public async Task ReportingFailuresPublishHttpErrorsAndHeadUsesTheSameCompleteRepresentation(string failureOrigin)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        int authorizationCalls = 0, businessCalls = 0;
        var source = new ObservingSnapshotSource(() =>
        {
            if (failureOrigin == "unexpected-source-cancellation") { throw new OperationCanceledException("secret unexpected source cancellation"); }
            throw new IOException("secret snapshot failure");
        });
        IApplicationBuilder app = new ApplicationBuilder(services);
        app.UseCisHealthcheck(source, new HealthcheckOptions { DetailsEnabled = true }, _ =>
        {
            authorizationCalls++;
            return ValueTask.FromException<bool>(new InvalidOperationException("secret authorization failure"));
        });
        app.Run(_ => { businessCalls++; return Task.CompletedTask; });
        RequestDelegate middleware = app.Build();
        long getLength = 0;
        foreach (string method in new[] { HttpMethods.Get, HttpMethods.Head })
        {
            var context = new DefaultHttpContext();
            var lifetime = new ObservingRequestLifetime();
            context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
            context.Request.Path = failureOrigin == "authorization" ? "/_health/details" : "/_health";
            context.Request.Method = method;
            using var body = new MemoryStream();
            context.Response.Body = body;

            await middleware(context).WaitAsync(TransportTestSupport.Limit);

            Assert.IsFalse(lifetime.Aborted, "An uncanceled reporting failure produces one complete HTTP error.");
            Assert.AreEqual(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
            Assert.AreEqual("application/json; charset=utf-8", context.Response.ContentType);
            Assert.AreEqual("no-store, no-cache", context.Response.Headers.CacheControl.ToString());
            Assert.AreEqual("no-cache", context.Response.Headers.Pragma.ToString());
            Assert.AreEqual("0", context.Response.Headers.Expires.ToString());
            if (method == HttpMethods.Get)
            {
                getLength = body.Length;
                Assert.IsTrue(getLength is > 0 and <= 1024);
                JsonObject error = JsonNode.Parse(body.ToArray())!.AsObject();
                TransportTestSupport.ValidateError(error, "ReportingUnavailable");
                Assert.AreEqual(1, (int?)error["schemaVersion"]);
                Assert.IsFalse(error.ContainsKey("protocolVersion"), "HTTP errors do not use the local-pipe protocol envelope.");
                Assert.IsFalse(error.ContainsKey("view"), "The nullable pipe request view does not belong to the HTTP error document.");
            }
            else { Assert.AreEqual(0L, body.Length, "HEAD must suppress the actual middleware body write."); }
            Assert.AreEqual(getLength, context.Response.ContentLength, "HEAD retains the complete GET representation's length.");
        }
        Assert.AreEqual(failureOrigin == "authorization" ? 2 : 0, authorizationCalls);
        Assert.AreEqual(failureOrigin == "authorization" ? 0 : 2, source.Captures);
        Assert.AreEqual(0, businessCalls);
    }

    [TestMethod]
    public async Task ForbiddenResponseStreamFailureRemainsOutsideTheAuthorizationExceptionBoundary()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var source = new ObservingSnapshotSource(() => throw new InvalidOperationException("Denied authorization must not capture health."));
        IApplicationBuilder app = new ApplicationBuilder(services);
        int authorizationCalls = 0, businessCalls = 0;
        app.UseCisHealthcheck(source, new HealthcheckOptions { DetailsEnabled = true }, _ =>
        {
            authorizationCalls++;
            return ValueTask.FromResult(false);
        });
        app.Run(_ => { businessCalls++; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        var lifetime = new ObservingRequestLifetime();
        context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        context.Request.Path = "/_health/details";
        context.Request.Method = HttpMethods.Get;
        using var body = new GatedBodyStream();
        context.Response.Body = body;
        Task request = app.Build()(context);
        try
        {
            await body.Entered.WaitAsync(TransportTestSupport.Limit);
            Assert.IsFalse(request.IsCompleted);
            Assert.AreEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
            TransportTestSupport.ValidateError(JsonNode.Parse(body.RequestedBytes)!.AsObject(), "Forbidden");
            var failure = new IOException("secret denied-response stream failure");
            body.Fail(failure);
            IOException observed = await Assert.ThrowsExactlyAsync<IOException>(() => request.WaitAsync(TransportTestSupport.Limit));

            Assert.AreSame(failure, observed);
            Assert.AreEqual(1, body.Writes, "A response-stream fault must not trigger a second ReportingUnavailable response.");
            Assert.AreEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
            Assert.AreEqual(1, authorizationCalls);
            Assert.AreEqual(0, source.Captures);
            Assert.AreEqual(0, businessCalls);
            Assert.IsFalse(lifetime.Aborted);
        }
        finally { body.Complete(); }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("/")]
    [DataRow("/_health-other")]
    [DataRow("/_healthcare")]
    [DataRow("/business")]
    public async Task UnreservedPathsPreserveTheBusinessContextAndItsPendingCompletion(string? path)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var source = new ObservingSnapshotSource(() => throw new InvalidOperationException("Business requests must not capture health."));
        IApplicationBuilder app = new ApplicationBuilder(services);
        app.UseCisHealthcheck(source, new HealthcheckOptions { DetailsEnabled = false });
        var context = new DefaultHttpContext();
        context.Request.Path = new PathString(path);
        context.Request.Method = HttpMethods.Post;
        string? originalPath = context.Request.Path.Value;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int businessCalls = 0;
        app.Run(received =>
        {
            Assert.AreSame(context, received);
            Assert.AreEqual(originalPath, received.Request.Path.Value);
            businessCalls++;
            received.Response.StatusCode = StatusCodes.Status202Accepted;
            received.Response.ContentType = "text/plain";
            received.Response.Headers["X-Business"] = "preserved";
            return completed.Task;
        });
        Task request = app.Build()(context);
        try
        {
            Assert.AreEqual(1, businessCalls);
            Assert.AreEqual(0, source.Captures);
            Assert.IsFalse(request.IsCompleted, "The health branch must preserve the actual downstream completion.");
            Assert.AreEqual(StatusCodes.Status202Accepted, context.Response.StatusCode);
            Assert.AreEqual("text/plain", context.Response.ContentType);
            Assert.AreEqual("preserved", context.Response.Headers["X-Business"].ToString());
            Assert.IsFalse(context.Response.Headers.ContainsKey("Cache-Control"));
            Assert.IsFalse(context.Response.Headers.ContainsKey("Pragma"));
            Assert.IsFalse(context.Response.Headers.ContainsKey("Expires"));
        }
        finally { completed.TrySetResult(); }
        await request.WaitAsync(TransportTestSupport.Limit);
        Assert.AreEqual(1, businessCalls);
        Assert.AreEqual(0, source.Captures);
    }

    [TestMethod]
    [DataRow("complete")]
    [DataRow("fault")]
    [DataRow("cancel")]
    public async Task ActualBodyWriteCompletionFaultAndCancellationGovernTheHealthRequest(string outcome)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var options = new HealthcheckOptions { DetailsEnabled = false };
        await using var service = new HealthcheckService(options,
            new HealthApplicationIdentity { Name = "http-body-completion", InstanceId = Guid.Parse("2079eb46-0f80-4d18-8d0d-c7b80bba1e42") });
        service.MarkStartupCompleted();
        HealthSnapshot before = service.CaptureSnapshot();
        var source = new ObservingSnapshotSource(service.CaptureSnapshot);
        IApplicationBuilder app = new ApplicationBuilder(services);
        app.UseCisHealthcheck(source, options);
        int businessCalls = 0;
        app.Run(_ => { businessCalls++; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        var lifetime = new ObservingRequestLifetime { RequestAborted = cancellation.Token };
        context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        context.Request.Path = "/_HeAlTh/";
        context.Request.Method = HttpMethods.Get;
        using var body = new GatedBodyStream();
        context.Response.Body = body;
        Task request = app.Build()(context);
        try
        {
            await body.Entered.WaitAsync(TransportTestSupport.Limit);
            Assert.IsFalse(request.IsCompleted, "The actual body write remains outstanding.");
            Assert.AreEqual(1, body.Writes);
            Assert.AreEqual(cancellation.Token, body.Cancellation);
            Assert.AreEqual(1, source.Captures);
            Assert.AreEqual(0, businessCalls);
            Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.AreEqual("application/json; charset=utf-8", context.Response.ContentType);
            Assert.AreEqual((long)body.RequestedBytes.Length, context.Response.ContentLength);
            TransportTestSupport.ValidateSnapshot(JsonNode.Parse(body.RequestedBytes)!.AsObject(), "health", true, true, active: 0);

            if (outcome == "fault")
            {
                var failure = new IOException("Controlled response-stream failure.");
                body.Fail(failure);
                IOException observed = await Assert.ThrowsExactlyAsync<IOException>(() => request.WaitAsync(TransportTestSupport.Limit));
                Assert.AreSame(failure, observed);
                Assert.IsFalse(lifetime.Aborted);
            }
            else if (outcome == "cancel")
            {
                cancellation.Cancel();
                body.Cancel(cancellation.Token);
                await request.WaitAsync(TransportTestSupport.Limit);
                Assert.IsTrue(lifetime.Aborted);
            }
            else
            {
                body.Complete();
                await request.WaitAsync(TransportTestSupport.Limit);
                Assert.IsFalse(lifetime.Aborted);
            }
            Assert.AreEqual(1, source.Captures);
            Assert.AreEqual(0, businessCalls);
            Assert.AreEqual(before.Revision, service.CaptureSnapshot().Revision);
        }
        finally { body.Complete(); }
    }

    [TestMethod]
    public async Task HeadMiddlewareFormatsItsSnapshotAndNeverWritesToTheActualResponseBody()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var options = new HealthcheckOptions { DetailsEnabled = false };
        await using var service = new HealthcheckService(options,
            new HealthApplicationIdentity { Name = "http-head-body", InstanceId = Guid.Parse("44772f4c-e069-4c91-8d4c-9202e96a3d65") });
        service.MarkStartupCompleted();
        HealthSnapshot before = service.CaptureSnapshot();
        var source = new ObservingSnapshotSource(service.CaptureSnapshot);
        IApplicationBuilder app = new ApplicationBuilder(services);
        app.UseCisHealthcheck(source, options);
        int businessCalls = 0;
        app.Run(_ => { businessCalls++; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Path = "/_health";
        context.Request.Method = HttpMethods.Head;
        using var body = new MemoryStream();
        context.Response.Body = body;

        await app.Build()(context).WaitAsync(TransportTestSupport.Limit);

        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.AreEqual("application/json; charset=utf-8", context.Response.ContentType);
        Assert.IsTrue(context.Response.ContentLength is > 0, "HEAD still makes the complete encoded report length decision.");
        Assert.AreEqual("no-store, no-cache", context.Response.Headers.CacheControl.ToString());
        Assert.AreEqual("no-cache", context.Response.Headers.Pragma.ToString());
        Assert.AreEqual("0", context.Response.Headers.Expires.ToString());
        Assert.AreEqual(0L, body.Length, "The middleware itself must suppress HEAD writes before the server's wire behavior.");
        Assert.AreEqual(1, source.Captures);
        Assert.AreEqual(0, businessCalls);
        Assert.AreEqual(before.Revision, service.CaptureSnapshot().Revision);
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
    [DataRow("/_health-other")]
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

    private static async Task<HealthSnapshot> CaptureSettledHttpSnapshotAsync(HealthcheckOptions options)
    {
        var service = new HealthcheckService(options,
            new HealthApplicationIdentity { Name = "detached-http-receiving", InstanceId = Guid.Parse("b007fa8e-4587-4b15-afda-a36972815f0a") });
        try
        {
            service.MarkStartupCompleted();
            return service.CaptureSnapshot();
        }
        finally { await service.DisposeAsync().AsTask().WaitAsync(TransportTestSupport.Limit); }
    }

    private static Dictionary<string, string> PreserveResponseBeforeCancellation(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status202Accepted;
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.ContentLength = 7;
        context.Response.Headers.CacheControl = "private";
        context.Response.Headers.Pragma = "prior-pragma";
        context.Response.Headers.Expires = "prior-expires";
        context.Response.Headers.Allow = "POST";
        context.Response.Headers["X-Existing"] = "preserved";
        return context.Response.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
    }

    private static void AssertCanceledResponseWasUntouched(HttpContext context, MemoryStream body,
        IReadOnlyDictionary<string, string> originalHeaders)
    {
        Assert.AreEqual(StatusCodes.Status202Accepted, context.Response.StatusCode);
        Assert.AreEqual("text/plain; charset=utf-8", context.Response.ContentType);
        Assert.AreEqual(7L, context.Response.ContentLength);
        CollectionAssert.AreEquivalent(originalHeaders.Keys.ToArray(), context.Response.Headers.Keys.ToArray());
        foreach ((string name, string value) in originalHeaders) { Assert.AreEqual(value, context.Response.Headers[name].ToString()); }
        Assert.IsFalse(context.Response.HasStarted);
        Assert.AreEqual(0L, body.Length);
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

    private sealed class GatedBodyStream : MemoryStream
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Entered => entered.Task;
        internal int Writes { get; private set; }
        internal byte[] RequestedBytes { get; private set; } = [];
        internal CancellationToken Cancellation { get; private set; }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Writes++;
            RequestedBytes = buffer.ToArray();
            Cancellation = cancellationToken;
            entered.TrySetResult();
            return new ValueTask(completion.Task);
        }
        internal void Complete() => completion.TrySetResult();
        internal void Fail(Exception error) => completion.TrySetException(error);
        internal void Cancel(CancellationToken cancellationToken) => completion.TrySetCanceled(cancellationToken);
    }

    private sealed class ObservingRequestLifetime : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; }
        internal bool Aborted { get; private set; }
        public void Abort() => Aborted = true;
    }
}
