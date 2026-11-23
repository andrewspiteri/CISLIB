using CIS.CommonTesting;
using CIS.Heartbeat;
using CIS.Management;
using CIS.Management.Wire;
using CIS.RestClient;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ManagementRegistrationTests
{
    private const string ApplicationId = "6d2222a6-2ffd-4010-bf15-bd22a8b70f4b";
    private const string Time = "2026-11-23T10:00:00.0000000Z";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task StartupRegistersDirectlyThenUnchangedHeartbeatsReportOnlyTheLatestObservation()
    {
        await using var fixture = new Fixture();
        Assert.AreEqual(0, fixture.Script.TotalCalls);
        Assert.HasCount(0, fixture.Heartbeat.Registrations);
        fixture.InitialSuccess();
        await fixture.Runtime.StartAsync();

        Assert.AreEqual(3, fixture.Script.TotalCalls);
        Assert.HasCount(1, fixture.Heartbeat.Registrations);
        Assert.IsTrue(fixture.Runtime.CaptureStatus().IsConnected);
        Assert.AreEqual(TimeSpan.FromSeconds(5), fixture.Heartbeat.Registrations[0].Options.Interval);
        fixture.Observation.Value = new("ready", "healthy");
        fixture.Script.EnqueueResult(ScriptedManagementCallKind.Observation, Receipt(fixture.Id, "observation", "2"));
        fixture.Clock.Advance(TimeSpan.FromSeconds(31));
        ManualHeartbeatResult result = await fixture.Heartbeat.TriggerAsync(fixture.Runtime);

        Assert.AreEqual(ManualHeartbeatOutcome.Succeeded, result.Outcome);
        Assert.AreEqual(4, fixture.Script.TotalCalls);
        Assert.HasCount(1, fixture.Client.Inventories);
        Assert.AreEqual("1", fixture.Client.Inventories[0].Sequence);
        Assert.AreEqual("2", fixture.Client.Observations[1].Sequence);
        Assert.AreEqual("ready", fixture.Client.Observations[1].Lifecycle);
        Assert.IsFalse(fixture.Runtime.CaptureStatus().InventoryPending);
    }

    [TestMethod]
    public async Task AnOutageAfterBootstrapRetainsRegistrationAndDoesNotAccumulateMissedTicks()
    {
        await using var fixture = new Fixture();
        fixture.InitialSuccess();
        await fixture.Runtime.StartAsync();
        Assert.IsTrue(fixture.Runtime.CaptureStatus().IsRegistered);
        Assert.HasCount(1, fixture.Heartbeat.Registrations);
        ManagementRegistrationRequest original = fixture.Client.Registrations[0].Request;
        fixture.Script.EnqueueResult(ScriptedManagementCallKind.Observation, Failure<ManagementReportReceipt>());
        fixture.Clock.Advance(TimeSpan.FromSeconds(31));
        await fixture.Heartbeat.TriggerAsync(fixture.Runtime);
        Assert.IsFalse(fixture.Runtime.Admission.TryAdmit());
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        fixture.Script.EnqueueResult(ScriptedManagementCallKind.Observation, Receipt(fixture.Id, "observation", "3"));

        await fixture.Heartbeat.TriggerAsync(fixture.Runtime);

        Assert.AreEqual(5, fixture.Script.TotalCalls);
        Assert.HasCount(1, fixture.Client.Registrations);
        Assert.AreSame(original, fixture.Client.Registrations[0].Request);
        Assert.HasCount(3, fixture.Client.Observations);
        Assert.AreEqual("3", fixture.Client.Observations[2].Sequence);
        Assert.IsTrue(fixture.Runtime.CaptureStatus().IsConnected);
    }

    [TestMethod]
    public async Task RequiredInitialReceiptFailsDirectlyButLaterReportRefusalDoesNotRewriteConfirmedRegistration()
    {
        await using (var unavailable = new Fixture(requireInitial: true))
        {
            unavailable.Script.EnqueueResult(ScriptedManagementCallKind.Registration, Failure<ManagementRegistrationReceipt>());
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => unavailable.Runtime.StartAsync());
            Assert.AreEqual(1, unavailable.Script.TotalCalls);
            Assert.HasCount(0, unavailable.Heartbeat.Registrations);
        }
        await using var reported = new Fixture(requireInitial: true);
        reported.Script.EnqueueResult(ScriptedManagementCallKind.Registration, Registration(reported.Id));
        reported.Script.EnqueueResult(ScriptedManagementCallKind.Observation, Failure<ManagementReportReceipt>());

        await reported.Runtime.StartAsync();

        Assert.IsTrue(reported.Runtime.CaptureStatus().IsRegistered);
        Assert.IsFalse(reported.Runtime.CaptureStatus().IsConnected);
        Assert.HasCount(1, reported.Heartbeat.Registrations);
        Assert.AreEqual(2, reported.Script.TotalCalls);
    }

    [TestMethod]
    public async Task OneLatestReplacementAndAnIndependentUploadGatePreventCatalogFloodDuringReconnect()
    {
        await using var fixture = new Fixture();
        fixture.Script.EnqueueResult(ScriptedManagementCallKind.Registration, Registration(fixture.Id));
        fixture.Script.EnqueueResult(ScriptedManagementCallKind.Observation, Receipt(fixture.Id, "observation", "1"));
        fixture.Script.EnqueueResult(ScriptedManagementCallKind.InventoryReport, Failure<ManagementReportReceipt>());
        await fixture.Runtime.StartAsync();
        fixture.Inventory.Value = Catalog("intermediate");
        fixture.Inventory.Value = Catalog("latest");
        fixture.Clock.Advance(TimeSpan.FromSeconds(7));
        await fixture.Heartbeat.TriggerAsync(fixture.Runtime);
        Assert.HasCount(1, fixture.Client.Inventories, "Reconnect does not bypass the independent full-catalog interval.");
        Assert.IsTrue(fixture.Runtime.CaptureStatus().InventoryPending);
        fixture.Script.EnqueueResult(ScriptedManagementCallKind.Observation, Receipt(fixture.Id, "observation", "2"));
        fixture.Script.EnqueueResult(ScriptedManagementCallKind.InventoryReport, Receipt(fixture.Id, "inventory", "2"));
        fixture.Clock.Advance(TimeSpan.FromSeconds(31));
        await fixture.Heartbeat.TriggerAsync(fixture.Runtime);

        Assert.HasCount(2, fixture.Client.Inventories);
        Assert.AreEqual("latest", fixture.Client.Inventories[1].Modules[0].Version);
        Assert.AreEqual("2", fixture.Client.Inventories[1].Sequence);
        Assert.AreNotEqual(fixture.Client.Inventories[0].ObservedAtUtc, fixture.Client.Inventories[1].ObservedAtUtc);
        Assert.IsFalse(fixture.Runtime.CaptureStatus().InventoryPending);
    }

    [TestMethod]
    [DataRow("lost-response")]
    [DataRow("fault")]
    [DataRow("unrelated-cancellation")]
    public async Task AnUnconfirmedReplacementCannotBeForgottenWhenCurrentContentRevertsToTheLastConfirmedValue(string outcome)
    {
        var script = new ScriptedManagementClient();
        var observation = new ScriptedManagementObservationSource();
        var inventory = new ScriptedManagementInventorySource();
        foreach (string version in new[] { "a", "b", "a" })
        {
            observation.EnqueueValue(new("ready", "healthy"));
            inventory.EnqueueValue(Catalog(version));
        }
        await using var heartbeat = new ManualHeartbeat();
        var clock = new ManualTimeProvider(DateTimeOffset.Parse(Time, System.Globalization.CultureInfo.InvariantCulture));
        var client = new RecordingClient(script);
        await using var runtime = new ManagementRegistration(client, heartbeat, observation, inventory,
            new("management")
            {
                ProcessStartedAtUtc = clock.GetUtcNow(),
                ApplicationVersion = "1"
            }, clock);
        string id = runtime.CaptureStatus().RegistrationId;
        script.EnqueueResult(ScriptedManagementCallKind.Registration, Registration(id));
        script.EnqueueResult(ScriptedManagementCallKind.Observation, Receipt(id, "observation", "1"));
        script.EnqueueResult(ScriptedManagementCallKind.InventoryReport, Receipt(id, "inventory", "1"));
        await runtime.StartAsync();

        script.EnqueueResult(ScriptedManagementCallKind.Observation, Receipt(id, "observation", "2"));
        Exception? original = outcome == "fault" ? new InvalidOperationException("Original selected inventory fault.")
            : outcome == "unrelated-cancellation" ? new OperationCanceledException("Original unrelated cancellation fault.") : null;
        if (original is null)
        {
            script.EnqueueResult(ScriptedManagementCallKind.InventoryReport, new ManagementCallResult<ManagementReportReceipt>(
                RestResult<ManagementReportReceipt>.Failed("client.timeout", "The response was lost.",
                    new RestResultContext(failureKind: RestFailureKind.Timeout))));
        }
        else
        {
            script.EnqueueException(ScriptedManagementCallKind.InventoryReport, original);
        }
        clock.Advance(TimeSpan.FromSeconds(31));
        ManualHeartbeatResult unconfirmed = await heartbeat.TriggerAsync(runtime);
        Assert.AreEqual(ManualHeartbeatOutcome.Failed, unconfirmed.Outcome);
        if (original is not null)
        {
            Assert.AreSame(original, unconfirmed.Error);
        }
        Assert.IsTrue(runtime.CaptureStatus().InventoryPending);
        Assert.IsFalse(runtime.CaptureStatus().IsConnected);

        script.EnqueueResult(ScriptedManagementCallKind.Observation, Receipt(id, "observation", "3"));
        script.EnqueueResult(ScriptedManagementCallKind.InventoryReport, Receipt(id, "inventory", "3"));
        clock.Advance(TimeSpan.FromSeconds(31));
        ManualHeartbeatResult restored = await heartbeat.TriggerAsync(runtime);

        Assert.AreEqual(ManualHeartbeatOutcome.Succeeded, restored.Outcome);
        Assert.HasCount(3, client.Inventories);
        CollectionAssert.AreEqual(new[] { "a", "b", "a" }, client.Inventories.Select(value => value.Modules[0].Version).ToArray());
        CollectionAssert.AreEqual(new[] { "1", "2", "3" }, client.Inventories.Select(value => value.Sequence).ToArray());
        Assert.IsFalse(runtime.CaptureStatus().InventoryPending);
        Assert.IsTrue(runtime.CaptureStatus().IsConnected);
    }

    [TestMethod]
    public async Task InvalidLaterInventoryPreservesThePreviousCompletePendingValueAndRemainsVisible()
    {
        await using var fixture = new Fixture();
        fixture.Script.EnqueueResult(ScriptedManagementCallKind.Registration, Registration(fixture.Id));
        fixture.Script.EnqueueResult(ScriptedManagementCallKind.Observation, Receipt(fixture.Id, "observation", "1"));
        fixture.Script.EnqueueResult(ScriptedManagementCallKind.InventoryReport, Failure<ManagementReportReceipt>());
        await fixture.Runtime.StartAsync();
        ManagementInventorySnapshot original = fixture.Client.Inventories[0];
        fixture.Inventory.Value = new([], [], [new("scope", "job", "missing", "Job", ["continuous"])]);
        fixture.Script.EnqueueResult(ScriptedManagementCallKind.Observation, Receipt(fixture.Id, "observation", "2"));
        fixture.Script.EnqueueResult(ScriptedManagementCallKind.InventoryReport, Receipt(fixture.Id, "inventory", "1"));
        fixture.Clock.Advance(TimeSpan.FromSeconds(31));

        await fixture.Heartbeat.TriggerAsync(fixture.Runtime);

        Assert.AreSame(original, fixture.Client.Inventories[1]);
        ManagementRegistrationStatus status = fixture.Runtime.CaptureStatus();
        Assert.IsFalse(status.InventoryValid);
        Assert.IsFalse(status.IsConnected);
        Assert.AreEqual("management.inventory.local_invalid", status.LastErrorCode);
    }

    [TestMethod]
    public async Task InvalidLocalSelectionOrInitialContentFailsBeforeAnyNetworkOrPeriodicRegistration()
    {
        foreach (TimeSpan interval in new[] { TimeSpan.Zero, TimeSpan.FromTicks(1) })
        {
            await using var invalid = new Fixture(interval: interval);
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => invalid.Runtime.StartAsync());
            Assert.AreEqual(0, invalid.Script.TotalCalls);
            Assert.HasCount(0, invalid.Heartbeat.Registrations);
        }
        await using var content = new Fixture();
        content.Inventory.Value = new([], [], [new("scope", "job", "missing", "Job", ["continuous"])]);
        await Assert.ThrowsExactlyAsync<ManagementInventoryRejectedException>(() => content.Runtime.StartAsync());
        Assert.AreEqual(0, content.Script.TotalCalls);
        Assert.IsFalse(content.Runtime.CaptureStatus().InventoryValid);
    }

    [TestMethod]
    public async Task CompleteInventoryBytesIncludeTheAllocatedSequenceRatherThanItsFingerprintPlaceholder()
    {
        int bytes = ManagementWire.EncodeRequest(new ManagementInventorySnapshot("1", Time,
            [new("jobs", "a")], [], [])).Length;
        await using var fixture = new Fixture(wireLimits: new ManagementWireLimits(maximumInventoryRequestBytes: bytes));
        fixture.Inventory.Value = Catalog("a");
        fixture.InitialSuccess();
        await fixture.Runtime.StartAsync();
        for (int sequence = 2; sequence <= 10; sequence++)
        {
            fixture.Inventory.Value = Catalog(((char)('a' + sequence - 1)).ToString());
            string number = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
            fixture.Script.EnqueueResult(ScriptedManagementCallKind.Observation, Receipt(fixture.Id, "observation", number));
            if (sequence < 10)
            {
                fixture.Script.EnqueueResult(ScriptedManagementCallKind.InventoryReport, Receipt(fixture.Id, "inventory", number));
            }
            fixture.Clock.Advance(TimeSpan.FromSeconds(31));
            await fixture.Heartbeat.TriggerAsync(fixture.Runtime);
        }
        Assert.HasCount(9, fixture.Client.Inventories);
        Assert.AreEqual("9", fixture.Client.Inventories[^1].Sequence);
        Assert.IsFalse(fixture.Runtime.CaptureStatus().InventoryValid);
        Assert.AreEqual("management.inventory.local_invalid", fixture.Runtime.CaptureStatus().LastErrorCode);
    }

    [TestMethod]
    public async Task AConfirmedMissingRegistrationGetsAFreshExplicitIdentityButClosedAndConflictingOnesDoNot()
    {
        await using var missing = new Fixture();
        missing.InitialSuccess();
        await missing.Runtime.StartAsync();
        string original = missing.Id;
        missing.Script.EnqueueResult(ScriptedManagementCallKind.Observation, Failure<ManagementReportReceipt>(404, "management.not_found"));
        missing.Clock.Advance(TimeSpan.FromSeconds(31));
        await missing.Heartbeat.TriggerAsync(missing.Runtime);
        string replacement = missing.Id;
        Assert.AreNotEqual(original, replacement);
        Assert.IsFalse(missing.Runtime.CaptureStatus().IsRegistered);
        missing.Script.EnqueueResult(ScriptedManagementCallKind.Registration, Registration(replacement));
        missing.Script.EnqueueResult(ScriptedManagementCallKind.Observation, Receipt(replacement, "observation", "1"));
        missing.Script.EnqueueResult(ScriptedManagementCallKind.InventoryReport, Receipt(replacement, "inventory", "1"));
        missing.Clock.Advance(TimeSpan.FromSeconds(61));
        await missing.Heartbeat.TriggerAsync(missing.Runtime);
        Assert.AreEqual(replacement, missing.Client.Registrations[1].Id);

        await using var closed = new Fixture();
        closed.InitialSuccess();
        await closed.Runtime.StartAsync();
        string closedId = closed.Id;
        closed.Script.EnqueueResult(ScriptedManagementCallKind.Observation, Failure<ManagementReportReceipt>(409, "management.registration.closed"));
        closed.Clock.Advance(TimeSpan.FromSeconds(31));
        await closed.Heartbeat.TriggerAsync(closed.Runtime);
        closed.Clock.Advance(TimeSpan.FromDays(1));
        ManualHeartbeatResult skipped = await closed.Heartbeat.TriggerAsync(closed.Runtime);
        Assert.AreEqual(ManualHeartbeatOutcome.Skipped, skipped.Outcome);
        Assert.AreEqual(closedId, closed.Id);
        Assert.IsTrue(closed.Runtime.CaptureStatus().IsSuspended);
        Assert.HasCount(1, closed.Client.Registrations);
    }

    [TestMethod]
    public async Task DeadlineAndCanceledStopWaitCannotAbandonAnUncooperativeInitialCall()
    {
        await using var fixture = new Fixture(requireInitial: true);
        PendingManagementCall pending = fixture.Script.EnqueuePending(ScriptedManagementCallKind.Registration,
            Registration(fixture.Id), observeCancellation: false);
        Task startup = fixture.Runtime.StartAsync();
        await pending.Entered.WaitAsync(Deadline);
        try
        {
            fixture.Clock.Advance(TimeSpan.FromSeconds(31));
            Assert.IsFalse(startup.IsCompleted, "A deadline does not physically complete the selected provider.");
            using var wait = new CancellationTokenSource();
            Task stop = fixture.Runtime.StopAsync(wait.Token);
            wait.Cancel();
            await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => stop);
            Assert.IsFalse(startup.IsCompleted);
        }
        finally
        {
            pending.Release();
        }
        try
        {
            await startup.WaitAsync(Deadline);
            Assert.Fail("Stopping must cancel startup after the original attempt actually settles.");
        }
        catch (OperationCanceledException) when (startup.IsCanceled)
        {
        }
        await fixture.Runtime.StopAsync().WaitAsync(Deadline);
        Assert.HasCount(0, fixture.Heartbeat.Registrations);
    }

    [TestMethod]
    public async Task StopSignalsAcceptedReportingAndJoinsItsActualCleanupBeforeReturning()
    {
        await using var fixture = new Fixture();
        fixture.InitialSuccess();
        await fixture.Runtime.StartAsync();
        PendingManagementCall pending = fixture.Script.EnqueuePending(ScriptedManagementCallKind.Observation,
            Receipt(fixture.Id, "observation", "2"), observeCancellation: false);
        fixture.Clock.Advance(TimeSpan.FromSeconds(31));
        Task<ManualHeartbeatResult> callback = fixture.Heartbeat.TriggerAsync(fixture.Runtime);
        await pending.Entered.WaitAsync(Deadline);
        fixture.Runtime.BeginStopping();
        Task stop = fixture.Runtime.StopAsync();
        try
        {
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(callback.IsCompleted);
        }
        finally
        {
            pending.Release();
        }
        await callback.WaitAsync(Deadline);
        await stop.WaitAsync(Deadline);
        Assert.HasCount(0, fixture.Heartbeat.Registrations);
        Assert.IsTrue(fixture.Runtime.CaptureStatus().IsStopping);
        Assert.AreEqual(4, fixture.Script.TotalCalls, "Shutdown creates no mandatory final close request.");
    }

    [TestMethod]
    public async Task OriginalSnapshotProgrammingFaultsRemainFaultsAndDoNotConsumeTransportScripts()
    {
        await using var fixture = new Fixture();
        fixture.InitialSuccess();
        await fixture.Runtime.StartAsync();
        var original = new OperationCanceledException("Unrelated source fault.");
        fixture.Observation.Error = original;
        fixture.Clock.Advance(TimeSpan.FromSeconds(31));
        ManualHeartbeatResult callback = await fixture.Heartbeat.TriggerAsync(fixture.Runtime);
        Assert.AreEqual(ManualHeartbeatOutcome.Failed, callback.Outcome);
        Assert.AreSame(original, callback.Error);
        Assert.AreEqual(3, fixture.Script.TotalCalls);
        Assert.AreEqual("management.registration.fault", fixture.Runtime.CaptureStatus().LastErrorCode);
    }

    [TestMethod]
    public async Task TenMillisecondFlapsRetainEveryAttemptAndHealthObservationWithoutNormalLogFloods()
    {
        var log = new CountLogger();
        await using var fixture = new Fixture(interval: TimeSpan.FromMilliseconds(10), reconnect: TimeSpan.FromMilliseconds(10), logger: log);
        fixture.InitialSuccess();
        await fixture.Runtime.StartAsync();
        for (int index = 2; index <= 22; index++)
        {
            fixture.Script.EnqueueResult(ScriptedManagementCallKind.Observation, index % 2 == 0
                ? Failure<ManagementReportReceipt>() : Receipt(fixture.Id, "observation", index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(11));
            await fixture.Heartbeat.TriggerAsync(fixture.Runtime);
        }
        Assert.AreEqual(24, fixture.Script.TotalCalls);
        Assert.AreEqual(1, log.Count);
        Assert.IsTrue(fixture.Runtime.CaptureStatus().SuppressedDiagnostics >= 20);
        Assert.IsTrue(fixture.Runtime.CaptureStatus().FailedAttempts >= 10);
        Assert.IsTrue(fixture.Monitors.Monitors[0].Reports.Count >= 20);
        Assert.IsFalse(fixture.Monitors.Monitors[0].IsCritical);
    }

    private static ManagementInventoryContent Catalog(string version = "first") => new([new("jobs", version)], [], []);
    private static ManagementCallResult<ManagementRegistrationReceipt> Registration(string id) => Value(new ManagementRegistrationReceipt(id, ApplicationId, Time));
    private static ManagementCallResult<ManagementReportReceipt> Receipt(string id, string stream, string sequence)
        => Value(new ManagementReportReceipt(id, stream, sequence, sequence, "accepted", Time));
    private static ManagementCallResult<T> Value<T>(T value) => new(RestResult<T>.FromValue(value, new RestResultContext(200, hasValue: true)));
    private static ManagementCallResult<T> Failure<T>(int status = 503, string code = "management.unavailable")
        => new(RestResult<T>.Failed(code, "Expected refusal", new RestResultContext(status, failureKind: RestFailureKind.Http)),
            new("about:blank", "Expected refusal", status, code));

    private sealed class Fixture : IAsyncDisposable
    {
        internal Fixture(bool requireInitial = false, TimeSpan? interval = null, TimeSpan? reconnect = null, CountLogger? logger = null,
            ManagementWireLimits? wireLimits = null)
        {
            Client = new(Script);
            var options = new ManagementRegistrationOptions("management")
            {
                ProcessStartedAtUtc = DateTimeOffset.Parse(Time, System.Globalization.CultureInfo.InvariantCulture),
                ApplicationVersion = "1",
                ReportedInstanceId = "body-label",
                RequireInitialRegistration = requireInitial,
                ReportingInterval = interval ?? TimeSpan.FromSeconds(30),
                ControlCheckInterval = interval is { } selected && selected > TimeSpan.Zero && selected < TimeSpan.FromSeconds(5)
                    ? selected : TimeSpan.FromSeconds(5),
                InitialReconnectDelay = reconnect ?? TimeSpan.FromSeconds(5),
                MaximumReconnectDelay = reconnect ?? TimeSpan.FromSeconds(60),
                WireLimits = wireLimits ?? new()
            };
            Runtime = new(Client, Heartbeat, Observation, Inventory, options, Clock, Monitors, logger);
        }
        internal ScriptedManagementClient Script { get; } = new(128);
        internal RecordingClient Client
        {
            get;
        }
        internal ManualTimeProvider Clock { get; } = new(DateTimeOffset.Parse(Time, System.Globalization.CultureInfo.InvariantCulture));
        internal ManualHeartbeat Heartbeat { get; } = new();
        internal ObservationSource Observation { get; } = new();
        internal InventorySource Inventory { get; } = new();
        internal RecordingHealthMonitorFactory Monitors { get; } = new();
        internal ManagementRegistration Runtime
        {
            get;
        }
        internal string Id => Runtime.CaptureStatus().RegistrationId;
        internal void InitialSuccess()
        {
            Script.EnqueueResult(ScriptedManagementCallKind.Registration, Registration(Id));
            Script.EnqueueResult(ScriptedManagementCallKind.Observation, Receipt(Id, "observation", "1"));
            Script.EnqueueResult(ScriptedManagementCallKind.InventoryReport, Receipt(Id, "inventory", "1"));
        }
        public async ValueTask DisposeAsync()
        {
            await Runtime.StopAsync().WaitAsync(Deadline);
            await Heartbeat.DisposeAsync();
        }
    }

    private sealed class ObservationSource : IManagementObservationSource
    {
        internal ManagementObservationContent Value { get; set; } = new("starting", "healthy");
        internal Exception? Error
        {
            get; set;
        }
        public ManagementObservationContent CaptureObservation(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (Error is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(Error).Throw();
            }
            return Value;
        }
    }

    private sealed class InventorySource : IManagementInventorySource
    {
        internal ManagementInventoryContent Value { get; set; } = Catalog();
        public ManagementInventoryContent CaptureInventory(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Value;
        }
    }

    private sealed class CountLogger : ILogger<ManagementRegistration>
    {
        internal int Count
        {
            get; private set;
        }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Count++;
        }
    }

    private sealed class RecordingClient(IManagementClient inner) : IManagementClient
    {
        internal List<(string Id, ManagementRegistrationRequest Request)> Registrations { get; } = [];
        internal List<ManagementObservation> Observations { get; } = [];
        internal List<ManagementInventorySnapshot> Inventories { get; } = [];
        // These retained reporting stories use a controlled always-current control port.
        // Separate managed bootstrap tests exercise real control scripts and freshness.
        public Task<ManagementCallResult<ManagementControlConfirmation>> GetControlAsync(string id, string attemptId,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Value(new ManagementControlConfirmation(ApplicationId, id, "opaque-v1", true, 15000, attemptId)));
        }
        public Task<ManagementCallResult<ManagementRegistrationReceipt>> RegisterAsync(string id, ManagementRegistrationRequest request, ManagementCallOptions? options = null, CancellationToken token = default)
        {
            Registrations.Add((id, request));
            return inner.RegisterAsync(id, request, options, token);
        }
        public Task<ManagementCallResult<ManagementReportReceipt>> ReportObservationAsync(string id, ManagementObservation observation, ManagementCallOptions? options = null, CancellationToken token = default)
        {
            Observations.Add(observation);
            return inner.ReportObservationAsync(id, observation, options, token);
        }
        public Task<ManagementCallResult<ManagementReportReceipt>> ReportInventoryAsync(string id, ManagementInventorySnapshot inventory, ManagementCallOptions? options = null, CancellationToken token = default)
        {
            Inventories.Add(inventory);
            return inner.ReportInventoryAsync(id, inventory, options, token);
        }
        public Task<ManagementCallResult<ManagementCloseReceipt>> CloseRegistrationAsync(string id, ManagementCallOptions? options = null, CancellationToken token = default) => inner.CloseRegistrationAsync(id, options, token);
        public Task<ManagementCallResult<ManagementServiceInfo>> GetServiceInfoAsync(ManagementCallOptions? options = null, CancellationToken token = default) => inner.GetServiceInfoAsync(options, token);
        public Task<ManagementCallResult<ManagementPage<ManagementApplication>>> ListApplicationsAsync(ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken token = default) => inner.ListApplicationsAsync(page, options, token);
        public Task<ManagementCallResult<ManagementApplication>> GetApplicationAsync(string id, ManagementCallOptions? options = null, CancellationToken token = default) => inner.GetApplicationAsync(id, options, token);
        public Task<ManagementCallResult<ManagementPage<ManagementInstance>>> ListInstancesAsync(string id, ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken token = default) => inner.ListInstancesAsync(id, page, options, token);
        public Task<ManagementCallResult<ManagementPage<ManagementInventoryEntry>>> ListInventoryAsync(string id, ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken token = default) => inner.ListInventoryAsync(id, page, options, token);
        public Task<ManagementCallResult<ManagementCapabilities>> GetCapabilitiesAsync(string id, ManagementCallOptions? options = null, CancellationToken token = default) => inner.GetCapabilitiesAsync(id, options, token);
        public Task<ManagementCallResult<ManagementMetadataResult>> ReplaceMetadataAsync(string id, string key, string etag, ManagementMetadataCommand command, ManagementCallOptions? options = null, CancellationToken token = default) => inner.ReplaceMetadataAsync(id, key, etag, command, options, token);
        public Task<ManagementCallResult<ManagementMetadataResult>> GetMetadataOperationAsync(string id, string key, ManagementCallOptions? options = null, CancellationToken token = default) => inner.GetMetadataOperationAsync(id, key, options, token);
    }
}
