using System.Reflection;
using System.Runtime.CompilerServices;
using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthcheckBoundaryQualificationTests
{
    private static readonly Guid Instance = Guid.Parse("28637b8a-14a9-46e4-92bf-ceff0628ef31");

    [TestMethod]
    [DataRow("Name", 255, 0)]
    [DataRow("Name", 255, 1)]
    [DataRow("Name", 255, 2)]
    [DataRow("BuildVersion", 512, 0)]
    [DataRow("BuildVersion", 512, 1)]
    [DataRow("BuildVersion", 512, 2)]
    [DataRow("ServiceVersion", 512, 0)]
    [DataRow("ServiceVersion", 512, 1)]
    [DataRow("ServiceVersion", 512, 2)]
    [DataRow("HostName", 255, 0)]
    [DataRow("HostName", 255, 1)]
    [DataRow("HostName", 255, 2)]
    [DataRow("OsDescription", 1024, 0)]
    [DataRow("OsDescription", 1024, 1)]
    [DataRow("OsDescription", 1024, 2)]
    public void ExplicitIdentityRejectsControlMalformedAndOversizeScalarText(string field, int maximum, int fault)
    {
        string text = fault switch
        {
            0 => "valid\u0001text",
            1 => "valid" + '\ud800',
            _ => Scalars(maximum + 1)
        };
        HealthApplicationIdentity identity = Identity(field, text);
        Assert.Throws<ArgumentException>(() => new HealthcheckService(new HealthcheckOptions(), identity));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void RequiredApplicationNameRejectsNullEmptyAndWhitespace(int fault)
    {
        string? name = fault switch { 0 => null, 1 => "", _ => " \t\r\n" };
        Assert.Throws<ArgumentException>(() => new HealthcheckService(new HealthcheckOptions(), Identity("Name", name)));
    }

    [TestMethod]
    public async Task ValidIdentityCountsUnicodeScalarsAtEveryPublishedLimit()
    {
        var identity = new HealthApplicationIdentity
        {
            Name = Scalars(255),
            BuildVersion = Scalars(512),
            ServiceVersion = Scalars(512),
            HostName = Scalars(255),
            OsDescription = Scalars(1024),
            InstanceId = Instance
        };
        await using var service = new HealthcheckService(new HealthcheckOptions(), identity);
        HealthApplicationSnapshot actual = service.CaptureSnapshot().Application;
        Assert.AreEqual(identity.Name, actual.Name);
        Assert.AreEqual(identity.BuildVersion, actual.BuildVersion);
        Assert.AreEqual(identity.ServiceVersion, actual.ServiceVersion);
        Assert.AreEqual(identity.HostName, actual.HostName);
        Assert.AreEqual(identity.OsDescription, actual.OsDescription);
        Assert.AreEqual(Instance, actual.InstanceId);
    }

    [TestMethod]
    public async Task OptionalIdentityFieldsRemainUnavailableWhenExplicitlyOmitted()
    {
        await using var service = Service();
        HealthApplicationSnapshot actual = service.CaptureSnapshot().Application;
        Assert.AreEqual("boundary", actual.Name);
        Assert.IsNull(actual.BuildVersion);
        Assert.IsNull(actual.ServiceVersion);
        Assert.IsNull(actual.HostName);
        Assert.IsNull(actual.OsDescription);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task NumericUnitsPreserveTheNonblankBoundedScalarContract(int example)
    {
        // configuration.json requires nonblank <=32 scalar units; unlike
        // application identity it does not prohibit controls in mixed text.
        string unit = example switch { 0 => "ms", 1 => Scalars(32), _ => "m\u0000s" };
        await using var service = Service();
        await using INumericThresholdMonitor monitor = service.CreateNumericThreshold(Numeric(unit));
        Assert.AreEqual(unit, service.CaptureSnapshot().Monitors.Single().Numeric!.Unit);
        monitor.ReportValue(1);
        HealthMonitorSnapshot actual = service.CaptureSnapshot().Monitors.Single();
        Assert.AreEqual(unit, actual.Numeric!.Unit);
        Assert.AreEqual(1d, actual.Numeric.LastValue);
        Assert.AreEqual(MonitorState.Healthy, actual.State);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public async Task InvalidNumericUnitsRejectAtomicallyWithoutOccupyingCapacity(int fault)
    {
        string? unit = fault switch { 0 => "", 1 => " \t\n", 2 => "unit" + '\udfff', 3 => Scalars(33), _ => null };
        await using var service = Service(capacity: 1);
        HealthSnapshot before = service.CaptureSnapshot();
        Assert.Throws<ArgumentException>(() => service.CreateNumericThreshold(Numeric(unit!)));
        HealthSnapshot rejected = service.CaptureSnapshot();
        Assert.AreEqual(before.Revision, rejected.Revision);
        Assert.AreEqual(0, rejected.MonitorCounts.Active);
        await using IConditionMonitor accepted = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false });
        Assert.AreEqual(accepted.MonitorId, service.CaptureSnapshot().Monitors.Single().MonitorId);
    }

    [TestMethod]
    public async Task HealthyNumericObservationClearsBothOldAndSuppliedFailureReasons()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
        await using var service = Service(clock: clock);
        await using INumericThresholdMonitor monitor = service.CreateNumericThreshold(Numeric("ms"));
        var previous = new HealthDiagnostic("SlowDependency", "The previous sample breached the threshold.");
        monitor.ReportValue(20, previous);
        HealthMonitorSnapshot failed = service.CaptureSnapshot().Monitors.Single();
        Assert.AreEqual(MonitorState.Unhealthy, failed.State);
        Assert.AreSame(previous, failed.Diagnostic);
        clock.Advance(TimeSpan.FromSeconds(1));
        monitor.ReportValue(2, new HealthDiagnostic("UnusedHealthyReason", "A clear sample does not retain a failure reason."));
        HealthSnapshot recovered = service.CaptureSnapshot();
        HealthMonitorSnapshot actual = recovered.Monitors.Single();
        Assert.AreEqual(MonitorState.Healthy, actual.State);
        Assert.AreEqual(HealthStatus.Healthy, recovered.HealthStatus);
        Assert.IsNull(actual.Diagnostic);
        Assert.AreEqual(2d, actual.Numeric!.LastValue);
        Assert.AreEqual(clock.GetUtcNow(), actual.LastObservationAtUtc);
        Assert.AreSame(previous, failed.Diagnostic, "The old detached snapshot remains unchanged.");
    }

    [TestMethod]
    [DataRow("Successful")]
    [DataRow("Failed")]
    [DataRow("Skipped")]
    [DataRow("ExpectedCanceled")]
    [DataRow("ConsecutiveFailures")]
    public async Task RealReportingMarksTheExactSaturationTransitionAndNeverWraps(string counter)
    {
        await using var service = Service();
        await using IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 3 });
        // Technical exhaustion seed, consistent with existing counter-boundary
        // fixtures. Both observed transitions execute the public reporting API.
        HealthMonitorEntry entry = Entry(monitor);
        switch (counter)
        {
            case "Successful": entry.Successful = long.MaxValue - 1; break;
            case "Failed": entry.Failed = long.MaxValue - 1; break;
            case "Skipped": entry.Skipped = long.MaxValue - 1; break;
            case "ExpectedCanceled": entry.ExpectedCanceled = long.MaxValue - 1; break;
            case "ConsecutiveFailures": entry.ConsecutiveFailures = long.MaxValue - 1; entry.Failed = long.MaxValue - 1; break;
            default: Assert.Fail("Unknown counter case."); break;
        }
        Report(monitor, counter);
        HealthOutcomeSnapshot first = service.CaptureSnapshot().Monitors.Single().Outcomes!;
        Assert.AreEqual(long.MaxValue, Count(first, counter));
        Assert.IsTrue(first.CountersSaturated, "The exact transition to the cap must publish saturation immediately.");
        Report(monitor, counter);
        HealthOutcomeSnapshot repeated = service.CaptureSnapshot().Monitors.Single().Outcomes!;
        Assert.AreEqual(long.MaxValue, Count(repeated, counter));
        Assert.IsTrue(repeated.CountersSaturated);
        Assert.AreEqual(long.MaxValue, Count(first, counter), "The captured boundary value remains detached.");
    }

    [TestMethod]
    public async Task CreationAloneReclaimsACollectedHandleAndSettlesItsRetainedDisposalTask()
    {
        await using var service = Service(capacity: 1);
        (WeakReference abandoned, Task disposed, Guid oldId) = AbandonMonitor(service);
        for (int attempt = 0; attempt < 10 && abandoned.IsAlive; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        Assert.IsFalse(abandoned.IsAlive, "The test must establish actual collection before checking capacity reclamation.");
        Assert.IsFalse(disposed.IsCompleted);
        // No snapshot or other service sweep occurs between abandonment and
        // this actual factory call; admission itself must reclaim the slot.
        await using IConditionMonitor replacement = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false });
        Assert.AreNotEqual(oldId, replacement.MonitorId);
        Assert.IsTrue(disposed.IsCompletedSuccessfully, "The create-path sweep must dispatch the abandoned lifetime's completion.");
        HealthSnapshot observed = service.CaptureSnapshot();
        Assert.AreEqual(1, observed.MonitorCounts.Active);
        Assert.AreEqual(replacement.MonitorId, observed.Monitors.Single().MonitorId);
        Assert.AreEqual(HealthStatus.Healthy, observed.HealthStatus);
    }

    private static string Scalars(int count) => string.Concat(Enumerable.Repeat("\U0001f642", count));
    private static HealthApplicationIdentity Identity(string field, string? value) => new()
    {
        Name = field == "Name" ? value! : "boundary",
        InstanceId = Instance,
        BuildVersion = field == "BuildVersion" ? value : null,
        ServiceVersion = field == "ServiceVersion" ? value : null,
        HostName = field == "HostName" ? value : null,
        OsDescription = field == "OsDescription" ? value : null
    };
    private static HealthcheckService Service(int capacity = 1024, TimeProvider? clock = null) => new(new HealthcheckOptions { MaxMonitors = capacity },
        new HealthApplicationIdentity { Name = "boundary", InstanceId = Instance }, clock);
    private static NumericThresholdMonitorOptions Numeric(string unit) => new() { IsCritical = true, Comparison = NumericComparison.GreaterThan, Threshold = 10, Unit = unit };
    private static HealthMonitorEntry Entry(IHealthMonitor monitor)
    {
        IHealthCollectionState state = ((IHealthStateAccess)monitor).CollectionState;
        FieldInfo field = typeof(HealthStateCoordinator).GetField("active", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((Dictionary<Guid, HealthMonitorEntry>)field.GetValue(state)!)[monitor.MonitorId];
    }
    private static void Report(IConsecutiveFailureMonitor monitor, string counter)
    {
        switch (counter)
        {
            case "Successful": monitor.ReportSuccess(); break;
            case "Skipped": monitor.ReportSkipped(); break;
            case "ExpectedCanceled": monitor.ReportExpectedCancellation(); break;
            default: monitor.ReportFailure(); break;
        }
    }
    private static long Count(HealthOutcomeSnapshot snapshot, string counter) => counter switch
    {
        "Successful" => snapshot.Successful,
        "Failed" => snapshot.Failed,
        "Skipped" => snapshot.Skipped,
        "ExpectedCanceled" => snapshot.ExpectedCanceled,
        _ => snapshot.ConsecutiveFailures
    };
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Handle, Task Disposed, Guid Id) AbandonMonitor(HealthcheckService service)
    {
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = true });
        return (new WeakReference(monitor), monitor.WaitForDisposalAsync(), monitor.MonitorId);
    }
}
