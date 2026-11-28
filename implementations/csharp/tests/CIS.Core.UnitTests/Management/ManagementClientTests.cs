using System.Text;
using CIS.CommonTesting;
using CIS.Management;
using CIS.Management.Hosting;
using CIS.Management.Wire;
using CIS.RestClient;
using CIS.RestClient.Hosting;
using CIS.RestClient.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
[DoNotParallelize]
public sealed class ManagementClientTests
{
    private const string Id = "6d2222a6-2ffd-4010-bf15-bd22a8b70f4b";
    private const string Registration = "7d2222a6-2ffd-4010-bf15-bd22a8b70f4b";
    private const string Time = "2026-11-23T12:34:56.1234567Z";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task AllThirteenTypedMethodsPreserveTheConfiguredPrefixAndOriginalKeys()
    {
        var seen = new List<string>();
        var options = Options();
        RestClientOptions native = Native(options, new Handler((request, _) =>
        {
            var uri = new Uri(request.Url);
            Assert.IsTrue(uri.AbsolutePath.StartsWith("/tenant/control/management/v1/", StringComparison.Ordinal));
            seen.Add(request.Call.OperationName);
            string suffix = uri.AbsolutePath["/tenant/control/management/v1/".Length..];
            byte[] body = request.Call.OperationName switch
            {
                "service-info" => ManagementWire.EncodeResponse(new ManagementServiceInfo("1", [1], [])),
                "registration" => ManagementWire.EncodeResponse(new ManagementRegistrationReceipt(Registration, Id, Time)),
                "control" => ManagementWire.EncodeResponse(new ManagementControlConfirmation(Id, Registration, "opaque/9007199254740993", true, 15000, Id)),
                "observation" => ManagementWire.EncodeResponse(new ManagementReportReceipt(Registration, "observation", "1", "1", "accepted", Time)),
                "inventory-report" => ManagementWire.EncodeResponse(new ManagementReportReceipt(Registration, "inventory", "1", "1", "accepted", Time)),
                "registration-close" => ManagementWire.EncodeResponse(new ManagementCloseReceipt(Registration, Time)),
                "applications" => ManagementWire.EncodeResponse(new ManagementPage<ManagementApplication>([Application()])),
                "application" => ManagementWire.EncodeResponse(Application()),
                "instances" => ManagementWire.EncodeResponse(new ManagementPage<ManagementInstance>([])),
                "inventory" => ManagementWire.EncodeResponse(new ManagementPage<ManagementInventoryEntry>([])),
                "capabilities" => ManagementWire.EncodeResponse(new ManagementCapabilities(Id, [], [])),
                "metadata-replace" => ManagementWire.EncodeResponse(Result("prepared")),
                "metadata-operation" => ManagementWire.EncodeResponse(Result("found")),
                _ => throw new AssertFailedException("Unknown typed operation.")
            };
            if (request.Call.OperationName == "metadata-replace")
            {
                Assert.AreEqual("original-key", request.Headers.Snapshot()["Idempotency-Key"].Single());
                Assert.AreEqual("\"m1-1\"", request.Headers.Snapshot()["If-Match"].Single());
            }
            if (request.Call.OperationName == "metadata-operation")
            {
                Assert.AreEqual("applications/" + Id + "/metadata-operations/original-key", suffix);
            }
            return ValueTask.FromResult<IRestResponse>(new Response(200, new MemoryStream(body)));
        }));
        await using var rest = new HttpRestClient(native);
        var client = new ManagementClient(rest, options);

        Assert.IsTrue((await client.GetServiceInfoAsync()).Exchange.Success);
        Assert.IsTrue((await client.RegisterAsync(Registration, new(1, "reported", Time, "1"))).Exchange.Success);
        Assert.IsTrue((await client.GetControlAsync(Registration, Id)).Exchange.Success);
        Assert.IsTrue((await client.ReportObservationAsync(Registration, new("1", Time, "ready", "healthy"))).Exchange.Success);
        Assert.IsTrue((await client.ReportInventoryAsync(Registration, new("1", Time, [], [], []))).Exchange.Success);
        Assert.IsTrue((await client.CloseRegistrationAsync(Registration)).Exchange.Success);
        Assert.IsTrue((await client.ListApplicationsAsync(new(500, "opaque-cursor"))).Exchange.Success);
        Assert.IsTrue((await client.GetApplicationAsync(Id)).Exchange.Success);
        Assert.IsTrue((await client.ListInstancesAsync(Id)).Exchange.Success);
        Assert.IsTrue((await client.ListInventoryAsync(Id)).Exchange.Success);
        Assert.IsTrue((await client.GetCapabilitiesAsync(Id)).Exchange.Success);
        Assert.IsTrue((await client.ReplaceMetadataAsync(Id, "original-key", "\"m1-1\"", new(1, "Orders"))).Exchange.Success);
        Assert.IsTrue((await client.GetMetadataOperationAsync(Id, "original-key")).Exchange.Success);
        Assert.HasCount(13, seen.Distinct().ToArray());
    }

