using System.Diagnostics;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.Tests.UnitTests.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
[DoNotParallelize]
public sealed class RestTraceForwardingTests
{
    private const string Parent = "00-11111111111111111111111111111111-2222222222222222-01";
    private const string Manual = "00-33333333333333333333333333333333-4444444444444444-00";

    [TestMethod]
    [DataRow("https://service.local/orders", true)]
    [DataRow("https://SERVICE.LOCAL/orders", true)]
    [DataRow("https://service.local./orders", true)]
    [DataRow("https://local/orders", false)]
    [DataRow("https://localhost/orders", false)]
    [DataRow("http://127.0.0.1/orders", false)]
    [DataRow("https://evil.local.example/orders", false)]
    [DataRow("https://example.test/a.local?redirect=service.local", false)]
    public async Task AutomaticSelectionUsesOnlyTheFinalDnsHost(string destination, bool expected)
    {
        RestClientSnapshot client = RestClientSnapshot.Capture(new("orders") { BaseUrl = destination });
        using HttpRequestMessage native = await NativeAsync(client, RestCallSnapshot.Capture(client, null), Parent);
        AssertHeader(native, expected ? Parent : null);
    }

    [TestMethod]
    [DataRow(null, null, true)]
    [DataRow(false, null, false)]
    [DataRow(true, null, true)]
    [DataRow(true, false, false)]
    [DataRow(false, true, true)]
    public async Task CallOverrideAndImmutableSnapshotsPreserveTheirCapturedNullableChoice(bool? clientChoice, bool? callChoice, bool expected)
    {
        var options = new RestClientOptions("orders") { BaseUrl = "https://service.local/orders", ForwardTraceId = clientChoice };
        RestClientSnapshot client = RestClientSnapshot.Capture(options);
        var input = new RestRequestOptions { ForwardTraceId = callChoice };
        RestCallSnapshot call = RestCallSnapshot.Capture(client, input);
        options.ForwardTraceId = !clientChoice.GetValueOrDefault(); input.ForwardTraceId = !callChoice.GetValueOrDefault();
        using HttpRequestMessage native = await NativeAsync(client, call, Parent);
        AssertHeader(native, expected ? Parent : null);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task CustomizationAndContentAcquisitionFinishBeforeSelectionAndAuthenticationObservesTheSelectedHeader(bool acquisition, bool local)
    {
        string destination = local ? "https://final.local/orders" : "https://ordinary.test/orders";
        int authentications = 0;
        var options = new RestClientOptions("orders")
        {
            BaseUrl = local ? "https://initial.test/orders" : "https://initial.local/orders",
            Authenticate = (authentication, _) =>
            {
                authentications++;
                Assert.AreEqual(local, authentication.Headers.Snapshot().ContainsKey("traceparent"));
                if (local) { Assert.AreEqual(Parent, authentication.Headers.Snapshot()["traceparent"].Single()); }
                Assert.AreEqual(destination, authentication.Url);
                return Task.CompletedTask;
            }
        };
        options.AllowedOrigins.Add(new Uri(options.BaseUrl!).GetLeftPart(UriPartial.Authority));
        options.AllowedOrigins.Add(new Uri(destination).GetLeftPart(UriPartial.Authority));
        if (!acquisition) { options.ConfigureRequest = (request, _) => { request.Url = destination; return Task.CompletedTask; }; }
        RestClientSnapshot client = RestClientSnapshot.Capture(options);
        using HttpRequestMessage native = await NativeAsync(client, RestCallSnapshot.Capture(client, null), Parent,
            acquisition ? request => request.Url = destination : null);
        Assert.AreEqual(1, authentications); Assert.AreEqual(destination, native.RequestUri!.AbsoluteUri);
        AssertHeader(native, local ? Parent : null);
    }

    [TestMethod]
    [DataRow("default")]
    [DataRow("enabled")]
    [DataRow("disabled")]
    [DataRow("no-activity")]
    [DataRow("manual")]
    public async Task ActualNativeSendForwardsOnlyExistingEntryContextWhenEnabledAndPreservesManualHeaders(string mode)
    {
        var received = new TaskCompletionSource<(string Parent, string State, string Baggage)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var controlParent = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(context =>
        {
            if (context.Request.Path == "/control") { controlParent.TrySetResult(context.Request.Headers["traceparent"].ToString()); }
            else { received.TrySetResult((context.Request.Headers["traceparent"].ToString(), context.Request.Headers["tracestate"].ToString(), context.Request.Headers["baggage"].ToString())); }
            context.Response.StatusCode = 204; return Task.CompletedTask;
        });
        int nativeActivities = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "System.Net.Http",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ => Interlocked.Increment(ref nativeActivities)
        };
        ActivitySource.AddActivityListener(listener);
        Activity? previous = Activity.Current; Activity.Current = null;
        Activity? entry = null;
        try
        {
            if (mode != "no-activity")
            {
                entry = new Activity("trace-forwarding-caller").SetIdFormat(ActivityIdFormat.W3C).Start();
                entry.TraceStateString = "vendor=state-secret"; entry.AddBaggage("baggage-secret", "private");
            }
            string? expected = mode == "manual" ? Manual : mode == "enabled" ? entry!.Id : null;
            var options = new RestClientOptions("orders")
            { BaseUrl = server.Address + "/", ForwardTraceId = mode is "enabled" or "no-activity" ? true : mode is "disabled" or "manual" ? false : null };
            await using var client = new HttpRestClient(options);
            var input = new RestRequestOptions();
            if (mode == "manual") { input.Headers.Set("traceparent", Manual); }
            RestResult<RestNoValue> result = await client.Get<RestNoValue>("orders", input).WaitAsync(RestHttpTestSupport.Deadline);
            Assert.IsTrue(result.Success); Assert.AreEqual(1, result.Context.AttemptCount);
            var headers = await received.Task.WaitAsync(RestHttpTestSupport.Deadline);
            Assert.AreEqual(expected ?? string.Empty, headers.Parent);
            Assert.AreEqual(string.Empty, headers.State); Assert.AreEqual(string.Empty, headers.Baggage);
            Assert.AreEqual(entry?.TraceId.ToHexString(), result.Context.CorrelationId);
            // A separate ordinary client proves that native tracing/propagation
            // is live; CIS deliberately omits that implicit propagation handler.
            using var control = new HttpClient(new SocketsHttpHandler { UseProxy = false,
                ActivityHeadersPropagator = DistributedContextPropagator.CreateDefaultPropagator() });
            using var controlBudget = new CancellationTokenSource(RestHttpTestSupport.Deadline);
            using HttpResponseMessage response = await control.GetAsync(server.Address + "/control", controlBudget.Token);
            Assert.IsTrue(response.IsSuccessStatusCode);
            Assert.IsFalse(string.IsNullOrEmpty(await controlParent.Task.WaitAsync(RestHttpTestSupport.Deadline)));
            Assert.IsTrue(Volatile.Read(ref nativeActivities) > 0, "The control must actually exercise native HTTP tracing.");
        }
        finally { entry?.Dispose(); Activity.Current = previous; }
    }

