using CIS.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.UnitTests.Configuration.ConfigurationCentralTestSupport;

namespace CIS.Tests.UnitTests.Configuration;

[TestClass]
public sealed class ConfigurationCentralAcceptanceTests
{
    [TestMethod]
    public async Task PairedInitializationUnchangedAcceptanceAndExplicitRemovalKeepOneRootAndTheActualRevision()
    {
        var store = new CentralStore { Current = Snapshot(common: "{\"Value\":2}", application: "{\"Value\":10}") };
        ConfigurationCatalog catalog = Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        var retained = (RefreshSettings)entry.Instance;
        int events = 0;
        ConfigurationDesiredIdentity? publishedDesired = null;
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        retained.ConfigurationChanged += (_, _) =>
        {
            events++;
            publishedDesired = catalog.Reader.Read(retained, _ => entry.Acceptance!.LastAccepted!.Desired);
        };
        await coordinator.LoadAsync(CancellationToken.None);
        Assert.AreEqual(10, retained.Value);
        Assert.AreEqual(0L, Acceptance(coordinator).Revision);
        Assert.AreEqual(0, events);
        Assert.IsTrue(coordinator.CaptureCentralReadiness().IsReady);
        Assert.AreEqual(0, store.LegacyReads);

        store.Current = Snapshot(2, "{\"Value\":3}", "{\"Value\":10}");
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(ConfigurationAcceptanceOutcome.AppliedUnchanged, Acceptance(coordinator).Outcome);
        Assert.AreEqual("2", Acceptance(coordinator).LastAccepted!.Desired.AssignmentGeneration);
        Assert.AreEqual(0L, entry.Revision);
        Assert.AreEqual(0, events);

        store.Current = Snapshot(3, common: "{\"Value\":3}");
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreSame(retained, entry.Instance);
        Assert.AreEqual(3, retained.Value);
        Assert.AreEqual(1L, entry.Revision);
        Assert.AreEqual(1, events);
        Assert.AreEqual(store.Current.Desired, publishedDesired);
        Assert.IsNull(Acceptance(coordinator).LastAccepted!.ApplicationSource);

        store.Current = Snapshot(4);
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(1, retained.Value, "Only a complete explicit defaults release selects captured defaults.");
        Assert.AreEqual(2L, entry.Revision);
        Assert.AreEqual(2, events);
    }

    [TestMethod]
    public async Task AValidApplicationOverrideCannotMaskAnInvalidKnownCommonSource()
    {
        var store = new CentralStore { Current = Snapshot(application: "{\"Value\":8}") };
        ConfigurationCatalog catalog = Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        ConfigurationSnapshotProvenance? accepted = Acceptance(coordinator).LastAccepted;
        store.Current = Snapshot(2, "{\"Value\":\"invalid\"}", "{\"Value\":99}");
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        ConfigurationAcceptanceStatus rejected = Acceptance(coordinator);
        Assert.AreEqual(ConfigurationAcceptanceOutcome.Rejected, rejected.Outcome);
        Assert.AreEqual(store.Current.Desired, rejected.Desired);
        Assert.AreSame(accepted, rejected.LastAccepted);
        Assert.IsTrue(rejected.HasIntactAcceptedState);
        Assert.AreEqual(8, ((RefreshSettings)entry.Instance).Value);
        Assert.AreEqual(0L, entry.Revision);
        Assert.IsFalse(await coordinator.ReconcileAsync(entry), "An unchanged rejected pair must be validated again.");
        Assert.IsTrue(coordinator.CaptureCentralReadiness().IsReady);
    }

    [TestMethod]
    public async Task RestartPolicyLoadsLiveInitiallyStagesOnlyRuntimeChangesAndClearsPendingOnActiveValues()
    {
        var store = new CentralStore { Current = Snapshot(application: "{\"Value\":5}") };
        ConfigurationCatalog catalog = Catalog(store, ConfigurationUpdateMode.RestartRequired);
        ConfigurationEntry entry = catalog.Entries.Single();
        int events = 0;
        entry.Instance.ConfigurationChanged += (_, _) => events++;
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        Assert.AreEqual(5, ((RefreshSettings)entry.Instance).Value);
        ConfigurationSnapshotProvenance? original = Acceptance(coordinator).LastAccepted;
        store.Current = Snapshot(2, application: "{\"Value\":7}");
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(ConfigurationAcceptanceOutcome.PendingRestart, Acceptance(coordinator).Outcome);
        Assert.AreSame(original, Acceptance(coordinator).LastAccepted);
        Assert.AreEqual(5, ((RefreshSettings)entry.Instance).Value);
        Assert.AreEqual(0L, entry.Revision);
        Assert.AreEqual(0, events);
        Assert.IsTrue(coordinator.CaptureCentralReadiness().IsReady);

        store.Current = Snapshot(3, application: "{\"Value\":5}");
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(ConfigurationAcceptanceOutcome.AppliedUnchanged, Acceptance(coordinator).Outcome);
        Assert.AreEqual("3", Acceptance(coordinator).LastAccepted!.Desired.AssignmentGeneration);
        Assert.AreEqual(0L, entry.Revision);
        Assert.AreEqual(0, events);
    }

