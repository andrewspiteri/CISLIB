using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthcheckSnapshotReceivingBoundaryTests
{
    [TestMethod]
    [DataRow("data", "data")]
    [DataRow("application", "data.Application")]
    [DataRow("counts", "data.MonitorCounts")]
    [DataRow("monitors", "data.Monitors")]
    public void SnapshotConstructionValidatesItsNamedRequiredInputsBeforeCopying(string missing, string parameter)
    {
        HealthSnapshot accepted = new(Data());
        Assert.AreEqual(0, accepted.Monitors.Count);
        Assert.AreEqual(0, accepted.MonitorCounts.Active);
        Assert.IsNotNull(accepted.Application);

        var invalid = new HealthSnapshotData
        {
            Application = missing == "application" ? null! : Application(),
            MonitorCounts = missing == "counts" ? null! : new(0, 0, 0, 0, 0),
            Monitors = missing == "monitors" ? null! : []
        };
        ArgumentNullException error = Assert.ThrowsExactly<ArgumentNullException>(() => new HealthSnapshot(missing == "data" ? null! : invalid));
        Assert.AreEqual(parameter, error.ParamName);
    }

    [TestMethod]
    [DataRow(false, "data")]
    [DataRow(true, "data.Measurements")]
    public void MonitorSnapshotConstructionValidatesItsRequiredDataAndMeasurementSequence(bool missingMeasurements, string parameter)
    {
        var valid = new HealthMonitorSnapshot(new HealthMonitorSnapshotData { MonitorId = Guid.NewGuid() });
        Assert.AreEqual(0, valid.Measurements.Count);
        var invalid = new HealthMonitorSnapshotData { MonitorId = valid.MonitorId, Measurements = null! };

        ArgumentNullException error = Assert.ThrowsExactly<ArgumentNullException>(() => new HealthMonitorSnapshot(missingMeasurements ? invalid : null!));

        Assert.AreEqual(parameter, error.ParamName);
    }

    [TestMethod]
    [DataRow("application", "identity")]
    [DataRow("outcome", "data")]
    [DataRow("execution", "data")]
    public void DetachedSnapshotComponentsRejectMissingRequiredData(string component, string parameter)
    {
        ArgumentNullException error = component switch
        {
            "application" => Assert.ThrowsExactly<ArgumentNullException>(() => new HealthApplicationSnapshot(null!, false, false)),
            "outcome" => Assert.ThrowsExactly<ArgumentNullException>(() => new HealthOutcomeSnapshot(null!)),
            "execution" => Assert.ThrowsExactly<ArgumentNullException>(() => new HealthExecutionSnapshot(null!)),
            _ => throw new ArgumentOutOfRangeException(nameof(component))
        };
        Assert.AreEqual(parameter, error.ParamName);
    }

    [TestMethod]
    [DataRow("monitor-options", "options")]
    [DataRow("failures-policy", "policy")]
    [DataRow("availability-policy", "policy")]
    public void DetachedBindingRequestsRejectMissingInputsAtTheirOwnBoundary(string missing, string parameter)
    {
        var options = new HealthExecutionBindingOptions { RegistrationId = Guid.NewGuid() };
        ArgumentNullException error = missing switch
        {
            "monitor-options" => Assert.ThrowsExactly<ArgumentNullException>(() => BindingRequest.ForMonitor(Guid.NewGuid(), null!)),
            "failures-policy" => Assert.ThrowsExactly<ArgumentNullException>(() => BindingRequest.ForConsecutiveFailures(null!, options)),
            "availability-policy" => Assert.ThrowsExactly<ArgumentNullException>(() => BindingRequest.ForAvailability(null!, options)),
            _ => throw new ArgumentOutOfRangeException(nameof(missing))
        };
        Assert.AreEqual(parameter, error.ParamName);
    }

    [TestMethod]
    public void ABindingTargetCannotBeConstructedWithoutItsStateIssuedReportingHandle()
    {
        ArgumentNullException error = Assert.ThrowsExactly<ArgumentNullException>(() => new BindingTarget(Guid.NewGuid(), Guid.NewGuid(), HealthBindingOwnership.ExternalMonitor, null!));
        Assert.AreEqual("reportingHandle", error.ParamName);
    }

    [TestMethod]
    public void AnUnavailableRuntimeExceptionMessageRemainsAnObservableDetachedDiagnostic()
    {
        HealthDiagnostic diagnostic = HealthDiagnostic.FromException(new UnavailableMessageException(), "Unavailable");
        Assert.AreEqual("Unavailable", diagnostic.Code);
        Assert.IsNull(diagnostic.Message);
        Assert.AreEqual(typeof(UnavailableMessageException).FullName, diagnostic.ExceptionType);
        Assert.IsTrue(diagnostic.IsTruncated);
    }

    private static HealthApplicationSnapshot Application() => new(new HealthApplicationIdentity { Name = "snapshot input", InstanceId = Guid.NewGuid() }, false, false);
    private static HealthSnapshotData Data() => new() { Application = Application(), MonitorCounts = new(0, 0, 0, 0, 0) };
    private sealed class UnavailableMessageException : Exception
    {
        public override string Message => throw new InvalidOperationException("This runtime accessor is unavailable.");
    }
}
