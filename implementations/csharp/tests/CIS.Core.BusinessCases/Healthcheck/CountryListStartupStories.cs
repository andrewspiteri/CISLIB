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
        CountryListStartupExpectations.Verify(report, failureMode, sourceReference: true);
    }
}
