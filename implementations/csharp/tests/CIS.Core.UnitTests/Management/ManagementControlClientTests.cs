using CIS.CommonTesting;
using CIS.Management;
using CIS.Management.Hosting;
using CIS.Management.Wire;
using CIS.RestClient;
using CIS.RestClient.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ManagementControlClientTests
{
    private const string Authority = "33333333-3333-4333-8333-333333333333";
    private const string Registration = "22222222-2222-4222-8222-222222222222";
    private const string Attempt = "44444444-4444-4444-8444-444444444444";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task ControlPreservesThePrefixExactAttemptAndOpaqueFactsWithoutInstallingAdmission()
    {
        var options = Options();
        int calls = 0;
        await using var rest = new HttpRestClient(Native(options, (request, _) =>
        {
            calls++;
            Assert.AreEqual("GET", request.Method);
            Assert.AreEqual("https://management.invalid/platform/management/v1/registrations/" + Registration + "/control",
                request.Url);
            Assert.AreEqual(Attempt, request.Query.Snapshot().Single(item => item.Key == "attemptId").Value);
            Assert.AreEqual("no-store", request.Headers.Snapshot()["Cache-Control"].Single());
            return new(200, new MemoryStream(ManagementWire.EncodeResponse(Confirmation())));
        }));
        var client = new ManagementClient(rest, options);

        ManagementCallResult<ManagementControlConfirmation> result = await client.GetControlAsync(Registration, Attempt);

        Assert.IsTrue(result.Exchange.Success);
        Assert.AreEqual(200, result.Exchange.Context.HttpStatusCode);
        Assert.AreEqual(Confirmation(), result.Exchange.Value);
        Assert.AreEqual("opaque/Cafe\u0301/9007199254740993", result.Exchange.Value!.ControlRevision);
        Assert.IsNull(result.OperationKey, "An echoed attempt is not a mutation operation key.");
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task MismatchedControlEchoesAreNativeBusinessRejectionsAndStillJoinActualCleanup(bool wrongRegistration)
    {
        var options = Options();
        ManagementControlConfirmation body = wrongRegistration ? Confirmation() with
        {
            RegistrationId = Authority
        }
            : Confirmation() with
            {
                AttemptId = Authority
            };
        var stream = new HeldCleanupStream(ManagementWire.EncodeResponse(body));
        await using var rest = new HttpRestClient(Native(options, (_, _) => new(200, stream)));
        var client = new ManagementClient(rest, options);
        Task<ManagementCallResult<ManagementControlConfirmation>> call = client.GetControlAsync(Registration, Attempt);
        try
        {
            await stream.Entered.Task.WaitAsync(Deadline);
            Assert.IsFalse(call.IsCompleted, "A rejected confirmation still owns the actual response cleanup.");
        }
        finally
        {
            stream.Release.TrySetResult();
        }
        ManagementCallResult<ManagementControlConfirmation> result = await call.WaitAsync(Deadline);
        Assert.IsFalse(result.Exchange.Success);
        Assert.AreEqual(RestFailureKind.Business, result.Exchange.Context.FailureKind);
        Assert.AreEqual("management.control.invalid_confirmation", result.Exchange.ErrorCode);
        Assert.AreEqual(200, result.Exchange.Context.HttpStatusCode);
        Assert.IsFalse(result.Exchange.Context.HasValue);
        Assert.AreEqual(1, stream.Disposals);
    }

    [TestMethod]
    public async Task ASelectedGenericReadPolicyCannotRetryControlOrHideItsFirstRefusal()
    {
        var options = Options();
        options.ReadRetryPolicy = RestRetryPolicy.TransientHttp;
        int calls = 0;
        byte[] problem = ManagementWire.EncodeResponse(new ManagementProblem("about:blank", "Unavailable", 503, "management.dependency.unavailable"));
        await using var rest = new HttpRestClient(Native(options, (_, _) =>
        {
            calls++;
            return new(503, new MemoryStream(problem), "application/problem+json");
        }));
        var client = new ManagementClient(rest, options, [options.ReadRetryPolicy]);

        ManagementCallResult<ManagementControlConfirmation> result = await client.GetControlAsync(Registration, Attempt);

        Assert.IsFalse(result.Exchange.Success);
        Assert.AreEqual(503, result.Exchange.Context.HttpStatusCode);
        Assert.AreEqual("management.dependency.unavailable", result.Problem!.Code);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task ScriptedControlRetainsExactResultsAndSafeAttemptMetadataWithNoPrecanceledConsumption()
    {
        var script = new ScriptedManagementClient(1);
        var selected = new ManagementCallResult<ManagementControlConfirmation>(RestResult<ManagementControlConfirmation>.FromValue(
            Confirmation(), new RestResultContext(200, hasValue: true)));
        script.EnqueueResult(ScriptedManagementCallKind.Control, selected);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => script.GetControlAsync(Registration, "invalid"));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Task<ManagementCallResult<ManagementControlConfirmation>> before = script.GetControlAsync(Registration, Attempt, canceled.Token);
        try
        {
            await before;
            Assert.Fail("A precanceled call must consume no control step.");
        }
        catch (OperationCanceledException) when (before.IsCanceled)
        {
        }
        Assert.AreEqual(0, script.TotalCalls);
        Assert.AreSame(selected, await script.GetControlAsync(Registration, Attempt));
        Assert.AreEqual(Attempt, script.Calls.Single().AttemptId);
        Assert.IsNull(script.Calls.Single().OperationKey);
        Assert.IsNull(typeof(ScriptedManagementCall).GetProperty("Credentials"));
    }

    [TestMethod]
    public void AManualAdmissionPortRetainsOneSelectedObservationAndOriginalSubscriberFault()
    {
        var manual = new ManualManagedWorkAdmission();
        Assert.IsFalse(manual.TryAdmit());
        Assert.AreEqual(ManagedWorkAdmissionState.AwaitingBootstrap, manual.CaptureSnapshot().State);
        var ready = new ManagedWorkAdmissionSnapshot(ManagedWorkAdmissionState.Ready, true, 1, AuthorityId: Authority,
            RegistrationId: Registration, ControlRevision: "opaque-v1");
        var original = new InvalidOperationException("Selected listener fault.");
        ManagedWorkAdmissionSnapshot? observed = null;
        manual.Changed += (_, args) =>
        {
            observed = manual.CaptureSnapshot();
            Assert.AreSame(ready, args.Snapshot);
            throw original;
        };

        InvalidOperationException caught = Assert.ThrowsExactly<InvalidOperationException>(() => manual.Set(ready));

        Assert.AreSame(original, caught);
        Assert.AreSame(ready, observed);
        Assert.AreSame(ready, manual.CaptureSnapshot());
        Assert.IsTrue(manual.TryAdmit());
        Assert.AreEqual(2, manual.AdmissionChecks);
    }

    [TestMethod]
    public void ServiceInfoKeepsItsExistingConstructorAndOptionalProviderBoundWithoutInventingItForLegacyData()
    {
        var legacy = new ManagementServiceInfo("1", [1], []);
        ManagementServiceInfo? decodedLegacy = ManagementWire.DecodeResponse<ManagementServiceInfo>(ManagementWire.EncodeResponse(legacy));
        Assert.IsNotNull(decodedLegacy);
        Assert.IsNull(decodedLegacy.MaximumInventoryRequestBytes);
        var advertised = legacy with
        {
            MaximumInventoryRequestBytes = 4 * 1024 * 1024
        };
        ManagementServiceInfo? decoded = ManagementWire.DecodeResponse<ManagementServiceInfo>(ManagementWire.EncodeResponse(advertised));
        Assert.IsNotNull(decoded);
        Assert.AreEqual(4 * 1024 * 1024, decoded.MaximumInventoryRequestBytes);
    }

    private static ManagementControlConfirmation Confirmation() => new(Authority, Registration,
        "opaque/Cafe\u0301/9007199254740993", true, 15000, Attempt);
    private static ManagementClientOptions Options() => new("control") { BaseUrl = "https://management.invalid/platform" };
    private static RestClientOptions Native(ManagementClientOptions options, Func<RestRequestContext, CancellationToken, Response> respond)
    {
        var native = new RestClientOptions(options.RestClientName) { LogCalls = false };
        native.ConfigureCisManagementRestClient(options);
        native.Handlers.Add(new Handler(respond));
        return native;
    }

    private sealed class Handler(Func<RestRequestContext, CancellationToken, Response> respond) : IRestExchangeHandler
    {
        public ValueTask<IRestResponse> HandleAsync(RestRequestContext request, RestExchange next, CancellationToken token)
            => ValueTask.FromResult<IRestResponse>(respond(request, token));
    }

    private sealed class Response(int status, Stream body, string mediaType = "application/json") : IRestResponse
    {
        public int HttpStatusCode => status;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers
        {
            get;
        } = new Dictionary<string, IReadOnlyList<string>>
        {
            ["Content-Type"] = [mediaType],
            ["Retry-After"] = ["0"]
        };
        public Stream Body => body;
        public ValueTask DisposeAsync() => body.DisposeAsync();
    }

    private sealed class HeldCleanupStream(byte[] bytes) : MemoryStream(bytes)
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Disposals
        {
            get; private set;
        }
        public override async ValueTask DisposeAsync()
        {
            Disposals++;
            Entered.TrySetResult();
            await Release.Task;
            await base.DisposeAsync();
        }
    }
}
