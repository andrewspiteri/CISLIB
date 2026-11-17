using System.Text;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.BusinessCases.Startup;

[TestClass]
public sealed class StartupCountryListStories
{
    [TestMethod]
    public async Task RealConsumerPreparesThenPrimesThenReadsBeforeReadinessAndRecurringRefresh()
    {
        string example = Path.Combine(AppContext.BaseDirectory, "startup-country-list", "CIS.Startup.Examples.CountryList.dll");
        Assert.IsTrue(File.Exists(example), "Build and copy the actual Startup consumer example before running this story.");
        ProcessResult result = await ProcessRunner.RunAsync("dotnet",
            ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", example, "--self-test"],
            AppContext.BaseDirectory, TimeSpan.FromSeconds(45), null, new UTF8Encoding(false, true));
        Assert.IsTrue(result.OutputDrained, "The real child and its output must settle within the bounded process runner.");
        Assert.IsFalse(result.CaptureTruncated);
        Assert.AreEqual(0, result.ExitCode, result.StandardError + result.StandardOutput);
        Assert.AreEqual("", result.StandardError);
        JsonObject report = JsonNode.Parse(result.StandardOutput)!.AsObject();
        Assert.AreEqual("Startup.CountryList", report["example"]!.GetValue<string>());
        JsonObject before = report["before"]!.AsObject();
        Assert.AreEqual(500, before["readinessStatus"]!.GetValue<int>());
        Assert.AreEqual(503, before["businessStatus"]!.GetValue<int>());
        Assert.AreEqual(0, before["businessRequests"]!.GetValue<int>());
        Assert.IsTrue(before["startupPending"]!.GetValue<bool>());
        JsonObject after = report["after"]!.AsObject();
        Assert.AreEqual(200, after["readinessStatus"]!.GetValue<int>());
        Assert.AreEqual(200, after["businessStatus"]!.GetValue<int>());
        Assert.AreEqual(3, after["countryCount"]!.GetValue<int>());
        Assert.IsTrue(after["readerStarted"]!.GetValue<bool>());
        Assert.IsTrue(after["sameTypeManager"]!.GetValue<bool>());
        Assert.IsFalse(after["refreshCompleted"]!.GetValue<bool>(), "Readiness must not wait for the first refresh.");
        Assert.IsTrue(report["refresh"]!["completed"]!.GetValue<bool>());
        Assert.IsTrue(report["refresh"]!["count"]!.GetValue<int>() >= 1);
        JsonObject configuration = report["configuration"]!.AsObject();
        Assert.AreEqual(3, configuration["constructorLimit"]!.GetValue<int>(), "Stage 1 construction receives already-populated settings.");
        Assert.IsTrue(configuration["rootSharedAcrossScopes"]!.GetValue<bool>());
        Assert.IsTrue(configuration["rootRetainedAfterReload"]!.GetValue<bool>());
        Assert.AreEqual(3, configuration["initialLimit"]!.GetValue<int>());
        Assert.AreEqual(3, configuration["maskedLimit"]!.GetValue<int>());
        Assert.AreEqual(0, configuration["maskedEventCount"]!.GetValue<int>());
        Assert.AreEqual(1, configuration["reloadedLimit"]!.GetValue<int>(), "Removing the override exposes the latest common value.");
        Assert.AreEqual(1, configuration["reloadedEventCount"]!.GetValue<int>(), "The retained subscriber sees one actual effective change.");
        Assert.AreEqual(1, configuration["businessCountryCount"]!.GetValue<int>());
        Assert.AreEqual(1, configuration["watchSubscriptions"]!.GetValue<int>());
        Assert.IsTrue(configuration["watchDetached"]!.GetValue<bool>());
        Assert.IsTrue(report["stopped"]!.GetValue<bool>());
    }
}
