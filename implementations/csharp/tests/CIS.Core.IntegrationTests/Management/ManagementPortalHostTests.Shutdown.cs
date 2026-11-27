using System.Text.Json;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementPortalHostTests
{
    [TestMethod]
    public async Task ActualHostStopsWithAnUnadmittedIncompleteTlsPeer()
    {
        await RunFixtureAsync(browser: false, hostShutdown: true);
    }

    [TestMethod]
    public async Task ActualHostJoinsACanceledAuthenticatedReadBeforeDisposingItsSessionStore()
    {
        await RunFixtureAsync(browser: false, canceledHostRead: true);
    }

    private static void VerifyCanceledHostReadReport(JsonElement report)
    {
        Assert.IsTrue(report.GetProperty("nativeOidc").GetBoolean());
        Assert.IsFalse(report.GetProperty("browserChecked").GetBoolean());
        Assert.IsTrue(report.GetProperty("sessionsRealPostgres").GetBoolean());
        foreach (string fact in new[] { "hostStopped", "hostExited", "hostStdoutClosed", "hostStderrClosed", "hostDisconnected", "hostNativeJoined", "socketsDrained" })
        {
            Assert.IsTrue(report.GetProperty(fact).GetBoolean(), "Actual native custody did not settle: " + fact);
        }
        JsonElement observations = report.GetProperty("observations");
        Assert.AreEqual(0, observations.GetProperty("hostExitCode").GetInt32());
        Assert.IsTrue(observations.GetProperty("canceledReadHeldInSessionSql").GetBoolean());
        Assert.IsTrue(observations.GetProperty("canceledReadRetainedDuringStop").GetBoolean());
        Assert.AreEqual("complete", observations.GetProperty("hostShutdownPhase").GetString());
        Assert.AreEqual(0, observations.GetProperty("hostShutdownHandlers").GetInt32());
        Assert.AreEqual(0, observations.GetProperty("hostShutdownRequests").GetInt32());
        CollectionAssert.AreEqual(new[] { "real-authenticated-read-canceled-during-session-sql", "awaited-host-owner-stop" },
            report.GetProperty("cases").EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.AreEqual(0, report.GetProperty("metadataAppliedKeys").GetArrayLength());
        Assert.AreEqual(0, report.GetProperty("metadataConflictKeys").GetArrayLength());
    }

    private static void VerifyIncompleteTlsHostShutdownReport(JsonElement report)
    {
        Assert.IsTrue(report.GetProperty("hostShutdown").GetBoolean());
        Assert.IsFalse(report.GetProperty("nativeOidc").GetBoolean(), "This startup/stop case does not qualify an authentication journey.");
        Assert.IsFalse(report.GetProperty("browserChecked").GetBoolean());
        Assert.IsTrue(report.GetProperty("sessionsRealPostgres").GetBoolean());
        foreach (string fact in new[] { "hostStopped", "hostExited", "hostStdoutClosed", "hostStderrClosed", "hostDisconnected", "hostNativeJoined", "socketsDrained" })
        {
            Assert.IsTrue(report.GetProperty(fact).GetBoolean(), "Actual native custody did not settle: " + fact);
        }
        JsonElement observations = report.GetProperty("observations");
        Assert.AreEqual(0, observations.GetProperty("hostExitCode").GetInt32());
        Assert.IsTrue(observations.GetProperty("hostClosedIncompleteTlsPeer").GetBoolean());
        Assert.AreEqual("complete", observations.GetProperty("hostShutdownPhase").GetString());
        Assert.AreEqual(0, observations.GetProperty("hostShutdownHandlers").GetInt32());
        CollectionAssert.AreEqual(new[]
        {
            "ordinary-public-health-startup",
            "native-host-stop-closes-owned-incomplete-tls-peer",
            "awaited-host-owner-stop"
        }, report.GetProperty("cases").EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.AreEqual(0, report.GetProperty("metadataAppliedKeys").GetArrayLength());
        Assert.AreEqual(0, report.GetProperty("metadataConflictKeys").GetArrayLength());
        foreach (string workflow in new[] { "configuration", "rollout", "r08", "configurationRepairs", "versionWorkflow", "instanceStatus", "firstVersion", "manualSession" })
        {
            Assert.IsFalse(report.TryGetProperty(workflow, out _), "The focused shutdown case must not run " + workflow + ".");
        }
    }
}
