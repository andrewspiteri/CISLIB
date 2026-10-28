using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorUnavailableResourceFaultTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    [TestMethod]
    public async Task AnUnavailableTimerDisposalMessageCannotAcknowledgeSuccessfulStop()
    {
        var disposalFailure = new UnavailableMessageException();
        HealthDiagnostic later = HealthDiagnostic.FromException(disposalFailure);
        Assert.IsNull(later.Message);
        Assert.AreEqual(typeof(UnavailableMessageException).FullName, later.ExceptionType);
        Assert.IsTrue(later.IsTruncated);
        int accessorReadsBeforeResources = disposalFailure.MessageReads;

        var clock = new ResourceClock(disposalFailure);
        HealthcheckService? service = null;
        HealthCollectorRunner? runner = null;
        Exception? primary = null;
        InvalidOperationException? actualTerminalFault = null;
        InvalidOperationException? actualAdmissionRejection = null;
        var actualOperations = new List<Task>();
        var errors = new List<Exception>();
        int sutAsyncDisposals = 0, sutAsyncDisposalExits = 0, sutUnderlyingDrainsCompleted = 0;
        Task? sutUnderlyingDrain = null, fixtureOwnedDrain = null;
        bool sutDrainSucceededBeforeFallback = false;

        async Task ObserveAsync(Func<Task> begin)
        {
            Task actual = await HealthCollectorTestCleanup.EnterAsync(Deadline, begin);
            actualOperations.Add(actual);
            await actual.WaitAsync(Deadline);
        }

        try
        {
            service = new(new(), new() { Name = "unavailable resource fault", InstanceId = Guid.NewGuid() }, clock);
            runner = new(service);
            await ObserveAsync(() => runner.StartAsync());

            // Observe successful actual System timer Change before the caller
            // initiates terminal Stop. No timer callback is fabricated.
            await clock.ChangeReturned.Task.WaitAsync(Deadline);
            try { await ObserveAsync(() => runner.StopAsync()); }
            catch (InvalidOperationException error) when (error.GetType() == typeof(InvalidOperationException))
            { actualTerminalFault = error; }

            try { await ObserveAsync(() => runner.StartAsync()); }
            catch (InvalidOperationException error) when (error.GetType() == typeof(InvalidOperationException))
            { actualAdmissionRejection = error; }
        }
        catch (Exception error) { primary = error; }
        finally
        {
            // Every exact original public task remains in this collection.
            // Independently invoke every present real cleanup API, even when
            // a sibling is faulted or pending. The observed terminal fault is
            // accepted only by reference; other cleanup errors stay visible.
            void Begin(Func<Task> operation)
            {
                try { actualOperations.Add(Task.Run(operation)); }
                catch (Exception error) { Record(error); }
            }
            if (runner is not null)
            {
                Begin(() => runner.StopAsync());
                Begin(() => runner.DisposeAsync().AsTask());
            }
            if (service is not null) { Begin(() => service.DisposeAsync().AsTask()); }
            if (clock.Timer?.UnderlyingDrain is { } actualTimerDrain) { actualOperations.Add(actualTimerDrain); }
            Task drains = Task.WhenAll(actualOperations);
            try { await drains.WaitAsync(Deadline); }
            catch (Exception error) { Record(drains.Exception is { } fault ? fault : error); }
            foreach (Task actual in actualOperations)
            {
                if (!actual.IsFaulted && !actual.IsCanceled) { continue; }
                try { actual.GetAwaiter().GetResult(); }
                catch (Exception error) { Record(error); }
            }
            if (clock.Timer is { } ownedTimer)
            {
                // Save the SUT facts BEFORE independent fixture cleanup. A
                // missing SUT disposal cannot borrow this owner's fallback.
                sutAsyncDisposals = ownedTimer.AsyncDisposals;
                sutAsyncDisposalExits = ownedTimer.AsyncDisposalExits;
                sutUnderlyingDrainsCompleted = ownedTimer.UnderlyingDrainsCompleted;
                sutUnderlyingDrain = ownedTimer.UnderlyingDrain;
                sutDrainSucceededBeforeFallback = sutUnderlyingDrain?.IsCompletedSuccessfully == true;
                if (sutUnderlyingDrain is not null && !actualOperations.Contains(sutUnderlyingDrain))
                {
                    actualOperations.Add(sutUnderlyingDrain);
                    try { await sutUnderlyingDrain.WaitAsync(Deadline); }
                    catch (Exception error) { Record(error); }
                }
                // The clock created and retains this actual System timer.
                // Independently drain it on every exit after the public drain
                // attempt, without changing the saved SUT facts or histories.
                try
                {
                    fixtureOwnedDrain = Task.Run(ownedTimer.DisposeFixtureOwnedTimerAsync);
                    await fixtureOwnedDrain.WaitAsync(Deadline);
                }
                catch (Exception error) { Record(error); }
            }
            foreach (Task actual in actualOperations)
            {
                if (!actual.IsFaulted && !actual.IsCanceled) { continue; }
                try { actual.GetAwaiter().GetResult(); }
                catch (Exception error) { Record(error); }
            }
            if (fixtureOwnedDrain is { } ownedDrain && (ownedDrain.IsFaulted || ownedDrain.IsCanceled))
            {
                try { ownedDrain.GetAwaiter().GetResult(); }
                catch (Exception error) { Record(error); }
            }
            foreach (Exception error in clock.UnknownErrors) { Record(error); }
        }

        void Record(Exception error)
        {
            if (error is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions) { Record(inner); }
            }
            else if (!ReferenceEquals(error, primary) && !ReferenceEquals(error, actualTerminalFault) &&
                !ReferenceEquals(error, actualAdmissionRejection) && !errors.Any(known => ReferenceEquals(known, error)))
            { errors.Add(error); }
        }

        if (errors.Count != 0)
        {
            if (primary is not null) { errors.Insert(0, primary); }
            throw new AggregateException("Actual resource work or cleanup remains unresolved.", errors);
        }
        if (primary is not null) { ExceptionDispatchInfo.Capture(primary).Throw(); }

        Assert.AreEqual(1, clock.TimerCreations);
        Assert.AreEqual(0, clock.Callbacks);
        Assert.AreEqual(1, clock.DisposalFailures.Count);
        Assert.AreSame(disposalFailure, clock.DisposalFailures.Single());
        Assert.AreEqual(accessorReadsBeforeResources + 1, disposalFailure.MessageReads);
        Assert.IsNotNull(clock.Timer);
        Assert.AreEqual(1, clock.Timer.Changes);
        Assert.IsTrue(clock.DisposalEntered.Task.IsCompletedSuccessfully);
        Assert.AreEqual(1, sutAsyncDisposals);
        Assert.AreEqual(1, sutAsyncDisposalExits);
        Assert.AreEqual(1, sutUnderlyingDrainsCompleted);
        Assert.IsNotNull(sutUnderlyingDrain);
        Assert.IsTrue(sutDrainSucceededBeforeFallback);
        Assert.AreEqual(1, clock.Timer.FixtureOwnedDisposals);
        Assert.IsNotNull(fixtureOwnedDrain);
        Assert.IsTrue(fixtureOwnedDrain.IsCompletedSuccessfully);
        Assert.IsTrue(actualOperations.All(actual => actual.IsCompleted));
        Assert.IsNotNull(actualAdmissionRejection);
        // The actual timer disposal failed after its underlying resource drain.
        // Unavailable diagnostic text is accepted input, not successful Stop.
        Assert.IsNotNull(actualTerminalFault,
            "A real timer disposal failure must remain a terminal runner failure when its runtime message is unavailable.");
    }

    private sealed class UnavailableMessageException : Exception
    {
        private int messageReads;
        internal int MessageReads => Volatile.Read(ref messageReads);
        public override string Message
        {
            get
            {
                Interlocked.Increment(ref messageReads);
                throw new InvalidOperationException("This runtime message accessor is unavailable.");
            }
        }
    }

    private sealed class ResourceClock(Exception disposalFailure) : TimeProvider
    {
        private int timerCreations, callbacks;
        internal int TimerCreations => Volatile.Read(ref timerCreations);
        internal int Callbacks => Volatile.Read(ref callbacks);
        internal ObservedTimer? Timer { get; private set; }
        internal TaskCompletionSource DisposalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ChangeReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConcurrentQueue<Exception> UnknownErrors { get; } = new();
        internal ConcurrentQueue<Exception> DisposalFailures { get; } = new();
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp()
        {
            try { return TimeProvider.System.GetTimestamp(); }
            catch (Exception error) { UnknownErrors.Enqueue(error); throw; }
        }
        public override DateTimeOffset GetUtcNow()
        {
            try { return TimeProvider.System.GetUtcNow(); }
            catch (Exception error) { UnknownErrors.Enqueue(error); throw; }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                if (Interlocked.Increment(ref timerCreations) != 1 ||
                    dueTime != Timeout.InfiniteTimeSpan || period != Timeout.InfiniteTimeSpan)
                { throw new InvalidOperationException("The idle runner must create exactly one initially disabled timer."); }
                ITimer actual = TimeProvider.System.CreateTimer(value =>
                {
                    Interlocked.Increment(ref callbacks);
                    try { callback(value); }
                    catch (Exception error) { UnknownErrors.Enqueue(error); throw; }
                }, state, dueTime, period);
                Timer = new ObservedTimer(this, actual, disposalFailure);
                return Timer;
            }
            catch (Exception error) { UnknownErrors.Enqueue(error); throw; }
        }

        internal sealed class ObservedTimer(ResourceClock clock, ITimer actual, Exception disposalFailure) : ITimer
        {
            private int changes, asyncDisposals, asyncDisposalExits, underlyingDrainsCompleted, fixtureOwnedDisposals;
            internal int Changes => Volatile.Read(ref changes);
            internal int AsyncDisposals => Volatile.Read(ref asyncDisposals);
            internal int AsyncDisposalExits => Volatile.Read(ref asyncDisposalExits);
            internal int UnderlyingDrainsCompleted => Volatile.Read(ref underlyingDrainsCompleted);
            internal int FixtureOwnedDisposals => Volatile.Read(ref fixtureOwnedDisposals);
            internal Task? UnderlyingDrain { get; private set; }

            internal Task DisposeFixtureOwnedTimerAsync()
            {
                Interlocked.Increment(ref fixtureOwnedDisposals);
                return actual.DisposeAsync().AsTask();
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                try
                {
                    if (Interlocked.Increment(ref changes) != 1 ||
                        dueTime != Timeout.InfiniteTimeSpan || period != Timeout.InfiniteTimeSpan)
                    { throw new InvalidOperationException("The idle pump must make one real disabled timer Change."); }
                    if (!actual.Change(dueTime, period)) { throw new InvalidOperationException("The real timer rejected Change."); }
                }
                catch (Exception error) { clock.UnknownErrors.Enqueue(error); throw; }
                clock.ChangeReturned.TrySetResult();
                return true;
            }

            public async ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref asyncDisposals);
                clock.DisposalEntered.TrySetResult();
                try
                {
                    try
                    {
                        UnderlyingDrain = actual.DisposeAsync().AsTask();
                        await UnderlyingDrain.ConfigureAwait(false);
                        Interlocked.Increment(ref underlyingDrainsCompleted);
                    }
                    catch (Exception error) { clock.UnknownErrors.Enqueue(error); throw; }
                    clock.DisposalFailures.Enqueue(disposalFailure);
                    throw disposalFailure;
                }
                finally { Interlocked.Increment(ref asyncDisposalExits); }
            }

            public void Dispose()
            {
                var error = new InvalidOperationException("The runner must use the timer's asynchronous drain.");
                clock.UnknownErrors.Enqueue(error);
                try { actual.Dispose(); }
                catch (Exception cleanup) { clock.UnknownErrors.Enqueue(cleanup); throw; }
                throw error;
            }
        }
    }
}
