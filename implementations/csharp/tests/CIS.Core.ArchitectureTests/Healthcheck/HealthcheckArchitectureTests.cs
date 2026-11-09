using System.Reflection;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests.Healthcheck;

[TestClass]
public sealed class HealthcheckArchitectureTests
{
    private static readonly string[] PublicNames =
    [
        "HealthCollectionDisposition", "HealthCollectionAttempt", "HealthCollectionResultKind", "HealthCollectionResult",
        "HealthCollectorOptions", "IHealthCollector", "IHealthCollectionLease", "IHealthCollectionRegistration",
        "HealthExecutionBindingOptions", "HealthFactDisposition", "HealthExecutionFactKind", "HealthExecutionPhase",
        "HealthExecutionStepStatus", "HealthExecutionFact", "HealthExecutionSnapshot", "IHealthExecutionBinding",
        "HealthApplicationIdentity", "HealthDiagnostic", "HealthStatus", "MonitorState", "HealthPolicyKind", "NumericComparison",
        "HealthExecutionOutcome", "HealthMeasurementValueKind", "HealthReportView", "HealthMeasurement", "IHealthSnapshotSource",
        "IHealthMonitorFactory", "IHealthMonitor", "IConditionMonitor", "IConsecutiveFailureMonitor", "INumericThresholdMonitor",
        "IAvailabilityMonitor", "HealthReportBuffer", "HealthSnapshot", "HealthApplicationSnapshot", "HealthMonitorCounts",
        "HealthMonitorSnapshot", "HealthOutcomeSnapshot", "HealthNumericSnapshot", "HealthCollectionSnapshot",
        "HealthcheckCapacityException", "HealthcheckOptions", "HealthMonitorOptions", "ConditionMonitorOptions",
        "ConsecutiveFailureMonitorOptions", "NumericThresholdMonitorOptions", "AvailabilityMonitorOptions", "HealthExecutionBinder",
        "HealthcheckService", "HealthCollectorRunner", "HealthPipeServer", "HealthReportFormatter",
        "AspNetCore.HealthcheckApplicationBuilderExtensions"
    ];

    private static readonly string[] SnapshotNames =
    [
        "HealthSnapshot", "HealthApplicationSnapshot", "HealthMonitorCounts", "HealthMonitorSnapshot",
        "HealthOutcomeSnapshot", "HealthNumericSnapshot", "HealthCollectionSnapshot", "HealthExecutionSnapshot"
    ];


    [TestMethod]
    public void PublicHealthcheckSurfaceStaysInTheSingleCoreAssembly()
    {
        Assembly core = typeof(HealthcheckService).Assembly;
        Assert.AreEqual("CIS.Core", core.GetName().Name);
        string[] actual = core.GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("CIS.Healthcheck", StringComparison.Ordinal) == true)
            .Select(type => type.FullName!).ToArray();
        CollectionAssert.AreEquivalent(PublicNames.Select(name => "CIS.Healthcheck." + name).ToArray(), actual);
    }

    [TestMethod]
    public void SnapshotsExposeImmutableOwnedDataWithoutRetainingLiveMonitors()
    {
        Assembly core = typeof(HealthcheckService).Assembly;
        foreach (string name in SnapshotNames)
        {
            Type type = core.GetType("CIS.Healthcheck." + name, throwOnError: true)!;
            Assert.IsTrue(type.IsSealed, name);
            Assert.AreEqual(0, type.GetConstructors().Length, name);
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.IsNull(property.SetMethod, name + "." + property.Name);
            }
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                Assert.IsTrue(field.IsPrivate && field.IsInitOnly, name + "." + field.Name);
                Assert.IsFalse(BorrowsMutableOrLiveState(field.FieldType), name + "." + field.Name);
            }
        }
    }
    private static bool BorrowsMutableOrLiveState(Type type)
    {
        Type[] owners = [typeof(HealthcheckService), typeof(HealthCollectorRunner), typeof(HealthExecutionBinder),
            typeof(IHealthMonitorFactory), typeof(IHealthMonitor), typeof(IHealthCollector),
            typeof(IHealthCollectionLease), typeof(IHealthCollectionRegistration), typeof(IHealthExecutionBinding)];
        return type.IsArray || typeof(Exception).IsAssignableFrom(type) || typeof(Delegate).IsAssignableFrom(type)
            || owners.Any(owner => owner.IsAssignableFrom(type))
            || type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>)
                || type.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                || type.GetGenericArguments().Any(BorrowsMutableOrLiveState));
    }
}