    private static async Task<HttpRequestMessage> NativeAsync(RestClientSnapshot client, RestCallSnapshot call, string? trace,
        Action<RestRequestContext>? acquisition = null)
    {
        RestRequestContext request = RestRequestFinalizer.Create(client, call, "GET", "", 0);
        HttpRequestMessage? native = null;
        try
        {
            RestDeclaredFailure? failure = await RestRequestFinalizer.PrepareAsync(request, client, call,
                _ => { acquisition?.Invoke(request); return ValueTask.CompletedTask; },
                (_, _) => throw new AssertFailedException("No signing representation was requested."), default,
                () => native = RestNativeRequest.Create(request, null), capturedTraceParent: trace);
            Assert.IsNull(failure); RestNativeRequest.Finalize(native!, request); return native!;
        }
        catch { native?.Dispose(); throw; }
    }

    private static void AssertHeader(HttpRequestMessage native, string? expected)
    {
        if (expected is null) { Assert.IsFalse(native.Headers.TryGetValues("traceparent", out _)); }
        else { Assert.AreEqual(expected, native.Headers.GetValues("traceparent").Single()); }
        Assert.IsFalse(native.Headers.TryGetValues("tracestate", out _)); Assert.IsFalse(native.Headers.TryGetValues("baggage", out _));
    }
}
