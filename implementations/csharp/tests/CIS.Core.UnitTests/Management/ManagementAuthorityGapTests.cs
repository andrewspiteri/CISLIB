using System.Collections.Concurrent;
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
public sealed class ManagementAuthorityGapTests
{
    private const string Utc = "2026-11-24T00:00:00.0000000Z";

    [TestMethod]
    [DataRow("expiry", false, false)]
    [DataRow("expiry", true, false)]
    [DataRow("clock", false, false)]
    [DataRow("clock", true, false)]
    [DataRow("clock", false, true)]
    [DataRow("expiry", false, true)]
    public async Task ControlRenewalRetainsPriorAuthorityGapsAndWaitsForTheAuthoritativeConfigurationRead(
        string cause, bool reportingClosed, bool failedPrerequisiteObserved)
    {
        var store = new CentralStore();
        string originalRelease = store.Current.Desired!.ReleaseId;
        ConfigurationOptions configured = Options(store);
        await using ConfigurationRuntime configuration = await ConfigurationRuntime.PrepareAsync(
            new ConfigurationObjectTestSupport.SelectedTypes(typeof(RefreshSettings)), new("CentralHost", store)
            {
                PollingInterval = TimeSpan.FromHours(1),
                SnapshotTarget = configured.SnapshotTarget,
                Enrollments = configured.Enrollments
            });
        var clock = new ManualTimeProvider(DateTimeOffset.Parse(Utc, System.Globalization.CultureInfo.InvariantCulture));
        var admission = new ManagedWorkAdmission(clock);
        admission.BindRegistration(Registration);
        ManagedControlAttempt bootstrap = admission.BeginAttempt(Registration, Guid.NewGuid().ToString("D"));
        Assert.IsTrue(admission.ValidateConfirmation(bootstrap, Confirmation(bootstrap.AttemptId, "bootstrap")));
        await using var bridge = new ManagementConfigurationReconciler(configuration, admission, Target());
        var client = new ScriptedManagementClient();
        QueueControl("initial");
        client.EnqueueResult(ScriptedManagementCallKind.Observation,
            Value(new ManagementReportReceipt(Registration, "observation", "1", "1", "accepted", Utc)));
        client.EnqueueResult(ScriptedManagementCallKind.InventoryReport,
            Value(new ManagementReportReceipt(Registration, "inventory", "1", "1", "accepted", Utc)));
        var observations = new ScriptedManagementObservationSource();
        observations.EnqueueValue(new("starting", "healthy"));
        var inventory = new ScriptedManagementInventorySource();
        inventory.EnqueueValue(new([], [], []));
        await using var heartbeat = new ManualHeartbeat();
        var prerequisite = new OtherPrerequisite();
        await using var reporter = new ManagementRegistration(admission, client, heartbeat, observations, inventory,
            new("management")
            {
                ReportingInterval = TimeSpan.FromHours(1),
                ProcessStartedAtUtc = clock.GetUtcNow(),
                ApplicationVersion = "1"
            }, [bridge, prerequisite], clock);
        reporter.AdoptBootstrap(new(admission, Registration, new(1, "reported", Utc, "1"),
            new(Registration, ConfigurationCentralTestSupport.Application, Utc), Authority));
        reporter.BindConfigurationGap(bridge.RequireGap);
        await reporter.StartAsync();
        Assert.IsTrue(admission.TryAdmit());
        long originalEpoch = configuration.CaptureCentralReadiness().RequiredEpoch;
        if (reportingClosed)
        {
            admission.InvalidateReporting();
            Assert.IsFalse(admission.TryAdmit());
            Assert.AreEqual(originalEpoch, configuration.CaptureCentralReadiness().RequiredEpoch);
        }
        var publications = new ConcurrentQueue<ManagedWorkAdmissionSnapshot>();
        admission.Changed += (_, change) => publications.Enqueue(change.Snapshot);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        store.Read = async (_, _) =>
        {
            Interlocked.Increment(ref reads);
            entered.TrySetResult();
            await release.Task;
            return Snapshot(2, application: "{\"Value\":2}");
        };
        QueueControl("after-pause");
        prerequisite.Ready = !failedPrerequisiteObserved;
        if (cause == "clock")
        {
            clock.SetUtcNow(clock.GetUtcNow().AddHours(1));
            clock.Advance(TimeSpan.FromSeconds(6));
        }
        else
        {
            clock.Advance(TimeSpan.FromSeconds(20));
        }
        Task<bool>? renewal = null;
        try
        {
            if (failedPrerequisiteObserved)
            {
                // This observation consumes the untrusted clock sample while another prerequisite also refuses.
                ManagedWorkAdmissionSnapshot observed = admission.CaptureSnapshot();
                Assert.IsFalse(observed.AdmissionAllowed);
                Assert.AreEqual(cause == "clock" ? "management.control.clock_gap" : "management.control.expired", observed.ReasonCode);
                prerequisite.Ready = true;
            }
            // The other rows intentionally make no admission/status/health observation after the clock change.
            renewal = reporter.OnElapsedAsync(CancellationToken.None);
            await entered.Task.WaitAsync(Deadline);
            Assert.IsFalse(await renewal.WaitAsync(Deadline));
            Assert.IsFalse(admission.TryAdmit(), "Successful control alone must not reopen work while the new authoritative read is held.");
            Assert.AreEqual(originalEpoch + 1, configuration.CaptureCentralReadiness().RequiredEpoch);
            Assert.AreEqual(originalRelease, configuration.CaptureStatus().Entries.Single().Acceptance!.LastAccepted!.Desired.ReleaseId);
            string reason = cause == "clock" ? "management.control.clock_gap" : "management.control.expired";
            ManagedWorkAdmissionSnapshot gap = publications.Single(snapshot => snapshot.ReasonCode == reason);
            Assert.IsFalse(gap.AdmissionAllowed);
            Assert.IsTrue(gap.Revision < publications.First(snapshot => snapshot.ReasonCode == "management.control.checking").Revision,
                "Publish the retained old-window gap before the new Reconciling observation.");

            QueueControl("still-waiting-one");
            clock.Advance(TimeSpan.FromSeconds(7));
            Assert.IsFalse(await reporter.OnElapsedAsync(CancellationToken.None));
            QueueControl("still-waiting-two");
            clock.Advance(TimeSpan.FromSeconds(20));
            Assert.IsFalse(await reporter.OnElapsedAsync(CancellationToken.None));
            Assert.AreEqual(originalEpoch + 1, configuration.CaptureCentralReadiness().RequiredEpoch,
                "The same expired confirmation cannot create another epoch while its read is incomplete.");
            Assert.HasCount(1, publications.Where(snapshot => snapshot.ReasonCode == reason).ToArray());
            Assert.AreEqual(1, reads);

            release.TrySetResult();
            Assert.IsTrue(await configuration.ReconcileCentralAsync().WaitAsync(Deadline));
            QueueControl("after-reconciliation");
            clock.Advance(TimeSpan.FromSeconds(30));
            Assert.IsTrue(await reporter.OnElapsedAsync(CancellationToken.None));
            Assert.IsTrue(admission.TryAdmit());
            Assert.AreEqual(Snapshot(2).Desired!.ReleaseId, configuration.CaptureStatus().Entries.Single().Acceptance!.LastAccepted!.Desired.ReleaseId);
            Assert.AreEqual(originalEpoch + 1, configuration.CaptureCentralReadiness().CompletedEpoch);
            Assert.AreEqual(1, reads, "The later ready control joins retained completion without another sweep.");

            QueueControl("a-later-window");
            clock.Advance(TimeSpan.FromSeconds(20));
            _ = await reporter.OnElapsedAsync(CancellationToken.None);
            Assert.IsTrue(await configuration.ReconcileCentralAsync().WaitAsync(Deadline));
            Assert.AreEqual(originalEpoch + 2, configuration.CaptureCentralReadiness().RequiredEpoch,
                "Completing a new ready confirmation must allow its own later expired window to create another epoch.");
            Assert.AreEqual(2, reads);
        }
        finally
        {
            release.TrySetResult();
            if (renewal is not null)
            {
                await renewal.WaitAsync(Deadline);
            }
        }

        void QueueControl(string revision)
        {
            client.EnqueueControl(call => Value(Confirmation(call.AttemptId!, revision)));
        }
    }

    private static ManagementControlConfirmation Confirmation(string attempt, string revision)
        => new(Authority, Registration, revision, true, 15000, attempt);

    private static ManagementCallResult<T> Value<T>(T value)
        => new(RestResult<T>.FromValue(value, new(200, hasValue: true)));

    private sealed class OtherPrerequisite : IManagedStateReconciler
    {
        internal bool Ready
        {
            get; set;
        } = true;

        public bool IsReady => Ready;

        public Task<bool> ReconcileAsync(ManagementControlConfirmation confirmation, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Ready);
        }
    }
}
