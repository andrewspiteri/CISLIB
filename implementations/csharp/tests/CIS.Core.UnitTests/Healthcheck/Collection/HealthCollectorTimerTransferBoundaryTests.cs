using System.Reflection;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorTimerTransferBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AStartupInterruptionAfterTransferCannotDisposeTheSameTimerTwice(bool stopDuringFactory)
    {
        using var clock = new TransferClock();
        var service = new HealthcheckService(new(), timeProvider: clock);
        var runner = new HealthCollectorRunner(service);
        var starting = new OwnedStart(runner);
        using var context = new StartupObserverContext();
        using var interruption = new ObservedInterruption(starting.Thread);
        Task? joined = null, observed = null, returned = null, stop = null;
        Exception? primary = null;
        try
        {
            starting.Start();
            await clock.FactoryEntered.Task.WaitAsync(Deadline);
            joined = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => runner.StartAsync());
            Assert.IsFalse(joined.IsCompleted, "The second public start joins the actual pending timer factory.");
            observed = ObserveInContext(context, joined);
            if (stopDuringFactory)
            {
                stop = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => runner.StopAsync());
                Assert.IsFalse(stop.IsCompleted, "The in-flight factory still owns a real timer.");
            }

            clock.ReleaseFactory();
            Thread postingThread = await context.PostEntered.Task.WaitAsync(Deadline);
            Assert.AreSame(starting.Thread, postingThread,
                "Only the fixture-owned startup thread may be paused at the real public-start continuation boundary.");
            Assert.IsTrue(joined.IsCompletedSuccessfully,
                "The joined start has completed; timer publication or stop handoff already succeeded before this boundary.");

            InGate(ReadGate(runner), () =>
            {
                // The fixture owns this gate and calls Stop reentrantly. It
                // never writes runner state or invokes a private operation.
                stop ??= runner.StopAsync();
                Assert.AreEqual(1, clock.DisposalRequests,
                    "Normal publication and stop-during-factory handoff must each issue one resource disposal.");
                context.ReleasePost();
                context.PostExited.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
                starting.WaitUntilBlocked();
                starting.Thread.Interrupt();
                MethodBase?[] frames = interruption.Thrown.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
                MethodInfo? expected = typeof(HealthCollectorRunner).GetMethod(nameof(HealthCollectorRunner.StartAsync), [typeof(CancellationToken)]);
                Assert.IsNotNull(expected);
                Assert.IsTrue(frames.Any(frame => Equals(frame, expected)),
                    "The completed public start must still own the interrupted call after the successful transfer; private helper frames may be inlined.");
            });

            returned = await starting.Returned.Task.WaitAsync(Deadline);
            await returned.WaitAsync(Deadline);
            await observed.WaitAsync(Deadline);
            await starting.JoinAsync();
            Assert.AreEqual(1, clock.DisposalRequests,
                "A later startup failure cannot reclaim a timer already transferred to the runner or its stop disposer.");
            Assert.AreEqual(0, clock.ActualDisposals);
            Assert.IsFalse(clock.TimerDrained.IsCompleted);
            Assert.IsNotNull(stop);
            Assert.IsFalse(stop.IsCompleted,
                "Stop must retain the actual timer's pending disposal barrier after the later startup interruption.");

            clock.ReleaseDisposal();
            await stop.WaitAsync(Deadline);
            await clock.TimerDrained.WaitAsync(Deadline);
            Assert.AreEqual(1, clock.DisposalRequests);
            Assert.AreEqual(1, clock.ActualDisposals);
            Assert.AreEqual(1, clock.DisposalExits);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            clock.ReleaseFactory();
            context.ReleasePost();
            clock.ReleaseDisposal();
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, [],
                () => starting.JoinAsync(),
                ObserveActualStartAsync,
                () => joined ?? Task.CompletedTask,
                () => observed ?? Task.CompletedTask,
                () => stop ?? Task.CompletedTask,
                () => runner.DisposeAsync().AsTask(),
                () => service.DisposeAsync().AsTask(),
                () => clock.DisposeFixtureTimerAsync());
        }

        async Task ObserveActualStartAsync()
        {
            Task actual = returned ?? await starting.Returned.Task;
            await actual;
        }
    }

    private static Task ObserveInContext(SynchronizationContext context, Task start)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        try { SynchronizationContext.SetSynchronizationContext(context); return ObserveAsync(start); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }

        static async Task ObserveAsync(Task actual) => await actual;
    }

    private static object ReadGate(HealthCollectorRunner runner)
    {
        FieldInfo? field = typeof(HealthCollectorRunner).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        object? gate = field.GetValue(runner);
        Assert.IsNotNull(gate);
        return gate;
    }

    private static void InGate(object gate, Action operation)
    {
        bool entered = Monitor.TryEnter(gate, Deadline);
        Assert.IsTrue(entered, "The fixture must acquire the real synchronization boundary within its deadline.");
        try { operation(); }
        finally { Monitor.Exit(gate); }
    }

    private sealed class OwnedStart
    {
        private bool started;
        internal OwnedStart(HealthCollectorRunner runner)
        {
            Thread = new Thread(() =>
            {
                try { Returned.TrySetResult(runner.StartAsync()); }
                catch (Exception error) { Returned.TrySetException(error); }
            })
            { IsBackground = true, Name = "health-timer-transfer-start" };
        }
        internal Thread Thread { get; }
        internal TaskCompletionSource<Task> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Start() { started = true; Thread.Start(); }
        internal void WaitUntilBlocked()
        {
            bool blocked = SpinWait.SpinUntil(() => Returned.Task.IsCompleted ||
                (Thread.ThreadState & ThreadState.WaitSleepJoin) != 0, Deadline);
            Assert.IsTrue(blocked && !Returned.Task.IsCompleted,
                "The owned startup call must wait at the held real gate after Post has returned.");
        }
        internal Task JoinAsync() => Task.Run(() =>
        {
            if (started) { Assert.IsTrue(Thread.Join(Deadline), "The fixture-owned startup thread must actually exit."); }
        });
    }

    // .NET 10 calls a captured SynchronizationContext.Post while completing the
    // awaited task, even when asynchronous continuations prohibit inlining:
    // https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskContinuation.cs#L392
    // This observes the public StartAsync completion. Post never throws into
    // the runtime's continuation-dispatch path; finally always releases it.
    private sealed class StartupObserverContext : SynchronizationContext, IDisposable
    {
        private readonly ManualResetEventSlim release = new();
        internal TaskCompletionSource<Thread> PostEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource PostExited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            PostEntered.TrySetResult(Thread.CurrentThread);
            release.Wait();
            ThreadPool.UnsafeQueueUserWorkItem(static item => item.Callback(item.State), (Callback: callback, State: state), preferLocal: false);
            PostExited.TrySetResult();
        }
        internal void ReleasePost() => release.Set();
        public void Dispose() { release.Set(); release.Dispose(); }
    }

    private sealed class ObservedInterruption : IDisposable
    {
        private readonly EventHandler<FirstChanceExceptionEventArgs> observer;
        internal ObservedInterruption(Thread owned)
        {
            observer = (_, eventArgs) =>
            {
                if (ReferenceEquals(Thread.CurrentThread, owned) && eventArgs.Exception is ThreadInterruptedException)
                {
                    // FirstChance runs while the runtime is still building the exception's
                    // own trace frame by frame. Capture this live owned call stack before
                    // publishing; another thread must not inspect a still-changing trace.
                    MethodBase?[] frames = new System.Diagnostics.StackTrace(fNeedFileInfo: false)
                        .GetFrames().Select(frame => frame.GetMethod()).ToArray();
                    Thrown.TrySetResult(frames);
                }
            };
            AppDomain.CurrentDomain.FirstChanceException += observer;
        }
        internal TaskCompletionSource<MethodBase?[]> Thrown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Dispose() => AppDomain.CurrentDomain.FirstChanceException -= observer;
    }

    private sealed class TransferClock : TimeProvider, IDisposable
    {
        private readonly ManualResetEventSlim factoryRelease = new();
        private readonly TaskCompletionSource disposalRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ITimer? actualTimer;
        private int disposalRequests, actualDisposals, disposalExits;
        internal TaskCompletionSource FactoryEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int DisposalRequests => Volatile.Read(ref disposalRequests);
        internal int ActualDisposals => Volatile.Read(ref actualDisposals);
        internal int DisposalExits => Volatile.Read(ref disposalExits);
        internal Task TimerDrained => drained.Task;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ITimer created = TimeProvider.System.CreateTimer(callback, state, dueTime, period);
            actualTimer = created;
            FactoryEntered.TrySetResult();
            factoryRelease.Wait();
            return new Timer(this, created);
        }
        internal void ReleaseFactory() => factoryRelease.Set();
        internal void ReleaseDisposal() => disposalRelease.TrySetResult();
        internal Task DisposeFixtureTimerAsync() => actualTimer?.DisposeAsync().AsTask() ?? Task.CompletedTask;
        public void Dispose() => factoryRelease.Dispose();

        private sealed class Timer(TransferClock owner, ITimer actual) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => actual.Change(dueTime, period);
            public async ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.disposalRequests);
                await owner.disposalRelease.Task.ConfigureAwait(false);
                Interlocked.Increment(ref owner.actualDisposals);
                await actual.DisposeAsync().ConfigureAwait(false);
                Interlocked.Increment(ref owner.disposalExits);
                owner.drained.TrySetResult();
            }
            public void Dispose() => actual.Dispose();
        }
    }
}
