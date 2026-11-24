using CIS.CommonTesting;
using CIS.Configuration;
using CIS.Management;
using CIS.Management.Hosting;
using CIS.RestClient;
using CIS.Tests.UnitTests.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.UnitTests.Configuration.ConfigurationCentralTestSupport;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ManagementBootstrapTests
{
    private const string Utc = "2026-11-24T00:00:00.0000000Z";

    [TestMethod]
    public async Task AdoptedSessionKeepsItsClosedGateAndOriginalFactsButRequiresFreshFinalControl()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse(Utc, System.Globalization.CultureInfo.InvariantCulture));
        var admission = new ManagedWorkAdmission(clock);
        admission.BindRegistration(Registration);
        ManagedControlAttempt original = admission.BeginAttempt(Registration, Guid.NewGuid().ToString("D"));
        Assert.IsTrue(admission.ValidateConfirmation(original, new(Authority, Registration, "bootstrap", true, 15000, original.AttemptId)));
        Assert.IsFalse(admission.TryAdmit(), "Bootstrap control identifies the target; only final reconciliation can grant work.");
        var request = new ManagementRegistrationRequest(1, "reported", Utc, "1");
        var receipt = new ManagementRegistrationReceipt(Registration, ConfigurationCentralTestSupport.Application, Utc);
        var client = new ScriptedManagementClient();
        client.EnqueueControl(call => Value(new ManagementControlConfirmation(Authority, call.RegistrationId!, "final", true, 15000, call.AttemptId!)));
        client.EnqueueResult(ScriptedManagementCallKind.Observation, Value(new ManagementReportReceipt(Registration, "observation", "1", "1", "accepted", Utc)));
        client.EnqueueResult(ScriptedManagementCallKind.InventoryReport, Value(new ManagementReportReceipt(Registration, "inventory", "1", "1", "accepted", Utc)));
        var observation = new ScriptedManagementObservationSource();
        observation.EnqueueValue(new("starting", "healthy"));
        var inventory = new ScriptedManagementInventorySource();
        inventory.EnqueueValue(new([], [], []));
        await using var heartbeat = new ManualHeartbeat();
        await using var runtime = new ManagementRegistration(admission, client, heartbeat, observation, inventory,
            new("management")
            {
                ProcessStartedAtUtc = clock.GetUtcNow(),
                ApplicationVersion = "1"
            }, timeProvider: clock);
        runtime.AdoptBootstrap(new(admission, Registration, request, receipt, Authority));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.IsFalse(admission.TryAdmit());
        await runtime.StartAsync();
        Assert.AreEqual(Registration, runtime.CaptureStatus().RegistrationId);
        Assert.AreEqual(ConfigurationCentralTestSupport.Application, runtime.CaptureStatus().ApplicationId);
        Assert.AreSame(admission, runtime.Admission);
        Assert.IsTrue(admission.TryAdmit());
        Assert.AreEqual("final", admission.CaptureSnapshot().ControlRevision);
        Assert.AreEqual(0, client.Calls.Count(call => call.Kind == ScriptedManagementCallKind.Registration));
        Assert.AreEqual(1, client.Calls.Count(call => call.Kind == ScriptedManagementCallKind.Control));
        Assert.AreNotEqual(original.AttemptId, client.Calls.Single(call => call.Kind == ScriptedManagementCallKind.Control).AttemptId);
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.AdoptBootstrap(new(admission, Registration, request, receipt, Authority)));
    }

    [TestMethod]
    public async Task RepeatedFailuresWithinOneConnectivityGapShareOneEpochAndHealthyControlsDoNotCreateAnother()
    {
        var store = new CentralStore();
        await using ConfigurationRuntime runtime = await ConfigurationRuntime.PrepareAsync(
            new ConfigurationObjectTestSupport.SelectedTypes(typeof(RefreshSettings)), Options(store));
        var admission = new ManagedWorkAdmission();
        await using var reconciler = new ManagementConfigurationReconciler(runtime, admission, Target());
        long initial = runtime.CaptureCentralReadiness().RequiredEpoch;
        reconciler.RequireGap();
        await runtime.ReconcileCentralAsync();
        int reads = store.SnapshotReads;
        reconciler.RequireGap();
        reconciler.RequireGap();
        Assert.AreEqual(initial + 1, runtime.CaptureCentralReadiness().RequiredEpoch);
        Assert.AreEqual(reads, store.SnapshotReads, "Polling completion alone does not manufacture a new connectivity gap.");
        var confirmation = new ManagementControlConfirmation(Authority, Registration, "fresh", true, 15000, Guid.NewGuid().ToString("D"));
        Assert.IsTrue(await reconciler.ReconcileAsync(confirmation));
        Assert.IsTrue(await reconciler.ReconcileAsync(confirmation));
        Assert.AreEqual(initial + 1, runtime.CaptureCentralReadiness().RequiredEpoch);
        Assert.AreEqual(reads, store.SnapshotReads);
        reconciler.RequireGap();
        await runtime.ReconcileCentralAsync();
        Assert.AreEqual(initial + 2, runtime.CaptureCentralReadiness().RequiredEpoch);
    }

    private static ManagementCallResult<T> Value<T>(T value)
        => new(RestResult<T>.FromValue(value, new(200, hasValue: true)));

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FirstFreshControlRetriesOneFailedOfflineSweepInTheSameEpochBeforeTheOneHourPoll(bool stillDraining)
    {
        var store = new CentralStore();
        ConfigurationOptions originalOptions = Options(store);
        var options = new ConfigurationOptions("CentralHost", store)
        {
            PollingInterval = TimeSpan.FromHours(1),
            SnapshotTarget = Target(),
            Enrollments = originalOptions.Enrollments
        };
        await using ConfigurationRuntime configuration = await ConfigurationRuntime.PrepareAsync(
            new ConfigurationObjectTestSupport.SelectedTypes(typeof(RefreshSettings)), options);
        var admission = new ManagedWorkAdmission();
        await using var bridge = new ManagementConfigurationReconciler(configuration, admission, Target());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        store.Read = async (_, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                entered.TrySetResult();
                if (stillDraining)
                {
                    await release.Task;
                }
                return ConfigurationSnapshotReadResult.Failure();
            }
            return Snapshot(2, application: "{\"Value\":2}");
        };
        bridge.RequireGap();
        Task<bool> offline = configuration.ReconcileCentralAsync();
        try
        {
            await entered.Task.WaitAsync(Deadline);
            if (!stillDraining)
            {
                Assert.IsFalse(await offline);
                Assert.AreSame(offline, configuration.ReconcileCentralAsync(), "Ordinary callbacks retain the completed failed result.");
            }
            long epoch = configuration.CaptureCentralReadiness().RequiredEpoch;
            var confirmation = new ManagementControlConfirmation(Authority, Registration, "recovered", true, 15000, Guid.NewGuid().ToString("D"));
            Assert.IsFalse(await bridge.ReconcileAsync(confirmation));
            release.TrySetResult();
            Assert.IsTrue(await configuration.ReconcileCentralAsync().WaitAsync(Deadline));
            Assert.AreEqual(epoch, configuration.CaptureCentralReadiness().RequiredEpoch);
            Assert.AreEqual(epoch, configuration.CaptureCentralReadiness().CompletedEpoch);
            Assert.AreEqual(2, attempts, "One fresh-control recovery gets exactly one authoritative retry, without waiting for polling.");
            Assert.IsTrue(await bridge.ReconcileAsync(confirmation));
            Assert.IsTrue(await bridge.ReconcileAsync(confirmation));
            Assert.AreEqual(2, attempts, "Subsequent healthy controls do not sweep again.");
        }
        finally
        {
            release.TrySetResult();
            _ = await offline.WaitAsync(Deadline);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FreshControlReportingRefusalsCloseAdmissionWithoutCreatingAConfigurationConnectivityEpoch(bool status)
    {
        var store = new CentralStore();
        ConfigurationOptions configurationOptions = Options(store);
        await using ConfigurationRuntime configuration = await ConfigurationRuntime.PrepareAsync(
            new ConfigurationObjectTestSupport.SelectedTypes(typeof(RefreshSettings)), configurationOptions);
        var clock = new ManualTimeProvider(DateTimeOffset.Parse(Utc, System.Globalization.CultureInfo.InvariantCulture));
        var admission = new ManagedWorkAdmission(clock);
        admission.BindRegistration(Registration);
        ManagedControlAttempt bootstrap = admission.BeginAttempt(Registration, Guid.NewGuid().ToString("D"));
        Assert.IsTrue(admission.ValidateConfirmation(bootstrap, new(Authority, Registration, "bootstrap", true, 15000, bootstrap.AttemptId)));
        await using var bridge = new ManagementConfigurationReconciler(configuration, admission, Target());
        var client = new ScriptedManagementClient();
        client.EnqueueControl(call => Value(new ManagementControlConfirmation(Authority, Registration, "fresh", true, 15000, call.AttemptId!)));
        client.EnqueueResult(ScriptedManagementCallKind.Observation, Value(new ManagementReportReceipt(Registration, "observation", "1", "1", "accepted", Utc)));
        client.EnqueueResult(ScriptedManagementCallKind.InventoryReport, Value(new ManagementReportReceipt(Registration, "inventory", "1", "1", "accepted", Utc)));
        client.EnqueueResult(ScriptedManagementCallKind.Observation, Value(new ManagementReportReceipt(Registration, "observation", "2", "2", "accepted", Utc)));
        var observations = new ScriptedManagementObservationSource();
        observations.EnqueueValue(new("starting", "healthy"));
        observations.EnqueueValue(new("ready", "healthy"));
        var inventory = new ScriptedManagementInventorySource();
        inventory.EnqueueValue(new([], [], []));
        if (status)
        {
            inventory.EnqueueValue(new([], [], []));
        }
        else
        {
            inventory.EnqueueException(new ManagementInventoryRejectedException("management.inventory.local_invalid"));
        }
        await using var heartbeat = new ManualHeartbeat();
        await using var reporter = new ManagementRegistration(admission, client, heartbeat, observations, inventory,
            new("management")
            {
                ReportingInterval = TimeSpan.FromMilliseconds(10),
                ProcessStartedAtUtc = clock.GetUtcNow(),
                ApplicationVersion = "1"
            },
            [bridge], clock);
        reporter.AdoptBootstrap(new(admission, Registration, new(1, "reported", Utc, "1"), new(Registration, ConfigurationCentralTestSupport.Application, Utc), Authority));
        reporter.BindConfigurationGap(bridge.RequireGap);
        var statusClient = new ScriptedManagementConfigurationClient();
        if (status)
        {
            statusClient.EnqueueResult(ScriptedManagementConfigurationCallKind.ReportStatus,
                new ManagementConfigurationCallResult<ManagementReportReceipt>(RestResult<ManagementReportReceipt>.FromValue(
                    new(Registration, "configuration-status", "1", "1", "accepted", Utc), new(200, hasValue: true))));
            statusClient.EnqueueResult(ScriptedManagementConfigurationCallKind.ReportStatus,
                new ManagementConfigurationCallResult<ManagementReportReceipt>(RestResult<ManagementReportReceipt>.Failed(
                    "http.400", "Refused status", new(400, failureKind: RestFailureKind.Http))));
            reporter.AttachConfigurationStatus(statusClient, configuration, configurationOptions.Enrollments);
        }
        await reporter.StartAsync();
        Assert.IsTrue(admission.TryAdmit());
        long epoch = configuration.CaptureCentralReadiness().RequiredEpoch;
        clock.Advance(TimeSpan.FromMilliseconds(50));
        _ = await heartbeat.TriggerAsync(reporter);
        Assert.IsFalse(admission.TryAdmit());
        Assert.AreEqual("management.reporting.unavailable", admission.CaptureSnapshot().ReasonCode);
        Assert.AreEqual(epoch, configuration.CaptureCentralReadiness().RequiredEpoch);
        Assert.IsTrue(configuration.CaptureCentralReadiness().IsReady);
        Assert.AreEqual(1, client.Calls.Count(call => call.Kind == ScriptedManagementCallKind.Control), "The fresh control has not expired.");
        if (status)
        {
            return;
        }

        QueueCapture();
        QueueControlFailure();
        clock.Advance(TimeSpan.FromSeconds(7));
        _ = await heartbeat.TriggerAsync(reporter);
        _ = await configuration.ReconcileCentralAsync();
        Assert.AreEqual(epoch + 1, configuration.CaptureCentralReadiness().RequiredEpoch);
        QueueCapture();
        QueueControlFailure();
        clock.Advance(TimeSpan.FromSeconds(20));
        _ = await heartbeat.TriggerAsync(reporter);
        _ = await configuration.ReconcileCentralAsync();
        Assert.AreEqual(epoch + 1, configuration.CaptureCentralReadiness().RequiredEpoch, "Repeated unavailable control remains one real gap.");

        QueueCapture();
        client.EnqueueControl(call => Value(new ManagementControlConfirmation(Authority, Registration, "recovered", true, 15000, call.AttemptId!)));
        client.EnqueueResult(ScriptedManagementCallKind.Observation,
            Value(new ManagementReportReceipt(Registration, "observation", "5", "5", "accepted", Utc)));
        clock.Advance(TimeSpan.FromSeconds(30));
        _ = await heartbeat.TriggerAsync(reporter);
        Assert.IsTrue(admission.TryAdmit());
        QueueCapture();
        QueueControlFailure();
        clock.Advance(TimeSpan.FromSeconds(7));
        _ = await heartbeat.TriggerAsync(reporter);
        _ = await configuration.ReconcileCentralAsync();
        Assert.AreEqual(epoch + 2, configuration.CaptureCentralReadiness().RequiredEpoch, "A later genuine control gap requires another epoch.");

        void QueueCapture()
        {
            observations.EnqueueValue(new("ready", "healthy"));
            inventory.EnqueueValue(new([], [], []));
        }

        void QueueControlFailure()
        {
            client.EnqueueControl(_ => new(RestResult<ManagementControlConfirmation>.Failed("http.503", "Control unavailable",
                new(503, failureKind: RestFailureKind.Http))));
        }
    }
}
