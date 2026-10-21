using System.Text;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.BusinessCases.Healthcheck;

[TestClass]
public sealed class CountryListStartupStories
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("", "none")]
    [DataRow("--fail-prime", "priming")]
    [DataRow("--fail-refresh-registration", "registration")]
    [DataRow("--fail-later-startup", "later-startup")]
    public async Task RealApplicationKeepsReportingThroughRequiredStartupAndActualOwnerDrain(string option, string failureMode)
    {
        string host = Path.Combine(AppContext.BaseDirectory, "healthcheck-country-list", "CIS.Healthcheck.Examples.CountryList.dll");
        Assert.IsTrue(File.Exists(host), "Build and copy the actual application example before running its stories.");
        var environment = System.Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(key => key.StartsWith("CIS_HEALTHCHECK_", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(key => key, _ => (string?)null, StringComparer.OrdinalIgnoreCase);
        string[] arguments = ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "--self-test"];
        if (option.Length != 0) { arguments = [.. arguments, option]; }
        ProcessResult result = await ProcessRunner.RunAsync("dotnet", arguments, AppContext.BaseDirectory,
            TimeSpan.FromSeconds(45), environment, new UTF8Encoding(false, true));
        Assert.AreEqual(0, result.ExitCode, result.StandardError + result.StandardOutput);
        Assert.AreEqual("", result.StandardError);
        JsonObject report = JsonNode.Parse(result.StandardOutput)!.AsObject();
        TestContext.WriteLine(report.ToJsonString());
        Assert.AreEqual("countryListSelfTest", (string?)report["kind"]);
        Assert.AreEqual(".NET 10.0.12", (string?)report["runtime"]);
        Assert.AreEqual(failureMode, (string?)report["failureMode"]);
        Assert.AreEqual(true, (bool?)report["reportingBeforePriming"]);
        Assert.AreEqual(true, (bool?)report["sourceReferenceDevelopmentExample"]);
        bool success = failureMode == "none";
        Assert.AreEqual(failureMode != "priming", (bool?)report["primeSucceeded"]);
        Assert.AreEqual(success, (bool?)report["startupSucceeded"]);
        Assert.AreEqual(success, (bool?)report["refreshRegistered"]);

        JsonObject[] observations = report["observations"]!.AsArray().Select(node => node!.AsObject()).ToArray();
        string[] expectedStages = failureMode switch
        {
            "none" => ["before-prime", "priming", "after-prime", "startup-completed", "after-refresh-failures", "critical-flagged", "critical-cleared", "stopping"],
            "priming" => ["before-prime", "priming", "prime-failed", "stopping"],
            _ => ["before-prime", "priming", "after-prime", "startup-failed", "stopping"]
        };
        CollectionAssert.AreEqual(expectedStages, observations.Select(row => (string)row["stage"]!).ToArray());
        Dictionary<string, JsonObject> stages = observations.ToDictionary(row => (string)row["stage"]!, StringComparer.Ordinal);
        foreach (string stage in new[] { "before-prime", "priming" })
        {
            JsonObject row = stages[stage];
            Assert.AreEqual(false, (bool?)row["ready"], stage);
            Assert.AreEqual("Healthy", (string?)row["healthStatus"], stage);
            Assert.AreEqual("Pending", (string?)row["availabilityState"], stage);
            Assert.AreEqual(1, (int?)row["pending"], stage);
            Assert.AreEqual(0, (int?)row["countries"], stage);
            Assert.AreEqual(0, (int?)row["businessRequests"], stage);
        }
        if (failureMode != "priming")
        {
            JsonObject published = stages["after-prime"];
            Assert.AreEqual(3, (int?)published["countries"]);
            Assert.AreEqual("Healthy", (string?)published["availabilityState"]);
            Assert.AreEqual(false, (bool?)published["ready"], "Usable data alone must not complete other required startup steps.");
            Assert.AreEqual(0, (int?)published["businessRequests"]);
        }
        if (success)
        {
            foreach (string stage in new[] { "startup-completed", "after-refresh-failures", "critical-cleared" })
            {
                JsonObject row = stages[stage];
                Assert.AreEqual(true, (bool?)row["ready"], stage);
                Assert.AreEqual("Healthy", (string?)row["healthStatus"], stage);
                Assert.AreEqual("Healthy", (string?)row["availabilityState"], stage);
                Assert.AreEqual(3, (int?)row["countries"], stage);
                Assert.AreEqual(1, (int?)row["businessRequests"], "Reporting must bypass the business side effect.");
            }
            Assert.AreEqual("3", (string?)stages["after-refresh-failures"]["refreshFailed"]);
            Assert.AreEqual("Unhealthy", (string?)stages["after-refresh-failures"]["refreshState"]);
            Assert.AreEqual(false, (bool?)stages["critical-flagged"]["ready"]);
            Assert.AreEqual("Unhealthy", (string?)stages["critical-flagged"]["healthStatus"]);
            Assert.AreEqual(1, (int?)stages["critical-flagged"]["criticalFailing"]);
        }
        else
        {
            JsonObject failed = stages[failureMode == "priming" ? "prime-failed" : "startup-failed"];
            Assert.AreEqual("Unhealthy", (string?)failed["healthStatus"]);
            Assert.AreEqual(false, (bool?)failed["ready"]);
            Assert.AreEqual(1, (int?)failed["criticalFailing"]);
            Assert.AreEqual(0, (int?)failed["pending"]);
            Assert.AreEqual(failureMode == "priming" ? 0 : 3, (int?)failed["countries"]);
            Assert.AreEqual(0, (int?)failed["businessRequests"]);
            Assert.IsNull(failed["refreshState"]);
            if (failureMode == "priming")
            {
                Assert.AreEqual(1, (int?)failed["active"], "The application failure survives withdrawal of the failed child availability monitor.");
                Assert.IsNull(failed["availabilityState"]);
            }
            else { Assert.AreEqual("Healthy", (string?)failed["availabilityState"]); }
        }
        Assert.AreEqual(false, (bool?)stages["stopping"]["ready"]);
        JsonObject drain = report["ownerDrain"]!.AsObject();
        Assert.AreEqual(success, (bool?)drain["applicable"]);
        Assert.AreEqual(success, (bool?)drain["disposalPending"]);
        Assert.AreEqual(false, (bool?)drain["sourceDisposedBeforeRelease"]);
        Assert.AreEqual(success, (bool?)drain["sourceDisposedAfterRelease"]);
        Assert.AreEqual(success, (bool?)report["actualOwnerDrain"]);
        Assert.AreEqual(true, (bool?)report["passed"]);
    }
}
