using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthcheckResidualReceivingBoundaryTests
{
    [TestMethod]
    [DataRow(int.MinValue)]
    [DataRow(-1)]
    [DataRow(0)]
    public void AConstructedCapacityErrorRejectsANonpositiveConfiguredLimit(int limit)
    {
        ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new HealthcheckCapacityException(limit));

        Assert.AreEqual("limit", error.ParamName);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(int.MaxValue)]
    public void AConstructedCapacityErrorRetainsItsPositiveLimitAndStableCode(int limit)
    {
        var error = new HealthcheckCapacityException(limit);

        Assert.AreEqual(limit, error.Limit);
        Assert.AreEqual("Healthcheck.MonitorCapacity", error.Code);
    }

    [TestMethod]
    public void AnExecutionFlowCannotBeMarkedWithoutItsCollectionState()
    {
        IDisposable? unexpectedScope = null;
        try
        {
            ArgumentNullException error = Assert.ThrowsExactly<ArgumentNullException>(() =>
                unexpectedScope = HealthCollectionExecution.Enter(null!,
                    new Guid("44cb2634-dbc9-46d8-9a3c-f6a0c825304a"),
                    new Guid("4a1a199b-707b-4442-99f1-f76542269d0f"),
                    new Guid("d97c739a-24a0-4d32-974e-38f541c3e626")));

            Assert.AreEqual("state", error.ParamName);
        }
        finally
        {
            // A missing input guard must not leave its erroneously returned flow
            // installed in this test's execution context. No owner work is created.
            unexpectedScope?.Dispose();
        }
    }
}