    [TestMethod]
    public async Task PollFailurePreservesAcceptedReadinessButCannotCompleteANewRequiredGapOrInventDesiredFacts()
    {
        var store = new CentralStore { Current = Snapshot(application: "{\"Value\":4}") };
        ConfigurationCatalog catalog = Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        ConfigurationAcceptanceStatus accepted = Acceptance(coordinator);
        store.Current = ConfigurationSnapshotReadResult.Failure();
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        Assert.AreSame(accepted, Acceptance(coordinator));
        Assert.IsTrue(coordinator.CaptureCentralReadiness().IsReady);

        long gap = coordinator.RequireCentralReconciliation();
        Assert.IsFalse(coordinator.CaptureCentralReadiness().IsReady);
        Task<bool> first = coordinator.ReconcileCentralAsync();
        Assert.IsFalse(await first);
        int reads = store.SnapshotReads;
        Assert.AreSame(first, coordinator.ReconcileCentralAsync());
        Assert.AreEqual(gap, coordinator.RequireCentralReconciliation(), "An incomplete gap is coalesced, never rearmed by short control callbacks.");
        Assert.AreEqual(reads, store.SnapshotReads);

        store.Current = ConfigurationSnapshotReadResult.Failure("Configuration.SourceDigest", Desired(2));
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(ConfigurationAcceptanceOutcome.Rejected, Acceptance(coordinator).Outcome);
        Assert.AreEqual(accepted.LastAccepted, Acceptance(coordinator).LastAccepted);
        Assert.IsTrue(coordinator.CaptureCentralReadiness().IsReady, "An authoritative identified rejection can complete the gap with intact accepted roots.");
        Assert.AreEqual(gap, coordinator.CaptureCentralReadiness().CompletedEpoch);
    }

    [TestMethod]
    public async Task SubscriberFailureCannotUndoAcceptanceAndOnlyALaterChangedPublicationClearsItsDiagnostic()
    {
        var store = new CentralStore();
        ConfigurationCatalog catalog = Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        EventHandler<ConfigurationChangedEventArgs> failing = (_, _) => throw new InvalidOperationException("PRIVATE handler");
        int delivered = 0;
        entry.Instance.ConfigurationChanged += failing;
        entry.Instance.ConfigurationChanged += (_, _) => delivered++;
        store.Current = Snapshot(2, application: "{\"Value\":2}");
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(1, delivered);
        Assert.AreEqual("Configuration.Notification", Acceptance(coordinator).NotificationFailureCode);
        Assert.IsNull(Acceptance(coordinator).FailureCode);
        Assert.AreEqual(1L, entry.Revision);
        entry.Instance.ConfigurationChanged -= failing;
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual("Configuration.Notification", Acceptance(coordinator).NotificationFailureCode);
        store.Current = Snapshot(3, application: "{\"Value\":3}");
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.IsNull(Acceptance(coordinator).NotificationFailureCode);
        Assert.AreEqual(2, delivered);
    }

    [TestMethod]
    public async Task OrdinaryPollRecoveryReplacesACompletedFailedGapResultWithoutAnAdditionalControlSweep()
    {
        var store = new CentralStore();
        ConfigurationCatalog catalog = Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        store.Current = ConfigurationSnapshotReadResult.Failure();
        long gap = coordinator.RequireCentralReconciliation();
        Task<bool> failed = coordinator.ReconcileCentralAsync();
        Assert.IsFalse(await failed);
        Assert.AreSame(failed, coordinator.ReconcileCentralAsync());
        store.Current = Snapshot(2, application: "{\"Value\":2}");
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(gap, coordinator.CaptureCentralReadiness().CompletedEpoch);
        int reads = store.SnapshotReads;
        Assert.IsTrue(await coordinator.ReconcileCentralAsync());
        Assert.AreEqual(reads, store.SnapshotReads, "A short control callback observes retained recovery rather than starting another sweep.");
    }

