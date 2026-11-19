using CIS.Workflows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Workflows;

[TestClass]
public sealed class StateMachineGuardCleanupTests
{
    [TestMethod]
    [DataRow("success-no-target")]
    [DataRow("success-wrong-current")]
    [DataRow("failed-state-change")]
    [DataRow("busy-selected-work")]
    [DataRow("cancel-wrong-kind")]
    [DataRow("success-secondary")]
    [DataRow("unentered-compensation")]
    public void PublicConstructionCannotRepresentContradictoryTerminalFacts(string scenario)
    {
        if (scenario == "unentered-compensation")
        {
            Assert.ThrowsExactly<ArgumentException>(() => new StateTransitionHandlerReport("resolve",
                PipelineStepExecutionStatus.NotStarted, PipelineStepErrorStatus.Succeeded, errorDuration: TimeSpan.Zero));
            return;
        }

        var exception = new StateTransitionFailure("test.failed", "The operation failed.",
            StateTransitionPhase.Disposal, StateTransitionFailureKind.Exception);
        var rejection = new StateTransitionFailure("StateMachine.Busy", "Another attempt owns the machine.",
            StateTransitionPhase.Admission, StateTransitionFailureKind.Rejection);
        StateTransitionOutcome outcome = scenario switch
        {
            "failed-state-change" => StateTransitionOutcome.Failed,
            "busy-selected-work" => StateTransitionOutcome.Busy,
            "cancel-wrong-kind" => StateTransitionOutcome.Cancelled,
            _ => StateTransitionOutcome.Succeeded
        };
        MachineState? target = scenario is "success-no-target" or "cancel-wrong-kind" ? null : MachineState.Ready;
        MachineState current = scenario is "failed-state-change" or "success-secondary" ? MachineState.Ready : MachineState.Idle;
        StateTransitionFailure? error = outcome == StateTransitionOutcome.Succeeded ? null
            : outcome == StateTransitionOutcome.Busy ? rejection : exception;
        Assert.ThrowsExactly<ArgumentException>(() => StateTransitionResult<MachineState, MachineTrigger>.Create(
            "test-machine", Guid.NewGuid(), MachineTrigger.Start, MachineState.Idle, target, current, outcome, error,
            lifecycleFailures: scenario == "success-secondary" ? [exception] : null));
    }

    [TestMethod]
    public void PublicFailureResultDetachesDiagnosticsAndPreservesOriginalCauseForConsumerMocks()
    {
        var original = new InvalidOperationException("The owned object failed to dispose.");
        var failure = new StateTransitionFailure("StateMachine.Disposal", "Owned disposal failed.",
            StateTransitionPhase.Disposal, StateTransitionFailureKind.Exception, original);
        var supplied = new List<StateTransitionFailure> { failure };
        StateTransitionResult<MachineState, MachineTrigger> result = StateTransitionResult<MachineState, MachineTrigger>.Create(
            "mock-machine", Guid.NewGuid(), MachineTrigger.Start, MachineState.Idle, MachineState.Ready, MachineState.Idle,
            StateTransitionOutcome.Failed, failure, TimeSpan.FromMilliseconds(3),
            new StateTransitionHandlerReport("resolve", PipelineStepExecutionStatus.Succeeded, PipelineStepErrorStatus.NotCalled),
            lifecycleFailures: supplied);
        supplied.Clear();

        Assert.IsFalse(result.Success);
        Assert.IsFalse(result.StateChanged);
        Assert.AreSame(original, result.Error?.Exception);
        Assert.AreSame(failure, result.LifecycleFailures.Single());
        Assert.AreEqual(PipelineStepExecutionStatus.Succeeded, result.HandlerReport?.ExecutionStatus);
        Assert.AreEqual(MachineState.Idle, result.CurrentState);
    }

