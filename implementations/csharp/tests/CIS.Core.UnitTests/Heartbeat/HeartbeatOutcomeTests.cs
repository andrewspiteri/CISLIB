using CIS.Heartbeat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Heartbeat;

[TestClass]
public sealed class HeartbeatOutcomeTests
{
    [TestMethod]
    [DataRow("condition")]
    [DataRow("callback")]
    public async Task FalseResultsDoNotInventExceptionsOrInvokeTheErrorHandler(string phase)
    {
        await using var scope = new HeartbeatTestScope();
        var subscriber = new HeartbeatProbe { Callback = _ => Task.FromResult(false) };
        HeartbeatRecordingObserver facts = scope.Register(subscriber,
            condition: phase == "condition" ? _ => Task.FromResult(false) : null);
        scope.Activate();
        scope.Executor.RunNext();
        scope.WaitSettled(facts);
        HeartbeatObservedFact outcome = facts.Last(HeartbeatEvent.Outcome);
        Assert.AreEqual(1, facts.Count(HeartbeatEvent.Outcome));
        Assert.AreEqual(phase == "condition" ? HeartbeatResult.Skipped : HeartbeatResult.Failed, outcome.Notification.Outcome);
        Assert.AreEqual(phase == "condition" ? 0 : 1, subscriber.Calls);
        Assert.AreEqual(0, subscriber.Errors);
        Assert.IsNull(outcome.OriginalErrorType);
        if (phase == "condition") { Assert.AreEqual(false, outcome.Notification.ConditionResult); }
        else { Assert.AreEqual(false, outcome.Notification.CallbackResult); }
    }

    [TestMethod]
    [DataRow("condition")]
    [DataRow("callback")]
    public async Task ErrorHandlingCleanupAndReportingEachRetainTheInstanceSlotAndOwnerUntilTheyFinish(string phase)
    {
        await using var scope = new HeartbeatTestScope();
        TaskCompletionSource handler = scope.Hold();
        TaskCompletionSource cleanup = scope.Hold();
        TaskCompletionSource reporting = scope.Hold();
        var originating = new InvalidOperationException("originating failure");
        Exception? received = null;
        var subscriber = new HeartbeatProbe
        {
            Callback = _ => Task.FromException<bool>(originating),
            Error = error => { received = error; return handler.Task; }
        };
        var observer = new HeartbeatRecordingObserver { Flush = () => new ValueTask(reporting.Task) };
        HeartbeatRecordingObserver facts = scope.Register(subscriber,
            condition: phase == "condition" ? _ => Task.FromException<bool>(originating) : null,
            cleanup: () => new ValueTask(cleanup.Task), observer: observer);
        var independent = new HeartbeatProbe();
        HeartbeatRecordingObserver independentFacts = scope.Register(independent);
        scope.Activate();
        scope.Executor.RunNext();
        Task unregister = scope.Owner.Service.UnregisterAsync(subscriber);
        Assert.AreSame(originating, received);
        Assert.AreEqual(1, subscriber.Errors);
        Assert.AreEqual(new HeartbeatSchedulerCounts(2, 2, 1, 1), scope.Runtime.Inspect());
        Assert.AreEqual(2, scope.Owner.Holds);
        Assert.AreEqual(0, facts.Count(HeartbeatEvent.Outcome));
        Assert.IsFalse(unregister.IsCompleted);

        scope.Clock.Advance(TimeSpan.FromMilliseconds(1));
        handler.TrySetResult();
        HeartbeatTestScope.WaitUntil(() => facts.Count(HeartbeatEvent.ErrorHandlingCompleted) == 1,
            "Awaited error handling did not complete.");
        Assert.IsFalse(unregister.IsCompleted);
        Assert.AreEqual(1, scope.Runtime.Inspect().Dispatched);
        Assert.AreEqual(0, scope.Executor.PendingCount);
        Assert.AreEqual(0, facts.Count(HeartbeatEvent.Outcome));

        scope.Clock.Advance(TimeSpan.FromMilliseconds(1));
        cleanup.TrySetResult();
        HeartbeatTestScope.WaitUntil(() => observer.Flushes == 1, "The owned reporting handoff was not reached.");
        Assert.AreEqual(1, facts.Count(HeartbeatEvent.Outcome));
        Assert.AreEqual(HeartbeatResult.Failed, facts.Last(HeartbeatEvent.Outcome).Notification.Outcome);
        Assert.AreEqual(typeof(InvalidOperationException).FullName, facts.Last(HeartbeatEvent.Outcome).OriginalErrorType);
        Assert.AreEqual(0, facts.Count(HeartbeatEvent.Settled));
        Assert.IsFalse(unregister.IsCompleted);
        Assert.AreEqual(2, scope.Owner.Holds);

        scope.Clock.Advance(TimeSpan.FromMilliseconds(1));
        reporting.TrySetResult();
        await unregister.WaitAsync(HeartbeatTestScope.Deadline);
        HeartbeatNotification settled = facts.Last(HeartbeatEvent.Settled).Notification;
        Assert.IsTrue(settled.SettledAtUtc.HasValue && settled.WorkCompletedAtUtc.HasValue
            && settled.SettledAtUtc.Value > settled.WorkCompletedAtUtc.Value);
        Assert.AreEqual(1, facts.Count(HeartbeatEvent.Settled));
        Assert.AreEqual(1, facts.Count(HeartbeatEvent.Outcome));
        HeartbeatTestScope.WaitUntil(() => scope.Executor.PendingCount == 1, "The slot was not reused after actual settlement.");
        scope.Executor.RunNext();
        scope.WaitSettled(independentFacts);
        Assert.AreEqual(1, independent.Calls);
    }

