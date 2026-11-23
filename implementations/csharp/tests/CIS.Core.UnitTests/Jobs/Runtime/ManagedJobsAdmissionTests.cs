using CIS.CommonTesting;
using CIS.Coordination;
using CIS.Jobs;
using CIS.Jobs.Hosting;
using CIS.Management;
using CIS.Tests.UnitTests.Jobs.Scheduled;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Jobs;

[TestClass]
public sealed class ManagedJobsAdmissionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task ClosedGateDefersNewContinuousDutyBeforeScopesOrOwnershipAndCanBeExplicitlyStartedAfterReconciliation()
    {
        var admission = new ManualManagedWorkAdmission();
        await using var fixture = new JobRuntimeTestSupport(managedAdmission: admission);
        Assert.AreEqual(ContinuousJobStartOutcome.Deferred,
            await fixture.Runner.StartAsync("wallet-service", "wallet.compaction", "partition"));
        Assert.AreEqual(0, fixture.Control.CreatedScopes);
        Assert.HasCount(0, fixture.Coordination.Calls);
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().DutyReservations);
        Ready(admission, 1);
        fixture.EnqueueGrant();

        Assert.AreEqual(ContinuousJobStartOutcome.Started,
            await fixture.Runner.StartAsync("wallet-service", "wallet.compaction", "partition"));
        await fixture.Control.Entered.Task.WaitAsync(Deadline);

        Assert.HasCount(1, fixture.Coordination.Calls);
        Assert.HasCount(1, fixture.Control.Invocations);
    }

    [TestMethod]
    public async Task RunningContinuousIterationSettlesWhileNextIterationWaitsAndReopensUsingTheSameValidLease()
    {
        var admission = new ManualManagedWorkAdmission();
        Ready(admission, 1);
        await using var fixture = new JobRuntimeTestSupport(managedAdmission: admission);
        var firstEntered = Signal();
        var secondEntered = Signal();
        var secondRelease = Signal();
        int calls = 0;
        CancellationToken originalCallback = default;
        JobExecutionSettings? originalSettings = null;
        fixture.Control.Callback = async (context, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                originalCallback = token;
                originalSettings = context.Settings;
                firstEntered.TrySetResult();
                await fixture.Control.WorkRelease.Task;
                return new(ContinuousIterationOutcome.MoreWork);
            }
            secondEntered.TrySetResult();
            await secondRelease.Task;
            return new(ContinuousIterationOutcome.Idle);
        };
        ManualCoordinationLease lease = fixture.EnqueueGrant();
        try
        {
            await fixture.Runner.StartAsync("wallet-service", "wallet.compaction", "partition");
            await firstEntered.Task.WaitAsync(Deadline);
            JobConfigurationSnapshot retained = fixture.Runtime.Configuration;
            Close(admission, 2);
            Assert.IsFalse(originalCallback.IsCancellationRequested);
            Assert.IsFalse(fixture.Runtime.CaptureStatus().Stopping);
            Assert.AreSame(retained, fixture.Runtime.Configuration);
            Assert.AreSame(retained.Jobs[("wallet-service", "wallet.compaction")].Execution, originalSettings);
            fixture.Control.WorkRelease.TrySetResult();
            await JobRuntimeTestSupport.UntilAsync(() => fixture.Runtime.CaptureStatus().ActiveContinuousInvocations == 0
                && fixture.Control.DisposedScopes >= 3);
            Assert.AreEqual(1, Volatile.Read(ref calls));
            Assert.IsFalse(secondEntered.Task.IsCompleted);
            Assert.AreEqual(CoordinationLeaseState.Owned, lease.State);
            Assert.HasCount(1, fixture.Coordination.Calls);

            Ready(admission, 3);
            await secondEntered.Task.WaitAsync(Deadline);

            Assert.AreEqual(2, Volatile.Read(ref calls));
            Assert.HasCount(1, fixture.Coordination.Calls);
            Assert.AreEqual(CoordinationLeaseState.Owned, lease.State);
            Close(admission, 4);
            Task stop = fixture.Runner.StopAsync("wallet-service", "wallet.compaction", "partition");
            Assert.IsFalse(stop.IsCompleted, "Closing admission or requesting stop cannot certify an unfinished callback.");
            secondRelease.TrySetResult();
            await stop.WaitAsync(Deadline);
        }
        finally
        {
            fixture.Control.WorkRelease.TrySetResult();
            secondRelease.TrySetResult();
        }
    }

    [TestMethod]
    public async Task GateClosingDuringFreshIterationScopePreventsCallbackAndWakeupUsesExistingOwnership()
    {
        var admission = new ManualManagedWorkAdmission();
        Ready(admission, 1);
        await using var fixture = new JobRuntimeTestSupport(managedAdmission: admission);
        int scopes = 0;
        var reached = Signal();
        fixture.Control.BeforeScope = (_, _) =>
        {
            if (Interlocked.Increment(ref scopes) == 3)
            {
                Close(admission, 2);
                reached.TrySetResult();
            }
            return Task.CompletedTask;
        };
        ManualCoordinationLease lease = fixture.EnqueueGrant();
        await fixture.Runner.StartAsync("wallet-service", "wallet.compaction", "partition");
        await reached.Task.WaitAsync(Deadline);
        await JobRuntimeTestSupport.UntilAsync(() => fixture.Control.DisposedScopes == 3);
        Assert.IsTrue(fixture.Control.Invocations.IsEmpty);
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().ActiveContinuousInvocations);
        Assert.AreEqual(CoordinationLeaseState.Owned, lease.State);

        Ready(admission, 3);
        await fixture.Control.Entered.Task.WaitAsync(Deadline);

        Assert.HasCount(1, fixture.Coordination.Calls);
        Assert.HasCount(1, fixture.Control.Invocations);
    }

    [TestMethod]
    public async Task ClosedGateBlocksRetryAcquisitionAndChangedWakesItWithoutAnotherRetryDelay()
    {
        var admission = new ManualManagedWorkAdmission();
        Ready(admission, 1);
        await using var fixture = new JobRuntimeTestSupport(managedAdmission: admission);
        fixture.Coordination.Enqueue(new(CoordinationAcquireOutcome.Unavailable));
        fixture.EnqueueGrant();
        await fixture.Runner.StartAsync("wallet-service", "wallet.compaction", "partition");
        Assert.HasCount(1, fixture.Coordination.Calls);
        long checks = admission.AdmissionChecks;
        Close(admission, 2);
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await JobRuntimeTestSupport.UntilAsync(() => admission.AdmissionChecks > checks);
        Assert.HasCount(1, fixture.Coordination.Calls);
        Assert.IsTrue(fixture.Control.Invocations.IsEmpty);

        Ready(admission, 3);
        await fixture.Control.Entered.Task.WaitAsync(Deadline);

        Assert.HasCount(2, fixture.Coordination.Calls);
        Assert.HasCount(1, fixture.Control.Invocations);
        Assert.IsFalse(fixture.Runtime.CaptureSnapshot().Any(item => item.State == JobLocalState.Faulted));
    }

    [TestMethod]
    public async Task PhysicalAcquireRechecksAfterAuthorizationAndDoesNotTurnClosedAdmissionIntoAFault()
    {
        var admission = new ManualManagedWorkAdmission();
        Ready(admission, 1);
        await using var fixture = new JobRuntimeTestSupport(managedAdmission: admission);
        var reached = Signal();
        int scopes = 0;
        fixture.Control.BeforeScope = (_, _) =>
        {
            if (Interlocked.Increment(ref scopes) == 2)
            {
                Close(admission, 2);
                reached.TrySetResult();
            }
            return Task.CompletedTask;
        };
        fixture.EnqueueGrant();
        await fixture.Runner.StartAsync("wallet-service", "wallet.compaction", "partition");
        await reached.Task.WaitAsync(Deadline);
        await JobRuntimeTestSupport.UntilAsync(() => fixture.Control.DisposedScopes >= 2);
        Assert.HasCount(0, fixture.Coordination.Calls);
        Assert.IsTrue(fixture.Control.Invocations.IsEmpty);
        Assert.IsFalse(fixture.Runtime.CaptureSnapshot().Any(item => item.State == JobLocalState.Faulted));

        Ready(admission, 3);
        await fixture.Control.Entered.Task.WaitAsync(Deadline);

        Assert.HasCount(1, fixture.Coordination.Calls);
    }

    [TestMethod]
    public async Task CallbackAdmissionConsultsLivePortRatherThanRetainedReadySnapshotOrChangedNotification()
    {
        var admission = new UnsignalledAdmission();
        await using var fixture = new JobRuntimeTestSupport(managedAdmission: admission);
        await fixture.Runtime.StartAsync();
        await using JobDutyReservation duty = fixture.Runtime.TryReserveContinuous(
            new("wallet-service", "wallet.compaction", "partition", JobExecutionMode.Continuous)).Duty!;
        await using ManualCoordinationLease lease = fixture.Grant();
        Assert.IsTrue(fixture.Runtime.AttachLease(duty, lease));
        using JobInvocationReservation invocation = fixture.Runtime.TryReserveIteration(duty)!;
        admission.Allowed = false;

        Assert.IsFalse(fixture.Runtime.TryEnterInvocation(invocation));

        Assert.IsTrue(admission.CaptureSnapshot().AdmissionAllowed, "A retained Ready observation cannot authorize a later callback.");
        Assert.IsFalse(invocation.Entered);
        Assert.IsTrue(fixture.Control.Invocations.IsEmpty);
        Assert.AreEqual(CoordinationLeaseState.Owned, lease.State);
    }

    [TestMethod]
    public async Task ClosedScheduledOccurrenceRemainsWithItsSourceAndCanUseTheSameExplicitAdmissionAfterReconciliation()
    {
        var admission = new ManualManagedWorkAdmission();
        await using var fixture = new ScheduledDispatcherFixture(managedAdmission: admission);
        await fixture.Runtime.StartAsync();
        AdmittedJobRun run = fixture.Run();
        ScheduledJobResult deferred = await fixture.Dispatcher.DispatchAsync(run);
        Assert.AreEqual(ScheduledJobOutcome.Deferred, deferred.Outcome);
        Assert.AreEqual(JobRuntime.ManagedAdmissionClosed, deferred.ReasonCode);
        Assert.AreEqual(0, fixture.AdmissionSource.Calls);
        Assert.AreEqual(0, fixture.Acquisitions);
        Assert.AreEqual(0, fixture.Calls);
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().PendingDispatches);

        Ready(admission, 1);
        ScheduledJobResult completed = await fixture.Dispatcher.DispatchAsync(run);

        Assert.AreEqual(ScheduledJobOutcome.Completed, completed.Outcome);
        Assert.AreSame(run, fixture.Contexts.Single().AdmittedRun);
        Assert.AreEqual(1, fixture.AdmissionSource.Calls);
        Assert.AreEqual(1, fixture.Acquisitions);
    }

    [TestMethod]
    [DataRow("source", 0)]
    [DataRow("initialScope", 0)]
    [DataRow("acquisition", 1)]
    [DataRow("workScope", 1)]
    public async Task ScheduledOccurrenceRechecksAtRealAdmissionAfterEachAwaitedBoundary(string boundary, int acquisitions)
    {
        var admission = new ManualManagedWorkAdmission();
        Ready(admission, 1);
        await using var fixture = new ScheduledDispatcherFixture(managedAdmission: admission);
        fixture.AdmissionSource.Validate = (_, _) =>
        {
            if (boundary == "source")
            {
                Close(admission, 2);
            }
            return ValueTask.FromResult(new JobAdmissionValidation(true));
        };
        fixture.Opening = (scope, _) =>
        {
            if (boundary == "initialScope" && scope == 1 || boundary == "workScope" && scope == 2)
            {
                Close(admission, 2);
            }
            return ValueTask.CompletedTask;
        };
        fixture.Acquire = (request, _) =>
        {
            ManualCoordinationLease lease = fixture.NewLease(request);
            if (boundary == "acquisition")
            {
                Close(admission, 2);
            }
            return Task.FromResult(new ManagedCoordinationResult(CoordinationAcquireOutcome.Acquired, lease));
        };
        await fixture.Runtime.StartAsync();
        JobConfigurationSnapshot retained = fixture.Runtime.Configuration;

        ScheduledJobResult result = await fixture.Dispatcher.DispatchAsync(fixture.Run());

        Assert.AreEqual(ScheduledJobOutcome.Deferred, result.Outcome);
        Assert.AreEqual(acquisitions, fixture.Acquisitions);
        Assert.AreEqual(0, fixture.Calls);
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().DutyReservations);
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().InvocationReservations);
        Assert.AreSame(retained, fixture.Runtime.Configuration);
        Assert.IsTrue(fixture.Leases.All(lease => lease.State == CoordinationLeaseState.Released));
    }

    [TestMethod]
    public async Task AdmittedScheduledCallbackRetainsCapturedSettingsTokenAndOriginalCompletionWhileAnotherOccurrenceDefers()
    {
        var admission = new ManualManagedWorkAdmission();
        Ready(admission, 1);
        await using var fixture = new ScheduledDispatcherFixture(managedAdmission: admission);
        var committed = new TransactionCompletion(TransactionOutcome.Committed, TransactionEndReason.CommitRequested, true, false, false);
        var original = new ScheduledJobResult(ScheduledJobOutcome.Completed, completion: committed);
        TaskCompletionSource<ScheduledJobResult> callback = fixture.Hold(original);
        fixture.Handler = (_, _, _) => callback.Task;
        await fixture.Runtime.StartAsync();
        AdmittedJobRun run = fixture.Run();
        Task<ScheduledJobResult> running = fixture.Dispatcher.DispatchAsync(run);
        await fixture.CallbackStarted.Task.WaitAsync(Deadline);
        Close(admission, 2);

        Assert.IsFalse(running.IsCompleted);
        Assert.IsFalse(fixture.CallbackToken.IsCancellationRequested);
        Assert.AreEqual(CoordinationLeaseState.Owned, fixture.Leases.Single().State);
        Assert.AreSame(run.Settings, fixture.Contexts.Single().Settings);
        Assert.AreEqual(ScheduledJobOutcome.Deferred, (await fixture.Dispatcher.DispatchAsync(fixture.Run("another", "another"))).Outcome);
        Assert.AreEqual(1, fixture.AdmissionSource.Calls);
        Assert.AreEqual(1, fixture.Acquisitions);
        callback.TrySetResult(original);

        Assert.AreSame(original, await running.WaitAsync(Deadline));
        Assert.AreEqual(TransactionOutcome.Committed, original.Completion!.Outcome);
        Assert.AreEqual(CoordinationLeaseState.Released, fixture.Leases.Single().State);
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().ActiveScheduledInvocations);
    }

    [TestMethod]
    public async Task ExistingHostingSelectsOptionalPortPassivelyAndPreservesOriginalConstructorContract()
    {
        var admission = new ManualManagedWorkAdmission();
        await using var fixture = new JobRuntimeTestSupport();
        var services = new ServiceCollection();
        services.AddSingleton<IJobCatalog>(fixture.Catalog);
        services.AddSingleton<IJobExecutionScopeFactory>(new ScopeMustNotRun());
        services.AddSingleton<IManagedWorkAdmission>(admission);
        services.AddCisJobs(fixture.Configuration(0));
        Assert.AreEqual(0L, admission.AdmissionChecks);
        await using ServiceProvider provider = services.BuildServiceProvider();
        JobRuntime runtime = provider.GetRequiredService<JobRuntime>();
        Assert.AreEqual(0L, admission.AdmissionChecks, "Constructing the runtime must not perform live admission or work.");
        await runtime.StartAsync();

        Assert.AreEqual(ContinuousJobStartOutcome.Deferred, await provider.GetRequiredService<IContinuousJobRunner>()
            .StartAsync("wallet-service", "wallet.compaction", "partition"));

        Assert.IsTrue(admission.AdmissionChecks > 0);
        Assert.IsNotNull(typeof(JobRuntime).GetConstructor([typeof(IJobCatalog), typeof(IJobExecutionScopeFactory),
            typeof(JobConfigurationSnapshot), typeof(IJobRunSource), typeof(IJobScheduleEvaluator), typeof(TimeProvider)]));
    }

    [TestMethod]
    public async Task ExistingConfiguredTargetSupervisorWakesAfterReconciliationWithoutAnotherConfigurationRevision()
    {
        var admission = new ManualManagedWorkAdmission();
        await using var fixture = new JobRuntimeTestSupport(managedAdmission: admission);
        await using var supervisor = new JobHostingSupervisor(fixture.Runtime, new()
        {
            ContinuousTargets = [new("wallet-service", "wallet.compaction", "partition", JobExecutionMode.Continuous)]
        });
        await fixture.Runtime.StartAsync();
        await supervisor.StartAsync(CancellationToken.None);
        Assert.HasCount(0, fixture.Coordination.Calls);
        Assert.IsTrue(fixture.Control.Invocations.IsEmpty);
        JobConfigurationSnapshot initial = fixture.Runtime.Configuration;
        fixture.EnqueueGrant();

        Ready(admission, 1);
        await fixture.Control.Entered.Task.WaitAsync(Deadline);

        Assert.HasCount(1, fixture.Coordination.Calls);
        Assert.HasCount(1, fixture.Control.Invocations);
        Assert.AreSame(initial, fixture.Runtime.Configuration);
        Assert.IsNull(supervisor.LastFailure);
    }

    [TestMethod]
    public async Task ManagedHostingRefusesAnUngatedBorrowedRuntimeInsteadOfSilentlyBypassingItsSelectedPort()
    {
        await using var fixture = new JobRuntimeTestSupport();
        var services = new ServiceCollection();
        services.AddSingleton<IJobCatalog>(fixture.Catalog);
        services.AddSingleton(fixture.Runtime);
        services.AddSingleton<IManagedWorkAdmission>(new ManualManagedWorkAdmission());
        services.AddCisJobs(fixture.Configuration(0));
        await using ServiceProvider provider = services.BuildServiceProvider();

        Assert.ThrowsExactly<InvalidOperationException>(() => provider.GetRequiredService<JobHostingSupervisor>());

        Assert.HasCount(0, fixture.Coordination.Calls);
        Assert.IsTrue(fixture.Control.Invocations.IsEmpty);
        Assert.AreEqual(0, fixture.Control.CreatedScopes);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Ready(ManualManagedWorkAdmission admission, long revision)
        => admission.Set(new(ManagedWorkAdmissionState.Ready, true, revision));
    private static void Close(ManualManagedWorkAdmission admission, long revision)
        => admission.Set(new(ManagedWorkAdmissionState.ContinuingOnly, false, revision, "management.control.unavailable"));

    // A stale observation deliberately disagrees with the live check, without a timer/event.
    // This proves the native admission point consumes TryAdmit, not a cached Ready publication.
    private sealed class UnsignalledAdmission : IManagedWorkAdmission
    {
        internal bool Allowed = true;
        public bool TryAdmit() => Allowed;
        public ManagedWorkAdmissionSnapshot CaptureSnapshot() => new(ManagedWorkAdmissionState.Ready, true, 1);
        public event EventHandler<ManagedWorkAdmissionChangedEventArgs>? Changed
        {
            add
            {
            }
            remove
            {
            }
        }
    }

    private sealed class ScopeMustNotRun : IJobExecutionScopeFactory
    {
        public ValueTask<IJobExecutionScope> CreateAsync(JobScopeRequest request, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Closed managed admission must precede native execution-scope construction.");
    }
}
