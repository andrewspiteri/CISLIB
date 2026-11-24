using CIS.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.UnitTests.Configuration.ConfigurationCentralTestSupport;

namespace CIS.Tests.UnitTests.Configuration;

[TestClass]
public sealed class ConfigurationCentralSessionTests
{
    [TestMethod]
    public async Task HealthyReadinessUsesItsRetainedAggregateWhileARootGateIsHeldElsewhere()
    {
        var store = new CentralStore();
        ConfigurationCatalog catalog = Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        var held = ConfigurationRefreshTestSupport.Signal();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            lock (entry.ValueGate)
            {
                held.TrySetResult();
                release.Wait();
            }
        })
        {
            IsBackground = true
        };
        holder.Start();
        try
        {
            await held.Task.WaitAsync(Deadline);
            int reads = store.SnapshotReads;
            ConfigurationCentralReadiness status = await Task.Run(coordinator.CaptureCentralReadiness).WaitAsync(Deadline);
            Assert.IsTrue(status.IsReady);
            Assert.AreEqual(status.RequiredEpoch, status.CompletedEpoch);
            Assert.AreEqual(reads, store.SnapshotReads);
        }
        finally
        {
            release.Set();
            Assert.IsTrue(holder.Join(Deadline));
        }
    }

    [TestMethod]
    public async Task ShortReconciliationKickReturnsWhileSynchronousProviderWorkIsHeldAndStopStillJoinsItsOwner()
    {
        var store = new CentralStore();
        ConfigurationCatalog catalog = Catalog(store);
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        var entered = ConfigurationRefreshTestSupport.Signal();
        var returned = new TaskCompletionSource<Task<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        store.Read = (_, _) =>
        {
            entered.TrySetResult();
            release.Wait();
            return ValueTask.FromResult(Snapshot(2, application: "{\"Value\":2}"));
        };
        coordinator.RequireCentralReconciliation();
        var caller = new Thread(() => returned.TrySetResult(coordinator.ReconcileCentralAsync())) { IsBackground = true };
        caller.Start();
        Task? stop = null;
        Task<bool>? expectedOwner = null;
        try
        {
            await entered.Task.WaitAsync(Deadline);
            expectedOwner = await returned.Task.WaitAsync(Deadline);
            Assert.IsFalse(expectedOwner.IsCompleted, "The short trigger returns the actual retained owner while synchronous provider work remains held.");
            Assert.AreSame(expectedOwner, coordinator.ReconcileCentralAsync());
            stop = coordinator.StopAsync();
            Assert.IsFalse(stop.IsCompleted, "Queue dispatch cannot replace actual provider settlement with a completed drain.");
        }
        finally
        {
            release.Set();
            Assert.IsTrue(caller.Join(Deadline));
            if (expectedOwner is not null)
            {
                Assert.IsFalse(await expectedOwner.WaitAsync(Deadline));
            }
            if (stop is not null)
            {
                await stop.WaitAsync(Deadline);
            }
        }
    }

    [TestMethod]
    public async Task ActualLiveSetterFaultClosesRetainedAdmissionBeforeAnyCoordinatorObservation()
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
        entry.ObservedDesired = next.Desired;
        entry.ObservedProvenance = next.Provenance;
        ((W2ObservedSettings)entry.Instance).RejectBetaAssignment = true;
        Assert.IsTrue(coordinator.CaptureCentralReadiness().IsReady);
        Assert.ThrowsExactly<CoreConfigurationException>(() => pipeline.ApplySnapshot(entry, prepared, next.Provenance!, 1,
            CancellationToken.None, initial: false));
        Assert.IsFalse(coordinator.CaptureCentralReadiness().IsReady,
            "The actual value-gate fault closes the shared atomic admission flag before a refresh observer could update status.");
        Assert.IsTrue(entry.Disabled);
    }

    [TestMethod]
    public async Task LowerAndEqualConflictingDecisionsCannotRegressDesiredOrAcceptedFacts()
    {
        var store = new CentralStore { Current = Snapshot(3, application: "{\"Value\":3}") };
        store.Read = (_, _) => ValueTask.FromResult(store.Current);
        ConfigurationCatalog catalog = Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        ConfigurationAcceptanceStatus accepted = Acceptance(coordinator);
        store.Current = Snapshot(2, application: "{\"Value\":2}");
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        Assert.AreSame(accepted, Acceptance(coordinator));
        Assert.AreEqual("Configuration.StaleGeneration", coordinator.CaptureStatus().Entries.Single().FailureCode);
        store.Current = Snapshot(3, application: "{\"Value\":99}", release: 99);
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        Assert.AreSame(accepted, Acceptance(coordinator));
        Assert.AreEqual("Configuration.EqualGenerationConflict", coordinator.CaptureStatus().Entries.Single().FailureCode);
        store.Current = Snapshot(3, application: "{\"Value\":8}");
        Assert.IsFalse(await coordinator.ReconcileAsync(entry), "Equal target/release with conflicting publication references must also refuse.");
        Assert.AreEqual(3, ((RefreshSettings)entry.Instance).Value);
        Assert.AreEqual(0L, entry.Revision);
    }

    [TestMethod]
    public async Task FailedIdentifiedHigherDecisionAllowsItsLaterCompleteContentWithoutKeepingAnOlderFingerprint()
    {
        var store = new CentralStore { Current = Snapshot(application: "{\"Value\":1}") };
        ConfigurationCatalog catalog = Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        store.Current = ConfigurationSnapshotReadResult.Failure("Configuration.SourceDigest", Desired(2, release: 2));
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        store.Current = Snapshot(2, application: "{\"Value\":2}");
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(2, ((RefreshSettings)entry.Instance).Value);
        Assert.AreEqual(ConfigurationAcceptanceOutcome.Applied, Acceptance(coordinator).Outcome);
    }

    [TestMethod]
    [DataRow("release")]
    [DataRow("common")]
    [DataRow("application")]
    [DataRow("absence")]
    [DataRow("publication")]
    [DataRow("source")]
    public async Task SameCohortReregistrationPreservesEqualGenerationDecisionAndSourceReferenceIntegrity(string conflict)
    {
        ConfigurationSnapshotTarget original = Target(cohort: "canary");
        var store = new CentralStore
        {
            Current = Snapshot(10, common: "{\"Value\":4}", application: "{\"Value\":10}", target: original)
        };
        ConfigurationCatalog catalog = Catalog(store, target: original);
        ConfigurationEntry entry = catalog.Entries.Single();
        var retained = (RefreshSettings)entry.Instance;
        int events = 0;
        retained.ConfigurationChanged += (_, _) => events++;
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        ConfigurationAcceptanceStatus accepted = Acceptance(coordinator);
        long retainedBytes = entry.ByteBudget!.OwnedBytes;
        ConfigurationSnapshotTarget replacement = Target(NewRegistration, "canary");
        ConfigurationSnapshotReadResult changed = conflict switch
        {
            "release" => Snapshot(10, common: "{\"Value\":4}", application: "{\"Value\":10}", target: replacement, release: 11),
            "common" => Snapshot(10, common: "{\"Value\":99}", application: "{\"Value\":10}", target: replacement),
            "application" => Snapshot(10, common: "{\"Value\":4}", application: "{\"Value\":11}", target: replacement),
            "absence" => Snapshot(10, application: "{\"Value\":10}", target: replacement),
            "publication" or "source" => ChangedReference(conflict, replacement),
            _ => throw new AssertFailedException("Select one declared same-generation conflict.")
        };
        store.Current = changed;

        Assert.IsFalse(await coordinator.RebindSnapshotTargetAsync(replacement).WaitAsync(Deadline));

        ConfigurationSnapshotRequest request = store.Requests.Last();
        ConfigurationSnapshotTarget? actualTarget = request.ExpectedTarget;
        Assert.AreEqual(replacement, actualTarget);
        Assert.IsNull(request.SnapshotToken, "Even equal-generation integrity checking needs a fresh current-session response.");
        Assert.IsNull(request.RetainedPair, "The old session's payload/envelope is not retained as a comparison cache.");
        Assert.AreSame(accepted, Acceptance(coordinator));
        Assert.AreEqual(original, accepted.LastAccepted!.Desired.Target);
        Assert.AreEqual(10, retained.Value);
        Assert.AreEqual(0L, entry.Revision);
        Assert.AreEqual(0, events);
        Assert.AreEqual(10L, entry.HighestGeneration);
        Assert.AreEqual("Configuration.EqualGenerationConflict", coordinator.CaptureStatus().Entries.Single().FailureCode);
        Assert.IsFalse(coordinator.CaptureCentralReadiness().IsReady);
        Assert.IsTrue(entry.ByteBudget.OwnedBytes < retainedBytes,
            "Only compact metadata survives; the actual serialized read releases the old pair's byte ownership before refusing the conflict.");

        static ConfigurationSnapshotReadResult ChangedReference(string kind, ConfigurationSnapshotTarget target)
        {
            ConfigurationSnapshotReadResult complete = Snapshot(10, common: "{\"Value\":4}", application: "{\"Value\":10}", target: target);
            ConfigurationPublicationReference common = complete.Provenance!.CommonSource!;
            var changed = new ConfigurationPublicationReference(kind == "source" ? Id(901) : common.SourceId,
                kind == "publication" ? Id(902) : common.PublicationId, common.Sha256);
            return ConfigurationSnapshotReadResult.Complete(complete.Common!, complete.Application!, "fresh-session-pair",
                new(complete.Desired!, changed, complete.Provenance.ApplicationSource));
        }
    }

    [TestMethod]
    public async Task SameCohortReregistrationAcceptsTheSameLogicalPairWithFreshProvenanceWithoutAValueChange()
    {
        ConfigurationSnapshotTarget original = Target(cohort: "canary");
        var store = new CentralStore
        {
            Current = Snapshot(10, common: "{\"Value\":4}", application: "{\"Value\":10}", target: original)
        };
        ConfigurationCatalog catalog = Catalog(store, target: original);
        ConfigurationEntry entry = catalog.Entries.Single();
        var retained = (RefreshSettings)entry.Instance;
        int events = 0;
        retained.ConfigurationChanged += (_, _) => events++;
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        ConfigurationSnapshotTarget replacement = Target(NewRegistration, "canary");
        ConfigurationSnapshotReadResult fresh = Snapshot(10, common: "{\"Value\":4}", application: "{\"Value\":10}", target: replacement);
        store.Current = fresh;

        Assert.IsTrue(await coordinator.RebindSnapshotTargetAsync(replacement).WaitAsync(Deadline));

        ConfigurationSnapshotRequest first = store.Requests.Last();
        Assert.IsNull(first.SnapshotToken);
        Assert.IsNull(first.RetainedPair);
        Assert.AreSame(retained, entry.Instance);
        Assert.AreEqual(10, retained.Value);
        Assert.AreEqual(0L, entry.Revision);
        Assert.AreEqual(0, events);
        Assert.AreEqual(ConfigurationAcceptanceOutcome.AppliedUnchanged, Acceptance(coordinator).Outcome);
        Assert.AreEqual(replacement, Acceptance(coordinator).LastAccepted!.Desired.Target);
        Assert.AreEqual(fresh.Desired, entry.ObservedDesired);
        Assert.AreEqual(fresh.Provenance, entry.ObservedProvenance);
        Assert.IsTrue(coordinator.CaptureCentralReadiness().IsReady);
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(fresh.SnapshotToken, store.Requests.Last().SnapshotToken);
        Assert.AreSame(fresh, store.Requests.Last().RetainedPair, "Only the newly validated session's complete pair becomes conditional state.");
    }

    [TestMethod]
    public async Task SameCohortReregistrationKeepsAnIdentifiedHigherReleaseButDoesNotInventItsUnknownSourceReferences()
    {
        var store = new CentralStore { Current = Snapshot(5, application: "{\"Value\":5}") };
        ConfigurationCatalog catalog = Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        store.Current = ConfigurationSnapshotReadResult.Failure("Configuration.SourceDigest", Desired(10, release: 10));
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        Assert.IsNull(entry.ObservedProvenance, "A higher identified failure cannot retain the lower release's source fingerprint.");
        ConfigurationSnapshotTarget replacement = Target(NewRegistration);
        store.Current = Snapshot(10, application: "{\"Value\":11}", target: replacement, release: 11);
        Assert.IsFalse(await coordinator.RebindSnapshotTargetAsync(replacement).WaitAsync(Deadline));
        Assert.AreEqual("Configuration.EqualGenerationConflict", coordinator.CaptureStatus().Entries.Single().FailureCode);
        Assert.AreEqual(5, ((RefreshSettings)entry.Instance).Value);
        Assert.IsFalse(coordinator.CaptureCentralReadiness().IsReady);

        store.Current = Snapshot(10, application: "{\"Value\":10}", target: replacement, release: 10);
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(10, ((RefreshSettings)entry.Instance).Value);
        Assert.AreEqual("10", Acceptance(coordinator).LastAccepted!.Desired.AssignmentGeneration);
        Assert.AreEqual(replacement, Acceptance(coordinator).LastAccepted!.Desired.Target);
        Assert.IsTrue(coordinator.CaptureCentralReadiness().IsReady);
    }

    [TestMethod]
    public async Task RebindCancelsAndJoinsIgnoredOldWorkBeforeItsFreshSameGenerationCohortCanAccept()
    {
        var store = new CentralStore { Current = Snapshot(5, application: "{\"Value\":5}") };
        ConfigurationCatalog catalog = Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        var entered = ConfigurationRefreshTestSupport.Signal();
        var releaseOld = ConfigurationRefreshTestSupport.Signal();
        CancellationToken oldToken = default;
        bool holdOld = true;
        ConfigurationSnapshotTarget replacement = Target(NewRegistration, "canary");
        store.Read = async (request, cancellation) =>
        {
            if (request.ExpectedTarget == Target() && holdOld)
            {
                oldToken = cancellation;
                entered.TrySetResult();
                await releaseOld.Task;
                return Snapshot(9, application: "{\"Value\":999}");
            }
            ConfigurationSnapshotTarget? actualTarget = request.ExpectedTarget;
            Assert.AreEqual(replacement, actualTarget);
            Assert.IsNull(request.SnapshotToken, "A registration/cohort change cannot reuse a previous session's conditional pair.");
            return Snapshot(5, application: "{\"Value\":6}", target: replacement, release: 6);
        };
        Task<bool> oldAttempt = coordinator.ReconcileAsync(entry);
        await entered.Task.WaitAsync(Deadline);
        Task<bool> rebound = coordinator.RebindSnapshotTargetAsync(replacement);
        Assert.IsTrue(oldToken.IsCancellationRequested);
        Assert.IsFalse(rebound.IsCompleted, "Actual old provider work remains owned even when it ignores cancellation.");
        Assert.IsFalse(coordinator.CaptureCentralReadiness().IsReady);
        holdOld = false;
        releaseOld.TrySetResult();
        Assert.IsFalse(await oldAttempt.WaitAsync(Deadline));
        Assert.IsTrue(await rebound.WaitAsync(Deadline));
        Assert.AreEqual(6, ((RefreshSettings)entry.Instance).Value);
        Assert.AreEqual(1L, entry.Revision);
        Assert.AreEqual(replacement, Acceptance(coordinator).LastAccepted!.Desired.Target);
        Assert.AreEqual(5L, entry.HighestGeneration, "An old canceled response cannot raise or lower the current map high-water.");
        Assert.IsTrue(coordinator.CaptureCentralReadiness().IsReady);
    }

    [TestMethod]
    public async Task RebindPreservesObservedMapHighWaterButResetsPerTargetEqualGenerationResolution()
    {
        var store = new CentralStore { Current = Snapshot(5, application: "{\"Value\":5}") };
        ConfigurationCatalog catalog = Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        store.Current = ConfigurationSnapshotReadResult.Failure("Configuration.SourceDigest", Desired(10, release: 10));
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        ConfigurationSnapshotTarget replacement = Target(NewRegistration, "cohort");
        store.Current = Snapshot(9, application: "{\"Value\":9}", target: replacement);
        Assert.IsFalse(await coordinator.RebindSnapshotTargetAsync(replacement).WaitAsync(Deadline));
        Assert.IsFalse(coordinator.CaptureCentralReadiness().IsReady);
        Assert.AreEqual(10L, entry.HighestGeneration);
        Assert.AreEqual(5, ((RefreshSettings)entry.Instance).Value);
        store.Current = Snapshot(10, application: "{\"Value\":10}", target: replacement, release: 88);
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(10, ((RefreshSettings)entry.Instance).Value);
        Assert.IsTrue(coordinator.CaptureCentralReadiness().IsReady);
    }

    [TestMethod]
    public async Task FreshIdentifiedRejectionOrRestartCanReconcileANewRegistrationWithIntactSameMapValues()
    {
        foreach (bool rejected in new[] { false, true })
        {
            var store = new CentralStore { Current = Snapshot(application: "{\"Value\":4}") };
            ConfigurationCatalog catalog = Catalog(store, ConfigurationUpdateMode.RestartRequired);
            ConfigurationEntry entry = catalog.Entries.Single();
            await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
            await coordinator.LoadAsync(CancellationToken.None);
            ConfigurationSnapshotTarget replacement = Target(NewRegistration);
            store.Current = rejected
                ? ConfigurationSnapshotReadResult.Failure("Configuration.SourceDigest", Desired(2, replacement, 2))
                : Snapshot(2, application: "{\"Value\":8}", target: replacement);
            Assert.IsTrue(await coordinator.RebindSnapshotTargetAsync(replacement).WaitAsync(Deadline));
            Assert.AreEqual(rejected ? ConfigurationAcceptanceOutcome.Rejected : ConfigurationAcceptanceOutcome.PendingRestart,
                Acceptance(coordinator).Outcome);
            Assert.AreEqual(4, ((RefreshSettings)entry.Instance).Value);
            Assert.AreEqual(Target(), Acceptance(coordinator).LastAccepted!.Desired.Target);
            Assert.AreEqual(replacement, Acceptance(coordinator).Desired!.Target);
            Assert.IsTrue(coordinator.CaptureCentralReadiness().IsReady);
        }
    }

    [TestMethod]
    public async Task ResponseTargetCannotRebindAHostAndTrustedRebindCannotMoveItsAuthorityOrFixedApplication()
    {
        var store = new CentralStore();
        ConfigurationCatalog catalog = Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        ConfigurationAcceptanceStatus accepted = Acceptance(coordinator);
        store.Current = Snapshot(2, application: "{\"Value\":90}", target: Target(NewRegistration));
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        Assert.AreSame(accepted, Acceptance(coordinator));
        Assert.AreEqual(Target(), coordinator.CaptureCentralReadiness().Target);
        var foreign = new ConfigurationSnapshotTarget(Id(999), ConfigurationCentralTestSupport.Application, "test", NewRegistration, "CentralHost");
        Assert.ThrowsExactly<InvalidOperationException>(() => coordinator.RebindSnapshotTargetAsync(foreign));
        Assert.AreEqual(1, ((RefreshSettings)entry.Instance).Value);
    }

    [TestMethod]
    public async Task StopClosesTheGateAndJoinsAnIgnoredInFlightReadWithoutAcceptingItsLateCandidate()
    {
        var store = new CentralStore();
        ConfigurationCatalog catalog = Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        var entered = ConfigurationRefreshTestSupport.Signal();
        var release = ConfigurationRefreshTestSupport.Signal();
        store.Read = async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return Snapshot(2, application: "{\"Value\":99}");
        };
        Task<bool> attempt = coordinator.ReconcileAsync(entry);
        await entered.Task.WaitAsync(Deadline);
        Task stopping = coordinator.StopAsync();
        Assert.IsFalse(stopping.IsCompleted);
        release.TrySetResult();
        Assert.IsFalse(await attempt.WaitAsync(Deadline));
        await stopping.WaitAsync(Deadline);
        Assert.AreEqual(1, ((RefreshSettings)entry.Instance).Value);
        Assert.AreEqual(0L, entry.Revision);
        Assert.AreEqual("1", Acceptance(coordinator).LastAccepted!.Desired.AssignmentGeneration);
        Assert.AreEqual(0, store.StoreDisposals);
    }
}
