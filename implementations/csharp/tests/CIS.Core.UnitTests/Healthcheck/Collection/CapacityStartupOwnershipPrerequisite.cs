using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

// A capacity-specific prerequisite using the established repeated-Start
// terminal-ownership proof. All timers below are real System timers.
internal static class CapacityStartupOwnershipPrerequisite
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    internal static void AssertSettledStart()
    {
        Observation observation = RunAndRelease();
        ThrowErrors(observation.Facts);
        RequireTerminal(observation);
        var elapsed = Stopwatch.StartNew();
        while (observation.Facts.Owners().Any(owner => owner.IsAlive) && elapsed.Elapsed < Deadline)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(1);
        }
        if (observation.Facts.Owners().Any(owner => owner.IsAlive))
        {
            throw new InvalidOperationException("The actual startup prerequisite owner graph remains live after terminal cleanup.");
        }
        ThrowErrors(observation.Facts);
        RequireTerminal(observation);
        if (observation.First is null || observation.Second is null || observation.Facts.StartCalls != 2 || observation.Facts.StartReturns != 2
            || observation.Facts.CreationEntries != observation.Facts.CreationReturns
            || observation.Facts.TimerDisposeEntries != observation.Facts.TimerDisposeReturns
            || observation.Facts.TimerDisposeReturns != observation.Facts.CreationReturns
            || observation.Facts.TimerDisposeSuccesses != observation.Facts.CreationReturns)
        {
            throw new InvalidOperationException("Actual startup calls and timer ownership are not fully accounted for.");
        }
        foreach (Task original in new Task[] { observation.First, observation.Second })
        {
            if (original.IsFaulted || original.IsCanceled) { original.GetAwaiter().GetResult(); }
            Assert.IsTrue(original.IsCompleted,
                "An accepted startup promise must settle after all actual runner resources and startup owners have returned and died.");
            original.GetAwaiter().GetResult();
        }
        Assert.AreEqual(1, observation.Facts.CreationReturns);
        Assert.AreEqual(Timeout.InfiniteTimeSpan, observation.Facts.InitialDue);
        Assert.AreEqual(Timeout.InfiniteTimeSpan, observation.Facts.InitialPeriod);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Observation RunAndRelease()
    {
        var facts = new Facts();
        var clock = new StartupClock(facts);
        facts.Own(clock);
        HealthcheckService? service = null;
        IConditionMonitor? monitor = null;
        HealthCollectorRunner? runner = null;
        Task? first = null, second = null;
        var actual = new List<Task>();
        try
        {
            service = new HealthcheckService(new(), new() { Name = "capacity startup prerequisite", InstanceId = Guid.NewGuid() }, clock);
            facts.Own(service);
            monitor = service.CreateCondition(new() { IsCritical = true, InitiallyFlagged = false });
            facts.Own(monitor);
            runner = new HealthCollectorRunner(service);
            facts.Own(runner);
            facts.StartCalls++;
            first = runner.StartAsync();
            facts.StartReturns++;
            facts.StartCalls++;
            second = runner.StartAsync();
            facts.StartReturns++;
        }
        catch (Exception error) { facts.Record(error); }
        finally
        {
            if (runner is not null)
            {
                Begin(() => runner.StopAsync());
                Begin(() => runner.DisposeAsync().AsTask());
            }
            if (service is not null) { Begin(() => service.DisposeAsync().AsTask()); }
            Task drains = Task.WhenAll(actual);
            try { drains.WaitAsync(Deadline).GetAwaiter().GetResult(); }
            catch (Exception error) { facts.Record(error); }
            foreach (Task task in actual)
            {
                if (!task.IsCompleted) { continue; }
                Observe(task);
                if (task.Exception is { } aggregate)
                {
                    foreach (Exception error in aggregate.InnerExceptions) { facts.Record(error); }
                }
            }
            foreach (Task task in new Task?[] { first, second }.OfType<Task>())
            {
                if (task.IsCompleted) { Observe(task); }
            }
            GC.KeepAlive(monitor);
        }
        return new(first, second, facts, actual.Count, actual.Count(task => task.IsCompletedSuccessfully));

        void Begin(Func<Task> call)
        {
            facts.TerminalCalls++;
            try { actual.Add(call()); facts.TerminalReturns++; }
            catch (Exception error) { facts.Record(error); }
        }
        void Observe(Task task)
        {
            try { task.GetAwaiter().GetResult(); }
            catch (Exception error) { facts.Record(error); }
        }
    }

    private static void RequireTerminal(Observation observation)
    {
        if (observation.Facts.TerminalCalls != 3 || observation.Facts.TerminalReturns != 3
            || observation.ActualTerminalTasks != 3 || observation.ActualTerminalSuccesses != 3)
        {
            throw new InvalidOperationException("Every actual startup Stop, runner disposal and service disposal must return successfully.");
        }
    }
    private static void ThrowErrors(Facts facts)
    {
        Exception[] errors = facts.Errors();
        if (errors.Length == 1) { ExceptionDispatchInfo.Capture(errors[0]).Throw(); }
        if (errors.Length > 1) { throw new AggregateException("Actual startup producer or resource failures remain unresolved.", errors); }
    }
    private sealed record Observation(Task? First, Task? Second, Facts Facts, int ActualTerminalTasks, int ActualTerminalSuccesses);
    private sealed class Facts
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        private readonly List<WeakReference> owners = [];
        internal int StartCalls, StartReturns, TerminalCalls, TerminalReturns;
        internal int CreationEntries, CreationReturns, TimerDisposeEntries, TimerDisposeReturns, TimerDisposeSuccesses;
        internal TimeSpan InitialDue, InitialPeriod;
        internal void Own(object owner) { lock (gate) { owners.Add(new(owner)); } }
        internal WeakReference[] Owners() { lock (gate) { return owners.ToArray(); } }
        internal Exception[] Errors() { lock (gate) { return errors.ToArray(); } }
        internal void Record(Exception error) { lock (gate) { if (!errors.Any(existing => ReferenceEquals(existing, error))) { errors.Add(error); } } }
    }
    private sealed class StartupClock(Facts facts) : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref facts.CreationEntries);
            try
            {
                ITimer actual = base.CreateTimer(callback, state, dueTime, period);
                var observed = new StartupTimer(actual, facts);
                facts.Own(actual);
                facts.Own(observed);
                facts.InitialDue = dueTime;
                facts.InitialPeriod = period;
                Interlocked.Increment(ref facts.CreationReturns);
                return observed;
            }
            catch (Exception error) { facts.Record(error); throw; }
        }
    }
    private sealed class StartupTimer(ITimer actual, Facts facts) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            try { return actual.Change(dueTime, period); }
            catch (Exception error) { facts.Record(error); throw; }
        }
        public void Dispose()
        {
            try { actual.Dispose(); }
            catch (Exception error) { facts.Record(error); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref facts.TimerDisposeEntries);
            try { await actual.DisposeAsync(); Interlocked.Increment(ref facts.TimerDisposeSuccesses); }
            catch (Exception error) { facts.Record(error); throw; }
            finally { Interlocked.Increment(ref facts.TimerDisposeReturns); }
        }
    }
}
