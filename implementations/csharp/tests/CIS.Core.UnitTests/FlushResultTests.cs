using System.Reflection;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class FlushResultTests
{
    [TestMethod]
    [DataRow("empty")]
    [DataRow("filtered")]
    [DataRow("closed")]
    [DataRow("physical")]
    [DataRow("write-timeout")]
    [DataRow("flush-timeout")]
    [DataRow("cancel")]
    [DataRow("pre-cancel")]
    [DataRow("loss")]
    [DataRow("write-failure")]
    [DataRow("flush-failure")]
    [DataRow("later-failure")]
    [DataRow("exhaustion")]
    [DataRow("clock")]
    [DataRow("dual")]
    [DataRow("long-timeout")]
    [DataRow("system-timeout")]
    public async Task ActualPublicFlushReturnsTruthfulOwnedResults(string mode)
    {
        using var root = new TemporaryDirectory();
        FlushResultExpectations.Validate(mode, await FlushResultFixture.ExecuteAsync(mode, root.Path));
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    [DataRow(-10000L)]
    [DataRow(long.MinValue)]
    public async Task InvalidBudgetWinsOverCancellationAndDoesNotRegister(long ticks)
    {
        using var root = new TemporaryDirectory(); using var source = new CancellationTokenSource(); source.Cancel();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new FlushResultFixture.Clock();
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(),
            new LoggingOptions { FileLevel = CoreLogLevel.None }, _ => ValueTask.CompletedTask, null, clock,
            () => new ValueTask(held.Task));
        try
        {
            ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => runtime.FlushAsync(TimeSpan.FromTicks(ticks), source.Token));
            Assert.AreEqual("timeout", error.ParamName);
            Assert.AreEqual(0, runtime.QueuedOutputs!.Console!.RegisteredFlushCount);
            Assert.AreEqual(0, clock.Created);
        }
        finally { held.TrySetResult(); runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion; }
    }

    [TestMethod]
    public async Task PositiveSubMillisecondBudgetNeverExpiresEarly()
    {
        var clock = new FlushResultFixture.Clock(); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), new LoggingOptions { FileLevel = CoreLogLevel.None },
            _ => new ValueTask(release.Task), null, clock, () => ValueTask.CompletedTask);
        try
        {
            runtime.CreateLogger("TinyBudget").Write(CoreLogLevel.Information, FlushResultFixture.Data("first"));
            Task<LogFlushResult> wait = runtime.FlushAsync(TimeSpan.FromTicks(1), CancellationToken.None);
            clock.Fire(); Assert.IsFalse(wait.IsCompleted);
            clock.Advance(TimeSpan.FromTicks(1)); clock.Fire();
            LogFlushResult result = await wait;
            Assert.AreEqual(LogCompletion.TimedOut, result.Completion); Assert.AreEqual(1L, result.Outputs[0].Pending);
            Assert.AreEqual(1, clock.Disposed);
        }
        finally { release.TrySetResult(); runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion; }
    }

    private sealed class JoiningClock : TimeProvider, IDisposable
    {
        internal readonly TaskCompletionSource CallbackStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource DisposalStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim ReleaseCallback = new();
        internal int Disposed, GuardExpired;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ITimer actual = TimeProvider.System.CreateTimer(_ =>
            {
                CallbackStarted.TrySetResult();
                if (!ReleaseCallback.Wait(TimeSpan.FromSeconds(15))) { Interlocked.Exchange(ref GuardExpired, 1); }
                callback(state);
            }, null, dueTime, period);
            return new JoiningTimer(this, actual);
        }
        private sealed class JoiningTimer(JoiningClock clock, ITimer actual) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => actual.Change(dueTime, period);
            public void Dispose() => actual.Dispose();
            public async ValueTask DisposeAsync()
            {
                clock.DisposalStarted.TrySetResult();
                await actual.DisposeAsync();
                Interlocked.Increment(ref clock.Disposed);
            }
        }
        public void Dispose() { ReleaseCallback.Set(); ReleaseCallback.Dispose(); }
    }

    [TestMethod]
    public async Task CallerCancellationJoinsAnActuallyActiveTimerCallbackBeforeDisposingItsSource()
    {
        using var clock = new JoiningClock(); using var caller = new CancellationTokenSource();
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), new LoggingOptions { FileLevel = CoreLogLevel.None },
            _ => new ValueTask(releaseWrite.Task), null, clock, () => ValueTask.CompletedTask);
        Task<LogFlushResult>? wait = null;
        try
        {
            runtime.CreateLogger("TimerJoin").Write(CoreLogLevel.Information, FlushResultFixture.Data("first"));
            wait = runtime.FlushAsync(TimeSpan.FromMilliseconds(30), caller.Token);
            await clock.CallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(15)); caller.Cancel();
            await clock.DisposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.IsFalse(wait.IsCompleted); Assert.AreEqual(0, clock.Disposed);
            await Assert.ThrowsExactlyAsync<TimeoutException>(() => wait.WaitAsync(TimeSpan.FromMilliseconds(50)));
            clock.ReleaseCallback.Set();
            try { await wait.WaitAsync(TimeSpan.FromSeconds(15)); Assert.Fail("Expected caller cancellation."); }
            catch (OperationCanceledException error) { Assert.AreEqual(caller.Token, error.CancellationToken); }
            Assert.AreEqual(1, clock.Disposed); Assert.AreEqual(0, clock.GuardExpired);
            Assert.AreEqual(1, runtime.QueuedOutputs!.Console!.RegisteredFlushCount);
        }
        finally
        {
            clock.ReleaseCallback.Set(); caller.Cancel();
            if (wait is not null) { try { await wait.WaitAsync(TimeSpan.FromSeconds(15)); } catch (OperationCanceledException) { } }
            releaseWrite.TrySetResult(); runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    [TestMethod]
    public void PublicResultsHaveNoMutationOrConstructionSurface()
    {
        foreach (Type type in new[] { typeof(LogFlushResult), typeof(LogOutputDrainResult), typeof(LogOutputRejectionSnapshot) })
        {
            Assert.IsTrue(type.IsSealed); Assert.AreEqual(0, type.GetConstructors().Length);
            foreach (PropertyInfo property in type.GetProperties()) { Assert.IsNull(property.GetSetMethod(true)); }
        }
    }
}
