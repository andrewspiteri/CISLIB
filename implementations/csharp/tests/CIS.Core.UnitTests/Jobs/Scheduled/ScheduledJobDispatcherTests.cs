using CIS.Coordination;
using CIS.Jobs;
using CIS.Messaging;
using CIS.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Jobs.Scheduled;

[TestClass]
public sealed class ScheduledJobDispatcherTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task UsesExactAdmittedSnapshotInputAndOperationThroughFreshAuthorizedScopes()
    {
        await using var fixture = new ScheduledDispatcherFixture();
        await fixture.Runtime.StartAsync();
        AdmittedJobRun run = fixture.Run();
        ScheduledJobResult result = await fixture.Dispatcher.DispatchAsync(run);
        Assert.AreEqual(ScheduledJobOutcome.Completed, result.Outcome);
        Assert.AreEqual(1, fixture.Acquisitions);
        Assert.AreEqual(1, fixture.Calls);
        JobExecutionContext context = fixture.Contexts.Single();
        Assert.AreSame(run, context.AdmittedRun);
        Assert.AreSame(run.Settings, context.Settings);
        Assert.AreSame(run.Input, context.AdmittedRun!.Input);
        Assert.AreSame(run.Operation, context.AdmittedRun.Operation);
        Assert.AreEqual("original-business-scope", context.AdmittedRun.Operation.RequestedScope);
        CollectionAssert.AreEqual(new[] { 1, 2 }, fixture.DisposedScopes.ToArray());
        Assert.AreEqual(CoordinationLeaseState.Released, fixture.Leases.Single().State);
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().DutyReservations);
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().ActiveScheduledInvocations);
    }

    [TestMethod]
    [DataRow(CoordinationAcquireOutcome.Busy, ScheduledJobOutcome.Busy)]
    [DataRow(CoordinationAcquireOutcome.Unavailable, ScheduledJobOutcome.Deferred)]
    [DataRow(CoordinationAcquireOutcome.CapacityExceeded, ScheduledJobOutcome.Deferred)]
    [DataRow(CoordinationAcquireOutcome.Indeterminate, ScheduledJobOutcome.Indeterminate)]
    [DataRow(CoordinationAcquireOutcome.Unauthorized, ScheduledJobOutcome.Unauthorized)]
    [DataRow(CoordinationAcquireOutcome.InvalidRequest, ScheduledJobOutcome.Unsupported)]
    public async Task AcquisitionFactsNeverBecomeCallbacksOrFabricatedCompletion(CoordinationAcquireOutcome acquisition,
        ScheduledJobOutcome expected)
    {
        await using var fixture = new ScheduledDispatcherFixture();
        fixture.Acquire = (_, _) => Task.FromResult(new ManagedCoordinationResult(acquisition));
        await fixture.Runtime.StartAsync();
        Assert.AreEqual(expected, (await fixture.Dispatcher.DispatchAsync(fixture.Run())).Outcome);
        Assert.AreEqual(0, fixture.Calls);
        Assert.IsEmpty(fixture.Leases);
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().InvocationReservations);
    }

    [TestMethod]
    [DataRow("source")]
    [DataRow("target")]
    [DataRow("contract")]
    [DataRow("schedule")]
    [DataRow("validation")]
    public async Task UnsupportedOrUnvalidatedAdmissionCannotReachOwnership(string invalid)
    {
        await using var fixture = new ScheduledDispatcherFixture();
        await fixture.Runtime.StartAsync();
        AdmittedJobRun run = invalid switch
        {
            "source" => fixture.Run(source: "unselected-source"),
            "target" => fixture.Run(job: "unknown-job"),
            "contract" => fixture.Run(contract: new("unregistered-input", 1)),
            "schedule" => fixture.Run(settings: new(JobExecutionMode.Scheduled)),
            _ => fixture.Run()
        };
        fixture.AdmissionSource.Validate = (_, _) => ValueTask.FromResult(new JobAdmissionValidation(false));
        Assert.AreEqual(ScheduledJobOutcome.Unsupported, (await fixture.Dispatcher.DispatchAsync(run)).Outcome);
        Assert.AreEqual(0, fixture.Acquisitions);
        Assert.AreEqual(0, fixture.Calls);
        Assert.AreEqual(invalid == "validation" ? 1 : 0, fixture.AdmissionSource.Calls);
    }

    [TestMethod]
    public async Task PreCancellationStartsNoAdmissionAndForgedOperationCannotConstructMatchingCommand()
    {
        await using var fixture = new ScheduledDispatcherFixture();
        await fixture.Runtime.StartAsync();
        AdmittedJobRun run = fixture.Run();
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Dispatcher.DispatchAsync(run, caller.Token));
        Assert.AreEqual(0, fixture.AdmissionSource.Calls);
        Assert.AreEqual(0, fixture.Acquisitions);
        Assert.ThrowsExactly<ArgumentException>(() => new AdmittedJobRun(run.ExecutionScope, run.JobId, run.ResourceId,
            run.OccurrenceId, new("Dormancy", "original-business-scope", "forged-key"), run.AdmissionRevision, run.Settings,
            run.Input, run.SourceId, run.AdmissionReference));
    }

    [TestMethod]
    public async Task PendingBoundPrecedesExternalValidationAndIgnoredCancellationRetainsActualOccupancy()
    {
        await using var fixture = new ScheduledDispatcherFixture(maximumPending: 1);
        TaskCompletionSource<JobAdmissionValidation> validated = fixture.Hold(new JobAdmissionValidation(false));
        fixture.AdmissionSource.Validate = (_, _) => new(validated.Task);
        await fixture.Runtime.StartAsync();
        Task<ScheduledJobResult> first = fixture.Dispatcher.DispatchAsync(fixture.Run());
        await fixture.AdmissionSource.Started.Task.WaitAsync(Deadline);
        Assert.AreEqual(1, fixture.Runtime.CaptureStatus().PendingDispatches);
        Assert.AreEqual(ScheduledJobOutcome.Deferred, (await fixture.Dispatcher.DispatchAsync(fixture.Run("other"))).Outcome);
        Assert.AreEqual(1, fixture.AdmissionSource.Calls);
        Task stopping = fixture.Runtime.StopAsync();
        Assert.IsFalse(stopping.IsCompleted);
        Assert.IsFalse(first.IsCompleted);
        validated.TrySetResult(new(true));
        Assert.AreEqual(ScheduledJobOutcome.Deferred, (await first.WaitAsync(Deadline)).Outcome);
        await stopping.WaitAsync(Deadline);
        Assert.AreEqual(0, fixture.Acquisitions);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BusinessAndCategoryPressureDeferBeforeLeaseButActualAuthorizationStillTakesPrecedence(bool category)
    {
        await using var fixture = new ScheduledDispatcherFixture(maximumInvocations: category ? 2 : 1,
            categoryMaximum: category ? 1 : null);
        TaskCompletionSource<ScheduledJobResult> callback = fixture.Hold(new ScheduledJobResult(ScheduledJobOutcome.Failed));
        fixture.Handler = (_, _, _) => callback.Task;
        await fixture.Runtime.StartAsync();
        Task<ScheduledJobResult> running = fixture.Dispatcher.DispatchAsync(fixture.Run());
        await fixture.CallbackStarted.Task.WaitAsync(Deadline);
        Assert.AreEqual(ScheduledJobOutcome.Deferred, (await fixture.Dispatcher.DispatchAsync(fixture.Run("other"))).Outcome);
        Assert.AreEqual(1, fixture.Acquisitions);
        fixture.Authorized = _ => false;
        Assert.AreEqual(ScheduledJobOutcome.Unauthorized, (await fixture.Dispatcher.DispatchAsync(fixture.Run("third"))).Outcome);
        Assert.AreEqual(1, fixture.Acquisitions);
        callback.TrySetResult(new(ScheduledJobOutcome.Completed));
        await running.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task FreshExecutionScopeMustAuthorizeAgainAfterSuccessfulAcquisition()
    {
        await using var fixture = new ScheduledDispatcherFixture();
        fixture.Authorized = number => number != 2;
        await fixture.Runtime.StartAsync();
        Assert.AreEqual(ScheduledJobOutcome.Unauthorized, (await fixture.Dispatcher.DispatchAsync(fixture.Run())).Outcome);
        Assert.AreEqual(1, fixture.Acquisitions);
        Assert.AreEqual(0, fixture.Calls);
        Assert.AreEqual(CoordinationLeaseState.Released, fixture.Leases.Single().State);
    }

    [TestMethod]
    public async Task DisablementDuringIgnoredAcquireStopsCallbackAndReleasesOnlyAfterActualAdmissionScopeCleanup()
    {
        await using var fixture = new ScheduledDispatcherFixture();
        TaskCompletionSource<ManagedCoordinationResult> acquired = fixture.Hold(new ManagedCoordinationResult(CoordinationAcquireOutcome.Unavailable));
        fixture.Acquire = (_, _) => acquired.Task;
        await fixture.Runtime.StartAsync();
        Task<ScheduledJobResult> dispatch = fixture.Dispatcher.DispatchAsync(fixture.Run());
        await fixture.AcquisitionStarted.Task.WaitAsync(Deadline);
        Assert.IsTrue(fixture.Runtime.TryApplyConfiguration(fixture.Configuration(2, enabled: false)));
        var request = new CoordinationRequest(ScheduledDispatcherFixture.Scope, ScheduledDispatcherFixture.Job, "partition");
        acquired.TrySetResult(new(CoordinationAcquireOutcome.Acquired, fixture.NewLease(request)));
        Assert.AreEqual(ScheduledJobOutcome.Deferred, (await dispatch.WaitAsync(Deadline)).Outcome);
        Assert.AreEqual(0, fixture.Calls);
        Assert.AreEqual(CoordinationLeaseState.Released, fixture.Leases.Single().State);
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().DutyReservations);
    }

    [TestMethod]
    public async Task ActualScopeCleanupRetainsInvocationCapacityAndStableKeyUntilItSettles()
    {
        await using var fixture = new ScheduledDispatcherFixture(maximumInvocations: 1);
        TaskCompletionSource cleaned = fixture.Hold();
        fixture.Cleanup = number => number == 2 ? new(cleaned.Task) : ValueTask.CompletedTask;
        await fixture.Runtime.StartAsync();
        Task<ScheduledJobResult> first = fixture.Dispatcher.DispatchAsync(fixture.Run());
        await fixture.WorkCleanupStarted.Task.WaitAsync(Deadline);
        Assert.IsFalse(first.IsCompleted);
        Assert.AreEqual(1, fixture.Runtime.CaptureStatus().ActiveScheduledInvocations);
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().PendingDispatches);
        Assert.AreEqual(CoordinationLeaseState.Owned, fixture.Leases.Single().State);
        Assert.AreEqual(ScheduledJobOutcome.Busy, (await fixture.Dispatcher.DispatchAsync(fixture.Run(occurrence: "new-occurrence"))).Outcome);
        Assert.AreEqual(ScheduledJobOutcome.Deferred, (await fixture.Dispatcher.DispatchAsync(fixture.Run("other"))).Outcome);
        Assert.AreEqual(1, fixture.Acquisitions);
        cleaned.TrySetResult();
        Assert.AreEqual(ScheduledJobOutcome.Completed, (await first.WaitAsync(Deadline)).Outcome);
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().ActiveScheduledInvocations);
    }

    [TestMethod]
    [DataRow(ScheduledJobOutcome.Completed, TransactionOutcome.Committed)]
    [DataRow(ScheduledJobOutcome.PreviouslyCompleted, TransactionOutcome.Committed)]
    [DataRow(ScheduledJobOutcome.Indeterminate, TransactionOutcome.Unknown)]
    public async Task OriginalBusinessCompletionSurvivesLossCancellationAndCleanupFault(ScheduledJobOutcome outcome,
        TransactionOutcome completionOutcome)
    {
        await using var fixture = new ScheduledDispatcherFixture();
        TaskCompletionSource<ScheduledJobResult> callback = fixture.Hold(new ScheduledJobResult(ScheduledJobOutcome.Failed));
        fixture.Handler = (_, _, _) => callback.Task;
        fixture.Cleanup = number => number == 2
            ? ValueTask.FromException(new InvalidOperationException("scope cleanup diagnostic")) : ValueTask.CompletedTask;
        await fixture.Runtime.StartAsync();
        using var caller = new CancellationTokenSource();
        Task<ScheduledJobResult> dispatch = fixture.Dispatcher.DispatchAsync(fixture.Run(), caller.Token);
        await fixture.CallbackStarted.Task.WaitAsync(Deadline);
        await fixture.Leases.Single().LoseAsync(CoordinationLossReason.Superseded);
        caller.Cancel();
        Assert.IsFalse(dispatch.IsCompleted);
        Assert.AreEqual(1, fixture.Runtime.CaptureStatus().ActiveScheduledInvocations);
        Assert.AreEqual(ScheduledJobOutcome.Busy, (await fixture.Dispatcher.DispatchAsync(fixture.Run(occurrence: "new-occurrence"))).Outcome);
        var completion = new TransactionCompletion(completionOutcome, TransactionEndReason.CommitRequested, true, true, true);
        var original = new ScheduledJobResult(outcome, completion: completion);
        callback.TrySetResult(original);
        Assert.AreSame(original, await dispatch.WaitAsync(Deadline));
        Assert.AreSame(completion, original.Completion);
        Assert.IsNotNull(fixture.Runtime.CaptureStatus().LastCleanupDiagnostic);
        JobRuntimeSnapshot snapshot = fixture.Runtime.CaptureSnapshot().Single();
        Assert.AreEqual(outcome == ScheduledJobOutcome.Indeterminate ? JobLocalState.Faulted : JobLocalState.Lost, snapshot.State);
        Assert.AreEqual(outcome switch
        {
            ScheduledJobOutcome.Completed => "jobs.completed_after_ownership_loss",
            ScheduledJobOutcome.PreviouslyCompleted => "jobs.previously_completed_after_ownership_loss",
            _ => "jobs.indeterminate"
        }, snapshot.LastOutcome);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnexpectedCallbackFaultIsFailedOnceWithoutAutomaticReplay(bool unexpectedCancellation)
    {
        await using var fixture = new ScheduledDispatcherFixture();
        fixture.Handler = (_, _, _) => Task.FromException<ScheduledJobResult>(unexpectedCancellation
            ? new OperationCanceledException("unsignalled") : new InvalidOperationException("business fault"));
        await fixture.Runtime.StartAsync();
        Assert.AreEqual(ScheduledJobOutcome.Failed, (await fixture.Dispatcher.DispatchAsync(fixture.Run())).Outcome);
        Assert.AreEqual(1, fixture.Calls);
        Assert.AreEqual(1, fixture.Acquisitions);
        JobRuntimeSnapshot snapshot = fixture.Runtime.CaptureSnapshot().Single();
        Assert.AreEqual(JobLocalState.Faulted, snapshot.State);
        Assert.AreEqual("jobs.failed", snapshot.LastOutcome);
    }

    [TestMethod]
    public async Task TimeoutSignalsCooperationButKeepsUnfinishedCallbackAndItsOriginalResult()
    {
        var settings = new JobExecutionSettings(JobExecutionMode.Scheduled, timeout: TimeSpan.FromSeconds(1),
            schedule: JobScheduleDescriptor.FixedDelay(TimeSpan.FromMinutes(5), JobMissedRunPolicy.Skip));
        await using var fixture = new ScheduledDispatcherFixture(settings: settings);
        TaskCompletionSource<ScheduledJobResult> callback = fixture.Hold(new ScheduledJobResult(ScheduledJobOutcome.Failed));
        fixture.Handler = (_, _, _) => callback.Task;
        await fixture.Runtime.StartAsync();
        Task<ScheduledJobResult> dispatch = fixture.Dispatcher.DispatchAsync(fixture.Run());
        await fixture.CallbackStarted.Task.WaitAsync(Deadline);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(fixture.CallbackToken.IsCancellationRequested);
        Assert.IsFalse(dispatch.IsCompleted);
        Assert.AreEqual(1, fixture.Runtime.CaptureStatus().ActiveScheduledInvocations);
        callback.TrySetResult(new(ScheduledJobOutcome.Indeterminate));
        Assert.AreEqual(ScheduledJobOutcome.Indeterminate, (await dispatch.WaitAsync(Deadline)).Outcome);
    }

    [TestMethod]
    public async Task GracefulStoppingDoesNotImmediatelyCancelAnAdmittedCallbackAndJoinsItsActualEnd()
    {
        await using var fixture = new ScheduledDispatcherFixture();
        TaskCompletionSource<ScheduledJobResult> callback = fixture.Hold(new ScheduledJobResult(ScheduledJobOutcome.Failed));
        fixture.Handler = (_, _, _) => callback.Task;
        await fixture.Runtime.StartAsync();
        Task<ScheduledJobResult> dispatch = fixture.Dispatcher.DispatchAsync(fixture.Run());
        await fixture.CallbackStarted.Task.WaitAsync(Deadline);
        Task stopping = fixture.Runtime.StopAsync();
        Assert.AreEqual(CoordinationLeaseState.Draining, fixture.Leases.Single().State);
        Assert.IsFalse(fixture.CallbackToken.IsCancellationRequested);
        Assert.IsFalse(stopping.IsCompleted);
        await fixture.Leases.Single().LoseAsync(CoordinationLossReason.ShutdownDeadlineExceeded);
        Assert.IsTrue(fixture.CallbackToken.IsCancellationRequested);
        Assert.IsFalse(stopping.IsCompleted);
        callback.TrySetResult(new(ScheduledJobOutcome.Cancelled));
        await dispatch.WaitAsync(Deadline);
        await stopping.WaitAsync(Deadline);
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().DutyReservations);
    }

    [TestMethod]
    public async Task RedeliveryKeepsOriginalIdentityAndDifferentResourcesCanRunConcurrently()
    {
        await using var fixture = new ScheduledDispatcherFixture();
        await fixture.Runtime.StartAsync();
        AdmittedJobRun run = fixture.Run();
        await fixture.Dispatcher.DispatchAsync(run);
        await fixture.Dispatcher.DispatchAsync(run);
        Assert.AreSame(fixture.Contexts[0].AdmittedRun!.Operation, fixture.Contexts[1].AdmittedRun!.Operation);
        Assert.AreEqual(run.OccurrenceId, fixture.Contexts[1].AdmittedRun!.OccurrenceId);
        Assert.AreEqual(2, fixture.Acquisitions);
        Assert.AreNotEqual(fixture.Leases[0].Grant.Generation, fixture.Leases[1].Grant.Generation);

        TaskCompletionSource<ScheduledJobResult> callback = fixture.Hold(new ScheduledJobResult(ScheduledJobOutcome.Failed));
        fixture.Handler = (_, _, _) => callback.Task;
        Task<ScheduledJobResult> first = fixture.Dispatcher.DispatchAsync(fixture.Run("resource-a"));
        await UntilAsync(() => fixture.Runtime.CaptureStatus().ActiveScheduledInvocations == 1);
        Task<ScheduledJobResult> second = fixture.Dispatcher.DispatchAsync(fixture.Run("resource-b"));
        await UntilAsync(() => fixture.Runtime.CaptureStatus().ActiveScheduledInvocations == 2);
        Assert.AreEqual(4, fixture.Acquisitions);
        callback.TrySetResult(new(ScheduledJobOutcome.Completed));
        await Task.WhenAll(first, second).WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task NewLocalSettingsDoNotReplaceAlreadyAdmittedExecutionSnapshot()
    {
        await using var fixture = new ScheduledDispatcherFixture();
        TaskCompletionSource<ScheduledJobResult> callback = fixture.Hold(new ScheduledJobResult(ScheduledJobOutcome.Failed));
        fixture.Handler = (_, _, _) => callback.Task;
        TaskCompletionSource opening = fixture.Hold();
        TaskCompletionSource started = ScheduledDispatcherFixture.Signal();
        fixture.Opening = (number, _) =>
        {
            if (number != 2)
            {
                return ValueTask.CompletedTask;
            }

            started.TrySetResult();
            return new(opening.Task);
        };
        await fixture.Runtime.StartAsync();
        AdmittedJobRun run = fixture.Run(revision: 99);
        Task<ScheduledJobResult> dispatch = fixture.Dispatcher.DispatchAsync(run);
        await started.Task.WaitAsync(Deadline);
        var changed = new JobExecutionSettings(JobExecutionMode.Scheduled, timeout: TimeSpan.FromSeconds(9),
            schedule: JobScheduleDescriptor.FixedDelay(TimeSpan.FromHours(1), JobMissedRunPolicy.Skip));
        Assert.IsTrue(fixture.Runtime.TryApplyConfiguration(fixture.Configuration(2, execution: changed,
            categoryTimeout: TimeSpan.FromSeconds(1))));
        opening.TrySetResult();
        await fixture.CallbackStarted.Task.WaitAsync(Deadline);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.IsFalse(fixture.CallbackToken.IsCancellationRequested);
        Assert.IsFalse(dispatch.IsCompleted);
        callback.TrySetResult(new(ScheduledJobOutcome.Completed));
        Assert.AreEqual(ScheduledJobOutcome.Completed, (await dispatch.WaitAsync(Deadline)).Outcome);
        Assert.AreSame(run.Settings, fixture.Contexts.Single().Settings);
        Assert.AreEqual(99L, fixture.Contexts.Single().AdmittedRun!.AdmissionRevision);
        Assert.AreEqual(2L, fixture.Runtime.Configuration.Revision);
    }

    [TestMethod]
    public async Task DisabledDefinitionsInspectWithoutSourceAndEnabledActivationRequiresCompatibleCapabilities()
    {
        await using var fixture = new ScheduledDispatcherFixture(enabled: false);
        var sourceFree = new JobRuntime(fixture.Runtime.Catalog, new NeverScope(), new JobConfigurationSnapshot(0));
        await using (sourceFree)
        {
            await sourceFree.StartAsync();
            Assert.HasCount(1, sourceFree.Catalog.Inspect());
            Assert.AreEqual(ScheduledJobOutcome.Deferred,
                (await new ScheduledJobDispatcher(sourceFree).DispatchAsync(fixture.Run())).Outcome);
        }

        var missing = new JobRuntime(fixture.Runtime.Catalog, new NeverScope(), fixture.Configuration(1));
        await using (missing)
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => missing.StartAsync());
        }

        var recurring = new JobExecutionSettings(JobExecutionMode.Scheduled,
            schedule: JobScheduleDescriptor.Weekly([DayOfWeek.Monday], new(2, 0), "Europe/Malta", JobMissedRunPolicy.CatchUpBounded,
                2, TimeSpan.FromDays(7)), scheduledActivation: JobScheduledActivation.AutomaticRecurrence);
        var unsupported = new JobRuntime(fixture.Runtime.Catalog, new NeverScope(), fixture.Configuration(1, execution: recurring), fixture.AdmissionSource);
        await using (unsupported)
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => unsupported.StartAsync());
        }

        var supported = new JobRuntime(fixture.Runtime.Catalog, new NeverScope(), fixture.Configuration(1, execution: recurring),
            fixture.AdmissionSource, new Evaluator());
        await using (supported)
        {
            await supported.StartAsync();
            Assert.IsTrue(supported.CaptureStatus().Started);
        }
    }

    private static async Task UntilAsync(Func<bool> predicate)
    {
        using var cancelled = new CancellationTokenSource(Deadline);
        while (!predicate())
        {
            await Task.Delay(1, cancelled.Token);
        }
    }

    private sealed class NeverScope : IJobExecutionScopeFactory
    {
        public ValueTask<IJobExecutionScope> CreateAsync(JobScopeRequest request, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Inspecting or validating capabilities must not create a business scope.");
    }

    private sealed class Evaluator : IJobScheduleEvaluator
    {
        public bool CanEvaluate(JobScheduleDescriptor schedule) => true;
    }
}