    [TestMethod]
    [DataRow("http://service.invalid/")]
    [DataRow("https://user:password@service.invalid/")]
    [DataRow("https://service.invalid/?tenant=a")]
    [DataRow("https://service.invalid/#fragment")]
    [DataRow("https://service.invalid/a/../b")]
    [DataRow("https://service.invalid/a/%2e%2e/b")]
    [DataRow("https://service.invalid/a%2fb")]
    [DataRow("https://service.invalid/a\nb")]
    [DataRow("https://service.invalid/a\rb")]
    [DataRow("https://service.invalid/a\tb")]
    public void InvalidBootstrapIsAConfigurationFaultBeforeAnyCall(string url)
    {
        var script = new ScriptedRestClient();
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementClient(script, new("management") { BaseUrl = url }));
        Assert.AreEqual(0, script.TotalCalls);
    }

    [TestMethod]
    public async Task ExplicitEndpointPrecedesEnvironmentAndAReadClientCreatesNoReporter()
    {
        string? original = Environment.GetEnvironmentVariable("CIS_MANAGEMENT_URL");
        try
        {
            Environment.SetEnvironmentVariable("CIS_MANAGEMENT_URL", "http://invalid.example/");
            var script = new ScriptedRestClient();
            script.EnqueueJsonValue("""{"serviceVersion":"1","protocolMajors":[1],"features":[]}""");
            var client = new ManagementClient(script, Options());
            await client.GetServiceInfoAsync();
            Assert.AreEqual("https://service.invalid/tenant/control/management/v1/service-info", script.Calls.Single().Url);
            Assert.AreEqual(1, script.TotalCalls);
            Assert.ThrowsExactly<ArgumentException>(() => new ManagementClient(script, new("management")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CIS_MANAGEMENT_URL", original);
        }
    }

    [TestMethod]
    public async Task NullRequiredRouteIdsFailBeforeDispatchAndDoNotConsumeASubstitute()
    {
        var rest = new ScriptedRestClient();
        rest.EnqueueJsonValue("""{"serviceVersion":"1","protocolMajors":[1],"features":[]}""");
        var client = new ManagementClient(rest, Options());

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.GetApplicationAsync(null!));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.RegisterAsync(null!, new(1, "reported", Time, "1")));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.GetMetadataOperationAsync(null!, "original-key"));
        Assert.AreEqual(0, rest.TotalCalls);
        Assert.IsTrue((await client.GetServiceInfoAsync()).Exchange.Success);
        Assert.AreEqual(1, rest.TotalCalls);
    }

    [TestMethod]
    public async Task CallerCancellationDuringPureSetupWinsOverSimultaneousBudgetExpiry()
    {
        var rest = new ScriptedRestClient();
        rest.EnqueueJsonValue("""{"serviceVersion":"1","protocolMajors":[1],"features":[]}""");
        using var caller = new CancellationTokenSource();
        var clock = new CancelingSetupClock(caller);
        var client = new ManagementClient(rest, Options(), clock: clock);

        Task<ManagementCallResult<ManagementServiceInfo>> call = client.GetServiceInfoAsync(cancellationToken: caller.Token);
        try
        {
            await call;
            Assert.Fail("Caller cancellation must win over a simultaneously exhausted setup budget.");
        }
        catch (OperationCanceledException) when (call.IsCanceled)
        {
        }

        Assert.IsTrue(caller.IsCancellationRequested);
        Assert.AreEqual(0, rest.TotalCalls);
        Assert.IsTrue((await new ManagementClient(rest, Options()).GetServiceInfoAsync()).Exchange.Success);
        Assert.AreEqual(1, rest.TotalCalls);
    }

    [TestMethod]
    public async Task NativeProblemMappingRetainsHttpAndOriginalResultFactsWithoutAutomaticReplay()
    {
        var options = Options();
        options.ReadRetryPolicy = RestRetryPolicy.TransientHttp;
        var problem = new ManagementProblem("about:blank", "Completion unknown", 503, "management.operation_unknown",
            Operation: Result("prepared").Operation, Result: Result("prepared") with
            {
                OriginalCompletion = "unknown"
            });
        int calls = 0;
        RestClientOptions native = Native(options, new Handler((_, _) =>
        {
            calls++;
            return ValueTask.FromResult<IRestResponse>(new Response(503, new MemoryStream(ManagementWire.EncodeResponse(problem)),
                "application/problem+json; charset=utf-8"));
        }));
        await using var rest = new HttpRestClient(native);
        var client = new ManagementClient(rest, options, [RestRetryPolicy.TransientHttp]);

        ManagementCallResult<ManagementMetadataResult> result = await client.ReplaceMetadataAsync(Id, "original-key", "\"m1-1\"", new(1, "Orders"));

        Assert.IsFalse(result.Exchange.Success);
        Assert.AreEqual(503, result.Exchange.Context.HttpStatusCode);
        Assert.AreEqual("management.operation_unknown", result.Exchange.Context.RemoteErrorCode);
        Assert.AreEqual("original-key", result.OperationKey);
        Assert.AreEqual("unknown", result.Problem!.Result!.OriginalCompletion);
        Assert.AreEqual("original-key", result.Problem.Operation!.Key);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task NativeBoundSecurityFailureRemainsUnknownCauseAndAuthenticationIsNotOverridden()
    {
        var options = Options();
        RestClientOptions native = Native(options, new Handler((_, _) => ValueTask.FromResult<IRestResponse>(
            new Response(403, new MemoryStream(ManagementWire.EncodeResponse(new ManagementProblem("about:blank", "Denied", 403,
                "management.forbidden"))), "application/problem+json"))));
        native.SecurityBinding = new("fixture", new CIS.Security.SecuritySubject("issuer", "subject"), "policy",
            RestSecurityCredentialKind.RequestAuthentication, ["https://service.invalid"]);
        native.Authenticate = (_, _) => Task.CompletedTask;
        await using var rest = new HttpRestClient(native);
        var client = new ManagementClient(rest, options);

        ManagementCallResult<ManagementApplication> result = await client.GetApplicationAsync(Id);

        Assert.AreEqual(403, result.Exchange.Context.HttpStatusCode);
        Assert.IsNotNull(result.Exchange.Context.SecurityFailure);
        Assert.AreEqual(CIS.Security.SecurityFailureKind.CauseUnknown, result.Exchange.Context.SecurityFailure.Kind);
        Assert.AreEqual("management.forbidden", result.Problem!.Code);
    }

    [TestMethod]
    public async Task InvalidAndTruncatedBodiesStayNativeFailuresWithoutInventedTypedFacts()
    {
        var options = Options();
        int call = 0;
        RestClientOptions native = Native(options, new Handler((_, _) =>
        {
            call++;
            if (call == 1)
            {
                return ValueTask.FromResult<IRestResponse>(new Response(200, new MemoryStream(Encoding.UTF8.GetBytes("{}"))));
            }
            byte[] prefix = ManagementWire.EncodeResponse(new ManagementProblem("about:blank", "Unavailable", 503, "management.unavailable"));
            byte[] oversized = [.. prefix, .. Enumerable.Repeat((byte)' ', ManagementWire.MaximumErrorBodyBytes).ToArray()];
            return ValueTask.FromResult<IRestResponse>(new Response(503, new MemoryStream(oversized), "application/problem+json"));
        }));
        await using var rest = new HttpRestClient(native);
        var client = new ManagementClient(rest, options);

        ManagementCallResult<ManagementApplication> malformed = await client.GetApplicationAsync(Id);
        ManagementCallResult<ManagementApplication> truncated = await client.GetApplicationAsync(Id);

        Assert.AreEqual(RestFailureKind.InvalidResponse, malformed.Exchange.Context.FailureKind);
        Assert.IsNull(malformed.Problem);
        Assert.IsTrue(truncated.Exchange.Context.ErrorBodyTruncated);
        Assert.IsNull(truncated.Problem);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CallerCancellationAndNativeDeadlineReachHeldCredentialsAndJoinActualWork(bool timeout)
    {
        var options = Options();
        var clock = new ManualTimeProvider(DateTimeOffset.Parse(Time));
        var entered = Signal();
        var release = Signal();
        CancellationToken actual = default;
        RestClientOptions native = Native(options);
        native.Authenticate = async (_, token) =>
        {
            actual = token;
            entered.TrySetResult();
            await release.Task;
            token.ThrowIfCancellationRequested();
        };
        await using var rest = new HttpRestClient(native, clock: clock);
        var client = new ManagementClient(rest, options, clock: clock);
        using var caller = new CancellationTokenSource();
        Task<ManagementCallResult<ManagementServiceInfo>> call = client.GetServiceInfoAsync(cancellationToken: caller.Token);
        try
        {
            await entered.Task.WaitAsync(Deadline);
            if (timeout)
            {
                clock.Advance(TimeSpan.FromSeconds(31));
            }
            else
            {
                caller.Cancel();
            }
            Assert.IsTrue(actual.IsCancellationRequested);
            Assert.IsFalse(call.IsCompleted);
            release.TrySetResult();
            if (timeout)
            {
                ManagementCallResult<ManagementServiceInfo> result = await call.WaitAsync(Deadline);
                Assert.AreEqual(RestFailureKind.Timeout, result.Exchange.Context.FailureKind);
                Assert.AreEqual(0, result.Exchange.Context.AttemptCount);
            }
            else
            {
                try
                {
                    await call.WaitAsync(Deadline);
                    Assert.Fail("Caller cancellation must remain cancellation.");
                }
                catch (OperationCanceledException) when (call.IsCanceled)
                {
                }
            }
        }
        finally
        {
            caller.Cancel();
            release.TrySetResult();
            try
            {
                await call.WaitAsync(Deadline);
            }
            catch (OperationCanceledException) when (call.IsCanceled)
            {
            }
        }
    }

    [TestMethod]
    public async Task ResponseCleanupCannotBeDeclaredCompleteByCancelingTheWait()
    {
        var stream = new HeldCleanupStream(ManagementWire.EncodeResponse(new ManagementServiceInfo("1", [1], [])));
        var options = Options();
        await using var rest = new HttpRestClient(Native(options, new Handler((_, _) =>
            ValueTask.FromResult<IRestResponse>(new Response(200, stream)))));
        var client = new ManagementClient(rest, options);
        using var caller = new CancellationTokenSource();
        Task<ManagementCallResult<ManagementServiceInfo>> call = client.GetServiceInfoAsync(cancellationToken: caller.Token);
        try
        {
            await stream.Cleaning.Task.WaitAsync(Deadline);
            caller.Cancel();
            Assert.IsFalse(call.IsCompleted);
        }
        finally
        {
            stream.Release.TrySetResult();
            try
            {
                await call.WaitAsync(Deadline);
            }
            catch (OperationCanceledException) when (call.IsCanceled)
            {
            }
        }
        Assert.AreEqual(1, stream.Disposals);
    }

    [TestMethod]
    public void RetrySelectionMustBeTheActualRegisteredConservativePolicy()
    {
        var options = Options();
        options.ReadRetryPolicy = RestRetryPolicy.TransientHttp;
        Assert.ThrowsExactly<InvalidOperationException>(() => new ManagementClient(new ScriptedRestClient(), options));
        options.ReadRetryPolicy = new("three", 3, TimeSpan.Zero, TimeSpan.Zero, [503]);
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementClient(new ScriptedRestClient(), options, [options.ReadRetryPolicy]));
        options.ReadRetryPolicy = new("auth", 2, TimeSpan.Zero, TimeSpan.Zero, [401, 403]);
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementClient(new ScriptedRestClient(), options, [options.ReadRetryPolicy]));
    }

    [TestMethod]
    public async Task PassiveRegistrationReusesNativeKeyedOwnersAndTheFirstExplicitDefault()
    {
        var services = new ServiceCollection();
        var first = Options();
        services.AddCisRestClient(new RestClientOptions("management").ConfigureCisManagementRestClient(first));
        int hosted = services.Count(item => item.ServiceType == typeof(IHostedService));
        services.AddCisManagementClient(first);
        var second = new ManagementClientOptions("other") { BaseUrl = "https://other.invalid/" };
        services.AddCisRestClient(new RestClientOptions("other").ConfigureCisManagementRestClient(second));
        services.AddCisManagementClient(second);
        Assert.AreEqual(hosted, services.Count(item => item.ServiceType == typeof(IHostedService)));
        Assert.IsFalse(services.Any(item => item.ServiceType == typeof(CIS.Startup.IStartable)));
        await using ServiceProvider root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        IManagementClient firstKey = root.GetRequiredKeyedService<IManagementClient>("management");
        Assert.AreSame(firstKey, root.GetRequiredService<IManagementClient>());
        Assert.AreSame<object>(firstKey, root.GetRequiredService<IManagementApiDiscoveryClient>());
        Assert.AreSame<object>(firstKey, root.GetRequiredKeyedService<IManagementApiDiscoveryClient>("management"));
        Assert.AreNotSame(firstKey, root.GetRequiredKeyedService<IManagementClient>("other"));
    }

    [TestMethod]
    public async Task CompleteApiDiscoveryUsesItsOwnDefaultAndRejectsOversizedPagesAndCrossApplicationResponses()
    {
        int calls = 0;
        var options = Options();
        await using var rest = new HttpRestClient(Native(options, new Handler((request, _) =>
        {
            calls++;
            Assert.AreEqual("32", request.Query.Snapshot().Single(pair => pair.Key == "pageSize").Value);
            byte[] body = request.Call.OperationName switch
            {
                "service-features" => ManagementWire.EncodeResponse(new ManagementServiceFeaturePage("complete-v1", [])),
                "api-capabilities" => ManagementWire.EncodeResponse(new ManagementApplicationApiCapabilityPage(Registration, "complete-v1", [])),
                _ => throw new AssertFailedException("Unexpected discovery operation.")
            };
            return ValueTask.FromResult<IRestResponse>(new Response(200, new MemoryStream(body)));
        })));
        var client = new ManagementClient(rest, options);
        Assert.IsTrue((await client.ListServiceFeaturesAsync()).Exchange.Success);
        Assert.IsFalse((await client.ListApplicationApiCapabilitiesAsync(Id)).Exchange.Success,
            "A page for another application cannot be admitted as this caller's response.");
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.ListServiceFeaturesAsync(new(65)));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.ListApplicationApiCapabilitiesAsync(Id, new(100)));
        Assert.AreEqual(2, calls, "Invalid discovery requests must not reach native dispatch.");
    }

    private static ManagementClientOptions Options() => new("management") { BaseUrl = "https://service.invalid/tenant/control" };
    private static RestClientOptions Native(ManagementClientOptions options, IRestExchangeHandler? handler = null)
    {
        var native = new RestClientOptions(options.RestClientName) { LogCalls = false };
        native.ConfigureCisManagementRestClient(options);
        if (handler is not null)
        {
            native.Handlers.Add(handler);
        }
        return native;
    }
    private static ManagementApplication Application() => new(Id, "orders", "Production", new("Orders"), "1", "\"m1-1\"");
    private static ManagementMetadataResult Result(string source) => new(new("metadata.replace", "mgmt-meta.v1:" + new string('a', 64), "original-key"),
        source, new(1, Id, "applied", "management.metadata.applied", new("Orders"), "2", "\"m1-2\"", Time), "committed");
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Handler(Func<RestRequestContext, CancellationToken, ValueTask<IRestResponse>> handle) : IRestExchangeHandler
    {
        public ValueTask<IRestResponse> HandleAsync(RestRequestContext request, RestExchange next, CancellationToken token) => handle(request, token);
    }

    private sealed class Response(int status, Stream body, string mediaType = "application/json") : IRestResponse
    {
        public int HttpStatusCode => status;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers
        {
            get;
        } = new Dictionary<string, IReadOnlyList<string>>
        {
            ["Content-Type"] = [mediaType]
        };
        public Stream Body => body;
        public ValueTask DisposeAsync() => body.DisposeAsync();
    }

    private sealed class HeldCleanupStream(byte[] bytes) : MemoryStream(bytes)
    {
        internal TaskCompletionSource Cleaning { get; } = Signal();
        internal TaskCompletionSource Release { get; } = Signal();
        internal int Disposals
        {
            get; private set;
        }
        public override async ValueTask DisposeAsync()
        {
            Disposals++;
            Cleaning.TrySetResult();
            await Release.Task;
            await base.DisposeAsync();
        }
    }

    private sealed class CancelingSetupClock(CancellationTokenSource caller) : TimeProvider
    {
        private int reads;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                return 0;
            }
            caller.Cancel();
            return TimeSpan.FromSeconds(31).Ticks;
        }
    }
}
