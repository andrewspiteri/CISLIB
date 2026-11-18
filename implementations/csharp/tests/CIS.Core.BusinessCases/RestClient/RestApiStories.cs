using System.Text;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.BusinessCases.RestClient;

[TestClass]
public sealed class RestApiStories
{
    [TestMethod]
    public async Task RealRestApiPreservesTypedOutcomesExplicitReplayCancellationAndHostMetrics()
    {
        string example = Path.Combine(AppContext.BaseDirectory, "rest-client-api", "CIS.RestClient.Examples.RestApi.dll");
        Assert.IsTrue(File.Exists(example), "Build and copy the actual REST example before this story.");
        var environment = Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(key => key.StartsWith("CORE_", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(key => key, _ => (string?)null, StringComparer.OrdinalIgnoreCase);
        environment["CORE_SERVICE_NAME"] = "RestClient.Story";
        environment["CORE_ENVIRONMENT"] = "acceptance";
        ProcessResult process = await ProcessRunner.RunAsync("dotnet",
            ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", example, "--self-test"],
            AppContext.BaseDirectory, TimeSpan.FromSeconds(45), environment, new UTF8Encoding(false, true),
            maximumCapturedBytesPerStream: 262_144);
        Assert.IsTrue(process.OutputDrained); Assert.IsFalse(process.CaptureTruncated);
        Assert.AreEqual(0, process.ExitCode, process.StandardOutput + process.StandardError);
        Assert.AreEqual("", process.StandardError);
        JsonObject report = JsonNode.Parse(process.StandardOutput)!.AsObject();
        Assert.AreEqual("RestClient.RestApi", report["example"]!.GetValue<string>());
        Assert.IsTrue(report["postSuccess"]!.GetValue<bool>());
        Assert.AreEqual(3, report["retryAttempts"]!.GetValue<int>());
        Assert.AreEqual("orders.declined", report["businessCode"]!.GetValue<string>());
        Assert.IsFalse(report["emptyHasValue"]!.GetValue<bool>());
        Assert.IsTrue(report["cancellationPreserved"]!.GetValue<bool>());
        Assert.AreEqual(5d, report["logicalCompletions"]!.GetValue<double>());
        Assert.AreEqual(0d, report["active"]!.GetValue<double>());
        Assert.IsTrue(report["nativeCount"]!.GetValue<double>() >= 6d);
        Assert.IsTrue(report["allSamplesHaveIdentity"]!.GetValue<bool>());
        Assert.AreEqual(".NET 10.0.12", report["runtime"]!["framework"]!.GetValue<string>());
        Assert.AreEqual("11.0.0.0", report["runtime"]!["diagnosticSourceVersion"]!.GetValue<string>());
        Assert.IsTrue(report["stopped"]!.GetValue<bool>());
    }
}