    [TestMethod]
    [DataRow("fault")]
    [DataRow("null")]
    public async Task ASecondaryHandlerFaultCannotRecurseOrReplaceTheOriginatingFailure(string handlerBehavior)
    {
        await using var scope = new HeartbeatTestScope();
        var originating = new InvalidOperationException("primary");
        var subscriber = new HeartbeatProbe
        {
            Callback = _ => Task.FromException<bool>(originating),
            Error = _ => handlerBehavior == "null" ? null! : Task.FromException(new IOException("secondary"))
        };
        HeartbeatRecordingObserver facts = scope.Register(subscriber);
        scope.Activate();
        scope.Executor.RunNext();
        scope.WaitSettled(facts);
        HeartbeatObservedFact outcome = facts.Last(HeartbeatEvent.Outcome);
        Assert.AreEqual(1, subscriber.Errors);
        Assert.AreEqual(1, facts.Count(HeartbeatEvent.Outcome));
        Assert.AreEqual(HeartbeatResult.Failed, outcome.Notification.Outcome);
        Assert.AreEqual(typeof(InvalidOperationException).FullName, outcome.OriginalErrorType);
        Assert.AreEqual(handlerBehavior == "null" ? typeof(InvalidOperationException).FullName : typeof(IOException).FullName,
            outcome.HandlerErrorType);
        Assert.AreEqual(HeartbeatStep.Failed, outcome.Notification.ErrorHandlingStatus);
    }

