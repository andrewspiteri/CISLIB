using System.Globalization;
using CIS.CommonTesting;
using CIS.Management;
using CIS.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ManagedAdmissionTests
{
    private const string Authority = "33333333-3333-4333-8333-333333333333";
    private const string Other = "55555555-5555-4555-8555-555555555555";
    private const string Registration = "22222222-2222-4222-8222-222222222222";
    private const string Time = "2026-11-23T10:00:00.0000000Z";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public void FreshnessExpiresAtAdmissionWithoutAnyHeartbeatAndRetainedSnapshotsCannotAuthorizeWork()
    {
        var clock = Clock();
        var gate = new ManagedWorkAdmission(clock);
        Assert.IsFalse(gate.TryAdmit());
        gate.BindRegistration(Registration);
        Confirm(gate, Registration);
        ManagedWorkAdmissionSnapshot retained = gate.CaptureSnapshot();
        clock.Advance(TimeSpan.FromMilliseconds(14999));
        Assert.IsTrue(gate.TryAdmit());
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsFalse(gate.TryAdmit());
        Assert.AreEqual(ManagedWorkAdmissionState.ContinuingOnly, gate.CaptureSnapshot().State);
        Assert.AreEqual("management.control.expired", gate.CaptureSnapshot().ReasonCode);
        Assert.IsTrue(retained.AdmissionAllowed, "A retained observation stays unchanged but grants no later permission.");
        Assert.AreEqual("opaque-v1", gate.CaptureSnapshot().ControlRevision);
    }

    [TestMethod]
    public void OldAttemptsRegistrationsAndUnexpectedAuthoritiesCannotReopenCurrentOrStoppingState()
    {
        var gate = new ManagedWorkAdmission(Clock());
        gate.BindRegistration(Registration);
        ManagedControlAttempt old = gate.BeginAttempt(Registration, Guid.NewGuid().ToString("D"));
        gate.Invalidate();
        Confirm(gate, Registration, revision: "newer");
        Assert.IsFalse(gate.CompleteReconciliation(old, Confirmation(old)));
        Assert.AreEqual("newer", gate.CaptureSnapshot().ControlRevision);
        ManagedControlAttempt previousRegistration = gate.BeginAttempt(Registration, Guid.NewGuid().ToString("D"));
        gate.BindRegistration(Other);
        Assert.IsFalse(gate.CompleteReconciliation(previousRegistration, Confirmation(previousRegistration)));
        Confirm(gate, Other);
        ManagedControlAttempt changedAuthority = gate.BeginAttempt(Other, Guid.NewGuid().ToString("D"));
        Assert.IsFalse(gate.ValidateConfirmation(changedAuthority, Confirmation(changedAuthority) with
        {
            AuthorityId = Other
        }));
        Assert.AreEqual("management.control.authority_changed", gate.CaptureSnapshot().ReasonCode);
        Assert.AreEqual(Authority, gate.CaptureSnapshot().AuthorityId);
        Confirm(gate, Other, revision: "restored-original-authority");
        ManagedControlAttempt late = gate.BeginAttempt(Other, Guid.NewGuid().ToString("D"));
        gate.BeginStopping();
        Assert.IsFalse(gate.CompleteReconciliation(late, Confirmation(late)));
        Assert.IsFalse(gate.TryAdmit());
        Assert.AreEqual(ManagedWorkAdmissionState.Stopping, gate.CaptureSnapshot().State);
    }

    [TestMethod]
    public void UtcDiscontinuityExplicitResumeAndLostLocalPrerequisiteRequireAnotherLiveConfirmation()
    {
        var clock = Clock();
        var gate = new ManagedWorkAdmission(clock);
        bool ready = true;
        gate.BindPrerequisites(() => ready);
        gate.BindRegistration(Registration);
        Confirm(gate, Registration);
        clock.SetUtcNow(clock.GetUtcNow().AddHours(1));
        Assert.IsFalse(gate.TryAdmit());
        Assert.AreEqual("management.control.clock_gap", gate.CaptureSnapshot().ReasonCode);
        Confirm(gate, Registration);
        ready = false;
        Assert.IsFalse(gate.TryAdmit());
        ready = true;
        Assert.IsFalse(gate.TryAdmit(), "A recovered prerequisite does not itself perform authoritative reconciliation.");
        Confirm(gate, Registration);
        gate.Invalidate();
        Assert.IsFalse(gate.TryAdmit());
        Confirm(gate, Registration);
        Assert.IsTrue(gate.TryAdmit());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ANewProcessRequiresLiveBootstrapEvenWithReadyCachedStateAndTheLegacyOptionalFlag(bool legacyRequired)
    {
        var retained = new Reconciler();
        await using var fixture = new Fixture([retained], legacyRequired);
        fixture.Script.EnqueueResult(ScriptedManagementCallKind.Registration, Failed<ManagementRegistrationReceipt>());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Runtime.StartAsync());

        Assert.IsFalse(fixture.Gate.TryAdmit());
        Assert.AreEqual(ManagedWorkAdmissionState.AwaitingBootstrap, fixture.Gate.CaptureSnapshot().State);
        Assert.AreEqual("accepted-local-state", retained.AcceptedState);
        Assert.AreEqual(0, retained.Calls);
        Assert.HasCount(0, fixture.Heartbeat.Registrations);
    }

    [TestMethod]
    public async Task DirectBootstrapRunsSelectedReconcilersSequentiallyBeforeReadinessAndPeriodicReporting()
    {
        var order = new List<int>();
        TaskCompletionSource entered = Signal();
        TaskCompletionSource release = Signal();
        var first = new Reconciler(async _ =>
        {
            order.Add(1);
            entered.TrySetResult();
            await release.Task;
            return true;
        });
        var second = new Reconciler(_ =>
        {
            order.Add(2);
            return Task.FromResult(true);
        });
        await using var fixture = new Fixture([first, second]);
        fixture.InitialSuccess();
        Task startup = fixture.Runtime.StartAsync();
        try
        {
            await entered.Task.WaitAsync(Deadline);
            Assert.IsFalse(fixture.Gate.TryAdmit());
            Assert.AreEqual(ManagedWorkAdmissionState.Reconciling, fixture.Gate.CaptureSnapshot().State);
            Assert.AreEqual(2, fixture.Script.TotalCalls);
            Assert.HasCount(0, fixture.Heartbeat.Registrations);
            CollectionAssert.AreEqual(new[] { 1 }, order);
        }
        finally
        {
            release.TrySetResult();
        }
        await startup.WaitAsync(Deadline);
        CollectionAssert.AreEqual(new[] { 1, 2 }, order);
        Assert.IsTrue(fixture.Gate.TryAdmit());
        Assert.AreEqual(4, fixture.Script.TotalCalls);
        Assert.HasCount(1, fixture.Heartbeat.Registrations);
    }

    [TestMethod]
    public async Task ObservedOutageClosesOnlyNewAdmissionAndReconnectionMustReconcileBeforeOpening()
    {
        var retained = new Reconciler();
        await using var fixture = new Fixture([retained]);
        fixture.InitialSuccess();
        await fixture.Runtime.StartAsync();
        TaskCompletionSource admittedWork = Signal();
        fixture.Script.EnqueueResult(ScriptedManagementCallKind.Control, Failed<ManagementControlConfirmation>());
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await fixture.Heartbeat.TriggerAsync(fixture.Runtime);

        Assert.IsFalse(fixture.Gate.TryAdmit());
        Assert.AreEqual(ManagedWorkAdmissionState.ContinuingOnly, fixture.Gate.CaptureSnapshot().State);
        Assert.IsFalse(admittedWork.Task.IsCompleted, "The management gate owns no cancellation of admitted work.");
        Assert.AreEqual("accepted-local-state", retained.AcceptedState);
        Assert.IsFalse(fixture.Monitors.Monitors[0].IsCritical, "Admission readiness must not turn an outage into failed liveness.");
        retained.Accept = false;
        fixture.EnqueueControl();
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        fixture.EnqueueReport("2");
        await fixture.Heartbeat.TriggerAsync(fixture.Runtime);
        Assert.IsFalse(fixture.Gate.TryAdmit());
        retained.Accept = true;
        fixture.EnqueueControl();
        fixture.Clock.Advance(TimeSpan.FromSeconds(13));
        await fixture.Heartbeat.TriggerAsync(fixture.Runtime);
        Assert.IsTrue(fixture.Gate.TryAdmit());
        admittedWork.TrySetResult();
        await admittedWork.Task;
    }

    [TestMethod]
    public async Task FiveSecondControlChecksDoNotTurnIntoThirtySecondPresenceOrInventoryFloods()
    {
        await using var fixture = new Fixture();
        fixture.InitialSuccess();
        await fixture.Runtime.StartAsync();
        for (int second = 5; second <= 30; second += 5)
        {
            fixture.EnqueueControl();
            if (second == 30)
            {
                fixture.EnqueueReport("2");
            }
            fixture.Clock.Advance(TimeSpan.FromSeconds(5));
            await fixture.Heartbeat.TriggerAsync(fixture.Runtime);
        }
        Assert.AreEqual(11, fixture.Script.TotalCalls);
        Assert.AreEqual(7, fixture.Script.Calls.Count(call => call.Kind == ScriptedManagementCallKind.Control));
        Assert.AreEqual(2, fixture.Script.Calls.Count(call => call.Kind == ScriptedManagementCallKind.Observation));
        Assert.AreEqual(1, fixture.Script.Calls.Count(call => call.Kind == ScriptedManagementCallKind.InventoryReport));
        Assert.AreEqual(2, fixture.Observation.TotalCaptures);
        Assert.AreEqual(2, fixture.Inventory.TotalCaptures);
    }

    [TestMethod]
    public async Task ExplicitShortReportsKeepTheirOwnCadenceWithoutAcceleratingControlChecks()
    {
        await using var fixture = new Fixture(reportingInterval: TimeSpan.FromMilliseconds(30));
        fixture.InitialSuccess();
        await fixture.Runtime.StartAsync();
        Assert.AreEqual(TimeSpan.FromMilliseconds(30), fixture.Heartbeat.Registrations.Single().Options.Interval);
        for (int sequence = 2; sequence <= 4; sequence++)
        {
            fixture.EnqueueReport(sequence.ToString(CultureInfo.InvariantCulture));
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(30));
            ManualHeartbeatResult result = await fixture.Heartbeat.TriggerAsync(fixture.Runtime);
            Assert.AreEqual(ManualHeartbeatOutcome.Succeeded, result.Outcome);
        }
        Assert.AreEqual(1, fixture.Script.Calls.Count(call => call.Kind == ScriptedManagementCallKind.Control));
        Assert.AreEqual(4, fixture.Script.Calls.Count(call => call.Kind == ScriptedManagementCallKind.Observation));
        fixture.EnqueueControl();
        fixture.EnqueueReport("5");
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(4910));

        ManualHeartbeatResult due = await fixture.Heartbeat.TriggerAsync(fixture.Runtime);

        Assert.AreEqual(ManualHeartbeatOutcome.Succeeded, due.Outcome);
        Assert.AreEqual(2, fixture.Script.Calls.Count(call => call.Kind == ScriptedManagementCallKind.Control));
        Assert.AreEqual(5, fixture.Script.Calls.Count(call => call.Kind == ScriptedManagementCallKind.Observation));
        Assert.AreEqual(1, fixture.Script.Calls.Count(call => call.Kind == ScriptedManagementCallKind.InventoryReport));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeadlineOrStoppingCannotAbandonHeldControlAndItsLateSuccessCannotOpenAdmission(bool stopping)
    {
        await using var fixture = new Fixture();
        fixture.Script.EnqueueResult(ScriptedManagementCallKind.Registration, Value(new ManagementRegistrationReceipt(fixture.Id, Other, Time)));
        TaskCompletionSource entered = Signal();
        TaskCompletionSource release = Signal();
        CancellationToken actual = default;
        fixture.Client.Control = async (id, attempt, token) =>
        {
            actual = token;
            entered.TrySetResult();
            await release.Task;
            return Value(new ManagementControlConfirmation(Authority, id, "opaque-v1", true, 15000, attempt));
        };
        Task startup = fixture.Runtime.StartAsync();
        try
        {
            await entered.Task.WaitAsync(Deadline);
            if (stopping)
            {
                fixture.Runtime.BeginStopping();
            }
            else
            {
                fixture.Clock.Advance(TimeSpan.FromSeconds(6));
            }
            Assert.IsTrue(actual.IsCancellationRequested);
            Assert.IsFalse(startup.IsCompleted);
            Assert.IsFalse(fixture.Gate.TryAdmit());
        }
        finally
        {
            release.TrySetResult();
        }
        if (stopping)
        {
            try
            {
                await startup.WaitAsync(Deadline);
                Assert.Fail("Stopping must remain cancellation after actual control work settles.");
            }
            catch (OperationCanceledException) when (startup.IsCanceled)
            {
            }
            Assert.AreEqual(ManagedWorkAdmissionState.Stopping, fixture.Gate.CaptureSnapshot().State);
        }
        else
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => startup);
        }
        Assert.IsFalse(fixture.Gate.TryAdmit());
        Assert.HasCount(0, fixture.Heartbeat.Registrations);
    }

    [TestMethod]
    public async Task ConfirmationAgeIncludesReconciliationInsteadOfStartingANewWindowAtResponseArrival()
    {
        Fixture? selected = null;
        var reconciler = new Reconciler(_ =>
        {
            selected!.Clock.Advance(TimeSpan.FromMilliseconds(200));
            return Task.FromResult(true);
        });
        await using var fixture = new Fixture([reconciler]);
        selected = fixture;
        fixture.Script.EnqueueResult(ScriptedManagementCallKind.Registration, Value(new ManagementRegistrationReceipt(fixture.Id, Other, Time)));
        fixture.Script.EnqueueControl(call =>
        {
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(900));
            return Value(new ManagementControlConfirmation(Authority, call.RegistrationId!, "opaque-v1", true, 1000, call.AttemptId!));
        });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Runtime.StartAsync());

        Assert.IsFalse(fixture.Gate.TryAdmit());
        Assert.AreEqual("management.control.expired", fixture.Gate.CaptureSnapshot().ReasonCode);
        Assert.AreEqual("accepted-local-state", reconciler.AcceptedState);
        Assert.AreEqual(2, fixture.Script.TotalCalls);
    }

    private static ManualTimeProvider Clock() => new(DateTimeOffset.Parse(Time, CultureInfo.InvariantCulture));
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ManagementCallResult<T> Value<T>(T value) => new(RestResult<T>.FromValue(value, new RestResultContext(200, hasValue: true)));
    private static ManagementCallResult<T> Failed<T>() => new(RestResult<T>.Failed("http.unavailable", "Expected outage",
        new RestResultContext(503, failureKind: RestFailureKind.Http)), new("about:blank", "Unavailable", 503, "management.dependency.unavailable"));
    private static ManagementControlConfirmation Confirmation(ManagedControlAttempt attempt, string revision = "opaque-v1")
        => new(Authority, attempt.RegistrationId, revision, true, 15000, attempt.AttemptId);
    private static void Confirm(ManagedWorkAdmission gate, string registration, string revision = "opaque-v1")
    {
        ManagedControlAttempt attempt = gate.BeginAttempt(registration, Guid.NewGuid().ToString("D"));
        Assert.IsTrue(gate.CompleteReconciliation(attempt, Confirmation(attempt, revision)));
    }

    private sealed class Reconciler(Func<CancellationToken, Task<bool>>? reconcile = null) : IManagedStateReconciler
    {
        internal bool Accept { get; set; } = true;
        internal int Calls
        {
            get; private set;
        }
        internal string AcceptedState { get; } = "accepted-local-state";
        public bool IsReady => Accept;
        public Task<bool> ReconcileAsync(ManagementControlConfirmation confirmation, CancellationToken token = default)
        {
            Calls++;
            return reconcile?.Invoke(token) ?? Task.FromResult(Accept);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Fixture(IManagedStateReconciler[]? reconcilers = null, bool legacyRequired = false, TimeSpan? reportingInterval = null)
        {
            Client = new(Script);
            Gate = new(Clock);
            Runtime = new(Gate, Client, Heartbeat, Observation, Inventory, new("managed")
            {
                RequireInitialRegistration = legacyRequired,
                IsHealthCritical = true,
                ReportingInterval = reportingInterval ?? TimeSpan.FromSeconds(30),
                ProcessStartedAtUtc = Clock.GetUtcNow(),
                ApplicationVersion = "1"
            }, reconcilers, Clock, Monitors);
            Observation.EnqueueValue(new("starting", "healthy"));
            Inventory.EnqueueValue(new([], [], []));
        }
        internal ScriptedManagementClient Script { get; } = new(128);
        internal Client Client
        {
            get;
        }
        internal ScriptedManagementObservationSource Observation { get; } = new();
        internal ScriptedManagementInventorySource Inventory { get; } = new();
        internal RecordingHealthMonitorFactory Monitors { get; } = new();
        internal ManualHeartbeat Heartbeat { get; } = new();
        internal ManualTimeProvider Clock { get; } = ManagedAdmissionTests.Clock();
        internal ManagedWorkAdmission Gate
        {
            get;
        }
        internal ManagementRegistration Runtime
        {
            get;
        }
        internal string Id => Runtime.CaptureStatus().RegistrationId;
        internal void EnqueueControl() => Script.EnqueueControl(call => Value(new ManagementControlConfirmation(Authority,
            call.RegistrationId!, "opaque-v1", true, 15000, call.AttemptId!)));
        internal void InitialSuccess()
        {
            Script.EnqueueResult(ScriptedManagementCallKind.Registration, Value(new ManagementRegistrationReceipt(Id, Other, Time)));
            EnqueueControl();
            Script.EnqueueResult(ScriptedManagementCallKind.Observation, Value(new ManagementReportReceipt(Id, "observation", "1", "1", "accepted", Time)));
            Script.EnqueueResult(ScriptedManagementCallKind.InventoryReport, Value(new ManagementReportReceipt(Id, "inventory", "1", "1", "accepted", Time)));
        }
        internal void EnqueueReport(string sequence)
        {
            Observation.EnqueueValue(new("ready", "healthy"));
            Inventory.EnqueueValue(new([], [], []));
            Script.EnqueueResult(ScriptedManagementCallKind.Observation, Value(new ManagementReportReceipt(Id, "observation", sequence, sequence, "accepted", Time)));
        }
        public async ValueTask DisposeAsync()
        {
            await Runtime.StopAsync().WaitAsync(Deadline);
            await Heartbeat.DisposeAsync();
        }
    }

    private sealed class Client(IManagementClient inner) : IManagementClient
    {
        internal Func<string, string, CancellationToken, Task<ManagementCallResult<ManagementControlConfirmation>>>? Control
        {
            get; set;
        }
        public Task<ManagementCallResult<ManagementControlConfirmation>> GetControlAsync(string id, string attempt, CancellationToken token = default)
            => Control?.Invoke(id, attempt, token) ?? inner.GetControlAsync(id, attempt, token);
        public Task<ManagementCallResult<ManagementRegistrationReceipt>> RegisterAsync(string id, ManagementRegistrationRequest request, ManagementCallOptions? options = null, CancellationToken token = default) => inner.RegisterAsync(id, request, options, token);
        public Task<ManagementCallResult<ManagementReportReceipt>> ReportObservationAsync(string id, ManagementObservation value, ManagementCallOptions? options = null, CancellationToken token = default) => inner.ReportObservationAsync(id, value, options, token);
        public Task<ManagementCallResult<ManagementReportReceipt>> ReportInventoryAsync(string id, ManagementInventorySnapshot value, ManagementCallOptions? options = null, CancellationToken token = default) => inner.ReportInventoryAsync(id, value, options, token);
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
