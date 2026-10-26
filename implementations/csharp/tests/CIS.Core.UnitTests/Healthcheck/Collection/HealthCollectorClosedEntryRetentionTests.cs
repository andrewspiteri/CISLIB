using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorClosedEntryRetentionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public void DisposingTheFirstIdleRegistrationReclaimsItsLiveRunnerEntry()
    {
        var failures = new List<Exception>();
        var facts = new InertFacts();
        var clock = new InertClock(facts);
        var owner = new InertOwner(facts);
        var monitors = new List<IConsecutiveFailureMonitor>();
        var registrations = new List<IHealthCollectionRegistration>();
        var publicTasks = new List<Task>();
        HealthcheckService? service = null;
        HealthCollectorRunner? runner = null;
        try
        {
            service = new(new(), new() { Name = "closed entry ownership", InstanceId = Guid.NewGuid() }, clock);
            runner = new(service);
            for (int index = 0; index < 3; index++)
            {
                IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = false, FailureThreshold = 1 });
                monitors.Add(monitor);
                registrations.Add(runner.Register(monitor, owner, new()));
            }

            RegistryObservation before = ReadActualRegistry(runner);
            Assert.AreEqual(3, before.Entries.Length);
            CollectionAssert.AreEqual(monitors.Select(m => m.MonitorId).ToArray(), before.MonitorIds);
            object firstEntry = before.Entries[0];

            // Both returned tasks are the actual public retirement observations.
            Task disposal = registrations[0].DisposeAsync().AsTask(); publicTasks.Add(disposal);
            Task settlement = registrations[0].WaitForDisposalAsync(); publicTasks.Add(settlement);
            Observe(Task.WhenAll(disposal, settlement));
            Task<HealthCollectionAttempt> retired = registrations[0].CollectAsync(); publicTasks.Add(retired);
            Assert.AreEqual(HealthCollectionDisposition.Retired, Observe(retired).Disposition);

            // The live service and other monitors still work; no Stop, Start,
            // collection callback, GC or replacement owner supplies this fence.
            foreach (IConsecutiveFailureMonitor monitor in monitors) { monitor.ReportSuccess(); }
            HealthSnapshot snapshot = service.CaptureSnapshot();
            Assert.IsFalse(snapshot.Application.IsStopping);
            Assert.AreEqual(3, snapshot.MonitorCounts.Active);
            Assert.IsTrue(snapshot.Monitors.All(m => m.Outcomes!.Successful == 1));

            RegistryObservation after = ReadActualRegistry(runner);
            Assert.IsFalse(after.Entries.Any(entry => ReferenceEquals(entry, firstEntry)),
                "A genuinely retired idle registration must leave its still-live runner's registry.");
            Assert.AreEqual(2, after.Entries.Length);
            CollectionAssert.AreEqual(monitors.Skip(1).Select(m => m.MonitorId).ToArray(), after.MonitorIds);
            Assert.AreEqual(monitors[1].MonitorId, registrations[1].MonitorId);
            Assert.AreEqual(monitors[2].MonitorId, registrations[2].MonitorId);
        }
        catch (Exception error) { Record(error); }
        finally
        {
            // Start every independent real owner drain even after a body/setup
            // error. Retain all original returned tasks and completed failures.
            var drains = new List<Task>(publicTasks);
            foreach (IHealthCollectionRegistration registration in registrations)
            {
                Start(() => registration.DisposeAsync().AsTask());
                Start(() => registration.WaitForDisposalAsync());
            }
            if (runner is not null) { Start(() => runner.StopAsync()); Start(() => runner.DisposeAsync().AsTask()); }
            foreach (IConsecutiveFailureMonitor monitor in monitors) { Start(() => monitor.DisposeAsync().AsTask()); }
            if (service is not null) { Start(() => service.DisposeAsync().AsTask()); }
            try { Observe(Task.WhenAll(drains)); } catch (Exception error) { Record(error); }
            foreach (Task task in drains.Where(t => t.IsCompleted))
            {
                try { task.GetAwaiter().GetResult(); }
                catch (Exception error) { Record(error); if (task.Exception is { } all) { Record(all); } }
            }
            if (facts.Acquisitions != 0 || facts.TimerCreations != 0)
            { Record(new InvalidOperationException("The inert owned-registration scenario unexpectedly started producer work.")); }
            GC.KeepAlive(registrations); GC.KeepAlive(monitors); GC.KeepAlive(owner); GC.KeepAlive(clock);
            GC.KeepAlive(runner); GC.KeepAlive(service);
            void Start(Func<Task> start) { try { drains.Add(start()); } catch (Exception error) { Record(error); } }
        }
        if (failures.Count == 1) { ExceptionDispatchInfo.Capture(failures[0]).Throw(); }
        if (failures.Count > 1) { throw new AggregateException("Owned registry observation and actual cleanup failures.", failures); }
        void Record(Exception error)
        {
            if (error is AggregateException aggregate) { foreach (Exception inner in aggregate.InnerExceptions) { Record(inner); } }
            else if (!failures.Any(prior => ReferenceEquals(prior, error))) { failures.Add(error); }
        }
    }

    // Observe only actual source-owned state under the actual source-owned lock.
    // Strict metadata failure is a runtime hold; no field or list is modified.
    private static RegistryObservation ReadActualRegistry(HealthCollectorRunner runner)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        Type type = typeof(HealthCollectorRunner);
        Type? entry = type.GetNestedType("RegistrationEntry", BindingFlags.NonPublic);
        FieldInfo? gateField = type.GetField("gate", fields), entriesField = type.GetField("entries", fields);
        FieldInfo? targetField = entry?.GetField("Target", fields);
        if (entry is null || gateField?.FieldType != typeof(object) || gateField.GetValue(runner) is not { } gate
            || entriesField?.FieldType != typeof(List<>).MakeGenericType(entry) || targetField?.FieldType != typeof(HealthCollectionTarget))
        { throw new InvalidOperationException("Actual runner registry metadata is unavailable."); }
        lock (gate)
        {
            if (entriesField.GetValue(runner) is not IList actual) { throw new InvalidOperationException("Actual runner registry is unavailable."); }
            object[] entries = actual.Cast<object>().ToArray();
            if (entries.Any(value => value.GetType() != entry)) { throw new InvalidOperationException("Actual registry entry type differs."); }
            Guid[] ids = entries.Select(value => targetField.GetValue(value) is HealthCollectionTarget target ? target.MonitorId
                : throw new InvalidOperationException("Actual registration target is unavailable.")).ToArray();
            return new(entries, ids);
        }
    }

    private static void Observe(Task task) => task.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static T Observe<T>(Task<T> task) => task.WaitAsync(Deadline).GetAwaiter().GetResult();
    private sealed record RegistryObservation(object[] Entries, Guid[] MonitorIds);
    private sealed class InertFacts { internal int Acquisitions, TimerCreations; }
    private sealed class InertOwner(InertFacts facts) : IHealthCollector
    {
        public bool TryAcquire(out IHealthCollectionLease? lease) { Interlocked.Increment(ref facts.Acquisitions); lease = null; return false; }
    }
    private sealed class InertClock(InertFacts facts) : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { Interlocked.Increment(ref facts.TimerCreations); throw new InvalidOperationException("Inert registration must not create a timer."); }
    }
}
