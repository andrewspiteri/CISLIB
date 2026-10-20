using CIS.Configuration;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthcheckOptionsTests
{
    [TestMethod]
    public void DefaultsRequireNoGlobalInitializationOrTransportActivation()
    {
        HealthcheckOptions options = HealthcheckOptions.Resolve(null, _ => null);
        Assert.AreEqual(4, options.MaxConcurrentCollections);
        Assert.AreEqual(1024, options.MaxMonitors);
        Assert.IsFalse(options.DetailsEnabled);
        Assert.IsFalse(options.PipeEnabled);
        Assert.IsNull(options.PipeName);
        Assert.AreEqual(8, options.PipeMaxClients);
        Assert.AreEqual(1000, options.PipeRequestTimeoutMs);
        Assert.AreEqual(4194304, options.MaxReportBytes);
    }

    [TestMethod]
    public void EnvironmentOverridesSetupAndReadsEachKnownKeyExactlyOnce()
    {
        var reads = new Dictionary<string, int>(StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CIS_HEALTHCHECK_MAX_CONCURRENT_COLLECTIONS"] = " 6 ",
            ["CIS_HEALTHCHECK_MAX_MONITORS"] = "2048",
            ["CIS_HEALTHCHECK_DETAILS_ENABLED"] = " TrUe ",
            ["CIS_HEALTHCHECK_PIPE_ENABLED"] = "true",
            ["CIS_HEALTHCHECK_PIPE_NAME"] = " local-health ",
            ["CIS_HEALTHCHECK_PIPE_MAX_CLIENTS"] = "9",
            ["CIS_HEALTHCHECK_PIPE_TIMEOUT_MS"] = "2000",
            ["CIS_HEALTHCHECK_MAX_REPORT_BYTES"] = "8192"
        };
        var setup = new HealthcheckOptions { MaxConcurrentCollections = 2, PipeName = "setup", DetailsEnabled = false };
        HealthcheckOptions selected = HealthcheckOptions.Resolve(setup, key =>
        {
            reads[key] = reads.GetValueOrDefault(key) + 1;
            return values.GetValueOrDefault(key);
        });
        Assert.AreEqual(6, selected.MaxConcurrentCollections);
        Assert.AreEqual(2048, selected.MaxMonitors);
        Assert.IsTrue(selected.DetailsEnabled);
        Assert.IsTrue(selected.PipeEnabled);
        Assert.AreEqual("local-health", selected.PipeName);
        Assert.AreEqual(9, selected.PipeMaxClients);
        Assert.AreEqual(2000, selected.PipeRequestTimeoutMs);
        Assert.AreEqual(8192, selected.MaxReportBytes);
        CollectionAssert.AreEquivalent(values.Keys.ToArray(), reads.Keys.ToArray());
        Assert.IsTrue(reads.Values.All(count => count == 1));
        Assert.AreEqual(2, setup.MaxConcurrentCollections);
        Assert.AreNotSame(setup, selected);
    }

    [TestMethod]
    public void BlankEnvironmentValuesPreserveSetupAndResolutionDoesNotCache()
    {
        var setup = new HealthcheckOptions { MaxMonitors = 57, PipeName = "setup" };
        HealthcheckOptions first = HealthcheckOptions.Resolve(setup, _ => " \t ");
        HealthcheckOptions second = HealthcheckOptions.Resolve(setup, key => key == "CIS_HEALTHCHECK_MAX_MONITORS" ? "58" : null);
        Assert.AreEqual(57, first.MaxMonitors);
        Assert.AreEqual("setup", first.PipeName);
        Assert.AreEqual(58, second.MaxMonitors);
        Assert.AreEqual(57, first.MaxMonitors);
    }

    [TestMethod]
    [DataRow("1,000")]
    [DataRow("1.0")]
    [DataRow("2147483648")]
    [DataRow("not-an-integer")]
    public void InvalidHigherPriorityIntegerFailsInsteadOfUsingSetup(string invalid)
    {
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() =>
            HealthcheckOptions.Resolve(new HealthcheckOptions { MaxMonitors = 37 }, key =>
                key == "CIS_HEALTHCHECK_MAX_MONITORS" ? invalid : null));
        Assert.IsTrue(error.Issues.Any(issue => issue.Code == "Healthcheck.InvalidValue" && issue.Key == "CIS_HEALTHCHECK_MAX_MONITORS"));
        Assert.IsFalse(error.Message.Contains(invalid, StringComparison.Ordinal));
        Assert.IsFalse(error.Issues.Any(issue => issue.Explanation.Contains(invalid, StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("CIS_HEALTHCHECK_MAX_CONCURRENT_COLLECTIONS", "0")]
    [DataRow("CIS_HEALTHCHECK_MAX_CONCURRENT_COLLECTIONS", "65")]
    [DataRow("CIS_HEALTHCHECK_MAX_MONITORS", "0")]
    [DataRow("CIS_HEALTHCHECK_MAX_MONITORS", "65537")]
    [DataRow("CIS_HEALTHCHECK_PIPE_MAX_CLIENTS", "0")]
    [DataRow("CIS_HEALTHCHECK_PIPE_MAX_CLIENTS", "65")]
    [DataRow("CIS_HEALTHCHECK_PIPE_TIMEOUT_MS", "49")]
    [DataRow("CIS_HEALTHCHECK_PIPE_TIMEOUT_MS", "60001")]
    [DataRow("CIS_HEALTHCHECK_MAX_REPORT_BYTES", "1023")]
    [DataRow("CIS_HEALTHCHECK_MAX_REPORT_BYTES", "16777217")]
    public void BoundsRemainEnforcedWithTransportsDisabled(string selectedKey, string value)
    {
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() =>
            HealthcheckOptions.Resolve(null, key => key == selectedKey ? value : null));
        Assert.IsTrue(error.Issues.Any(issue => issue.Key == selectedKey && issue.Code == "Healthcheck.OutOfRange"));
    }

    [TestMethod]
    [DataRow("yes")]
    [DataRow("1")]
    [DataRow("on")]
    public void BooleanEnvironmentValuesRequireTrueOrFalse(string value)
    {
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() =>
            HealthcheckOptions.Resolve(null, key => key == "CIS_HEALTHCHECK_DETAILS_ENABLED" ? value : null));
        Assert.AreEqual("Healthcheck.InvalidValue", error.Issues.Single().Code);
    }

    [TestMethod]
    [DataRow("anonymous")]
    [DataRow("ANONYMOUS")]
    [DataRow("../health")]
    [DataRow("_health")]
    [DataRow("health/name")]
    [DataRow("health name")]
    [DataRow("")]
    public void InvalidSuppliedPipeNamesFailEvenWhenPipeIsDisabled(string name)
    {
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() =>
            new HealthcheckOptions { PipeName = name }.ValidatedCopy());
        Assert.AreEqual("CIS_HEALTHCHECK_PIPE_NAME", error.Issues.Single().Key);
    }

    [TestMethod]
    public void MissingEnabledPipeNameFailsWithoutPublishingAResult()
    {
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() =>
            HealthcheckOptions.Resolve(new HealthcheckOptions { PipeEnabled = true }, _ => null));
        Assert.AreEqual("Healthcheck.RequiredValue", error.Issues.Single().Code);
        Assert.IsFalse(new HealthcheckOptions().PipeEnabled);
    }

    [TestMethod]
    public void InclusiveLimitsAndPortableMaximumPipeNameAreAccepted()
    {
        HealthcheckOptions low = new HealthcheckOptions
        {
            MaxConcurrentCollections = 1,
            MaxMonitors = 1,
            PipeMaxClients = 1,
            PipeRequestTimeoutMs = 50,
            MaxReportBytes = 1024
        }.ValidatedCopy();
        HealthcheckOptions high = new HealthcheckOptions
        {
            MaxConcurrentCollections = 64,
            MaxMonitors = 65536,
            PipeMaxClients = 64,
            PipeRequestTimeoutMs = 60000,
            MaxReportBytes = 16777216,
            PipeEnabled = true,
            PipeName = "A" + new string('-', 63)
        }.ValidatedCopy();
        Assert.AreEqual(1, low.MaxMonitors);
        Assert.AreEqual(65536, high.MaxMonitors);
        Assert.AreEqual(64, high.PipeName!.Length);
        Assert.ThrowsExactly<CoreConfigurationException>(() =>
            new HealthcheckOptions { PipeName = new string('a', 65) }.ValidatedCopy());
    }

    [TestMethod]
    public void InvalidOverridesDoNotValidateDiscardedFallbackValues()
    {
        var setup = new HealthcheckOptions { MaxMonitors = 0, PipeEnabled = true };
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() =>
            HealthcheckOptions.Resolve(setup, key => key switch
            {
                "CIS_HEALTHCHECK_MAX_MONITORS" => "invalid",
                "CIS_HEALTHCHECK_PIPE_ENABLED" => "invalid",
                _ => null
            }));
        Assert.AreEqual(2, error.Issues.Count);
        Assert.IsTrue(error.Issues.All(issue => issue.Code == "Healthcheck.InvalidValue"));
    }

    [TestMethod]
    public void IndependentInvalidSettingsAreReportedWithoutRawValues()
    {
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() =>
            new HealthcheckOptions { MaxMonitors = 0, PipeMaxClients = 0, PipeEnabled = true }.ValidatedCopy());
        CollectionAssert.AreEquivalent(new[] { "CIS_HEALTHCHECK_MAX_MONITORS", "CIS_HEALTHCHECK_PIPE_MAX_CLIENTS", "CIS_HEALTHCHECK_PIPE_NAME" },
            error.Issues.Select(issue => issue.Key).ToArray());
    }
}
