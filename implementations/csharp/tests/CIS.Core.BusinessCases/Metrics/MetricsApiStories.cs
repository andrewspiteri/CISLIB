using System.Text;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.BusinessCases.Metrics;

[TestClass]
public sealed class MetricsApiStories
{
    [TestMethod]
    public async Task RealMetricsApiRemainsObservableDuringRequiredStartupThenMeasuresOneActualOrder()
    {
        string example = Path.Combine(AppContext.BaseDirectory, "metrics-api", "CIS.Metrics.Examples.MetricApi.dll");
        Assert.IsTrue(File.Exists(example), "Build and copy the actual Metrics example before this story.");
        var environment = Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(key => key.StartsWith("CORE_", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(key => key, _ => (string?)null, StringComparer.OrdinalIgnoreCase);
        environment["CORE_SERVICE_NAME"] = "Metrics.Story";
        environment["CORE_ENVIRONMENT"] = "acceptance";
        ProcessResult result = await ProcessRunner.RunAsync("dotnet",
            ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", example, "--self-test"],
            AppContext.BaseDirectory, TimeSpan.FromSeconds(45), environment, new UTF8Encoding(false, true));
        Assert.IsTrue(result.OutputDrained);
        Assert.IsFalse(result.CaptureTruncated);
        Assert.AreEqual(0, result.ExitCode, result.StandardError + result.StandardOutput);
        Assert.AreEqual("", result.StandardError);
        JsonObject report = JsonNode.Parse(result.StandardOutput)!.AsObject();
        Assert.AreEqual("Metrics.MetricApi", report["example"]!.GetValue<string>());
        JsonObject before = report["before"]!.AsObject();
        Assert.AreEqual(200, before["metricsStatus"]!.GetValue<int>());
        Assert.AreEqual(500, before["readinessStatus"]!.GetValue<int>());
        Assert.AreEqual(503, before["businessStatus"]!.GetValue<int>());
        Assert.AreEqual(0, before["businessCalls"]!.GetValue<int>());
        Assert.IsTrue(before["startupPending"]!.GetValue<bool>());
        JsonObject after = report["after"]!.AsObject();
        Assert.AreEqual(200, after["metricsStatus"]!.GetValue<int>());
        Assert.AreEqual(200, after["readinessStatus"]!.GetValue<int>());
        Assert.AreEqual(200, after["businessStatus"]!.GetValue<int>());
        Assert.AreEqual(1, after["businessCalls"]!.GetValue<int>());
        Assert.IsTrue(after["readerStarted"]!.GetValue<bool>());
        Assert.IsTrue(after["sameTypeManager"]!.GetValue<bool>());
        JsonObject metrics = report["metrics"]!.AsObject();
        Assert.AreEqual("Metrics.Story", metrics["application"]!.GetValue<string>());
        Assert.AreEqual("acceptance", metrics["environment"]!.GetValue<string>());
        Assert.AreEqual(1d, metrics["completed"]!.GetValue<double>());
        Assert.AreEqual(1d, metrics["durationCount"]!.GetValue<double>());
        Assert.IsTrue(metrics["allSamplesHaveIdentity"]!.GetValue<bool>());
        CollectionAssert.AreEquivalent(new[] { "app_orders_completed_total", "app_orders_duration_seconds_bucket",
            "app_orders_duration_seconds_sum", "app_orders_duration_seconds_count",
            "http_server_request_duration_seconds_count", "cis_startup_operation_duration_seconds_count" },
            metrics["sampleNames"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray());
        Assert.AreEqual(".NET 10.0.12", report["runtime"]!["framework"]!.GetValue<string>());
        Assert.AreEqual("11.0.0.0", report["runtime"]!["diagnosticSourceVersion"]!.GetValue<string>());
        Assert.IsTrue(report["stopped"]!.GetValue<bool>());
    }
}
