using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectionRetirementOverlapBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task MonitorDisposalRetainsAnEarlierIdleCollectionRetirementUntilItsActualObserverReturns()
    {
        var service = new HealthcheckService(new(),
            new HealthApplicationIdentity { Name = "overlapping idle collection retirement", InstanceId = Guid.NewGuid() });
        IHealthCollectionState state = ((IHealthStateAccess)service).CollectionState;
        IConditionMonitor? monitor = null;
        Task<WithdrawalCall>? invocation = null;
        Task? attachmentDrain = null, monitorDrain = null, serviceDrain = null;
        CancellationTokenRegistration observer = default;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        int enteredCount = 0, returnedCount = 0;
        bool actualStateCallReturned = false;
        Exception? primaryFailure = null;
        try
        {
            monitor = service.CreateCondition(new() { IsCritical = true, InitiallyFlagged = false });
            HealthCollectionTarget target = state.RegisterCollection(monitor.MonitorId, Guid.NewGuid(), new());
            monitorDrain = monitor.WaitForDisposalAsync();
            observer = target.Retirement.Register(() =>
            {
                Interlocked.Increment(ref enteredCount);
                entered.TrySetResult();
                release.Wait();
                Interlocked.Increment(ref returnedCount);
            });

            // Task.Run(Func<Task>) would unwrap and wait for the tested drain.
            // A plain result wrapper instead exposes completion of the actual
            // synchronous state call and its synchronous Dispatch path.
            invocation = Task.Run(() => new WithdrawalCall(state.WithdrawCollection(target)));
            await entered.Task.WaitAsync(Deadline);
            Assert.AreEqual(1, Volatile.Read(ref enteredCount));
            Assert.AreEqual(0, Volatile.Read(ref returnedCount));
            Assert.IsTrue(target.Retirement.IsCancellationRequested);
            Assert.IsFalse(invocation.IsCompleted);
            Assert.AreEqual(1, service.CaptureSnapshot().MonitorCounts.Active);

            Task monitorDisposal = monitor.DisposeAsync().AsTask();
            Assert.AreSame(monitorDrain, monitorDisposal);
            Assert.IsFalse(monitorDrain.IsCompleted,
                "An idle collection's actual retirement observer still belongs to the monitor drain.");
            Assert.IsFalse(invocation.IsCompleted);
            Assert.AreEqual(0, service.CaptureSnapshot().MonitorCounts.Active);
            serviceDrain = service.DisposeAsync().AsTask();
            Assert.IsFalse(serviceDrain.IsCompleted);

            release.Set();
            WithdrawalCall returned = await invocation.WaitAsync(Deadline);
            actualStateCallReturned = true;
            attachmentDrain = returned.Drain;

            Assert.IsTrue(release.IsSet);
            Assert.AreEqual(1, Volatile.Read(ref returnedCount));
            // These are the actual state-owned TaskCompletionSource tasks.
            // The joined non-async state call has returned after CancelRetirement
            // and Dispatch (including completion publication) ran synchronously.
            // No caller continuation or asynchronous wrapper is assumed settled.
            Assert.IsTrue(attachmentDrain.IsCompletedSuccessfully,
                "The state-owned idle attachment drain must be published after its actual retirement dispatch returns.");
            Assert.IsTrue(monitorDrain.IsCompletedSuccessfully,
                "The shared monitor drain must complete after its sole actual retirement dispatch returns.");
            Assert.IsTrue(serviceDrain.IsCompletedSuccessfully,
                "The service has no active monitors, operations or remaining actual retirement dispatch.");
            await Task.WhenAll(attachmentDrain, monitorDrain, serviceDrain).WaitAsync(Deadline);
        }
        catch (Exception error) { primaryFailure = error; }

        Exception? cleanupFailure = null;
        try
        {
            release.Set();
            if (invocation is not null)
            {
                WithdrawalCall returned = await invocation.WaitAsync(Deadline);
                actualStateCallReturned = true;
                attachmentDrain ??= returned.Drain;
            }
            observer.Dispose();
            if (monitor is not null) { monitorDrain ??= monitor.DisposeAsync().AsTask(); }
            serviceDrain ??= service.DisposeAsync().AsTask();

            // After the actual synchronous dispatch and the sole observer have
            // both returned, this fixture owns no I/O, runner, lease, evaluation
            // or background callback. An incorrect managed completion signal is
            // the asserted defect; waiting on it again would hide that assertion.
            bool actualWorkJoined = actualStateCallReturned && Volatile.Read(ref returnedCount) == 1;
            Task[] drains = new[] { attachmentDrain, monitorDrain, serviceDrain }.OfType<Task>().ToArray();
            foreach (Task drain in drains)
            {
                if (primaryFailure is null || !actualWorkJoined || drain.IsCompleted)
                {
                    await drain.WaitAsync(Deadline);
                }
            }
        }
        catch (Exception error) { cleanupFailure = error; }
        GC.KeepAlive(monitor);
        if (primaryFailure is not null && cleanupFailure is not null)
        {
            throw new AggregateException("The state assertion and actual owned-work cleanup both failed.", primaryFailure, cleanupFailure);
        }
        if (cleanupFailure is not null) { ExceptionDispatchInfo.Capture(cleanupFailure).Throw(); }
        if (primaryFailure is not null) { ExceptionDispatchInfo.Capture(primaryFailure).Throw(); }
    }

    private sealed record WithdrawalCall(Task Drain);
}
