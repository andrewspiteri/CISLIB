using CIS.CommonTesting;
using CIS.Coordination;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.CommonTesting.Coordination;

[TestClass]
public sealed class ManualCoordinationLeaseTests
{
    [TestMethod]
    public async Task UncertainReleaseEndsLocallyOnceAndCannotBecomeLostOrOwnedAgain()
    {
        var uncertain = new CoordinationMutationResult(CoordinationMutationOutcome.Indeterminate);
        await using var lease = new ManualCoordinationLease(ScriptedCoordinationServiceTests.Grant(), uncertain);
        int notified = 0;
        using CancellationTokenRegistration registration = lease.OwnershipLost.Register(() => Interlocked.Increment(ref notified));

        Assert.AreSame(uncertain, await lease.ReleaseAsync());
        CoordinationLeaseTermination terminal = await lease.Termination;
        await lease.LoseAsync(CoordinationLossReason.Superseded);

        Assert.AreSame(uncertain, await lease.ReleaseAsync());
        Assert.AreSame(terminal, await lease.Termination);
        Assert.AreEqual(CoordinationLeaseState.Released, lease.State);
        Assert.IsNull(lease.LossReason);
        Assert.IsTrue(lease.OwnershipLost.IsCancellationRequested);
        Assert.AreEqual(1, notified);
        Assert.IsFalse(lease.ConfirmGrant(lease.Grant));
    }

    [TestMethod]
    public async Task LossReasonRemainsStableAcrossReleaseDrainAndLaterLoss()
    {
        await using var lease = new ManualCoordinationLease(ScriptedCoordinationServiceTests.Grant());

        await lease.LoseAsync(CoordinationLossReason.RenewalDeadlineExceeded);
        CoordinationLeaseTermination original = await lease.Termination;
        lease.BeginDraining();
        await lease.LoseAsync(CoordinationLossReason.Expired);
        await lease.ReleaseAsync();

        Assert.AreEqual(CoordinationLeaseState.Lost, lease.State);
        Assert.AreEqual(CoordinationLossReason.RenewalDeadlineExceeded, lease.LossReason);
        Assert.AreSame(original, await lease.Termination);
        Assert.AreEqual(CoordinationLossReason.RenewalDeadlineExceeded, original.LossReason);
    }

    [TestMethod]
    public async Task TerminalFactDoesNotClaimThatCancellationCallbacksOrConsumerWorkHaveSettled()
    {
        await using var lease = new ManualCoordinationLease(ScriptedCoordinationServiceTests.Grant());
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCallbackToReturn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actualBusinessWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = lease.OwnershipLost.Register(() =>
        {
            Assert.AreEqual(CoordinationLeaseState.Lost, Task.Run(() => lease.State).GetAwaiter().GetResult());
            callbackEntered.TrySetResult();
            allowCallbackToReturn.Task.GetAwaiter().GetResult();
        });

        Task loss = lease.LoseAsync(CoordinationLossReason.ShutdownDeadlineExceeded);
        try
        {
            await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsTrue(lease.Termination.IsCompletedSuccessfully);
            Assert.IsFalse(loss.IsCompleted);
            Assert.IsFalse(lease.CancellationCompletion.IsCompleted);
            Assert.IsFalse(actualBusinessWork.Task.IsCompleted);
        }
        finally
        {
            allowCallbackToReturn.TrySetResult();
        }

        await loss;
        Assert.IsTrue(lease.CancellationCompletion.IsCompletedSuccessfully);
        Assert.IsFalse(actualBusinessWork.Task.IsCompleted);
        actualBusinessWork.TrySetResult();
        await actualBusinessWork.Task;
    }

    [TestMethod]
    public async Task CallbackFaultIsRetainedWithoutReplacingTheExactScriptedReleaseResult()
    {
        var uncertain = new CoordinationMutationResult(CoordinationMutationOutcome.Indeterminate);
        await using var lease = new ManualCoordinationLease(ScriptedCoordinationServiceTests.Grant(), uncertain);
        var original = new InvalidOperationException("test callback failed");
        using CancellationTokenRegistration registration = lease.OwnershipLost.Register(() => throw original);

        Assert.AreSame(uncertain, await lease.ReleaseAsync());

        Assert.IsTrue(lease.CancellationCompletion.IsCompletedSuccessfully);
        Assert.IsNotNull(lease.CancellationException);
        Assert.IsTrue(lease.CancellationException is AggregateException aggregate && aggregate.InnerExceptions.Contains(original));
    }

    [TestMethod]
    public async Task DrainAndConfirmAreExplicitControlsThatPreserveTheExactGrant()
    {
        CoordinationGrant original = ScriptedCoordinationServiceTests.Grant();
        await using var lease = new ManualCoordinationLease(original);
        lease.BeginDraining(TimeSpan.FromSeconds(4));
        lease.BeginDraining(TimeSpan.FromSeconds(20));
        var confirmed = new CoordinationGrant(original.AuthorityId, original.Acquisition, original.Generation,
            original.ExpiresAtUtc.AddSeconds(30));

        Assert.IsTrue(lease.ConfirmGrant(confirmed));
        Assert.AreSame(confirmed, lease.Grant);
        Assert.AreEqual(CoordinationLeaseState.Draining, lease.State);
        Assert.AreEqual(TimeSpan.FromSeconds(4), lease.DrainBudget);
        Assert.IsFalse(lease.Termination.IsCompleted);
        Assert.ThrowsExactly<ArgumentException>(() => lease.ConfirmGrant(ScriptedCoordinationServiceTests.Grant(2)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => lease.BeginDraining(TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => lease.BeginDraining(TimeSpan.FromDays(2)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => lease.LoseAsync((CoordinationLossReason)999));
    }

    [TestMethod]
    public async Task PreCancelledReleaseLeavesManualOwnershipAndDisposalPreservesThePublicToken()
    {
        var lease = new ManualCoordinationLease(ScriptedCoordinationServiceTests.Grant());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => lease.ReleaseAsync(cancellation.Token));

        Assert.AreEqual(CoordinationLeaseState.Owned, lease.State);
        Assert.IsFalse(lease.OwnershipLost.IsCancellationRequested);
        await lease.DisposeAsync();
        await lease.DisposeAsync();
        Assert.IsTrue(lease.OwnershipLost.IsCancellationRequested);
        Assert.AreEqual(CoordinationLeaseState.Released, lease.State);
    }
}
