using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthBindingRetirementBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public Task RetirementWithoutOutstandingWorkCompletesTheSharedBindingBarrier(bool owned, bool availability)
    {
        RetirementObservation observed = RetireBindingAndReleaseActualOwners(owned, availability);
        ReviewRetiredBinding(observed);
        return observed.First!;
    }

    [TestMethod]
    public async Task DisposingTheActuallyExecutingMonitorRejectsItsOwnWaitBeforeReturningATask()
    {
        var service = Service();
        HealthCollectorRunner? runner = null;
        IHealthCollectionRegistration? registration = null;
        IConsecutiveFailureMonitor? ownedMonitor = null;
        Exception? primary = null;
        try
        {
            IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            ownedMonitor = monitor;
            Exception? observed = null;
            Task? incorrectlyReturnedBarrier = null;
            bool incompleteWhenReturned = false;
            var owner = new ObservingOwner(() =>
            {
                try
                {
                    incorrectlyReturnedBarrier = monitor.DisposeAsync().AsTask();
                    incompleteWhenReturned = !incorrectlyReturnedBarrier.IsCompleted;
                }
                catch (Exception error) { observed = error; }
            });
            runner = new HealthCollectorRunner(service);
            registration = runner.Register(monitor, owner, new());
            await runner.StartAsync().WaitAsync(Deadline);

            HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

            Assert.IsNotNull(observed);
            Assert.AreEqual(typeof(InvalidOperationException), observed.GetType());
            Assert.IsNull(incorrectlyReturnedBarrier, "Own disposal must reject the self-containing wait synchronously after requesting withdrawal.");
            Assert.IsFalse(incompleteWhenReturned);
            Assert.AreEqual(0, service.CaptureSnapshot().MonitorCounts.Active);
            Assert.AreEqual(HealthCollectionDisposition.Retired, attempt.Disposition);
            Assert.AreEqual(1, owner.Acquisitions);
            Assert.AreEqual(1, owner.Evaluations);
            Assert.AreEqual(1, owner.Cleanups);
            Assert.IsTrue(monitor.WaitForDisposalAsync().IsCompletedSuccessfully);
            GC.KeepAlive(monitor);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            Task association = DrainRegistrationAsync();
            Task collector = DrainRunnerAsync();
            Task state = DrainServiceAsync();
            Task drains = Task.WhenAll(association, collector, state);
            try
            {
                await drains.WaitAsync(Deadline);
            }
            catch (Exception cleanup) when (primary is not null)
            { throw new AggregateException("The receiving body failed and actual cleanup did not complete successfully.", primary, drains.Exception ?? cleanup); }
            catch (Exception cleanup) when (cleanup is not TimeoutException && drains.Exception is not null)
            { throw drains.Exception; }
            finally { GC.KeepAlive(ownedMonitor); }

            async Task DrainRegistrationAsync() { if (registration is not null) { await registration.DisposeAsync(); } }
            async Task DrainRunnerAsync() { if (runner is not null) { await runner.DisposeAsync(); } }
            async Task DrainServiceAsync() => await service.DisposeAsync();
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task ConcurrentWithdrawalAndFactDeliveryReleaseTheExternalBinding(bool outcome)
    {
        RetirementObservation[] observed = WithdrawMonitorsAndReleaseActualOwners(outcome);
        foreach (RetirementObservation retirement in observed) { ReviewRetiredBinding(retirement); }
        return observed[0].First!;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RetirementObservation[] WithdrawMonitorsAndReleaseActualOwners(bool outcome)
    {
        HealthcheckService service = Service();
        var observed = new List<RetirementObservation>();
        Exception? primary = null;
        var holds = new List<Exception>();
        try
        {
            for (int round = 0; round < 16; round++)
            {
                IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = false, FailureThreshold = 1 });
                IHealthExecutionBinding binding = HealthExecutionBinder.Bind(monitor, new() { RegistrationId = Guid.NewGuid() });
                using var start = new ManualResetEventSlim();
                DateTimeOffset at = DateTimeOffset.UtcNow;
                var fact = new HealthExecutionFact
                {
                    RegistrationId = binding.RegistrationId,
                    ExecutionId = 1,
                    OccurredAtUtc = at,
                    Kind = outcome ? HealthExecutionFactKind.Outcome : HealthExecutionFactKind.CallbackStarted,
                    SourcePhase = outcome ? HealthExecutionPhase.Callback : null,
                    Outcome = outcome ? HealthExecutionOutcome.Succeeded : null,
                    WorkCompletedAtUtc = outcome ? at : null,
                    CallbackStartedAtUtc = outcome ? null : at
                };
                Task? completion = null, cleanupCompletion = null, withdrawal = null;
                Task<HealthFactDisposition>? report = null;
                bool participantsJoined = false;
                BindingPhase? phase = null;
                bool bodyReturned = false, sourceReleaseJoined = false, missingSignalPhase = false;
                try
                {
                    report = Task.Run(() => { start.Wait(); return binding.Report(fact); });
                    withdrawal = Task.Run(() => { start.Wait(); monitor.Withdraw(); });
                    start.Set();
                    Task participants = Task.WhenAll(report, withdrawal);
                    try { JoinRetirementOperation(participants); }
                    catch (Exception) when (participants.IsFaulted) { throw participants.Exception!; }
                    finally { participantsJoined = participants.IsCompleted; }

                    Assert.IsTrue(report.Result is HealthFactDisposition.Recorded or HealthFactDisposition.Retired);
                    completion = binding.DisposeAsync().AsTask();
                    phase = ReadActualRetirement(binding, monitor, service, owned: false, expectedWithdrawn: true);
                    JoinRetirementOperation(phase.StateRelease);
                    JoinRetirementOperation(phase.EntryDisposal);
                    JoinRetirementOperation(phase.MonitorDisposal);
                    sourceReleaseJoined = true;
                    if (completion.IsCompleted) { JoinRetirementOperation(completion); }
                    else
                    {
                        if (!phase.Retired || !phase.ReleaseStarted || phase.ActualSettlement is not null)
                        { throw new InvalidOperationException("The withdrawn binding has no proved missing-release phase; its actual observer or work may remain active."); }
                        missingSignalPhase = true;
                    }
                    HealthSnapshot after = service.CaptureSnapshot();
                    Assert.AreEqual(0, after.MonitorCounts.Active);
                    Assert.AreEqual(HealthFactDisposition.Retired, binding.Report(null!));
                    Assert.AreEqual(after.Revision, service.CaptureSnapshot().Revision);
                    bodyReturned = true;
                }
                catch (Exception error) { primary = error; }
                finally
                {
                    start.Set();
                    if (!participantsJoined)
                    {
                        Task[] actualParticipants = new Task?[] { report, withdrawal }.OfType<Task>().ToArray();
                        if (actualParticipants.Length > 0)
                        {
                            Task actual = Task.WhenAll(actualParticipants);
                            try { JoinRetirementOperation(actual); }
                            catch (Exception error) { holds.Add(actual.Exception ?? error); }
                        }
                    }
                    try
                    {
                        cleanupCompletion = binding.DisposeAsync().AsTask();
                        bool provedPrivatePromise = primary is null && bodyReturned && sourceReleaseJoined && missingSignalPhase
                            && ReferenceEquals(cleanupCompletion, completion) && phase is { Retired: true, ReleaseStarted: true, ActualSettlement: null };
                        if (cleanupCompletion.IsCompleted || !provedPrivatePromise) { JoinRetirementOperation(cleanupCompletion); }
                    }
                    catch (Exception error) { holds.Add(error); }
                    try { JoinRetirementOperation(monitor.DisposeAsync().AsTask()); }
                    catch (Exception error) { holds.Add(error); }
                    try { JoinRetirementOperation(monitor.WaitForDisposalAsync()); }
                    catch (Exception error) { holds.Add(error); }
                    GC.KeepAlive(monitor);
                }
                if (primary is not null || holds.Count > 0) { break; }
                observed.Add(new(null, completion, cleanupCompletion, missingSignalPhase, phase!.Graph));
            }
        }
        catch (Exception error) { primary = error; }
        finally
        {
            try { JoinRetirementOperation(service.DisposeAsync().AsTask()); }
            catch (Exception error) { holds.Add(error); }
            try { JoinRetirementOperation(service.WaitForDisposalAsync()); }
            catch (Exception error) { holds.Add(error); }
        }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            throw new AggregateException("Actual concurrent participant or monitor/state cleanup did not settle successfully.", holds);
        }
        if (primary is not null) { ExceptionDispatchInfo.Capture(primary).Throw(); }
        return observed.ToArray();
    }
    internal static void ReviewRetiredBinding(RetirementObservation observed)
    {
        observed.Failure?.Throw();
        if (observed.MissingSignalPhase)
        {
            var deadline = Stopwatch.StartNew();
            while (observed.Graph.Any(owner => owner.IsAlive) && deadline.Elapsed < Deadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(owner => owner.IsAlive)) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(owner => owner.IsAlive))
            {
                throw new InvalidOperationException("The actual retired binding/monitor/state graph remains owned; its pending promise cannot be treated as a missing signal.");
            }
        }
        Task first = observed.First!, second = observed.Second!;
        if (first.IsFaulted || first.IsCanceled) { first.GetAwaiter().GetResult(); }
        if (second.IsFaulted || second.IsCanceled) { second.GetAwaiter().GetResult(); }
        Assert.AreSame(first, second);
        Assert.IsTrue(first.IsCompletedSuccessfully, "Actual binding disposal must publish its shared successful completion after all real monitor and state owners settle.");
        Assert.IsTrue(second.IsCompletedSuccessfully, "The second actual disposal call must observe that same completed barrier.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RetirementObservation RetireBindingAndReleaseActualOwners(bool owned, bool availability)
    {
        HealthcheckService service = Service();
        IHealthMonitor? external = null;
        IHealthExecutionBinding? binding = null;
        Task? first = null, second = null;
        BindingPhase? phase = null;
        bool bodyReturned = false, sourceReleaseJoined = false, missingSignalPhase = false;
        Exception? primary = null;
        var holds = new List<Exception>();
        try
        {
            var options = new HealthExecutionBindingOptions { RegistrationId = Guid.NewGuid(), SuccessPublishesUsableData = true };
            external = owned ? null : availability
                ? service.CreateAvailability(new() { IsCritical = false })
                : service.CreateConsecutiveFailures(new() { IsCritical = false, FailureThreshold = 1 });
            binding = owned ? availability
                ? HealthExecutionBinder.Bind(service, new AvailabilityMonitorOptions { IsCritical = false }, options)
                : HealthExecutionBinder.Bind(service, new ConsecutiveFailureMonitorOptions { IsCritical = false, FailureThreshold = 1 }, options)
                : HealthExecutionBinder.Bind(external!, options);
            HealthSnapshot before = service.CaptureSnapshot();
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
            { RegistrationId = binding.RegistrationId, Kind = HealthExecutionFactKind.Retired, OccurredAtUtc = DateTimeOffset.UtcNow }));
            HealthSnapshot retired = service.CaptureSnapshot();
            Assert.IsTrue(retired.Revision > before.Revision);
            Assert.AreEqual(owned ? 0 : 1, retired.MonitorCounts.Active);

            first = binding.DisposeAsync().AsTask();
            second = binding.DisposeAsync().AsTask();
            Assert.AreSame(first, second);
            phase = ReadActualRetirement(binding, external, service, owned, expectedWithdrawn: owned);
            JoinRetirementOperation(phase.StateRelease);
            sourceReleaseJoined = true;
            if (first.IsCompleted) { JoinRetirementOperation(Task.WhenAll(first, second)); }
            else
            {
                if (!phase.Retired || !phase.ReleaseStarted || phase.ActualSettlement is not null)
                {
                    throw new InvalidOperationException("The actual pending binding barrier has no proved missing-release phase; actual state work or its observer may still be running.");
                }
                missingSignalPhase = true;
            }

            Assert.AreEqual(retired.Revision, service.CaptureSnapshot().Revision);
            Assert.AreEqual(HealthFactDisposition.Retired, binding.Report(null!));
            if (external is IAvailabilityMonitor published) { published.ReportPublished(); }
            if (external is IConsecutiveFailureMonitor failures) { failures.ReportSuccess(); }
            if (!owned)
            {
                HealthSnapshot usable = service.CaptureSnapshot();
                Assert.AreEqual(1, usable.MonitorCounts.Active);
                Assert.AreEqual(1, usable.Monitors.Count);
                var retained = usable.Monitors.Single();
                Assert.IsNotNull(retained.Outcomes);
                Assert.AreEqual(1L, retained.Outcomes.Successful);
            }
            bodyReturned = true;
        }
        catch (Exception error) { primary = error; }
        finally
        {
            if (binding is not null)
            {
                try
                {
                    Task actual = binding.DisposeAsync().AsTask();
                    bool provedPrivatePromise = primary is null && bodyReturned && sourceReleaseJoined && missingSignalPhase
                        && ReferenceEquals(actual, first) && phase is { Retired: true, ReleaseStarted: true, ActualSettlement: null };
                    if (actual.IsCompleted || !provedPrivatePromise) { JoinRetirementOperation(actual); }
                }
                catch (Exception error) { holds.Add(error); }
            }
            if (external is not null)
            {
                try { JoinRetirementOperation(external.DisposeAsync().AsTask()); }
                catch (Exception error) { holds.Add(error); }
                try { JoinRetirementOperation(external.WaitForDisposalAsync()); }
                catch (Exception error) { holds.Add(error); }
            }
            if (phase is not null)
            {
                try { JoinRetirementOperation(phase.MonitorDisposal); }
                catch (Exception error) { holds.Add(error); }
                try { JoinRetirementOperation(phase.EntryDisposal); }
                catch (Exception error) { holds.Add(error); }
            }
            try { JoinRetirementOperation(service.DisposeAsync().AsTask()); }
            catch (Exception error) { holds.Add(error); }
            try { JoinRetirementOperation(service.WaitForDisposalAsync()); }
            catch (Exception error) { holds.Add(error); }
            GC.KeepAlive(external);
        }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The retirement body or actual monitor/state cleanup did not settle successfully.", holds);
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), first, second,
            primary is null && bodyReturned && sourceReleaseJoined && missingSignalPhase,
            phase?.Graph ?? []);
    }

    internal static BindingPhase ReadActualRetirement(IHealthExecutionBinding binding,
        IHealthMonitor? external, HealthcheckService service, bool owned, bool expectedWithdrawn)
    {
        Type binderType = typeof(HealthExecutionBinder).GetNestedType("ExecutionBinding", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Actual binding source type unavailable.");
        if (binding.GetType() != binderType) { throw new InvalidOperationException("Actual binding implementation differs from the reviewed source."); }
        FieldInfo[] targets = binderType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Where(field => field.FieldType == typeof(BindingTarget)).ToArray();
        if (targets.Length != 1 || targets[0].GetValue(binding) is not BindingTarget target
            || target.Ownership != (owned ? HealthBindingOwnership.CreatedMonitor : HealthBindingOwnership.ExternalMonitor) || target.MonitorId != binding.MonitorId
            || target.RegistrationId != binding.RegistrationId)
        { throw new InvalidOperationException("Actual binding target is unavailable or inconsistent."); }
        object reporting = target.ReportingHandle;
        Type coordinatorType = typeof(HealthStateCoordinator);
        Type reportingType = coordinatorType.GetNestedType("ExecutionReportingHandle", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Actual reporting source type unavailable.");
        Type stateBindingType = coordinatorType.GetNestedType("ExecutionBinding", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Actual state-binding source type unavailable.");
        if (reporting.GetType() != reportingType) { throw new InvalidOperationException("Actual reporting target implementation differs."); }
        object coordinator = ReadRetirementProperty(reporting, "State", coordinatorType);
        object stateBinding = ReadRetirementProperty(reporting, "Binding", stateBindingType);
        var monitor = (IHealthMonitor)ReadRetirementProperty(reporting, "Monitor", typeof(IHealthMonitor));
        if (!ReferenceEquals(coordinator, ((IHealthStateAccess)service).ExecutionState)
            || !ReferenceEquals(coordinator, ((IHealthStateAccess)monitor).ExecutionState)
            || monitor.MonitorId != binding.MonitorId || (!owned && !ReferenceEquals(monitor, external)))
        { throw new InvalidOperationException("Actual reporting/state/monitor ownership differs."); }
        var entry = (HealthMonitorEntry)ReadRetirementProperty(stateBinding, "Entry", typeof(HealthMonitorEntry));
        object stateGate = ReadRetirementField(coordinator, "gate", typeof(object))!;
        Task stateRelease;
        lock (stateGate)
        {
            if (ReadRetirementField(stateBinding, "Retired", typeof(bool)) is not true
                || (HealthBindingOwnership)ReadRetirementProperty(stateBinding, "Ownership", typeof(HealthBindingOwnership)) != target.Ownership
                || entry.Id != monitor.MonitorId || entry.Collection is not null || entry.PendingOperations != 0 || entry.Withdrawn != expectedWithdrawn
                || ReadRetirementField(stateBinding, "ReleaseTask", typeof(Task)) is not Task actualRelease)
            { throw new InvalidOperationException("Actual state retirement or no-collection phase is unproved."); }
            stateRelease = actualRelease;
        }
        object bindingGate = ReadRetirementField(binding, "gate", typeof(object))!;
        bool retired, releaseStarted;
        Task? actualSettlement;
        lock (bindingGate)
        {
            retired = (bool)ReadRetirementField(binding, "retired", typeof(bool))!;
            releaseStarted = (bool)ReadRetirementField(binding, "releaseStarted", typeof(bool))!;
            actualSettlement = (Task?)ReadRetirementField(binding, "actualSettlement", typeof(Task));
        }
        return new(stateRelease, entry.Disposal.Task, monitor.WaitForDisposalAsync(), retired, releaseStarted, actualSettlement,
            [new(binding), new(target), new(reporting), new(stateBinding), new(coordinator), new(entry), new(monitor), new(service)]);
    }

    private static object? ReadRetirementField(object owner, string name, Type expected)
    {
        FieldInfo? field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field?.FieldType != expected) { throw new InvalidOperationException("Actual retirement field/type is unavailable: " + name); }
        return field.GetValue(owner);
    }

    private static object ReadRetirementProperty(object owner, string name, Type expected)
    {
        PropertyInfo? property = owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.PropertyType != expected || property.GetValue(owner) is not { } actual)
        { throw new InvalidOperationException("Actual retirement property/type is unavailable: " + name); }
        return actual;
    }

    internal static void JoinRetirementOperation(Task actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    internal sealed record BindingPhase(Task StateRelease, Task EntryDisposal, Task MonitorDisposal, bool Retired, bool ReleaseStarted,
        Task? ActualSettlement, WeakReference[] Graph);
    internal sealed record RetirementObservation(ExceptionDispatchInfo? Failure, Task? First, Task? Second,
        bool MissingSignalPhase, WeakReference[] Graph);

    private static HealthcheckService Service() => new(new(), new HealthApplicationIdentity { Name = "retirement boundaries", InstanceId = Guid.NewGuid() });

    private sealed class ObservingOwner(Action evaluate) : IHealthCollector
    {
        private int acquisitions, evaluations, cleanups;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Evaluations => Volatile.Read(ref evaluations);
        internal int Cleanups => Volatile.Read(ref cleanups);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        { Interlocked.Increment(ref acquisitions); lease = new Lease(this); return true; }
        private void Evaluate() { Interlocked.Increment(ref evaluations); evaluate(); }
        private sealed class Lease(ObservingOwner owner) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                owner.Evaluate();
                return Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
            }
            public ValueTask DisposeAsync()
            { Interlocked.Increment(ref owner.cleanups); return ValueTask.CompletedTask; }
        }
    }
}
