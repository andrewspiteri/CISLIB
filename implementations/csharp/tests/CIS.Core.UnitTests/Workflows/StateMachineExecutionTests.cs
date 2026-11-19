using CIS.Workflows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Workflows;

[TestClass]
public sealed class StateMachineExecutionTests
{
    [TestMethod]
    public async Task DirectSelfAndSuccessiveTransitionsPublishImmutableResultsWithoutScopes()
    {
        await using var host = new StateMachineTestHost();
        var builder = host.Builder();
        builder.State(MachineState.Idle).On(MachineTrigger.Start, MachineState.Ready).On(MachineTrigger.Reset, MachineState.Idle);
        builder.State(MachineState.Ready).On(MachineTrigger.Finish, MachineState.Done);
        IStateMachine<MachineState, MachineTrigger> machine = builder.Build(MachineState.Idle);
        IReadOnlyCollection<MachineTrigger> permitted = machine.PermittedTriggers;
        Assert.IsNull(machine.LastTransition);
        StateTransitionResult<MachineState, MachineTrigger> self = await machine.Trigger(MachineTrigger.Reset);
        StateTransitionResult<MachineState, MachineTrigger> first = await machine.Trigger(MachineTrigger.Start);
        StateTransitionResult<MachineState, MachineTrigger> last = await machine.Trigger(MachineTrigger.Finish);

        Assert.IsTrue(self.Success);
        Assert.IsFalse(self.StateChanged);
        Assert.IsTrue(first.StateChanged);
        Assert.AreEqual(MachineState.Ready, first.CurrentState);
        Assert.AreEqual(MachineState.Done, machine.CurrentState);
        Assert.AreSame(last, machine.LastTransition);
        Assert.IsFalse(machine.IsExecuting);
        Assert.AreEqual(0, machine.PermittedTriggers.Count);
        CollectionAssert.AreEqual(new[] { MachineTrigger.Start, MachineTrigger.Reset }, permitted.ToArray());
        Assert.AreEqual(0, host.Scopes);
        Assert.AreEqual(0, host.Context.Instances.Count);
        Assert.AreEqual(0, host.Observation.Phases.Count);
        Assert.AreEqual(3, host.Observation.Admissions);
        Assert.AreEqual(3, host.Observation.Completions.Count);
    }