    [TestMethod]
    [DataRow("callback-true")]
    [DataRow("condition-false")]
    [DataRow("unstarted")]
    public async Task CleanupFailureOverridesARealResultButCannotFabricateAnUnstartedCallbackOutcome(string execution)
    {
        await using var scope = new HeartbeatTestScope();
        var subscriber = new HeartbeatProbe();
        HeartbeatRecordingObserver facts = scope.Register(subscriber,
            condition: execution == "condition-false" ? _ => Task.FromResult(false) : null,
            cleanup: () => ValueTask.FromException(new IOException("owned cleanup")));
        scope.Activate();
        Task? stop = execution == "unstarted" ? scope.Runtime.StopAsync() : null;
        scope.Executor.RunNext();
        scope.WaitSettled(facts);
        if (stop is not null) { await stop.WaitAsync(HeartbeatTestScope.Deadline); }
        Assert.AreEqual(0, subscriber.Errors, "Integration cleanup cannot invoke subscriber recovery again.");
        Assert.AreEqual(1, facts.Count(HeartbeatEvent.CleanupCompleted));
        Assert.AreEqual(1, facts.Count(HeartbeatEvent.IntegrationFault));
        if (execution == "unstarted")
        {
            Assert.AreEqual(0, subscriber.Calls);
            Assert.AreEqual(0, facts.Count(HeartbeatEvent.Outcome));
            Assert.AreEqual(1, facts.Count(HeartbeatEvent.Suppressed));
            Assert.IsNull(facts.Last(HeartbeatEvent.Suppressed).Notification.CallbackResult);
        }
        else
        {
            Assert.AreEqual(1, facts.Count(HeartbeatEvent.Outcome));
            HeartbeatObservedFact outcome = facts.Last(HeartbeatEvent.Outcome);
            Assert.AreEqual(HeartbeatResult.Failed, outcome.Notification.Outcome);
            Assert.AreEqual(typeof(IOException).FullName, outcome.CleanupErrorType);
            if (execution == "callback-true") { Assert.AreEqual(true, outcome.Notification.CallbackResult); }
            else { Assert.AreEqual(false, outcome.Notification.ConditionResult); }
        }
        HeartbeatInfrastructureFault fault = await scope.Runtime.InfrastructureFailure.WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual("Heartbeat.ExecutionCleanup", fault.Code);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task AcknowledgedKnownCancellationAndUnexplainedCancellationHaveDistinctOutcomes(bool known)
    {
        await using var scope = new HeartbeatTestScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var subscriber = new HeartbeatProbe
        {
            Callback = _ => known ? Task.FromCanceled<bool>(cancellation.Token)
                : Task.FromException<bool>(new OperationCanceledException("unrelated cancellation"))
        };
        HeartbeatRecordingObserver facts = scope.Register(subscriber, executionCancellation: cancellation.Token);
        scope.Activate();
        scope.Executor.RunNext();
        scope.WaitSettled(facts);
        HeartbeatNotification outcome = facts.Last(HeartbeatEvent.Outcome).Notification;
        Assert.AreEqual(known ? HeartbeatResult.ExpectedCancellation : HeartbeatResult.Failed, outcome.Outcome);
        Assert.AreEqual(known ? 0 : 1, subscriber.Errors);
        HeartbeatPhase? expectedCancellationPhase = known ? HeartbeatPhase.Callback : null;
        HeartbeatPhase? actualCancellationPhase = outcome.ExpectedCancellationPhase;
        Assert.AreEqual(expectedCancellationPhase, actualCancellationPhase);
        Assert.AreEqual(1, facts.Count(HeartbeatEvent.Outcome));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ARequestedTokenDoesNotReplaceANormallyReturnedBoolean(bool callbackResult)
    {
        await using var scope = new HeartbeatTestScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var subscriber = new HeartbeatProbe { Callback = _ => Task.FromResult(callbackResult) };
        HeartbeatRecordingObserver facts = scope.Register(subscriber, executionCancellation: cancellation.Token);
        scope.Activate();
        scope.Executor.RunNext();
        scope.WaitSettled(facts);
        HeartbeatNotification outcome = facts.Last(HeartbeatEvent.Outcome).Notification;
        Assert.AreEqual(callbackResult ? HeartbeatResult.Succeeded : HeartbeatResult.Failed, outcome.Outcome);
        Assert.AreEqual(callbackResult, outcome.CallbackResult);
        Assert.IsNull(outcome.ExpectedCancellationPhase);
        Assert.AreEqual(0, subscriber.Errors);
    }

    [TestMethod]
    [DataRow("condition")]
    [DataRow("callback")]
    [DataRow("handler")]
    public async Task DisposalExceptionsRetireTheRegistrationAfterItsActualFailureAndHandlingSettle(string phase)
    {
        await using var scope = new HeartbeatTestScope();
        TaskCompletionSource handler = scope.Hold();
        Exception original = phase == "handler" ? new InvalidOperationException("primary") : new ObjectDisposedException("dependency");
        var subscriber = new HeartbeatProbe
        {
            Callback = _ => Task.FromException<bool>(original),
            Error = _ => phase == "handler" ? Task.FromException(new ObjectDisposedException("handler dependency")) : handler.Task
        };
        HeartbeatRecordingObserver facts = scope.Register(subscriber,
            condition: phase == "condition" ? _ => Task.FromException<bool>(original) : null);
        scope.Activate();
        scope.Executor.RunNext();
        if (phase != "handler")
        {
            Assert.AreEqual(1, scope.Runtime.Inspect().Accepted);
            Assert.AreEqual(1, scope.Owner.Holds);
            Assert.AreEqual(0, facts.Count(HeartbeatEvent.Settled));
            handler.TrySetResult();
        }
        HeartbeatTestScope.WaitUntil(() => scope.Runtime.Inspect().Registrations == 0, "A disposal failure did not retire the registration.");
        Assert.AreEqual(1, facts.Count(HeartbeatEvent.Outcome));
        Assert.AreEqual(HeartbeatResult.Failed, facts.Last(HeartbeatEvent.Outcome).Notification.Outcome);
        Assert.AreEqual(1, facts.Count(HeartbeatEvent.Settled));
        Assert.AreEqual(1, facts.Count(HeartbeatEvent.Retired));
        Assert.AreEqual(1, subscriber.Errors, "A dependency disposal exception is not proof that the subscriber is unsafe to handle it.");
        scope.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(1, scope.Executor.Posts);
    }

    [TestMethod]
    public async Task ReportingFailureIsAnInfrastructureFaultAndCannotInvokeSubscriberRecoveryOrStopItsPeer()
    {
        await using var scope = new HeartbeatTestScope(capacity: 2);
        var first = new HeartbeatProbe();
        var other = new HeartbeatProbe();
        var observer = new HeartbeatRecordingObserver
        {
            OnReport = fact =>
            {
                if (fact.Kind == HeartbeatEvent.Outcome) { throw new IOException("reporting unavailable"); }
            }
        };
        scope.Register(first, observer: observer);
        HeartbeatRecordingObserver otherFacts = scope.Register(other);
        scope.Activate(2);
        scope.Executor.RunAvailable();
        scope.WaitSettled(observer);
        scope.WaitSettled(otherFacts);
        HeartbeatInfrastructureFault fault = await scope.Runtime.InfrastructureFailure.WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual("Heartbeat.Reporting", fault.Code);
        Assert.AreEqual(1, first.Calls);
        Assert.AreEqual(0, first.Errors);
        Assert.AreEqual(1, other.Calls);
        Assert.AreEqual(HeartbeatResult.Succeeded, otherFacts.Last(HeartbeatEvent.Outcome).Notification.Outcome);
    }
}
