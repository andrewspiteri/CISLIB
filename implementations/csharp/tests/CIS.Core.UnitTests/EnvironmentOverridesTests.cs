using System.Globalization;
using CIS.Configuration;
using CIS.Logging;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class EnvironmentOverridesTests
{
    [TestMethod]
    [DataRow("en-US")]
    [DataRow("tr-TR")]
    [DataRow("ar-SA")]
    public void IntegerGrammarIsIndependentOfCulture(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        string[] keys = ["CORE_LOG_QUEUE_CAPACITY", "CORE_LOG_FLUSH_INTERVAL_MS", "CORE_LOG_SHUTDOWN_TIMEOUT_MS", "CORE_LOG_FILE_MAX_BYTES", "CORE_LOG_FILE_RETAINED_FILES", "CORE_LOG_SCHEMA_VERSION", "CORE_LOG_MAX_ENTRY_BYTES"];
        string[] valid = ["+1", "+10", "+1", "+65536", "+1", "+1", "+16384"];
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            for (int index = 0; index < keys.Length; index++)
            {
                EnvironmentStoreTestData.AssignValidFacts();
                ConfigurationFixtureAdapter.ApplyOverrides(key => key == keys[index] ? valid[index] : null);
                EnvironmentStoreTestData.Values.Logging.Prepare(Path.GetTempPath());
                foreach (string malformed in new[] { "1,024", "1.0", "1e3", "1_024", "+ 1", "--1", "\u0661", "\u0967", "1\u0000", "\u200b1" })
                {
                    EnvironmentStoreTestData.AssignValidFacts();
                    CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => ConfigurationFixtureAdapter.ApplyOverrides(key => key == keys[index] ? malformed : null));
                    Assert.AreEqual("InvalidToken", error.Issues.Single().Code);
                    Assert.AreEqual(keys[index], error.Issues.Single().Key);
                }
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestMethod]
    [DataRow("CORE_SERVICE_NAME", "DD_SERVICE", true)]
    [DataRow("CORE_SERVICE_NAME", "DD_SERVICE", false)]
    [DataRow("CORE_ENVIRONMENT", "DD_ENV", true)]
    [DataRow("CORE_ENVIRONMENT", "DD_ENV", false)]
    [DataRow("CORE_SERVICE_VERSION", "DD_VERSION", true)]
    [DataRow("CORE_SERVICE_VERSION", "DD_VERSION", false)]
    public void NonblankCanonicalNeverReadsItsShadowedAlias(string canonical, string alias, bool valid)
    {
        EnvironmentStoreTestData.AssignValidFacts();
        var reads = new List<string>();
        string? Read(string key)
        {
            reads.Add(key);
            if (key == alias) { throw new InvalidOperationException("Shadowed alias was read."); }
            return key == canonical ? valid ? "Selected.Value" : "bad\nvalue" : null;
        }
        if (valid) { ConfigurationFixtureAdapter.ApplyOverrides(Read); }
        else
        {
            CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => ConfigurationFixtureAdapter.ApplyOverrides(Read));
            Assert.AreEqual("InvalidIdentity", error.Issues.Single().Code);
            Assert.AreEqual(canonical, error.Issues.Single().Key);
        }
        Assert.IsFalse(reads.Contains(alias, StringComparer.Ordinal));
        Assert.AreEqual(reads.Count, reads.Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    [DataRow("CORE_SERVICE_NAME", "DD_SERVICE", null)]
    [DataRow("CORE_SERVICE_NAME", "DD_SERVICE", "")]
    [DataRow("CORE_SERVICE_NAME", "DD_SERVICE", " \t ")]
    [DataRow("CORE_ENVIRONMENT", "DD_ENV", null)]
    [DataRow("CORE_ENVIRONMENT", "DD_ENV", "")]
    [DataRow("CORE_ENVIRONMENT", "DD_ENV", " \t ")]
    [DataRow("CORE_SERVICE_VERSION", "DD_VERSION", null)]
    [DataRow("CORE_SERVICE_VERSION", "DD_VERSION", "")]
    [DataRow("CORE_SERVICE_VERSION", "DD_VERSION", " \t ")]
    public void MissingOrBlankCanonicalUsesEachIdentityAlias(string canonical, string alias, string? token)
    {
        EnvironmentStoreTestData.AssignValidFacts();
        var reads = new List<string>();
        string? Read(string key)
        {
            reads.Add(key);
            return key == canonical ? token : key == alias ? " Alias.Selected " : null;
        }
        ConfigurationFixtureAdapter.ApplyOverrides(Read);
        string selected = canonical switch
        {
            "CORE_SERVICE_NAME" => EnvironmentStoreTestData.Values.ApplicationName,
            "CORE_ENVIRONMENT" => EnvironmentStoreTestData.Values.EnvironmentName,
            _ => EnvironmentStoreTestData.Values.ServiceVersion
        };
        Assert.AreEqual("Alias.Selected", selected);
        Assert.AreEqual(1, reads.Count(key => key == canonical));
        Assert.AreEqual(1, reads.Count(key => key == alias));
        Assert.AreEqual(reads.Count, reads.Distinct(StringComparer.Ordinal).Count());
    }

    public static IEnumerable<object[]> ControlCharacters => Enumerable.Range(0, 32).Concat(Enumerable.Range(127, 33)).Select(value => new object[] { value });

    [TestMethod]
    [DynamicData(nameof(ControlCharacters))]
    public void ConsumersRejectEveryInteriorIdentityControl(int codePoint)
    {
        foreach (string field in new[] { "ApplicationName", "EnvironmentName", "ServiceVersion", "BuildVersion", "CommitId", "BuildId" })
        {
            EnvironmentStoreTestData.AssignValidFacts();
            typeof(FixtureEnvironmentValues).GetProperty(field)!.SetValue(EnvironmentStoreTestData.Values, "before" + (char)codePoint + "after");
            CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => EnvironmentStoreTestData.Values.CaptureMetadata());
            Assert.AreEqual("InvalidIdentity", error.Issues.Single().Code);
            Assert.AreEqual("EnvironmentOptions." + field, error.Issues.Single().Key);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RequiredEnvironmentReadFailuresPropagate(bool canceled)
    {
        EnvironmentStoreTestData.AssignValidFacts();
        Exception failure = canceled ? new OperationCanceledException() : new IOException("Controlled read failure.");
        int reads = 0;
        string? Read(string key)
        {
            if (key != "CORE_SERVICE_VERSION") { return null; }
            reads++;
            throw failure;
        }
        Exception actual = canceled
            ? Assert.ThrowsExactly<OperationCanceledException>(() => ConfigurationFixtureAdapter.ApplyOverrides(Read))
            : Assert.ThrowsExactly<IOException>(() => ConfigurationFixtureAdapter.ApplyOverrides(Read));
        Assert.AreSame(failure, actual);
        Assert.AreEqual(1, reads);
        Assert.AreEqual("fixture-build", EnvironmentStoreTestData.Values.BuildVersion);
    }

    [TestMethod]
    public void ControlledInvalidMetadataAndLoggingInputsAreRejectedAtTheirConsumers()
    {
        EnvironmentStoreTestData.AssignValidFacts();
        EnvironmentStoreTestData.Values.ApplicationName = "invalid\nname";
        EnvironmentStoreTestData.Values.Logging.QueueCapacity = 0;
        Assert.AreEqual("invalid\nname", EnvironmentStoreTestData.Values.ApplicationName);
        Assert.AreEqual(0, EnvironmentStoreTestData.Values.Logging.QueueCapacity);
        Assert.ThrowsExactly<CoreConfigurationException>(() => EnvironmentStoreTestData.Values.CaptureMetadata());
        Assert.ThrowsExactly<CoreConfigurationException>(() => EnvironmentStoreTestData.Values.Logging.Prepare(Path.GetTempPath()));
    }
}