    [TestMethod]
    public async Task BrokenLiveSetterMakesLastAcceptanceHistoricalAndClosesReadinessWithoutClaimingRollback()
    {
        var store = new CentralStore { Current = Snapshot(application: "{\"Alpha\":10,\"Beta\":20}") };
        ConfigurationCatalog catalog = Catalog(store, selected: typeof(W2ObservedSettings));
        ConfigurationEntry entry = catalog.Entries.Single();
        var retained = (W2ObservedSettings)entry.Instance;
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        ConfigurationSnapshotProvenance? accepted = Acceptance(coordinator).LastAccepted;
        string? checksum = entry.AcceptedChecksum;
        retained.RejectBetaAssignment = true;
        store.Current = Snapshot(2, application: "{\"Alpha\":30,\"Beta\":40}");
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        ConfigurationAcceptanceStatus faulted = Acceptance(coordinator);
        Assert.AreEqual(ConfigurationAcceptanceOutcome.Faulted, faulted.Outcome);
        Assert.IsFalse(faulted.HasIntactAcceptedState);
        Assert.AreSame(accepted, faulted.LastAccepted);
        Assert.AreEqual(checksum, entry.AcceptedChecksum);
        Assert.AreEqual(0L, entry.Revision);
        Assert.AreEqual((30, 20), (retained.Alpha, retained.Beta));
        Assert.IsFalse(coordinator.CaptureCentralReadiness().IsReady);
        int reads = store.SnapshotReads;
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(reads, store.SnapshotReads, "A terminal entry cannot continue reading or assigning.");
    }

    [TestMethod]
    public async Task InitialUnchangedOrIdentifiedFailureCannotDeliverAnUnacceptedRoot()
    {
        foreach (ConfigurationSnapshotReadResult response in new[]
        {
            ConfigurationSnapshotReadResult.Unchanged(),
            ConfigurationSnapshotReadResult.Failure("Configuration.SourceDigest", Desired())
        })
        {
            var store = new CentralStore { Current = response };
            ConfigurationCatalog catalog = Catalog(store);
            await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
            Exception failure = await ConfigurationRefreshTestSupport.FailureAsync(coordinator.LoadAsync(CancellationToken.None));
            Assert.IsInstanceOfType<CoreConfigurationException>(failure);
            Assert.IsNull(Acceptance(coordinator).LastAccepted);
            Assert.IsFalse(Acceptance(coordinator).HasIntactAcceptedState);
            Assert.IsFalse(coordinator.CaptureCentralReadiness().IsReady);
        }
    }

    [TestMethod]
    public async Task MixedCatalogKeepsIndependentNoncentralReadsAndOnlyTheirLegacyWatchKeys()
    {
        var store = new CentralStore { NotificationMode = ConfigurationNotificationMode.RequiresReconciliation };
        ConfigurationCatalog catalog = ConfigurationCatalog.Create([typeof(RefreshSettings), typeof(OtherRefreshSettings)], Options(store));
        ConfigurationEntry ordinary = catalog.Entries.Single(entry => entry.Type == typeof(OtherRefreshSettings));
        store.Legacy[new("CentralHost", ordinary.Identity)] = ConfigurationRefreshTestSupport.Record(12, "legacy");
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        await coordinator.ActivateAsync(CancellationToken.None);
        Assert.AreEqual(12, ((OtherRefreshSettings)ordinary.Instance).Value);
        IReadOnlyList<ConfigurationRecordKey> watched = store.WatchedKeys ?? throw new AssertFailedException("The mixed store must acquire the legacy watch.");
        Assert.AreEqual(2, watched.Count);
        Assert.IsTrue(watched.All(key => key.Definition == ordinary.Identity));
        Assert.IsTrue(store.LegacyReads >= 4);
        Assert.IsNull(coordinator.CaptureStatus().Entries.Single(status => status.ConfigurationType == ordinary.Type).Acceptance);
        await coordinator.StopAsync();
        Assert.AreEqual(1, store.SubscriptionDisposals);
        Assert.AreEqual(0, store.StoreDisposals);
    }

