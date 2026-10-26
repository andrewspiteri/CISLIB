using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using CIS.Healthcheck;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorRunnerSchedulingTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset Epoch = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ManualCollectionBeforeStartRejectsWithoutAcquiringTheOwner()
    {
        await using var scope = new RunnerScope();
        Registration registration = scope.Add("manual");

        await Assert.ThrowsAsync<InvalidOperationException>(() => registration.Handle.CollectAsync()).WaitAsync(Deadline);
        Assert.AreEqual(0, registration.Collector.Acquisitions);
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> wait = registration.Handle.CollectAsync();
        Invocation invocation = await scope.NextForPendingCallerAsync(wait);
        // A caller joining the gated callback observes the actual operation,
        // independently of the first queued caller's completion notification.
        Task<HealthCollectionAttempt> coalesced = registration.Handle.CollectAsync();
        invocation.Complete();
        await coalesced.WaitAsync(Deadline);
        await scope.DisposeAsync();
        Assert.IsTrue(wait.IsCompleted,
            "The original queued caller must complete after its actual operation and resources have settled.");
        Assert.AreEqual(HealthCollectionDisposition.Committed, (await wait).Disposition);
    }

    [TestMethod]
    public async Task PreCanceledStartDoesNotEnableManualOrPeriodicAdmission()
    {
        await using var scope = new RunnerScope();
        Registration registration = scope.Add("start", TimeSpan.FromSeconds(5));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => scope.Runner.StartAsync(canceled.Token)).WaitAsync(Deadline);
        await Assert.ThrowsAsync<InvalidOperationException>(() => registration.Handle.CollectAsync()).WaitAsync(Deadline);
        Assert.ThrowsExactly<InvalidOperationException>(registration.Handle.ActivatePeriodic);
        Assert.AreEqual(0, registration.Collector.Acquisitions);

        Task<TimeSpan> startupChange = scope.Clock.NextChangeAsync();
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await startupChange;
        Assert.AreEqual(0, scope.Acquisitions, "Actual startup must leave unactivated periodic owners inert.");
        Task<TimeSpan> activatedChange = scope.Clock.NextChangeAsync();
        registration.Handle.ActivatePeriodic();
        await activatedChange;
        Assert.AreEqual(1, scope.Acquisitions, "Actual activation must acquire its first due owner before the next idle Change.");
        Invocation invocation = await scope.NextAsync();
        Task<HealthCollectionAttempt> wait = registration.Handle.CollectAsync();
        invocation.Complete();
        await wait.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task RepeatedStartIsHarmlessAndDoesNotActivateConfiguredPeriods()
    {
        await using var scope = new RunnerScope();
        Registration registration = scope.Add("explicit", TimeSpan.FromSeconds(2));

        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        scope.Clock.Advance(TimeSpan.FromDays(1));
        Assert.AreEqual(0, registration.Collector.Acquisitions);
        Assert.IsFalse(scope.Snapshot(registration).Collection!.InProgress);

        Task<HealthCollectionAttempt> wait = registration.Handle.CollectAsync();
        Invocation invocation = await scope.NextForPendingCallerAsync(wait);
        Task<HealthCollectionAttempt> coalesced = registration.Handle.CollectAsync();
        invocation.Complete();
        await coalesced.WaitAsync(Deadline);
        scope.Clock.Advance(TimeSpan.FromDays(1));
        Assert.AreEqual(1, registration.Collector.Acquisitions);
        await scope.DisposeAsync();
        await AssertOriginalQueuedCallersCompleteAsync(wait);
        await wait.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task PeriodicActivationRequiresBothStartAndAnExplicitInterval()
    {
        await using var scope = new RunnerScope();
        Registration periodic = scope.Add("periodic", TimeSpan.FromSeconds(1));
        Registration manual = scope.Add("manual");

        Assert.ThrowsExactly<InvalidOperationException>(periodic.Handle.ActivatePeriodic);
        Task<TimeSpan> startupChange = scope.Clock.NextChangeAsync();
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await startupChange;
        Assert.AreEqual(0, scope.Acquisitions, "Actual startup must leave unactivated periodic owners inert.");
        Assert.ThrowsExactly<InvalidOperationException>(manual.Handle.ActivatePeriodic);
        Task<TimeSpan> activatedChange = scope.Clock.NextChangeAsync();
        periodic.Handle.ActivatePeriodic();
        periodic.Handle.ActivatePeriodic();
        await activatedChange;
        Assert.AreEqual(1, scope.Acquisitions, "Actual activation must acquire its first due owner before the next idle Change.");
        Invocation invocation = await scope.NextAsync();
        Assert.AreEqual("periodic", invocation.Name);
        Assert.AreEqual(1, periodic.Collector.Acquisitions);
        Assert.AreEqual(0, manual.Collector.Acquisitions);
        Task<HealthCollectionAttempt> wait = periodic.Handle.CollectAsync();
        invocation.Complete();
        await wait.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task FirstPeriodicAttemptIsDueImmediatelyWithoutAdvancingTime()
    {
        await using var scope = new RunnerScope();
        Registration registration = scope.Add("first", TimeSpan.FromHours(1));
        Task<TimeSpan> startupChange = scope.Clock.NextChangeAsync();
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await startupChange;
        Assert.AreEqual(0, scope.Acquisitions, "Actual startup must leave unactivated periodic owners inert.");

        Task<TimeSpan> activatedChange = scope.Clock.NextChangeAsync();
        registration.Handle.ActivatePeriodic();
        await activatedChange;
        Assert.AreEqual(1, scope.Acquisitions, "Actual activation must acquire its first due owner before the next idle Change.");
        Invocation invocation = await scope.NextAsync();
        Assert.AreEqual(0L, scope.Clock.GetTimestamp());
        Task<HealthCollectionAttempt> wait = registration.Handle.CollectAsync();
        invocation.Complete();
        await wait.WaitAsync(Deadline);
        Assert.AreEqual(1, registration.Collector.Acquisitions);
    }

    [TestMethod]
    public async Task NextPeriodicAttemptUsesCompletionPlusIntervalInsteadOfStartPlusInterval()
    {
        await using var scope = new RunnerScope();
        TimeSpan interval = TimeSpan.FromSeconds(10);
        Registration registration = scope.Add("completion", interval);
        Task<TimeSpan> startupChange = scope.Clock.NextChangeAsync();
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await startupChange;
        Assert.AreEqual(0, scope.Acquisitions, "Actual startup must leave unactivated periodic owners inert.");
        Task<TimeSpan> activatedChange = scope.Clock.NextChangeAsync();
        registration.Handle.ActivatePeriodic();
        await activatedChange;
        Assert.AreEqual(1, scope.Acquisitions, "Actual activation must acquire its first due owner before the next idle Change.");
        Invocation first = await scope.NextAsync();
        Task<HealthCollectionAttempt> joined = registration.Handle.CollectAsync();

        scope.Clock.Advance(TimeSpan.FromSeconds(7));
        first.Complete();
        await joined.WaitAsync(Deadline);
        await scope.Clock.WaitForDueAsync(TimeSpan.FromSeconds(17).Ticks);
        scope.Clock.Advance(TimeSpan.FromSeconds(9));
        Assert.AreEqual(1, registration.Collector.Acquisitions);
        scope.Clock.Advance(TimeSpan.FromSeconds(1));
        Invocation second = await scope.NextAsync();
        Assert.AreEqual(2, registration.Collector.Acquisitions);
        Task<HealthCollectionAttempt> secondWait = registration.Handle.CollectAsync();
        second.Complete();
        await secondWait.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task PeriodicRecurrenceWaitsForActualLeaseCleanupBeforeStartingItsInterval()
    {
        await using var scope = new RunnerScope();
        Registration registration = scope.Add("cleanup", TimeSpan.FromSeconds(10));
        registration.Collector.BlockCleanup = true;
        Task<TimeSpan> startupChange = scope.Clock.NextChangeAsync();
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await startupChange;
        Assert.AreEqual(0, scope.Acquisitions, "Actual startup must leave unactivated periodic owners inert.");
        Task<TimeSpan> activatedChange = scope.Clock.NextChangeAsync();
        registration.Handle.ActivatePeriodic();
        await activatedChange;
        Assert.AreEqual(1, scope.Acquisitions, "Actual activation must acquire its first due owner before the next idle Change.");
        Invocation first = await scope.NextAsync();
        Task<HealthCollectionAttempt> joined = registration.Handle.CollectAsync();
        first.Complete();
        await first.CleanupStarted.WaitAsync(Deadline);
        scope.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.IsFalse(joined.IsCompleted);
        Assert.AreEqual(1, registration.Collector.Acquisitions);

        first.ReleaseCleanup();
        await joined.WaitAsync(Deadline);
        await scope.Clock.WaitForDueAsync(TimeSpan.FromSeconds(40).Ticks);
        scope.Clock.Advance(TimeSpan.FromSeconds(10));
        Invocation second = await scope.NextAsync();
        Assert.AreEqual(2, registration.Collector.Acquisitions);
        Task<HealthCollectionAttempt> secondWait = registration.Handle.CollectAsync();
        second.Complete();
        second.ReleaseCleanup();
        await secondWait.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task PeriodicEligibilityUsesMonotonicTimeAcrossUtcClockChanges()
    {
        await using var scope = new RunnerScope();
        Registration registration = scope.Add("monotonic", TimeSpan.FromSeconds(10));
        Task<TimeSpan> startupChange = scope.Clock.NextChangeAsync();
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await startupChange;
        Assert.AreEqual(0, scope.Acquisitions, "Actual startup must leave unactivated periodic owners inert.");
        Task<TimeSpan> activatedChange = scope.Clock.NextChangeAsync();
        registration.Handle.ActivatePeriodic();
        await activatedChange;
        Assert.AreEqual(1, scope.Acquisitions, "Actual activation must acquire its first due owner before the next idle Change.");
        Invocation first = await scope.NextAsync();
        Task<HealthCollectionAttempt> joined = registration.Handle.CollectAsync();
        first.Complete();
        await joined.WaitAsync(Deadline);
        await scope.Clock.WaitForDueAsync(TimeSpan.FromSeconds(10).Ticks);

        scope.Clock.SetUtcNow(Epoch.AddYears(5));
        scope.Clock.Advance(TimeSpan.FromSeconds(9));
        Assert.AreEqual(1, registration.Collector.Acquisitions);
        scope.Clock.SetUtcNow(Epoch.AddYears(-5));
        scope.Clock.Advance(TimeSpan.FromSeconds(1));
        Invocation second = await scope.NextAsync();
        Task<HealthCollectionAttempt> secondWait = registration.Handle.CollectAsync();
        second.Complete();
        await secondWait.WaitAsync(Deadline);
        Assert.AreEqual(2, registration.Collector.Acquisitions);
    }

    [TestMethod]
    public async Task MissedPeriodsCoalesceWhileAnActualOperationIsOutstanding()
    {
        await using var scope = new RunnerScope();
        TimeSpan interval = TimeSpan.FromSeconds(1);
        Registration registration = scope.Add("coalesced", interval);
        Task<TimeSpan> startupChange = scope.Clock.NextChangeAsync();
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await startupChange;
        Assert.AreEqual(0, scope.Acquisitions, "Actual startup must leave unactivated periodic owners inert.");
        Task<TimeSpan> activatedChange = scope.Clock.NextChangeAsync();
        registration.Handle.ActivatePeriodic();
        await activatedChange;
        Assert.AreEqual(1, scope.Acquisitions, "Actual activation must acquire its first due owner before the next idle Change.");
        Invocation first = await scope.NextAsync();
        Task<HealthCollectionAttempt> joined = registration.Handle.CollectAsync();

        scope.Clock.Advance(TimeSpan.FromDays(1));
        Assert.AreEqual(1, registration.Collector.Acquisitions);
        first.Complete();
        await joined.WaitAsync(Deadline);
        long nextDue = scope.Clock.GetTimestamp() + interval.Ticks;
        await scope.Clock.WaitForDueAsync(nextDue);
        Assert.AreEqual(1, registration.Collector.Acquisitions);
        scope.Clock.Advance(interval);
        Invocation second = await scope.NextAsync();
        Task<HealthCollectionAttempt> secondWait = registration.Handle.CollectAsync();
        second.Complete();
        await secondWait.WaitAsync(Deadline);
        Assert.AreEqual(2, registration.Collector.Acquisitions);
    }

    [TestMethod]
    public async Task ManualCompletionResetsAnActivatedPeriodicRegistrationsNextEligibility()
    {
        await using var scope = new RunnerScope();
        TimeSpan interval = TimeSpan.FromSeconds(10);
        Registration registration = scope.Add("manual-reset", interval);
        Task<TimeSpan> startupChange = scope.Clock.NextChangeAsync();
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await startupChange;
        Assert.AreEqual(0, scope.Acquisitions, "Actual startup must leave unactivated periodic owners inert.");
        Task<TimeSpan> activatedChange = scope.Clock.NextChangeAsync();
        registration.Handle.ActivatePeriodic();
        await activatedChange;
        Assert.AreEqual(1, scope.Acquisitions, "Actual activation must acquire its first due owner before the next idle Change.");
        Invocation first = await scope.NextAsync();
        Task<HealthCollectionAttempt> firstWait = registration.Handle.CollectAsync();
        first.Complete();
        await firstWait.WaitAsync(Deadline);
        await scope.Clock.WaitForDueAsync(interval.Ticks);

        scope.Clock.Advance(TimeSpan.FromSeconds(4));
        Task<HealthCollectionAttempt> manualWait = registration.Handle.CollectAsync();
        Invocation manual = await scope.NextForPendingCallerAsync(manualWait);
        Task<HealthCollectionAttempt> manualJoined = registration.Handle.CollectAsync();
        manual.Complete();
        await manualJoined.WaitAsync(Deadline);
        await scope.Clock.WaitForDueAsync(TimeSpan.FromSeconds(14).Ticks);
        scope.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.AreEqual(2, registration.Collector.Acquisitions);
        scope.Clock.Advance(TimeSpan.FromSeconds(4));
        Invocation periodic = await scope.NextAsync();
        Task<HealthCollectionAttempt> periodicWait = registration.Handle.CollectAsync();
        periodic.Complete();
        await periodicWait.WaitAsync(Deadline);
        Assert.AreEqual(3, registration.Collector.Acquisitions);
        await scope.DisposeAsync();
        await AssertOriginalQueuedCallersCompleteAsync(manualWait);
        await manualWait.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task ConcurrentManualCallersJoinOneActualEvaluationAndOnePolicyOutcome()
    {
        await using var scope = new RunnerScope();
        Registration registration = scope.Add("joined");
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        const int callers = 24;
        var start = new AsyncBarrier(callers + 1);
        var submitted = new AsyncBarrier(callers + 1);
        Task<Task<HealthCollectionAttempt>>[] participants = Enumerable.Range(0, callers).Select(async _ =>
        {
            await start.SignalAndWaitAsync(Deadline);
            Task<HealthCollectionAttempt> wait = registration.Handle.CollectAsync();
            await submitted.SignalAndWaitAsync(Deadline);
            return wait;
        }).ToArray();

        List<Exception> submissionErrors = [];
        try
        {
            await start.SignalAndWaitAsync(Deadline);
            await submitted.SignalAndWaitAsync(Deadline);
        }
        catch (Exception error) { submissionErrors.Add(error); }
        Task<Task<HealthCollectionAttempt>[]> participantsJoined = Task.WhenAll(participants);
        Task<HealthCollectionAttempt>[] waits = [];
        try { waits = await participantsJoined.WaitAsync(Deadline); }
        catch (Exception error)
        {
            if (participantsJoined.Exception is { } failures) { submissionErrors.AddRange(failures.InnerExceptions); }
            else { submissionErrors.Add(error); }
        }
        if (submissionErrors.Count == 1) { ExceptionDispatchInfo.Capture(submissionErrors[0]).Throw(); }
        if (submissionErrors.Count > 1) { throw new AggregateException("The actual manual-call participants failed.", submissionErrors); }
        Invocation invocation = await scope.NextAsync();
        Assert.AreEqual(1, registration.Collector.Acquisitions);
        Task<HealthCollectionAttempt> coalesced = registration.Handle.CollectAsync();
        invocation.Complete();
        await coalesced.WaitAsync(Deadline);
        long successful = scope.Snapshot(registration).Outcomes!.Successful;
        await scope.DisposeAsync();
        await AssertOriginalQueuedCallersCompleteAsync(waits);
        HealthCollectionAttempt[] attempts = await Task.WhenAll(waits).WaitAsync(Deadline);
        Assert.IsTrue(attempts.All(attempt => attempt.Disposition == HealthCollectionDisposition.Committed && !attempt.EvaluationFailed));
        Assert.AreEqual(1L, successful);
        Assert.AreEqual(1, registration.Collector.Acquisitions);
        Assert.AreEqual(1, registration.Collector.ReleasedLeases);
    }

    [TestMethod]
    public async Task PreCanceledManualWaitCreatesNoPendingMarkerOrOwnerLease()
    {
        await using var scope = new RunnerScope();
        Registration registration = scope.Add("pre-canceled");
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => registration.Handle.CollectAsync(canceled.Token)).WaitAsync(Deadline);
        Assert.AreEqual(0, registration.Collector.Acquisitions);
        Assert.IsFalse(scope.Snapshot(registration).Collection!.InProgress);
        Task<HealthCollectionAttempt> wait = registration.Handle.CollectAsync();
        Invocation invocation = await scope.NextForPendingCallerAsync(wait);
        Task<HealthCollectionAttempt> coalesced = registration.Handle.CollectAsync();
        invocation.Complete();
        await coalesced.WaitAsync(Deadline);
        Assert.AreEqual(1, registration.Collector.Acquisitions);
        await scope.DisposeAsync();
        Assert.IsTrue(wait.IsCompleted,
            "The original queued caller must complete after its actual operation and resources have settled.");
        Assert.AreEqual(HealthCollectionDisposition.Committed, (await wait).Disposition);
    }

    [TestMethod]
    public async Task PreCanceledJoinDoesNotDisturbTheAlreadyAdmittedOperation()
    {
        await using var scope = new RunnerScope();
        Registration registration = scope.Add("pre-canceled-join");
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> original = registration.Handle.CollectAsync();
        Invocation invocation = await scope.NextForPendingCallerAsync(original);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => registration.Handle.CollectAsync(canceled.Token)).WaitAsync(Deadline);
        Assert.IsFalse(original.IsCompleted);
        Assert.IsFalse(invocation.LifetimeCancellation.IsCancellationRequested);
        Task<HealthCollectionAttempt> coalesced = registration.Handle.CollectAsync();
        invocation.Complete();
        await coalesced.WaitAsync(Deadline);
        Assert.AreEqual(1, registration.Collector.Acquisitions);
        Assert.AreEqual(1L, scope.Snapshot(registration).Outcomes!.Successful);
        await scope.DisposeAsync();
        Assert.IsTrue(original.IsCompleted,
            "The original queued caller must complete after its actual operation and resources have settled.");
        Assert.AreEqual(HealthCollectionDisposition.Committed, (await original).Disposition);
    }

    [TestMethod]
    public async Task GlobalCapacityBoundsActualOwnerLeasesAcrossDifferentMonitors()
    {
        await using var scope = new RunnerScope(capacity: 2);
        Registration first = scope.Add("one");
        Registration second = scope.Add("two");
        Registration third = scope.Add("three");
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> firstWait = first.Handle.CollectAsync();
        Task<HealthCollectionAttempt> secondWait = second.Handle.CollectAsync();
        Invocation one = await scope.NextForPendingCallerAsync(firstWait);
        Invocation two = await scope.NextForPendingCallerAsync(secondWait);
        Task<HealthCollectionAttempt> firstJoined = first.Handle.CollectAsync();
        Task<HealthCollectionAttempt> secondJoined = second.Handle.CollectAsync();
        Task<HealthCollectionAttempt> thirdWait = third.Handle.CollectAsync();

        Assert.AreEqual(2, scope.ActiveLeases);
        Assert.AreEqual(0, third.Collector.Acquisitions);
        one.Complete();
        Invocation three = await scope.NextForPendingCallerAsync(thirdWait);
        Assert.AreEqual("three", three.Name);
        Task<HealthCollectionAttempt> thirdJoined = third.Handle.CollectAsync();
        Assert.IsFalse(secondWait.IsCompleted && firstWait.IsCompleted);
        Assert.AreEqual(2, scope.PeakLeases);
        two.Complete();
        three.Complete();
        await Task.WhenAll(firstJoined, secondJoined, thirdJoined).WaitAsync(Deadline);
        await scope.DisposeAsync();
        await AssertOriginalQueuedCallersCompleteAsync(firstWait, secondWait, thirdWait);
        await Task.WhenAll(firstWait, secondWait, thirdWait).WaitAsync(Deadline);
        Assert.AreEqual(0, scope.ActiveLeases);
    }

    [TestMethod]
    public async Task ManualRequestsQueuedBehindCapacityShareOneFutureEvaluation()
    {
        await using var scope = new RunnerScope(capacity: 1);
        Registration occupied = scope.Add("occupied");
        Registration pending = scope.Add("one-pending-marker");
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> occupiedWait = occupied.Handle.CollectAsync();
        Invocation first = await scope.NextForPendingCallerAsync(occupiedWait);
        Task<HealthCollectionAttempt> occupiedJoined = occupied.Handle.CollectAsync();
        Task<HealthCollectionAttempt>[] joins = Enumerable.Range(0, 32)
            .Select(_ => pending.Handle.CollectAsync()).ToArray();

        Assert.AreEqual(0, pending.Collector.Acquisitions);
        Assert.IsTrue(joins.All(wait => !wait.IsCompleted));
        first.Complete();
        Invocation admitted = await scope.NextForPendingCallerAsync(joins[0]);
        Assert.AreEqual("one-pending-marker", admitted.Name);
        Task<HealthCollectionAttempt> pendingJoined = pending.Handle.CollectAsync();
        admitted.Complete();
        await Task.WhenAll(occupiedJoined, pendingJoined).WaitAsync(Deadline);
        long successful = scope.Snapshot(pending).Outcomes!.Successful;
        await scope.DisposeAsync();
        await AssertOriginalQueuedCallersCompleteAsync([occupiedWait, .. joins]);
        HealthCollectionAttempt[] attempts = await Task.WhenAll(joins).WaitAsync(Deadline);
        await occupiedWait.WaitAsync(Deadline);
        Assert.IsTrue(attempts.All(attempt => attempt.Disposition == HealthCollectionDisposition.Committed));
        Assert.AreEqual(1, pending.Collector.Acquisitions);
        Assert.AreEqual(1L, successful);
        Assert.AreEqual(1, scope.PeakLeases);
    }

    [TestMethod]
    public async Task WithdrawingAQueuedRegistrationRetiresItsCallersWithoutAcquiringALease()
    {
        await using var scope = new RunnerScope(capacity: 1);
        Registration occupied = scope.Add("occupied");
        Registration pending = scope.Add("withdrawn-pending");
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> occupiedWait = occupied.Handle.CollectAsync();
        Invocation first = await scope.NextForPendingCallerAsync(occupiedWait);
        Task<HealthCollectionAttempt> occupiedJoined = occupied.Handle.CollectAsync();
        Task<HealthCollectionAttempt> pendingWait = pending.Handle.CollectAsync();

        pending.Handle.Withdraw();
        await pending.Handle.WaitForDisposalAsync().WaitAsync(Deadline);
        Assert.AreEqual(HealthCollectionDisposition.Retired, (await pendingWait.WaitAsync(Deadline)).Disposition);
        Assert.AreEqual(0, pending.Collector.Acquisitions);
        Assert.IsFalse(occupiedWait.IsCompleted);
        pending.Monitor.ReportSuccess();
        Assert.AreEqual(MonitorState.Healthy, scope.Snapshot(pending).State);
        first.Complete();
        await occupiedJoined.WaitAsync(Deadline);
        await scope.DisposeAsync();
        await AssertOriginalQueuedCallersCompleteAsync(occupiedWait);
        await occupiedWait.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task ServiceStoppingRetiresQueuedCallersWithoutWaitingForAnotherBlockedOwner()
    {
        await using var scope = new RunnerScope(capacity: 1);
        Registration occupied = scope.Add("ignores-cancellation");
        Registration pending = scope.Add("service-stopping-pending");
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> occupiedWait = occupied.Handle.CollectAsync();
        Invocation first = await scope.NextForPendingCallerAsync(occupiedWait);
        Task<HealthCollectionAttempt> occupiedJoined = occupied.Handle.CollectAsync();
        Task<HealthCollectionAttempt> pendingWait = pending.Handle.CollectAsync();

        scope.Service.BeginStopping();
        Assert.AreEqual(HealthCollectionDisposition.Retired, (await pendingWait.WaitAsync(Deadline)).Disposition);
        await first.CancellationObserved.WaitAsync(Deadline);
        Assert.IsFalse(occupiedWait.IsCompleted);
        Assert.AreEqual(0, pending.Collector.Acquisitions);
        Assert.AreEqual(1, scope.ActiveLeases);
        first.Complete();
        await occupiedJoined.WaitAsync(Deadline);
        await scope.DisposeAsync();
        await AssertOriginalQueuedCallersCompleteAsync(occupiedWait);
        await occupiedWait.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task ASlotRemainsOccupiedUntilTheAcquiredLeasesCleanupActuallySettles()
    {
        await using var scope = new RunnerScope(capacity: 1);
        Registration first = scope.Add("cleanup-slot");
        Registration next = scope.Add("waiting");
        first.Collector.BlockCleanup = true;
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> firstWait = first.Handle.CollectAsync();
        Invocation invocation = await scope.NextForPendingCallerAsync(firstWait);
        Task<HealthCollectionAttempt> firstJoined = first.Handle.CollectAsync();
        invocation.Complete();
        await invocation.CleanupStarted.WaitAsync(Deadline);
        Task<HealthCollectionAttempt> nextWait = next.Handle.CollectAsync();

        Assert.AreEqual(1, scope.ActiveLeases);
        Assert.AreEqual(0, next.Collector.Acquisitions);
        Assert.IsFalse(firstWait.IsCompleted);
        invocation.ReleaseCleanup();
        Invocation admitted = await scope.NextForPendingCallerAsync(nextWait);
        Assert.AreEqual("waiting", admitted.Name);
        Task<HealthCollectionAttempt> nextJoined = next.Handle.CollectAsync();
        admitted.Complete();
        await Task.WhenAll(firstJoined, nextJoined).WaitAsync(Deadline);
        await scope.DisposeAsync();
        await AssertOriginalQueuedCallersCompleteAsync(firstWait, nextWait);
        await Task.WhenAll(firstWait, nextWait).WaitAsync(Deadline);
        Assert.AreEqual(1, scope.PeakLeases);
    }

    [TestMethod]
    public async Task CancelingOneManualWaitDoesNotCancelWorkOrReleaseItsCapacitySlot()
    {
        await using var scope = new RunnerScope(capacity: 1);
        Registration first = scope.Add("wait-canceled");
        Registration next = scope.Add("after-real-settlement");
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        using var caller = new CancellationTokenSource();
        Task<HealthCollectionAttempt> canceledWait = first.Handle.CollectAsync(caller.Token);
        Invocation invocation = await scope.NextAsync();
        Task<HealthCollectionAttempt> survivingJoin = first.Handle.CollectAsync();
        Task<HealthCollectionAttempt> nextWait = next.Handle.CollectAsync();
        caller.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => canceledWait).WaitAsync(Deadline);
        Assert.IsFalse(invocation.LifetimeCancellation.IsCancellationRequested);
        Assert.IsFalse(survivingJoin.IsCompleted);
        Assert.AreEqual(0, next.Collector.Acquisitions);
        Assert.AreEqual(1, scope.ActiveLeases);
        invocation.Complete();
        Invocation admitted = await scope.NextAsync();
        Assert.AreEqual("after-real-settlement", admitted.Name);
        Task<HealthCollectionAttempt> nextJoined = next.Handle.CollectAsync();
        admitted.Complete();
        await Task.WhenAll(survivingJoin, nextJoined).WaitAsync(Deadline);
        long successful = scope.Snapshot(first).Outcomes!.Successful;
        await scope.DisposeAsync();
        await AssertOriginalQueuedCallersCompleteAsync(survivingJoin, nextWait);
        await Task.WhenAll(survivingJoin, nextWait).WaitAsync(Deadline);
        Assert.AreEqual(1L, successful);
        Assert.AreEqual(1, scope.PeakLeases);
    }

    [TestMethod]
    public async Task RotatingSelectionServesEveryDueRegistrationBeforeReturningToTheFirst()
    {
        await using var scope = new RunnerScope(capacity: 1);
        Registration[] registrations = Enumerable.Range(0, 6)
            .Select(index => scope.Add($"fair-{index}", TimeSpan.FromSeconds(1))).ToArray();
        Task<TimeSpan> startupChange = scope.Clock.NextChangeAsync();
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await startupChange;
        Assert.AreEqual(0, scope.Acquisitions, "Actual startup must leave unactivated periodic owners inert.");
        Task<TimeSpan> activatedChange = scope.Clock.NextChangeAsync();
        foreach (Registration registration in registrations) { registration.Handle.ActivatePeriodic(); }
        await activatedChange;
        Assert.AreEqual(1, scope.Acquisitions, "The real capacity-one dispatcher must acquire its first gated owner before the next idle Change.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < registrations.Length; index++)
        {
            Invocation invocation = await scope.NextAsync();
            Assert.IsTrue(seen.Add(invocation.Name), "A due registration was selected twice before another due registration was served.");
            Registration registration = registrations.Single(item => item.Name == invocation.Name);
            Task<HealthCollectionAttempt> wait = registration.Handle.CollectAsync();
            scope.Clock.Advance(TimeSpan.FromSeconds(1));
            invocation.Complete();
            await wait.WaitAsync(Deadline);
        }
        Assert.AreEqual(registrations.Length, seen.Count);
        Assert.AreEqual(1, scope.PeakLeases);
    }

    [TestMethod]
    public async Task ManyMissedPeriodsProduceOnePendingAttemptPerWaitingRegistration()
    {
        await using var scope = new RunnerScope(capacity: 1);
        Registration[] registrations = Enumerable.Range(0, 32)
            .Select(index => scope.Add($"pending-{index}", TimeSpan.FromSeconds(1))).ToArray();
        Task<TimeSpan> startupChange = scope.Clock.NextChangeAsync();
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await startupChange;
        Assert.AreEqual(0, scope.Acquisitions, "Actual startup must leave unactivated periodic owners inert.");
        Task<TimeSpan> activatedChange = scope.Clock.NextChangeAsync();
        foreach (Registration registration in registrations) { registration.Handle.ActivatePeriodic(); }
        await activatedChange;
        Assert.AreEqual(1, scope.Acquisitions, "The real capacity-one dispatcher must acquire its first gated owner before the next idle Change.");
        Invocation first = await scope.NextAsync();
        scope.Clock.Advance(TimeSpan.FromDays(30));
        Assert.AreEqual(1, registrations.Sum(item => item.Collector.Acquisitions));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        Invocation current = first;
        for (int index = 0; index < registrations.Length; index++)
        {
            Assert.IsTrue(seen.Add(current.Name));
            Registration registration = registrations.Single(item => item.Name == current.Name);
            Task<HealthCollectionAttempt> wait = registration.Handle.CollectAsync();
            current.Complete();
            await wait.WaitAsync(Deadline);
            if (index + 1 < registrations.Length) { current = await scope.NextAsync(); }
        }

        await scope.Clock.WaitForDueAsync(scope.Clock.GetTimestamp() + TimeSpan.FromSeconds(1).Ticks);
        Assert.AreEqual(registrations.Length, registrations.Sum(item => item.Collector.Acquisitions));
        Assert.IsTrue(registrations.All(item => item.Collector.Acquisitions == 1));
        Assert.AreEqual(1, scope.PeakLeases);
        // Public observations prove coalescing and bounded actual work; allocation/queue
        // structure is additionally inspected by the implementation review.
    }

    [TestMethod]
    public async Task CanceledStopWaitLeavesTheRealCallbackAndCleanupBarrierOutstanding()
    {
        await using var scope = new RunnerScope();
        Registration registration = scope.Add("stop-wait");
        registration.Collector.BlockCleanup = true;
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> collection = registration.Handle.CollectAsync();
        Invocation invocation = await scope.NextAsync();
        Task<HealthCollectionAttempt> coalesced = registration.Handle.CollectAsync();
        using var caller = new CancellationTokenSource();
        Task canceledStop = scope.Runner.StopAsync(caller.Token);
        caller.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => canceledStop).WaitAsync(Deadline);
        await invocation.CancellationObserved.WaitAsync(Deadline);
        Task actualStop = scope.Runner.StopAsync();
        Task disposal = scope.Runner.DisposeAsync().AsTask();
        Assert.IsFalse(actualStop.IsCompleted);
        Assert.IsFalse(disposal.IsCompleted);
        invocation.Complete();
        await invocation.CleanupStarted.WaitAsync(Deadline);
        Assert.IsFalse(actualStop.IsCompleted);
        Assert.IsFalse(disposal.IsCompleted);
        invocation.ReleaseCleanup();
        await Task.WhenAll(actualStop, disposal, coalesced).WaitAsync(Deadline);
        await scope.DisposeAsync();
        await AssertOriginalQueuedCallersCompleteAsync(collection);
        await Task.WhenAll(actualStop, disposal, collection).WaitAsync(Deadline);
        Assert.AreEqual(0, scope.ActiveLeases);
    }

    [TestMethod]
    public async Task StopBeforeStartIsTerminalAndRepeatedStopDoesNotWithdrawTheMonitor()
    {
        await using var scope = new RunnerScope();
        Registration registration = scope.Add("stopped");

        await scope.Runner.StopAsync().WaitAsync(Deadline);
        await scope.Runner.StopAsync().WaitAsync(Deadline);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Runner.StartAsync()).WaitAsync(Deadline);
        registration.Monitor.ReportSuccess();
        Assert.AreEqual(MonitorState.Healthy, scope.Snapshot(registration).State);
        Assert.AreEqual(1L, scope.Snapshot(registration).Outcomes!.Successful);
        Assert.AreEqual(0, registration.Collector.Acquisitions);
    }

    [TestMethod]
    public async Task StoppingRunnerClosesCollectionAdmissionButAllowsFinalDirectReports()
    {
        await using var scope = new RunnerScope();
        Registration registration = scope.Add("final-report", TimeSpan.FromSeconds(1));
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await scope.Runner.StopAsync().WaitAsync(Deadline);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Runner.StartAsync()).WaitAsync(Deadline);
        Assert.AreEqual(HealthCollectionDisposition.Retired, (await registration.Handle.CollectAsync().WaitAsync(Deadline)).Disposition);
        Assert.ThrowsExactly<ObjectDisposedException>(registration.Handle.ActivatePeriodic);
        registration.Monitor.ReportFailure(AcceptedDiagnostic("final", "Final owner report."));
        Assert.AreEqual(MonitorState.Unhealthy, scope.Snapshot(registration).State);
        Assert.IsFalse(scope.Service.CaptureSnapshot().Application.IsStopping);
        Assert.AreEqual(0, registration.Collector.Acquisitions);
    }

    private static async Task AssertOriginalQueuedCallersCompleteAsync(params Task<HealthCollectionAttempt>[] callers)
    {
        List<Exception> failures = [];
        foreach (Task<HealthCollectionAttempt> caller in callers)
        {
            if (!caller.IsCompleted) { continue; }
            try { await caller; }
            catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count == 1) { ExceptionDispatchInfo.Capture(failures[0]).Throw(); }
        if (failures.Count > 1) { throw new AggregateException("The original queued callers failed after actual settlement.", failures); }
        Assert.IsTrue(callers.All(caller => caller.IsCompleted),
            "The original queued callers must complete after all actual operations and resources have settled.");
        await Task.WhenAll(callers);
    }

    private sealed record Registration(string Name, IConsecutiveFailureMonitor Monitor,
        ProbeCollector Collector, IHealthCollectionRegistration Handle);

    private sealed class RunnerScope : IAsyncDisposable
    {
        private readonly List<Registration> registrations = [];
        private readonly Channel<Invocation> ownerProgress = Channel.CreateUnbounded<Invocation>();
        private int activeLeases;
        private int peakLeases;

        internal RunnerScope(int capacity = 4)
        {
            Clock = new ObservedTimeProvider();
            Service = new HealthcheckService(new HealthcheckOptions
            {
                MaxConcurrentCollections = capacity,
                MaxMonitors = 128
            }, new HealthApplicationIdentity { Name = "hc02-scheduling", InstanceId = Guid.NewGuid() }, Clock);
            Runner = new HealthCollectorRunner(Service);
        }

        internal ObservedTimeProvider Clock { get; }
        internal HealthcheckService Service { get; }
        internal HealthCollectorRunner Runner { get; }
        internal int ActiveLeases => Volatile.Read(ref activeLeases);
        internal int PeakLeases => Volatile.Read(ref peakLeases);
        internal int Acquisitions => registrations.Sum(registration => registration.Collector.Acquisitions);

        internal Registration Add(string name, TimeSpan? interval = null)
        {
            IConsecutiveFailureMonitor monitor = Service.CreateConsecutiveFailures(new()
            {
                Name = name,
                IsCritical = true,
                FailureThreshold = 1
            });
            var collector = new ProbeCollector(this, name);
            IHealthCollectionRegistration handle = Runner.Register(monitor, collector, new HealthCollectorOptions { Interval = interval });
            var registration = new Registration(name, monitor, collector, handle);
            registrations.Add(registration);
            return registration;
        }

        internal async Task<Invocation> NextAsync()
        {
            Invocation invocation = await ownerProgress.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
            Assert.IsTrue(invocation.EvaluationEntered,
                "The actual owner lease reached cleanup before the expected evaluation was entered.");
            return invocation;
        }

        internal async Task<Invocation> NextForPendingCallerAsync(Task<HealthCollectionAttempt> actualCaller)
        {
            // Each ProbeCollector lease remains gated until the receiving test
            // calls Complete. A real caller settling before that gate is released
            // is a public result failure, even when the owner was never entered.
            using var entryRead = new CancellationTokenSource();
            Task<Invocation> entry = ownerProgress.Reader.ReadAsync(entryRead.Token).AsTask().WaitAsync(Deadline);
            try
            {
                await Task.WhenAny(entry, actualCaller).WaitAsync(Deadline);
                Assert.IsFalse(actualCaller.IsCompleted,
                    "The actual manual caller settled before its owned evaluation gate was released.");
                Invocation invocation = await entry;
                Assert.IsTrue(invocation.EvaluationEntered,
                    "The actual owner lease reached cleanup before the expected evaluation was entered.");
                return invocation;
            }
            finally { entryRead.Cancel(); }
        }

        internal HealthMonitorSnapshot Snapshot(Registration registration) => Service.CaptureSnapshot().Monitors
            .Single(monitor => monitor.MonitorId == registration.Monitor.MonitorId);

        internal void Acquired()
        {
            int active = Interlocked.Increment(ref activeLeases);
            int observed;
            do { observed = Volatile.Read(ref peakLeases); }
            while (active > observed && Interlocked.CompareExchange(ref peakLeases, active, observed) != observed);
        }

        internal void Released() => Interlocked.Decrement(ref activeLeases);
        internal void ObserveOwnerProgress(Invocation invocation) => ownerProgress.Writer.TryWrite(invocation);

        public async ValueTask DisposeAsync()
        {
            foreach (Registration registration in registrations) { registration.Collector.ReleaseAll(); }
            Task runner = DrainRunnerAsync();
            Task service = DrainServiceAsync();
            Task[] monitors = registrations.Select(registration => DrainMonitorAsync(registration.Monitor)).ToArray();
            await Task.WhenAll([runner, service, .. monitors]).WaitAsync(Deadline);

            async Task DrainRunnerAsync() => await Runner.DisposeAsync();
            async Task DrainServiceAsync() => await Service.DisposeAsync();
            static async Task DrainMonitorAsync(IHealthMonitor monitor) => await monitor.DisposeAsync();
        }
    }

    private sealed class ProbeCollector(RunnerScope scope, string name) : IHealthCollector
    {
        private readonly ConcurrentBag<Invocation> leases = [];
        private int acquisitions;
        private int releasedLeases;
        private int releasing;

        internal bool BlockCleanup { get; set; }
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int ReleasedLeases => Volatile.Read(ref releasedLeases);

        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            var invocation = new Invocation(scope, this, name, BlockCleanup);
            leases.Add(invocation);
            Interlocked.Increment(ref acquisitions);
            scope.Acquired();
            if (Volatile.Read(ref releasing) != 0) { invocation.Complete(); invocation.ReleaseCleanup(); }
            lease = invocation;
            return true;
        }

        internal void Released() => Interlocked.Increment(ref releasedLeases);

        internal void ReleaseAll()
        {
            Volatile.Write(ref releasing, 1);
            foreach (Invocation invocation in leases) { invocation.Complete(); invocation.ReleaseCleanup(); }
        }
    }

    private sealed class Invocation(RunnerScope scope, ProbeCollector owner, string name, bool blockCleanup) : IHealthCollectionLease
    {
        private readonly TaskCompletionSource<HealthCollectionResult> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource cleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration cancellationRegistration;
        private int disposed;
        private int evaluationEntered;

        internal string Name { get; } = name;
        internal AsyncSignal CleanupStarted { get; } = new();
        internal AsyncSignal CancellationObserved { get; } = new();
        internal CancellationToken LifetimeCancellation { get; private set; }
        internal bool EvaluationEntered => Volatile.Read(ref evaluationEntered) != 0;

        public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
        {
            Interlocked.Exchange(ref evaluationEntered, 1);
            LifetimeCancellation = lifetimeCancellation;
            cancellationRegistration = lifetimeCancellation.Register(CancellationObserved.Set);
            scope.ObserveOwnerProgress(this);
            return result.Task;
        }

        internal void Complete() => result.TrySetResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        internal void ReleaseCleanup() => cleanup.TrySetResult();

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) { return; }
            // Every Next call expects evaluation before the test releases its
            // gate. Actual cleanup without that callback is an observable
            // alternate owner milestone, including a periodic first attempt.
            if (!EvaluationEntered) { scope.ObserveOwnerProgress(this); }
            CleanupStarted.Set();
            if (blockCleanup) { await cleanup.Task; }
            cancellationRegistration.Dispose();
            owner.Released();
            scope.Released();
        }
    }

    // Timer observation is a scheduling barrier, not a wall-clock delay. It lets a
    // test advance virtual time only after the runner has installed its next wakeup.
    private sealed class ObservedTimeProvider : TimeProvider
    {
        private readonly object gate = new();
        private readonly ManualTimeProvider manual = new(Epoch);
        private readonly Dictionary<ObservedTimer, long?> dueTimes = [];
        private TaskCompletionSource changed = NewSignal();
        private TaskCompletionSource<TimeSpan>? nextChange;

        public override long TimestampFrequency => manual.TimestampFrequency;
        public override long GetTimestamp() => manual.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => manual.GetUtcNow();
        internal void Advance(TimeSpan elapsed) => manual.Advance(elapsed);
        internal void SetUtcNow(DateTimeOffset value) => manual.SetUtcNow(value);
        internal Task<TimeSpan> NextChangeAsync()
        {
            var next = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.CompareExchange(ref nextChange, next, null) is not null)
            { throw new InvalidOperationException("The previous actual timer Change observation is still armed."); }
            return next.Task.WaitAsync(Deadline);
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ObservedTimer(this, manual.CreateTimer(callback, state, dueTime, period));
            Record(timer, dueTime);
            return timer;
        }

        internal async Task WaitForDueAsync(long timestamp)
        {
            using var deadline = new CancellationTokenSource(Deadline);
            while (true)
            {
                Task wait;
                lock (gate)
                {
                    if (dueTimes.Values.Any(due => due == timestamp)) { return; }
                    wait = changed.Task;
                }
                await wait.WaitAsync(deadline.Token);
            }
        }

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private void Record(ObservedTimer timer, TimeSpan dueTime)
        {
            TaskCompletionSource signal;
            lock (gate)
            {
                dueTimes[timer] = dueTime == Timeout.InfiniteTimeSpan ? null : checked(GetTimestamp() + dueTime.Ticks);
                signal = changed;
                changed = NewSignal();
            }
            signal.TrySetResult();
        }

        private void Remove(ObservedTimer timer)
        {
            TaskCompletionSource signal;
            lock (gate)
            {
                dueTimes.Remove(timer);
                signal = changed;
                changed = NewSignal();
            }
            signal.TrySetResult();
        }

        private sealed class ObservedTimer(ObservedTimeProvider owner, ITimer inner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                bool changed = inner.Change(dueTime, period);
                if (changed)
                {
                    owner.Record(this, dueTime);
                    Interlocked.Exchange(ref owner.nextChange, null)?.TrySetResult(dueTime);
                }
                return changed;
            }

            public void Dispose() { inner.Dispose(); owner.Remove(this); }

            public async ValueTask DisposeAsync()
            {
                await inner.DisposeAsync();
                owner.Remove(this);
            }
        }
    }
    private static HealthDiagnostic AcceptedDiagnostic(string? code = null, string? message = null, string? exceptionType = null)
    {
        HealthDiagnostic? diagnostic = null;
        ArgumentException? rejection = null;
        try { diagnostic = new HealthDiagnostic(code, message, exceptionType); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "Valid diagnostic constructor arguments must produce a detached diagnostic.");
        Assert.IsNotNull(diagnostic);
        return diagnostic;
    }

}
