using CIS.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Configuration;

[TestClass]
public sealed class ConfigurationCommitTests
{
    [TestMethod]
    public void InitialAcceptanceIsNeutralAndOnlyChangedRuntimeValuesAdvanceTheRetainedRootsRevision()
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2ObservedSettings>();
        W2ObservedSettings retained = fixture.Root;
        ConfigurationPreparedValues initial = fixture.Prepare(application: "{\"Alpha\":10,\"Beta\":20}");
        Assert.AreEqual((1, 2), (retained.Alpha, retained.Beta), "Preparation must not populate the live root.");
        Assert.IsFalse(fixture.Pipeline.Apply(fixture.Entry, initial, CancellationToken.None, () => false, initial: true));
        Assert.AreEqual((10, 20), (retained.Alpha, retained.Beta));
        Assert.AreEqual(0L, fixture.Entry.Revision);
        Assert.AreEqual(initial.Checksum, fixture.Entry.AcceptedChecksum);
        retained.ResetWrites();
        Assert.IsFalse(fixture.Pipeline.Apply(fixture.Entry, fixture.Prepare(application: "{\"Beta\":20,\"Alpha\":10}"),
            CancellationToken.None, () => false));
        Assert.AreEqual(0, retained.Writes, "Equal effective values must not invoke live setters.");
        Assert.AreEqual(0L, fixture.Entry.Revision);
        ConfigurationPreparedValues changed = fixture.Prepare(application: "{\"Alpha\":11,\"Beta\":21}");
        Assert.IsTrue(fixture.Pipeline.Apply(fixture.Entry, changed, CancellationToken.None, () => false));
        Assert.AreSame(retained, fixture.Root);
        Assert.AreEqual((11, 21), (retained.Alpha, retained.Beta));
        Assert.AreEqual(1L, fixture.Entry.Revision);
        Assert.AreEqual(changed.Checksum, fixture.Entry.AcceptedChecksum);
    }

    [TestMethod]
    public void ASecondDifferentInitialCommitCannotReplaceAnAcceptedBaseline()
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2ObservedSettings>();
        fixture.Baseline(application: "{\"Alpha\":10}");
        string? checksum = fixture.Entry.AcceptedChecksum;
        fixture.Root.ResetWrites();
        ConfigurationPreparedValues changed = fixture.Prepare(application: "{\"Alpha\":99}");
        ConfigurationMappingTestSupport.Issue("Configuration.Commit",
            () => fixture.Pipeline.Apply(fixture.Entry, changed, CancellationToken.None, () => false, initial: true));
        Assert.AreEqual((10, 2), (fixture.Root.Alpha, fixture.Root.Beta));
        Assert.AreEqual(0, fixture.Root.Writes);
        Assert.AreEqual(checksum, fixture.Entry.AcceptedChecksum);
        Assert.AreEqual(0L, fixture.Entry.Revision);
        Assert.IsFalse(fixture.Entry.Disabled);
    }

    [TestMethod]
    public void ValidationSeesACompleteDetachedCandidateAndCannotRetainTheLiveReplacementGraph()
    {
        W2OverlaySettings? inspected = null;
        var fixture = ConfigurationMappingTestSupport.Create<W2OverlaySettings>(validator: candidate => inspected = candidate);
        W2Child? originalChild = fixture.Root.Child;
        ConfigurationPreparedValues prepared = fixture.Prepare("{\"Child\":{\"Left\":7}}",
            "{\"Child\":{\"Right\":9},\"Items\":[3,4]}");
        Assert.IsNotNull(inspected);
        Assert.AreNotSame(fixture.Root, inspected);
        Assert.IsNotNull(inspected.Child);
        Assert.AreEqual((7, 9, 30, 5),
            (inspected.Child.Left, inspected.Child.Right, inspected.IntervalSeconds, inspected.TimeoutSeconds));
        Assert.AreSame(originalChild, fixture.Root.Child);
        Assert.AreEqual((10, 20), (fixture.Root.Child!.Left, fixture.Root.Child.Right));
        CollectionAssert.AreEqual(new[] { 1, 2 }, fixture.Root.Items);
        inspected.Child.Left = 900;
        inspected.Items[0] = 999;
        Assert.IsFalse(fixture.Pipeline.Apply(fixture.Entry, prepared, CancellationToken.None, () => false, initial: true));
        Assert.AreEqual((7, 9), (fixture.Root.Child!.Left, fixture.Root.Child.Right));
        CollectionAssert.AreEqual(new[] { 3, 4 }, fixture.Root.Items);
        Assert.AreNotSame(inspected.Child, fixture.Root.Child);
        Assert.AreNotSame(inspected.Items, fixture.Root.Items);
    }

    [TestMethod]
    public void AFactorySharedChildIsDetachedBeforeValidationAndFromThePreparedReplacementGraph()
    {
        var shared = new W2Child { Left = 10, Right = 20 };
        W2OverlaySettings? inspected = null;
        bool reject = true;
        var fixture = ConfigurationMappingTestSupport.Create<W2OverlaySettings>(
            factory: () => new W2OverlaySettings { Child = shared },
            validator: candidate =>
            {
                inspected = candidate;
                if (reject)
                {
                    candidate.Child!.Left = 700;
                    throw new InvalidOperationException("Reject the detached candidate.");
                }
            });
        ConfigurationMappingTestSupport.Issue("Configuration.Validation",
            () => fixture.Prepare(application: "{\"Child\":{\"Left\":7,\"Right\":9}}"));
        Assert.AreSame(shared, fixture.Root.Child);
        Assert.AreEqual((10, 20), (shared.Left, shared.Right));
        Assert.IsNotNull(inspected);
        Assert.AreNotSame(shared, inspected.Child);
        Assert.IsNull(fixture.Entry.AcceptedChecksum);
        Assert.AreEqual(0L, fixture.Entry.Revision);
        Assert.IsFalse(fixture.Entry.Disabled);

        reject = false;
        ConfigurationPreparedValues prepared = fixture.Prepare(application: "{\"Child\":{\"Left\":7,\"Right\":9}}");
        Assert.AreSame(shared, fixture.Root.Child);
        Assert.AreEqual((10, 20), (shared.Left, shared.Right));
        Assert.IsNotNull(inspected.Child);
        inspected.Child.Left = 900;
        Assert.IsFalse(fixture.Pipeline.Apply(fixture.Entry, prepared, CancellationToken.None, () => false, initial: true));
        Assert.IsNotNull(fixture.Root.Child);
        Assert.AreEqual((7, 9), (fixture.Root.Child.Left, fixture.Root.Child.Right));
        Assert.AreNotSame(shared, fixture.Root.Child);
        Assert.AreNotSame(inspected.Child, fixture.Root.Child);
        Assert.AreEqual((10, 20), (shared.Left, shared.Right));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ValidatorRejectionOrMutationCannotChangeLiveValuesOrAcceptedMetadata(bool mutate)
    {
        bool reject = false;
        var fixture = ConfigurationMappingTestSupport.Create<W2OverlaySettings>(validator: candidate =>
        {
            if (!reject) { return; }
            if (mutate) { candidate.Child!.Left = 999; }
            else { throw new InvalidOperationException("validator-secret"); }
        });
        fixture.Baseline(application: "{\"IntervalSeconds\":44}");
        string? checksum = fixture.Entry.AcceptedChecksum;
        W2Child? child = fixture.Root.Child;
        reject = true;
        CoreConfigurationException error = ConfigurationMappingTestSupport.Issue("Configuration.Validation",
            () => fixture.Prepare(application: "{\"IntervalSeconds\":99}"));
        Assert.AreEqual((44, 10, 20), (fixture.Root.IntervalSeconds, fixture.Root.Child!.Left, fixture.Root.Child.Right));
        Assert.AreSame(child, fixture.Root.Child);
        Assert.AreEqual(checksum, fixture.Entry.AcceptedChecksum);
        Assert.AreEqual(0L, fixture.Entry.Revision);
        Assert.IsFalse(fixture.Entry.Disabled);
        Assert.IsNull(error.InnerException);
        Assert.IsFalse(error.ToString().Contains("validator-secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AnApplicationValidatorMayRejectARepresentableButUndeclaredEnumValue()
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2Enums>(validator: candidate =>
        {
            if (!Enum.IsDefined(candidate.Mode)) { throw new InvalidOperationException("Semantic policy belongs to the application."); }
        });
        fixture.Baseline(application: "{\"Mode\":\"Ready\"}");
        string? checksum = fixture.Entry.AcceptedChecksum;
        ConfigurationMappingTestSupport.Issue("Configuration.Validation", () => fixture.Prepare(application: "{\"Mode\":99}"));
        Assert.AreEqual(W2Mode.Ready, fixture.Root.Mode);
        Assert.AreEqual(checksum, fixture.Entry.AcceptedChecksum);
        Assert.AreEqual(0L, fixture.Entry.Revision);
    }

    [TestMethod]
    [DataRow("retained", "Configuration.Ownership")]
    [DataRow("other-host", "Configuration.Ownership")]
    [DataRow("null", "Configuration.Factory")]
    [DataRow("throw", "Configuration.Factory")]
    public void UnsafeCandidateFactoriesFailBeforeAnyAssignmentToAnOwnedRoot(string kind, string expectedCode)
    {
        var retained = new W2ObservedSettings();
        var otherHost = ConfigurationMappingTestSupport.Create<W2ObservedSettings>();
        int calls = 0;
        var fixture = ConfigurationMappingTestSupport.Create(factory: () =>
        {
            if (calls++ == 0) { return retained; }
            return kind switch
            {
                "retained" => retained,
                "other-host" => otherHost.Root,
                "null" => null!,
                _ => throw new InvalidOperationException("factory-secret")
            };
        });
        ConfigurationMappingTestSupport.Issue(expectedCode, () => fixture.Prepare(application: "{\"Alpha\":99,\"Beta\":88}"));
        Assert.AreEqual((1, 2, 0), (retained.Alpha, retained.Beta, retained.Writes));
        Assert.AreEqual((1, 2, 0), (otherHost.Root.Alpha, otherHost.Root.Beta, otherHost.Root.Writes));
        Assert.IsNull(fixture.Entry.AcceptedChecksum);
        Assert.AreEqual(0L, fixture.Entry.Revision);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CancellationOrStoppingThatWinsWhileCommitWaitsOnTheGatePreventsEveryLiveAssignment(bool stoppingWins)
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2ObservedSettings>();
        fixture.Baseline();
        ConfigurationPreparedValues prepared = fixture.Prepare(application: "{\"Alpha\":10,\"Beta\":20}");
        fixture.Root.ResetWrites();
        string? checksum = fixture.Entry.AcceptedChecksum;
        using var cancellation = new CancellationTokenSource();
        int stopping = 0;
        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? error = null;
        var writer = new Thread(() =>
        {
            attempted.TrySetResult();
            try { fixture.Pipeline.Apply(fixture.Entry, prepared, cancellation.Token, () => Volatile.Read(ref stopping) != 0); }
            catch (Exception failure) { error = failure; }
            finally { finished.TrySetResult(); }
        }) { IsBackground = true };
        bool started = false;
        try
        {
            lock (fixture.Entry.ValueGate)
            {
                writer.Start();
                started = true;
                Assert.IsTrue(attempted.Task.Wait(ConfigurationObjectTestSupport.Deadline));
                ObserveBlocked(writer, finished.Task);
                Assert.IsFalse(finished.Task.IsCompleted);
                if (stoppingWins) { Volatile.Write(ref stopping, 1); }
                else { cancellation.Cancel(); }
            }
            Assert.IsTrue(finished.Task.Wait(ConfigurationObjectTestSupport.Deadline));
            Assert.IsInstanceOfType<OperationCanceledException>(error);
            Assert.AreEqual((1, 2, 0), (fixture.Root.Alpha, fixture.Root.Beta, fixture.Root.Writes));
            Assert.AreEqual(checksum, fixture.Entry.AcceptedChecksum);
            Assert.AreEqual(0L, fixture.Entry.Revision);
            Assert.IsFalse(fixture.Entry.Disabled);
        }
        finally
        {
            if (started) { Assert.IsTrue(writer.Join(ConfigurationObjectTestSupport.Deadline)); }
        }
    }

    [TestMethod]
    public void ACoordinatedReaderCannotObserveTheIntermediateTupleInsideARealPipelineCommit()
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2ObservedSettings>();
        fixture.Baseline();
        ConfigurationPreparedValues prepared = fixture.Prepare(application: "{\"Alpha\":100,\"Beta\":200}");
        using var alphaAssigned = new ManualResetEventSlim();
        using var releaseSetter = new ManualResetEventSlim();
        var writerFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readerAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readerFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? writerError = null;
        Exception? readerError = null;
        (int Alpha, int Beta)? observed = null;
        bool changed = false;
        fixture.Root.AfterAlphaAssignment = () =>
        {
            alphaAssigned.Set();
            if (!releaseSetter.Wait(ConfigurationObjectTestSupport.Deadline)) { throw new TimeoutException("Release the controlled test setter."); }
        };
        var writer = new Thread(() =>
        {
            try { changed = fixture.Pipeline.Apply(fixture.Entry, prepared, CancellationToken.None, () => false); }
            catch (Exception failure) { writerError = failure; }
            finally { writerFinished.TrySetResult(); }
        }) { IsBackground = true };
        var reader = new Thread(() =>
        {
            readerAttempted.TrySetResult();
            try { observed = fixture.Catalog.Reader.Read(fixture.Root, current => (current.Alpha, current.Beta)); }
            catch (Exception failure) { readerError = failure; }
            finally { readerFinished.TrySetResult(); }
        }) { IsBackground = true };
        bool writerStarted = false;
        bool readerStarted = false;
        try
        {
            writer.Start();
            writerStarted = true;
            Assert.IsTrue(alphaAssigned.Wait(ConfigurationObjectTestSupport.Deadline));
            Assert.AreEqual((100, 2), (fixture.Root.Alpha, fixture.Root.Beta));
            reader.Start();
            readerStarted = true;
            Assert.IsTrue(readerAttempted.Task.Wait(ConfigurationObjectTestSupport.Deadline));
            ObserveBlocked(reader, readerFinished.Task);
            Assert.IsFalse(readerFinished.Task.IsCompleted, "The private commit gate must cover every assignment.");
            releaseSetter.Set();
            Assert.IsTrue(writerFinished.Task.Wait(ConfigurationObjectTestSupport.Deadline));
            Assert.IsTrue(readerFinished.Task.Wait(ConfigurationObjectTestSupport.Deadline));
            Assert.IsNull(writerError);
            Assert.IsNull(readerError);
            Assert.IsTrue(changed);
            Assert.IsTrue(observed.HasValue);
            Assert.AreEqual((100, 200), observed.GetValueOrDefault());
            Assert.AreEqual(prepared.Checksum, fixture.Entry.AcceptedChecksum);
            Assert.AreEqual(1L, fixture.Entry.Revision);
        }
        finally
        {
            releaseSetter.Set();
            if (writerStarted) { Assert.IsTrue(writer.Join(ConfigurationObjectTestSupport.Deadline)); }
            if (readerStarted) { Assert.IsTrue(reader.Join(ConfigurationObjectTestSupport.Deadline)); }
            fixture.Root.AfterAlphaAssignment = null;
        }
    }

    [TestMethod]
    public void CancellationAfterCommitAdmissionCannotTurnAPartiallyAssignedCommitIntoACanceledResult()
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2ObservedSettings>();
        fixture.Baseline();
        ConfigurationPreparedValues prepared = fixture.Prepare(application: "{\"Alpha\":10,\"Beta\":20}");
        using var cancellation = new CancellationTokenSource();
        fixture.Root.AfterAlphaAssignment = cancellation.Cancel;
        try
        {
            Assert.IsTrue(fixture.Pipeline.Apply(fixture.Entry, prepared, cancellation.Token, () => false));
            Assert.IsTrue(cancellation.IsCancellationRequested);
            Assert.AreEqual((10, 20), (fixture.Root.Alpha, fixture.Root.Beta));
            Assert.AreEqual(prepared.Checksum, fixture.Entry.AcceptedChecksum);
            Assert.AreEqual(1L, fixture.Entry.Revision);
        }
        finally { fixture.Root.AfterAlphaAssignment = null; }
    }

    [TestMethod]
    public void AnUnexpectedLiveSetterFailureDisablesTheEntryWithoutAdvancingAcceptedMetadataOrClaimingRollback()
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2ObservedSettings>();
        fixture.Baseline();
        string? checksum = fixture.Entry.AcceptedChecksum;
        ConfigurationPreparedValues prepared = fixture.Prepare(application: "{\"Alpha\":10,\"Beta\":20}");
        fixture.Root.RejectBetaAssignment = true;
        CoreConfigurationException error = ConfigurationMappingTestSupport.Issue("Configuration.Commit",
            () => fixture.Pipeline.Apply(fixture.Entry, prepared, CancellationToken.None, () => false));
        Assert.IsTrue(fixture.Entry.Disabled);
        Assert.AreEqual(checksum, fixture.Entry.AcceptedChecksum);
        Assert.AreEqual(0L, fixture.Entry.Revision);
        Assert.IsNull(error.InnerException);
        Assert.IsFalse(error.ToString().Contains("live-setter-secret", StringComparison.Ordinal));
        // The broken passive-setter contract may have left partial live values.
        // Recovery is fail-closed; no rollback or successful notification is claimed.
        fixture.Root.RejectBetaAssignment = false;
        fixture.Root.ResetWrites();
        ConfigurationMappingTestSupport.Issue("Configuration.Disabled",
            () => fixture.Pipeline.Apply(fixture.Entry, prepared, CancellationToken.None, () => false));
        Assert.AreEqual(0, fixture.Root.Writes);
        Assert.AreEqual(checksum, fixture.Entry.AcceptedChecksum);
        Assert.AreEqual(0L, fixture.Entry.Revision);
    }

    [TestMethod]
    public void RevisionExhaustionRejectsBeforeSettersAndDoesNotPoisonAnOtherwiseValidEntry()
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2ObservedSettings>();
        fixture.Baseline();
        string? checksum = fixture.Entry.AcceptedChecksum;
        ConfigurationPreparedValues prepared = fixture.Prepare(application: "{\"Alpha\":10,\"Beta\":20}");
        fixture.Entry.Revision = long.MaxValue;
        fixture.Root.ResetWrites();
        ConfigurationMappingTestSupport.Issue("Configuration.Revision",
            () => fixture.Pipeline.Apply(fixture.Entry, prepared, CancellationToken.None, () => false));
        Assert.AreEqual((1, 2, 0), (fixture.Root.Alpha, fixture.Root.Beta, fixture.Root.Writes));
        Assert.AreEqual(checksum, fixture.Entry.AcceptedChecksum);
        Assert.AreEqual(long.MaxValue, fixture.Entry.Revision);
        Assert.IsFalse(fixture.Entry.Disabled);
    }

    private static void ObserveBlocked(Thread thread, Task finished)
        => Assert.IsTrue(SpinWait.SpinUntil(() => finished.IsCompleted
            || (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, ConfigurationObjectTestSupport.Deadline),
            "Observe actual completion or a thread blocked on the owned value gate.");
}