    [TestMethod]
    public async Task BudgetRequestsCancellationBeforeReportingAndJoinsActualHeldCancellationCallbacks()
    {
        await using var host = new StateMachineTestHost();
        var errorEntered = StateMachineTestHost.Signal();
        var allowErrorReturn = StateMachineTestHost.Signal();
        var errorPhaseFinished = StateMachineTestHost.Signal();
        var callbackEntered = StateMachineTestHost.Signal();
        var releaseCallback = StateMachineTestHost.Signal();
        var callbackFault = new InvalidOperationException("A registered cleanup callback failed after its real work ended.");
        CancellationToken cleanupToken = default;
        CancellationTokenRegistration registration = default;
        bool requestedAtExpiry = false;
        int observedExpiry = 0;
        host.Context.Forward = (_, _) => Task.FromResult(TransitionActionResult.Failed("business.rejected"));
        host.Context.Compensate = async (_, _, token) =>
        {
            cleanupToken = token;
            registration = token.Register(() =>
            {
                callbackEntered.TrySetResult();
                releaseCallback.Task.GetAwaiter().GetResult();
                throw callbackFault;
            });
            errorEntered.TrySetResult();
            await allowErrorReturn.Task;
        };
        host.Observation.Callback = () =>
        {
            if (host.Observation.Expiries != 0 && Interlocked.CompareExchange(ref observedExpiry, 1, 0) == 0)
            {
                requestedAtExpiry = cleanupToken.IsCancellationRequested;
            }
            if (host.Observation.Phases.Any(phase => phase.Phase == PipelinePhase.Error))
            {
                errorPhaseFinished.TrySetResult();
            }
        };
        var builder = host.Builder(new StateMachineOptions { ErrorHandlingTimeout = TimeSpan.FromMilliseconds(20) });
        builder.State(MachineState.Idle).On<StateMachineTestHandler>(MachineTrigger.Start, MachineState.Ready);
        IStateMachine<MachineState, MachineTrigger> machine = builder.Build(MachineState.Idle);
        Task<StateTransitionResult<MachineState, MachineTrigger>> run = machine.Trigger(MachineTrigger.Start);
        Task? advancing = null;
        try
        {
            await errorEntered.Task.WaitAsync(StateMachineTestHost.Deadline);
            advancing = Task.Run(() => host.Clock.Advance(TimeSpan.FromMilliseconds(21)));
            await advancing.WaitAsync(StateMachineTestHost.Deadline);
            await callbackEntered.Task.WaitAsync(StateMachineTestHost.Deadline);
            Assert.IsTrue(requestedAtExpiry);
            Assert.AreEqual(1, host.Observation.Expiries);
            allowErrorReturn.TrySetResult();
            await errorPhaseFinished.Task.WaitAsync(StateMachineTestHost.Deadline);
            Assert.IsFalse(run.IsCompleted);
            Assert.IsTrue(machine.IsExecuting);
            Assert.AreEqual(MachineState.Idle, machine.CurrentState);
            Assert.AreEqual(0, host.Context.Instances.Single().AsyncDisposals);
            Assert.IsFalse(host.Context.Dependencies.Single().Disposed);
            releaseCallback.TrySetResult();
            StateTransitionResult<MachineState, MachineTrigger> result = await run.WaitAsync(StateMachineTestHost.Deadline);
            Assert.AreEqual("business.rejected", result.ErrorCode);
            Assert.IsTrue(result.CleanupBudgetExpired);
            Assert.AreEqual(PipelineStepErrorStatus.Succeeded, result.HandlerReport?.ErrorStatus);
            Assert.AreEqual(0, result.ErrorHandlingFailures.Count);
            Exception? diagnostic = result.LifecycleFailures.Single().Exception;
            Assert.IsInstanceOfType<AggregateException>(diagnostic);
            Assert.AreSame(callbackFault, ((AggregateException)diagnostic!).InnerExceptions.Single());
            Assert.IsTrue(host.Context.Dependencies.Single().Disposed);
            Assert.AreEqual(1, host.Observation.Expiries);
        }
        finally
        {
            allowErrorReturn.TrySetResult();
            releaseCallback.TrySetResult();
            await StateMachineTestHost.SettleAsync(advancing);
            await StateMachineTestHost.SettleAsync(run);
            await registration.DisposeAsync();
        }
    }
}