    [TestMethod]
    public async Task InvalidAndUnmappedTriggersAdmitRejectionsButPreCancellationCannotReplaceLastTransition()
    {
        await using var host = new StateMachineTestHost();
        var builder = host.Builder();
        builder.State(MachineState.Idle).On<StateMachineTestHandler>(MachineTrigger.Start, MachineState.Ready);
        IStateMachine<MachineState, MachineTrigger> machine = builder.Build(MachineState.Idle);
        Assert.IsFalse(machine.CanTrigger((MachineTrigger)999));
        StateTransitionResult<MachineState, MachineTrigger> invalid = await machine.Trigger((MachineTrigger)999);
        Assert.AreEqual(StateTransitionOutcome.Rejected, invalid.Outcome);
        Assert.AreEqual("StateMachine.InvalidTrigger", invalid.ErrorCode);
        Assert.AreEqual((MachineTrigger)999, invalid.Trigger);
        Assert.AreSame(invalid, machine.LastTransition);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Task<StateTransitionResult<MachineState, MachineTrigger>> pending = machine.Trigger(MachineTrigger.Start, cancellation.Token);
        StateTransitionResult<MachineState, MachineTrigger> cancelled = await pending;
        Assert.IsTrue(pending.IsCompletedSuccessfully);
        Assert.AreEqual(StateTransitionOutcome.Cancelled, cancelled.Outcome);
        Assert.AreSame(invalid, machine.LastTransition);
        StateTransitionResult<MachineState, MachineTrigger> unmapped = await machine.Trigger(MachineTrigger.Finish);
        Assert.AreEqual("StateMachine.TriggerNotAllowed", unmapped.ErrorCode);
        Assert.AreSame(unmapped, machine.LastTransition);
        Assert.AreEqual(MachineState.Idle, invalid.CurrentState);
        Assert.IsNull(unmapped.TargetState);
        Assert.IsNull(unmapped.HandlerReport);
        Assert.AreEqual(0, host.Scopes);
        Assert.AreEqual(3, host.Lifetime.Entries);
        Assert.AreEqual(2, host.Observation.Admissions);
        Assert.AreEqual(3, host.Observation.Completions.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HeldWorkAndDisposalKeepSourceVisibleWhileReentrantAndOverlappingCallsReturnBusy(bool cancelDuringDisposal)
    {
        await using var host = new StateMachineTestHost();
        using var caller = new CancellationTokenSource();
        var entered = StateMachineTestHost.Signal();
        var releaseWork = StateMachineTestHost.Signal();
        var disposing = StateMachineTestHost.Signal();
        var releaseDispose = StateMachineTestHost.Signal();
        IStateMachine<MachineState, MachineTrigger>? machine = null;
        host.Context.Forward = async (transition, _) =>
        {
            Assert.AreEqual(MachineState.Idle, transition.SourceState);
            Assert.AreEqual(MachineState.Ready, transition.TargetState);
            Assert.AreEqual(StateTransitionOutcome.Busy, (await machine!.Trigger(MachineTrigger.Start)).Outcome);
            entered.TrySetResult();
            await releaseWork.Task;
            host.Clock.Advance(TimeSpan.FromMilliseconds(7));
            return TransitionActionResult.Succeeded();
        };
        host.Context.DisposeHandler = async () =>
        {
            disposing.TrySetResult();
            await releaseDispose.Task;
            host.Clock.Advance(TimeSpan.FromMilliseconds(3));
        };
        var builder = host.Builder();
        builder.State(MachineState.Idle).On<StateMachineTestHandler>(MachineTrigger.Start, MachineState.Ready, "resolve");
        builder.State(MachineState.Ready).On(MachineTrigger.Finish, MachineState.Done);
        machine = builder.Build(MachineState.Idle);
        Task<StateTransitionResult<MachineState, MachineTrigger>> run = machine.Trigger(MachineTrigger.Start, caller.Token);
        try
        {
            await entered.Task.WaitAsync(StateMachineTestHost.Deadline);
            Assert.IsTrue(machine.IsExecuting);
            Assert.AreEqual(MachineState.Idle, machine.CurrentState);
            Assert.IsTrue(machine.CanTrigger(MachineTrigger.Start));
            Assert.AreEqual(StateTransitionOutcome.Busy, (await machine.Trigger(MachineTrigger.Start)).Outcome);
            Assert.IsNull(machine.LastTransition);
            releaseWork.TrySetResult();
            await disposing.Task.WaitAsync(StateMachineTestHost.Deadline);
            if (cancelDuringDisposal)
            {
                caller.Cancel();
            }
            Assert.IsFalse(run.IsCompleted);
            Assert.AreEqual(MachineState.Idle, machine.CurrentState);
            Assert.IsNull(machine.LastTransition);
            Assert.IsFalse(host.Context.Dependencies.Single().Disposed);
            releaseDispose.TrySetResult();
            StateTransitionResult<MachineState, MachineTrigger> result = await run.WaitAsync(StateMachineTestHost.Deadline);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(TimeSpan.FromMilliseconds(10), result.Duration);
            Assert.AreEqual(TimeSpan.FromMilliseconds(7), result.HandlerReport?.ExecutionDuration);
            Assert.AreEqual(PipelineStepErrorStatus.NotCalled, result.HandlerReport?.ErrorStatus);
            Assert.AreSame(result, machine.LastTransition);
            Assert.AreEqual(MachineState.Ready, machine.CurrentState);
            Assert.IsTrue((await machine.Trigger(MachineTrigger.Finish)).Success);
            Assert.AreEqual(1, host.Scopes);
            Assert.AreEqual(0, host.Context.HandlerCauses.Count);
        }
        finally
        {
            releaseWork.TrySetResult();
            releaseDispose.TrySetResult();
            await StateMachineTestHost.SettleAsync(run);
        }
    }

    [TestMethod]
    [DataRow("expected")]
    [DataRow("empty-message")]
    [DataRow("sync")]
    [DataRow("async")]
    [DataRow("null-task")]
    [DataRow("null-result")]
    [DataRow("foreign-oce")]
    [DataRow("child-wrapper")]
    public async Task HandlerFailuresReturnNormalFailedTasksAfterExactlyOneCompensationAndOwnedCleanup(string mode)
    {
        await using var host = new StateMachineTestHost();
        Exception original = mode switch
        {
            "foreign-oce" => new OperationCanceledException("Unrelated work ended."),
            "child-wrapper" => new PipelineExecutionException(new OperationCanceledException("The child retained a failed cancellation-shaped fault.")),
            _ => new InvalidOperationException("The selected application boundary failed.")
        };
        host.Context.Forward = (_, _) => mode switch
        {
            "expected" => Task.FromResult(TransitionActionResult.Failed("business.rejected")),
            "empty-message" => Task.FromResult(TransitionActionResult.Failed("business.rejected", "  ")),
            "sync" => throw original,
            "null-task" => null!,
            "null-result" => Task.FromResult<TransitionActionResult>(null!),
            _ => Task.FromException<TransitionActionResult>(original)
        };
        var builder = host.Builder();
        builder.State(MachineState.Idle).On<StateMachineTestHandler>(MachineTrigger.Start, MachineState.Ready);
        IStateMachine<MachineState, MachineTrigger> machine = builder.Build(MachineState.Idle);
        Task<StateTransitionResult<MachineState, MachineTrigger>> run = machine.Trigger(MachineTrigger.Start);
        StateTransitionResult<MachineState, MachineTrigger> result = await run.WaitAsync(StateMachineTestHost.Deadline);

        Assert.IsTrue(run.IsCompletedSuccessfully);
        Assert.AreEqual(StateTransitionOutcome.Failed, result.Outcome);
        Assert.AreEqual(MachineState.Idle, machine.CurrentState);
        Assert.AreEqual(MachineState.Ready, result.TargetState);
        Assert.AreSame(result.Error, host.Context.HandlerCauses.Single());
        Assert.AreEqual(PipelineStepExecutionStatus.Failed, result.HandlerReport?.ExecutionStatus);
        Assert.AreEqual(PipelineStepErrorStatus.Succeeded, result.HandlerReport?.ErrorStatus);
        if (mode is "expected" or "empty-message")
        {
            Assert.AreEqual("business.rejected", result.ErrorCode);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));
        }
        if (mode is "sync" or "async" or "foreign-oce" or "child-wrapper")
        {
            Assert.AreSame(original, result.Error?.Exception);
        }
        if (mode.StartsWith("null", StringComparison.Ordinal))
        {
            Assert.AreEqual(StateTransitionFailureKind.ContractViolation, result.Error?.Kind);
        }
        CollectionAssert.AreEqual(new[] { "construct", "execute", "error", "handler.dispose", "scope.dispose" }, host.Context.Events);
        Assert.AreEqual(1, host.Context.Instances.Single().AsyncDisposals);
        Assert.AreEqual(0, host.Context.Instances[0].SyncDisposals);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConstructorAndDisposalCancellationExceptionsRemainFailedPhaseFacts(bool disposal)
    {
        await using var host = new StateMachineTestHost();
        using var caller = new CancellationTokenSource();
        var original = new OperationCanceledException("A phase fault used the execution token.", caller.Token);
        if (disposal)
        {
            host.Context.DisposeHandler = () => { caller.Cancel(); return ValueTask.FromException(original); };
        }
        else
        {
            host.Context.Construct = () => { caller.Cancel(); throw original; };
        }
        var builder = host.Builder();
        builder.State(MachineState.Idle).On<StateMachineTestHandler>(MachineTrigger.Start, MachineState.Ready);
        IStateMachine<MachineState, MachineTrigger> machine = builder.Build(MachineState.Idle);
        StateTransitionResult<MachineState, MachineTrigger> result = await machine.Trigger(MachineTrigger.Start, caller.Token);

        Assert.AreEqual(StateTransitionOutcome.Failed, result.Outcome);
        Assert.AreSame(original, result.Error?.Exception);
        Assert.AreEqual(disposal ? StateTransitionPhase.Disposal : StateTransitionPhase.Construction, result.Error?.Phase);
        Assert.AreEqual(disposal ? PipelineStepExecutionStatus.Succeeded : PipelineStepExecutionStatus.NotStarted,
            result.HandlerReport?.ExecutionStatus);
        Assert.AreEqual(PipelineStepErrorStatus.NotCalled, result.HandlerReport?.ErrorStatus);
        Assert.AreEqual(MachineState.Idle, machine.CurrentState);
        Assert.AreEqual(0, host.Context.HandlerCauses.Count);
        Assert.AreEqual(1, result.LifecycleFailures.Count);
        Assert.IsTrue(host.Context.Dependencies.Single().Disposed);
    }

    [TestMethod]
    public async Task FinalCheckpointCancellationKeepsSuccessfulForwardFactAndUsesIndependentCompensation()
    {
        await using var host = new StateMachineTestHost();
        using var caller = new CancellationTokenSource();
        host.Context.Forward = (_, _) => { caller.Cancel(); return Task.FromResult(TransitionActionResult.Succeeded()); };
        host.Context.Compensate = (_, _, token) =>
        {
            Assert.AreNotEqual(caller.Token, token);
            Assert.IsFalse(token.IsCancellationRequested);
            return Task.CompletedTask;
        };
        var builder = host.Builder();
        builder.State(MachineState.Idle).On<StateMachineTestHandler>(MachineTrigger.Start, MachineState.Ready);
        IStateMachine<MachineState, MachineTrigger> machine = builder.Build(MachineState.Idle);
        Task<StateTransitionResult<MachineState, MachineTrigger>> task = machine.Trigger(MachineTrigger.Start, caller.Token);
        StateTransitionResult<MachineState, MachineTrigger> result = await task;

        Assert.IsTrue(task.IsCompletedSuccessfully);
        Assert.AreEqual(StateTransitionOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(PipelineStepExecutionStatus.Succeeded, result.HandlerReport?.ExecutionStatus);
        Assert.AreEqual(PipelineStepErrorStatus.Succeeded, result.HandlerReport?.ErrorStatus);
        Assert.AreEqual(MachineState.Idle, machine.CurrentState);
        Assert.AreSame(result, machine.LastTransition);
    }

    [TestMethod]
    public async Task IgnoringForwardAndCompensationKeepTheGateAndServicesUntilActualSettlement()
    {
        await using var host = new StateMachineTestHost();
        using var caller = new CancellationTokenSource();
        var entered = StateMachineTestHost.Signal();
        var releaseWork = StateMachineTestHost.Signal();
        var compensating = StateMachineTestHost.Signal();
        var releaseCleanup = StateMachineTestHost.Signal();
        host.Context.Forward = async (_, _) =>
        {
            entered.TrySetResult();
            await releaseWork.Task;
            return TransitionActionResult.Succeeded();
        };
        host.Context.Compensate = async (_, _, token) =>
        {
            Assert.IsFalse(token.IsCancellationRequested);
            compensating.TrySetResult();
            await releaseCleanup.Task;
        };
        var builder = host.Builder(new StateMachineOptions { ErrorHandlingTimeout = TimeSpan.FromMilliseconds(20) });
        builder.State(MachineState.Idle).On<StateMachineTestHandler>(MachineTrigger.Start, MachineState.Ready);
        IStateMachine<MachineState, MachineTrigger> machine = builder.Build(MachineState.Idle);
        Task<StateTransitionResult<MachineState, MachineTrigger>> run = machine.Trigger(MachineTrigger.Start, caller.Token);
        try
        {
            await entered.Task.WaitAsync(StateMachineTestHost.Deadline);
            caller.Cancel();
            Assert.IsFalse(run.IsCompleted);
            Assert.AreEqual(0, host.Context.HandlerCauses.Count);
            Assert.AreEqual(0, host.Context.Instances[0].AsyncDisposals);
            Assert.AreEqual(StateTransitionOutcome.Busy, (await machine.Trigger(MachineTrigger.Start)).Outcome);
            releaseWork.TrySetResult();
            await compensating.Task.WaitAsync(StateMachineTestHost.Deadline);
            host.Clock.Advance(TimeSpan.FromMilliseconds(21));
            Assert.AreEqual(1, host.Observation.Expiries);
            Assert.IsFalse(run.IsCompleted);
            Assert.IsFalse(host.Context.Dependencies[0].Disposed);
            releaseCleanup.TrySetResult();
            StateTransitionResult<MachineState, MachineTrigger> result = await run.WaitAsync(StateMachineTestHost.Deadline);
            Assert.AreEqual(StateTransitionOutcome.Cancelled, result.Outcome);
            Assert.IsTrue(result.CleanupBudgetExpired);
            Assert.AreEqual(PipelineStepErrorStatus.Succeeded, result.HandlerReport?.ErrorStatus);
            Assert.AreEqual(0, result.ErrorHandlingFailures.Count);
            Assert.AreEqual(1, host.Observation.Expiries);
            Assert.AreEqual(MachineState.Idle, machine.CurrentState);
        }
        finally
        {
            releaseWork.TrySetResult();
            releaseCleanup.TrySetResult();
            await StateMachineTestHost.SettleAsync(run);
        }
    }

    [TestMethod]
    [DataRow("throw")]
    [DataRow("null")]
    [DataRow("cleanup-cancel")]
    [DataRow("foreign-cancel")]
    public async Task CompensationFailuresRemainSecondaryToTheOriginalBusinessCause(string mode)
    {
        await using var host = new StateMachineTestHost();
        var secondary = new OperationCanceledException("Not this cleanup token.");
        host.Context.Forward = (_, _) => Task.FromResult(TransitionActionResult.Failed("business.rejected"));
        host.Context.Compensate = (_, _, token) =>
        {
            if (mode == "cleanup-cancel")
            {
                host.Clock.Advance(TimeSpan.FromMilliseconds(21));
                token.ThrowIfCancellationRequested();
            }
            if (mode == "null")
            {
                return null!;
            }
            return Task.FromException(mode == "foreign-cancel" ? secondary : new InvalidOperationException("Compensation failed."));
        };
        var builder = host.Builder(new StateMachineOptions { ErrorHandlingTimeout = TimeSpan.FromMilliseconds(20) });
        builder.State(MachineState.Idle).On<StateMachineTestHandler>(MachineTrigger.Start, MachineState.Ready);
        StateTransitionResult<MachineState, MachineTrigger> result = await builder.Build(MachineState.Idle).Trigger(MachineTrigger.Start);

        Assert.AreEqual(StateTransitionOutcome.Failed, result.Outcome);
        Assert.AreEqual("business.rejected", result.ErrorCode);
        Assert.AreSame(result.Error, host.Context.HandlerCauses.Single());
        Assert.AreEqual(1, result.ErrorHandlingFailures.Count);
        Assert.AreEqual(mode == "cleanup-cancel" ? PipelineStepErrorStatus.Cancelled : PipelineStepErrorStatus.Failed, result.HandlerReport?.ErrorStatus);
        if (mode == "foreign-cancel")
        {
            Assert.AreSame(secondary, result.ErrorHandlingFailures[0].Exception);
        }
        Assert.IsTrue(host.Context.Dependencies.Single().Disposed);
    }

    [TestMethod]
    public async Task ClosedHostRejectsBeforeAnyCallObservationOrInstanceAdmission()
    {
        await using var host = new StateMachineTestHost();
        IStateMachine<MachineState, MachineTrigger> machine = host.Builder().Build(MachineState.Idle);
        host.Lifetime.Closed = true;
        Assert.ThrowsExactly<InvalidOperationException>(() => { _ = machine.Trigger(MachineTrigger.Start); });
        Assert.IsNull(machine.LastTransition);
        Assert.IsFalse(machine.IsExecuting);
        Assert.AreEqual(0, host.Observation.Calls);
        Assert.AreEqual(0, host.Lifetime.Entries);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnavailableOrThrowingObservationPreservesOperationalSuccess(bool factory)
    {
        await using var host = new StateMachineTestHost();
        if (factory)
        {
            host.ObserveCall = _ => throw new InvalidOperationException("Observation creation failed.");
        }
        else
        {
            host.Observation.Callback = () => throw new InvalidOperationException("Recording failed.");
        }
        var builder = host.Builder();
        builder.State(MachineState.Idle).On<StateMachineTestHandler>(MachineTrigger.Start, MachineState.Ready);
        IStateMachine<MachineState, MachineTrigger> machine = builder.Build(MachineState.Idle);
        StateTransitionResult<MachineState, MachineTrigger> result = await machine.Trigger(MachineTrigger.Start);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(MachineState.Ready, machine.CurrentState);
        Assert.AreEqual(0, result.LifecycleFailures.Count);
        Assert.IsTrue(host.Context.Dependencies.Single().Disposed);
    }
}
