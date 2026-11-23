using CIS.CommonTesting;
using CIS.Coordination.Hosting;
using CIS.Heartbeat;
using CIS.Heartbeat.Hosting;
using CIS.Hosting;
using CIS.Jobs;
using CIS.Management;
using CIS.Management.Hosting;
using CIS.Management.Wire;
using CIS.RestClient;
using CIS.RestClient.Hosting;
using CIS.Startup.Hosting;
using CIS.Tests.UnitTests.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ManagementRegistrationHostingTests
{
    private const string ApplicationId = "6d2222a6-2ffd-4010-bf15-bd22a8b70f4b";
    private const string Time = "2026-11-23T10:00:00.0000000Z";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task NativeHostSignalsReporterBeforeJobsDrainAndClosesRestBeforeJoiningReportingCleanup()
    {
        await using var jobs = new JobRuntimeTestSupport();
        jobs.Control.Callback = async (_, _) =>
        {
            await jobs.Control.WorkRelease.Task;
            return new(ContinuousIterationOutcome.Idle);
        };
        jobs.EnqueueGrant();
        await jobs.Runtime.StartAsync();
        await jobs.Runner.StartAsync("wallet-service", "wallet.compaction", "partition");
        await jobs.Control.Entered.Task.WaitAsync(Deadline);
        var dependencies = new CoordinationHostLifetime();
        dependencies.Bind(jobs.Runtime);
        await using var heartbeat = new ManualHeartbeat();
        var handler = new ReportingHandler();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(jobs.Clock);
        builder.Services.AddSingleton<IHeartbeatOwnerFactory>(heartbeat);
        builder.Services.AddSingleton(dependencies);
        var client = new ManagementClientOptions("reporting") { BaseUrl = "https://management.invalid/platform" };
        var rest = new RestClientOptions("reporting") { LogCalls = false };
        rest.ConfigureCisManagementRestClient(client);
        rest.Handlers.Add(handler);
        builder.Services.AddCisRestClient(rest);
        builder.Services.AddCisManagementClient(client);
        builder.Services.AddCisManagementRegistration(new("reporting")
        {
            ProcessStartedAtUtc = DateTimeOffset.Parse(Time, System.Globalization.CultureInfo.InvariantCulture),
            ApplicationVersion = "1"
        });
        builder.Services.AddCisHeartbeat();
        builder.Services.AddCisStartup();
        IHost host = builder.Build();
        Task<ManualHeartbeatResult>? callback = null;
        Task? stop = null;
        try
        {
            await host.StartAsync();
            ManagementRegistration reporter = host.Services.GetRequiredService<ManagementRegistration>();
            RestClientRuntime native = host.Services.GetRequiredService<RestClientRuntime>();
            Assert.IsTrue(reporter.CaptureStatus().IsConnected);
            jobs.Clock.Advance(TimeSpan.FromSeconds(31));
            callback = heartbeat.TriggerAsync(reporter);
            await handler.CleanupEntered.Task.WaitAsync(Deadline);
            stop = host.StopAsync();

            Assert.IsTrue(reporter.CaptureStatus().IsStopping);
            Assert.IsTrue(handler.ReportToken.IsCancellationRequested, "Reporter cancellation must not wait for the admitted job.");
            Assert.IsFalse(native.IsStopping, "The original Jobs dependency barrier still retains native REST.");
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(heartbeat.Registrations[0].IsClosing, "BeginStopping signals without prematurely joining Heartbeat.");
            jobs.Control.WorkRelease.TrySetResult();
            await JobRuntimeTestSupport.UntilAsync(() => native.IsStopping);

            Assert.IsFalse(stop.IsCompleted, "REST must retain actual response cleanup.");
            Assert.IsFalse(heartbeat.Registrations[0].IsClosing, "Management's owner drain follows the actual REST drain.");
            handler.CleanupRelease.TrySetResult();
            await callback.WaitAsync(Deadline);
            await stop.WaitAsync(Deadline);
            Assert.HasCount(0, heartbeat.Registrations);
            Assert.AreEqual(6, handler.Calls);
        }
        finally
        {
            jobs.Control.WorkRelease.TrySetResult();
            jobs.Control.ScopeCleanupRelease.TrySetResult();
            handler.CleanupRelease.TrySetResult();
            if (callback is not null)
            {
                await callback.WaitAsync(Deadline);
            }
            await (stop ?? host.StopAsync()).WaitAsync(Deadline);
            await ((IAsyncDisposable)host).DisposeAsync();
        }
    }

    [TestMethod]
    public async Task PassiveFacadeMetadataDrainsABoundOwnerEvenWhenNoBuildWasRequested()
    {
        var script = new ScriptedManagementClient();
        await using var heartbeat = new ManualHeartbeat();
        var clock = new ManualTimeProvider(DateTimeOffset.Parse(Time, System.Globalization.CultureInfo.InvariantCulture));
        var reporter = new ManagementRegistration(script, heartbeat, new Sources(), new Sources(),
            new("reporting")
            {
                ProcessStartedAtUtc = clock.GetUtcNow(),
                ApplicationVersion = "1"
            }, clock);
        string id = reporter.CaptureStatus().RegistrationId;
        script.EnqueueResult(ScriptedManagementCallKind.Registration, Value(new ManagementRegistrationReceipt(id, ApplicationId, Time)));
        script.EnqueueControl(call => Value(new ManagementControlConfirmation(ApplicationId, call.RegistrationId!, "opaque-v1", true, 15000, call.AttemptId!)));
        script.EnqueueResult(ScriptedManagementCallKind.Observation, Value(new ManagementReportReceipt(id, "observation", "1", "1", "accepted", Time)));
        script.EnqueueResult(ScriptedManagementCallKind.InventoryReport, Value(new ManagementReportReceipt(id, "inventory", "1", "1", "accepted", Time)));
        await reporter.StartAsync();
        script.EnqueueControl(call => Value(new ManagementControlConfirmation(ApplicationId, call.RegistrationId!, "opaque-v1", true, 15000, call.AttemptId!)));
        PendingManagementCall pending = script.EnqueuePending(ScriptedManagementCallKind.Observation,
            Value(new ManagementReportReceipt(id, "observation", "2", "2", "accepted", Time)), observeCancellation: false);
        clock.Advance(TimeSpan.FromSeconds(31));
        Task<ManualHeartbeatResult> callback = heartbeat.TriggerAsync(reporter);
        await pending.Entered.WaitAsync(Deadline);
        var lifetime = new ManagementRegistrationLifetime();
        lifetime.Bind(reporter);
        var application = new CISApplication(builder => builder.Services.AddSingleton(lifetime));
        Task disposal = application.DisposeAsync().AsTask();
        try
        {
            Assert.IsTrue(reporter.CaptureStatus().IsStopping);
            Assert.IsFalse(disposal.IsCompleted);
        }
        finally
        {
            pending.Release();
            await callback.WaitAsync(Deadline);
            await disposal.WaitAsync(Deadline);
        }
        Assert.HasCount(0, heartbeat.Registrations);
    }

    [TestMethod]
    public async Task ClosingPassiveLifetimeJoinsAnAlreadyAdmittedFactoryAndRefusesAnotherConstruction()
    {
        var lifetime = new ManagementRegistrationLifetime();
        IDisposable construction = lifetime.EnterConstruction();
        Task drain = lifetime.StopAsync();
        Assert.IsFalse(drain.IsCompleted);
        Assert.ThrowsExactly<InvalidOperationException>(() => lifetime.EnterConstruction());
        await using var heartbeat = new ManualHeartbeat();
        var reporter = new ManagementRegistration(new ScriptedManagementClient(), heartbeat, new Sources(), new Sources(), new("reporting"));
        lifetime.Bind(reporter);
        Assert.IsTrue(reporter.CaptureStatus().IsStopping);
        construction.Dispose();
        await drain.WaitAsync(Deadline);
        Assert.AreSame(drain, lifetime.StopAsync());
    }

    [TestMethod]
    public void RegistrationIsExplicitAndRequiresItsDedicatedQuietNativeAlias()
    {
        var services = new ServiceCollection();
        var options = new ManagementClientOptions("reporting") { BaseUrl = "https://management.invalid/platform" };
        services.AddCisRestClient(new RestClientOptions("reporting").ConfigureCisManagementRestClient(options));
        services.AddCisManagementClient(options);
        Assert.IsFalse(services.Any(item => item.ServiceType == typeof(ManagementRegistrationLifetime)));
        Assert.ThrowsExactly<InvalidOperationException>(() => services.AddCisManagementRegistration(new("reporting")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ACompetingAdmissionPortCannotBypassTheActualManagedBootstrapGate(bool addedLater)
    {
        var services = new ServiceCollection();
        var options = new ManagementClientOptions("reporting") { BaseUrl = "https://management.invalid/platform" };
        services.AddCisRestClient(new RestClientOptions("reporting") { LogCalls = false }.ConfigureCisManagementRestClient(options));
        services.AddCisManagementClient(options);
        var alternate = new ManualManagedWorkAdmission();
        alternate.Set(new(ManagedWorkAdmissionState.Ready, true, 1));
        if (!addedLater)
        {
            services.AddSingleton<IManagedWorkAdmission>(alternate);
            Assert.ThrowsExactly<InvalidOperationException>(() => services.AddCisManagementRegistration(new("reporting")));
        }
        else
        {
            services.AddCisManagementRegistration(new("reporting"));
            services.AddSingleton<IManagedWorkAdmission>(alternate);
            await using ServiceProvider provider = services.BuildServiceProvider();
            Assert.ThrowsExactly<InvalidOperationException>(() => provider.GetRequiredService<ManagementRegistration>());
        }
    }

    private static ManagementCallResult<T> Value<T>(T value) => new(RestResult<T>.FromValue(value, new RestResultContext(200, hasValue: true)));

    private sealed class Sources : IManagementObservationSource, IManagementInventorySource
    {
        public ManagementObservationContent CaptureObservation(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return new("starting", "unknown");
        }
        public ManagementInventoryContent CaptureInventory(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return new([], [], []);
        }
    }

    // Uses the actual native REST codec/mapper/cleanup owner; it makes no HTTPS or SQL claim.
    private sealed class ReportingHandler : IRestExchangeHandler
    {
        internal TaskCompletionSource CleanupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CleanupRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken ReportToken
        {
            get; private set;
        }
        internal int Calls
        {
            get; private set;
        }

        public ValueTask<IRestResponse> HandleAsync(RestRequestContext request, RestExchange next, CancellationToken token)
        {
            Calls++;
            string id = new Uri(request.Url).Segments[request.Call.OperationName == "registration" ? ^1 : ^2].TrimEnd('/');
            byte[] encoded;
            bool held = false;
            if (request.Body?.Value is ManagementRegistrationRequest)
            {
                encoded = ManagementWire.EncodeResponse(new ManagementRegistrationReceipt(id, ApplicationId, Time));
            }
            else if (request.Call.OperationName == "control")
            {
                string attemptId = request.Query.Snapshot().Single(item => item.Key == "attemptId").Value!;
                encoded = ManagementWire.EncodeResponse(new ManagementControlConfirmation(ApplicationId, id, "opaque-v1", true, 15000, attemptId));
            }
            else if (request.Body?.Value is ManagementObservation observation)
            {
                encoded = ManagementWire.EncodeResponse(new ManagementReportReceipt(id, "observation", observation.Sequence, observation.Sequence, "accepted", Time));
                held = observation.Sequence == "2";
                if (held)
                {
                    ReportToken = token;
                }
            }
            else if (request.Body?.Value is ManagementInventorySnapshot inventory)
            {
                encoded = ManagementWire.EncodeResponse(new ManagementReportReceipt(id, "inventory", inventory.Sequence, inventory.Sequence, "accepted", Time));
            }
            else
            {
                throw new InvalidOperationException("Unexpected management report request.");
            }
            Stream body = held ? new HeldCleanupStream(encoded, CleanupEntered, CleanupRelease) : new MemoryStream(encoded);
            return ValueTask.FromResult<IRestResponse>(new Response(body));
        }
    }

    private sealed class Response(Stream body) : IRestResponse
    {
        public int HttpStatusCode => 200;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers
        {
            get;
        } = new Dictionary<string, IReadOnlyList<string>>
        {
            ["Content-Type"] = ["application/json"]
        };
        public Stream Body => body;
        public ValueTask DisposeAsync() => body.DisposeAsync();
    }

    private sealed class HeldCleanupStream(byte[] bytes, TaskCompletionSource entered, TaskCompletionSource release) : MemoryStream(bytes)
    {
        public override async ValueTask DisposeAsync()
        {
            entered.TrySetResult();
            await release.Task;
            await base.DisposeAsync();
        }
    }
}