    [TestMethod]
    public async Task CoordinatedReaderSeesValuesRevisionAndProvenanceFromOneActualCommit()
    {
        var store = new CentralStore();
        ConfigurationCatalog catalog = Catalog(store, selected: typeof(W2ObservedSettings));
        ConfigurationEntry entry = catalog.Entries.Single();
        var retained = (W2ObservedSettings)entry.Instance;
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        using var assigned = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var readerEntered = ConfigurationRefreshTestSupport.Signal();
        var readerFinished = ConfigurationRefreshTestSupport.Signal();
        Exception? writerFailure = null;
        Exception? readerFailure = null;
        (int Alpha, int Beta, long Revision, string? Generation) observed = default;
        retained.AfterAlphaAssignment = () =>
        {
            assigned.Set();
            if (!release.Wait(Deadline))
            {
                throw new TimeoutException("Release the controlled live setter.");
            }
        };
        store.Current = Snapshot(2, application: "{\"Alpha\":10,\"Beta\":20}");
        var writer = new Thread(() =>
        {
            try
            {
                coordinator.ReconcileAsync(entry).GetAwaiter().GetResult();
            }
            catch (Exception failure) { writerFailure = failure; }
        })
        {
            IsBackground = true
        };
        var reader = new Thread(() =>
        {
            readerEntered.TrySetResult();
            try
            {
                observed = catalog.Reader.Read(retained, current => (current.Alpha, current.Beta,
                    entry.Revision, entry.Acceptance!.LastAccepted!.Desired.AssignmentGeneration));
            }
            catch (Exception failure) { readerFailure = failure; }
            finally { readerFinished.TrySetResult(); }
        })
        {
            IsBackground = true
        };
        bool writerStarted = false;
        bool readerStarted = false;
        try
        {
            writer.Start();
            writerStarted = true;
            Assert.IsTrue(assigned.Wait(Deadline));
            reader.Start();
            readerStarted = true;
            await readerEntered.Task.WaitAsync(Deadline);
            Assert.IsTrue(SpinWait.SpinUntil(() => readerFinished.Task.IsCompleted
                || (reader.ThreadState & ThreadState.WaitSleepJoin) != 0, Deadline));
            Assert.IsFalse(readerFinished.Task.IsCompleted);
            release.Set();
            await readerFinished.Task.WaitAsync(Deadline);
            Assert.IsTrue(writer.Join(Deadline));
            Assert.IsNull(writerFailure);
            Assert.IsNull(readerFailure);
            Assert.AreEqual((10, 20, 1L, "2"), observed);
        }
        finally
        {
            release.Set();
            if (writerStarted)
            {
                Assert.IsTrue(writer.Join(Deadline));
            }
            if (readerStarted)
            {
                Assert.IsTrue(reader.Join(Deadline));
            }
            retained.AfterAlphaAssignment = null;
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationOrSessionSupersessionWinningTheActualValueGateRefusesPreparedValues(bool sessionWins)
    {
        var store = new CentralStore();
        ConfigurationCatalog catalog = Catalog(store, selected: typeof(W2ObservedSettings));
        ConfigurationEntry entry = catalog.Entries.Single();
        var pipeline = new ConfigurationValuePipeline();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, pipeline);
        await coordinator.LoadAsync(CancellationToken.None);
        ConfigurationSnapshotReadResult next = Snapshot(2, application: "{\"Alpha\":10,\"Beta\":20}");
        ConfigurationPreparedValues prepared = pipeline.Prepare(entry, next.Common!.Payload, next.Application!.Payload);
        using IDisposable? ownership = prepared.Ownership;
        using var cancellation = new CancellationTokenSource();
        var attempted = ConfigurationRefreshTestSupport.Signal();
        var finished = ConfigurationRefreshTestSupport.Signal();
        Exception? failure = null;
        ConfigurationAcceptanceStatus accepted = Acceptance(coordinator);
        var writer = new Thread(() =>
        {
            attempted.TrySetResult();
            try
            {
                pipeline.ApplySnapshot(entry, prepared, next.Provenance!, 1, cancellation.Token, initial: false);
            }
            catch (Exception error) { failure = error; }
            finally { finished.TrySetResult(); }
        })
        {
            IsBackground = true
        };
        bool started = false;
        try
        {
            lock (entry.ValueGate)
            {
                entry.ObservedDesired = next.Desired;
                entry.ObservedProvenance = next.Provenance;
                writer.Start();
                started = true;
                Assert.IsTrue(attempted.Task.Wait(Deadline));
                Assert.IsTrue(SpinWait.SpinUntil(() => finished.Task.IsCompleted
                    || (writer.ThreadState & ThreadState.WaitSleepJoin) != 0, Deadline));
                Assert.IsFalse(finished.Task.IsCompleted);
                if (sessionWins)
                {
                    entry.SessionEpoch++;
                }
                else
                {
                    cancellation.Cancel();
                }
            }
            await finished.Task.WaitAsync(Deadline);
            Assert.IsNotNull(failure);
            Assert.AreEqual((1, 2), (((W2ObservedSettings)entry.Instance).Alpha, ((W2ObservedSettings)entry.Instance).Beta));
            Assert.AreSame(accepted, Acceptance(coordinator));
            Assert.AreEqual(0L, entry.Revision);
        }
        finally
        {
            if (started)
            {
                Assert.IsTrue(writer.Join(Deadline));
            }
        }
    }
}
