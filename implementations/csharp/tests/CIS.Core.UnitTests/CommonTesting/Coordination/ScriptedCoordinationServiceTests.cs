using CIS.CommonTesting;
using CIS.Coordination;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.CommonTesting.Coordination;

[TestClass]
public sealed class ScriptedCoordinationServiceTests
{
    [TestMethod]
    public async Task PublicPortReturnsExactSuppliedLeaseAndDetachedBoundedRequestRecords()
    {
        await using var lease = new ManualCoordinationLease(Grant());
        var original = new ManagedCoordinationResult(CoordinationAcquireOutcome.Acquired, lease);
        var scripts = new ScriptedCoordinationService();
        ICoordinationService service = scripts;
        scripts.Enqueue(original);
        IReadOnlyList<ScriptedCoordinationCall> before = scripts.Calls;

        ManagedCoordinationResult result = await service.AcquireAsync(new("requested-scope", "compact", "partition-1"));

        Assert.AreSame(original, result);
        Assert.AreSame(lease, result.Lease);
        Assert.IsEmpty(before);
        ScriptedCoordinationCall call = scripts.Calls.Single();
        Assert.AreEqual("requested-scope", call.ExecutionScope);
        Assert.AreEqual("compact", call.DutyId);
        Assert.AreEqual("partition-1", call.ResourceId);
        Assert.AreEqual(0, scripts.PendingScripts);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ScriptedCoordinationCall>)scripts.Calls).Clear());
    }

    [TestMethod]
    [DataRow(CoordinationAcquireOutcome.Busy)]
    [DataRow(CoordinationAcquireOutcome.Unauthorized)]
    [DataRow(CoordinationAcquireOutcome.InvalidRequest)]
    [DataRow(CoordinationAcquireOutcome.Unavailable)]
    [DataRow(CoordinationAcquireOutcome.CapacityExceeded)]
    [DataRow(CoordinationAcquireOutcome.Indeterminate)]
    public async Task NonownershipFactsAreSuppliedWithoutInventingAuthority(CoordinationAcquireOutcome outcome)
    {
        var scripts = new ScriptedCoordinationService();
        var original = new ManagedCoordinationResult(outcome, reasonCode: "test.scripted");
        scripts.Enqueue(original);

        Assert.AreSame(original, await scripts.AcquireAsync(new("scope", "job")));
        Assert.IsNull(original.Lease);
    }

    [TestMethod]
    public async Task PreCancellationConsumesNeitherScriptNorRecordingAndScriptedCancellationRemainsFaulted()
    {
        var scripts = new ScriptedCoordinationService();
        var original = new OperationCanceledException("original scripted fault");
        scripts.EnqueueFailure(original);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Task<ManagedCoordinationResult> cancelled = scripts.AcquireAsync(new("scope", "job"), cancellation.Token);
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => cancelled);
        Assert.IsTrue(cancelled.IsCanceled);
        Assert.AreEqual(1, scripts.PendingScripts);
        Assert.IsEmpty(scripts.Calls);

        Task<ManagedCoordinationResult> fault = scripts.AcquireAsync(new("scope", "job"));
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fault));
        Assert.IsTrue(fault.IsFaulted);
        Assert.AreEqual(0, scripts.PendingScripts);
        Assert.HasCount(1, scripts.Calls);
    }

    [TestMethod]
    public async Task ScriptAndRecordingPressureAreIndependentAndNeverDiscardTheNextScript()
    {
        var scripts = new ScriptedCoordinationService(scriptCapacity: 1, recordingCapacity: 1);
        scripts.Enqueue(new(CoordinationAcquireOutcome.Busy));
        Assert.ThrowsExactly<InvalidOperationException>(() => scripts.Enqueue(new(CoordinationAcquireOutcome.Unavailable)));
        Assert.AreEqual(CoordinationAcquireOutcome.Busy, (await scripts.AcquireAsync(new("scope", "first"))).Outcome);
        scripts.Enqueue(new(CoordinationAcquireOutcome.Indeterminate));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scripts.AcquireAsync(new("scope", "second")));

        Assert.AreEqual(1, scripts.PendingScripts);
        Assert.AreEqual("first", scripts.Calls.Single().DutyId);
        var empty = new ScriptedCoordinationService();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => empty.AcquireAsync(new("scope", "job")));
        Assert.IsEmpty(empty.Calls);
    }

    [TestMethod]
    public void PublicBoundsRejectEmptyAndUnboundedStorage()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScriptedCoordinationService(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScriptedCoordinationService(65_537));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScriptedCoordinationService(recordingCapacity: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScriptedCoordinationService(recordingCapacity: 65_537));
    }

    internal static CoordinationGrant Grant(long generation = 1)
    {
        return new(Guid.Parse("be94ee2f-fdf7-4221-a01f-ceb4d602732e"),
            new(new("test", "scope", "job"), "test-app", "test-instance", Guid.Parse("8e582c67-c794-40cf-a619-04524ecfe316")),
            generation, new DateTimeOffset(2026, 11, 22, 12, 0, 30, TimeSpan.Zero));
    }
}
