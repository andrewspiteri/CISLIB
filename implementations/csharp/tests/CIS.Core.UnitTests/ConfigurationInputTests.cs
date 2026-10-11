using System.Text.Json;
using CIS.Configuration;
using CIS.Logging.Configuration;
using CIS.Tests.UnitTests.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ConfigurationInputTests
{
    private static readonly string[] KnownNames = [
        "CORE_SERVICE_NAME", "DD_SERVICE", "CORE_ENVIRONMENT", "DD_ENV", "CORE_SERVICE_VERSION", "DD_VERSION",
        "CORE_LOG_LEVEL", "CORE_LOG_CONSOLE_FORMAT", "CORE_LOG_CONSOLE_LEVEL", "CORE_LOG_CONSOLE_STREAM",
        "CORE_LOG_HUMAN_DETAIL", "CORE_LOG_FILE_PATH", "CORE_LOG_FILE_FORMAT", "CORE_LOG_FILE_LEVEL",
        "CORE_LOG_QUEUE_CAPACITY", "CORE_LOG_FLUSH_INTERVAL_MS", "CORE_LOG_SHUTDOWN_TIMEOUT_MS",
        "CORE_LOG_FILE_MAX_BYTES", "CORE_LOG_FILE_RETAINED_FILES", "CORE_LOG_SCHEMA_VERSION", "CORE_LOG_MAX_ENTRY_BYTES"
    ];

    public static IEnumerable<object[]> Settings => ConfigurationSettings.All.Select(setting => new object[] { setting.Key });

    [TestMethod]
    public void CompiledSettingsMatchTheSharedCatalogAndStayReadOnly()
    {
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(FindCatalog()));
        JsonElement[] expected = catalog.RootElement.GetProperty("settings").EnumerateArray().ToArray();
        Assert.AreEqual(18, ConfigurationSettings.All.Count);
        for (int index = 0; index < expected.Length; index++)
        {
            ConfigurationSetting actual = ConfigurationSettings.All[index];
            Assert.AreEqual(expected[index].GetProperty("key").GetString(), actual.Key);
            string? alias = expected[index].GetProperty("aliases").EnumerateArray().Select(value => value.GetString()).SingleOrDefault();
            Assert.AreEqual(alias, actual.Alias);
            string property = expected[index].GetProperty("property").GetString()!;
            Assert.AreEqual(property.StartsWith("Logging.", StringComparison.Ordinal) ? "LoggingSetup." + property[8..] : "EnvironmentSetup." + property, actual.SetupKey);
        }

        var list = (IList<ConfigurationSetting>)ConfigurationSettings.All;
        Assert.IsTrue(list.IsReadOnly);
        Assert.ThrowsExactly<NotSupportedException>(() => list[0] = list[1]);
    }

    [TestMethod]
    public void CaptureReadsEveryRecognizedNameOnceAndNeverConsultsUnknownNames()
    {
        var source = new EnvironmentVariableStub();
        foreach (string name in KnownNames)
        {
            source.Values[name] = " raw." + name + " ";
        }

        source.Values["CORE_PASSWORD"] = "DoNotCapture";
        source.Values["DD_AGENT_HOST"] = "DoNotCapture";
        CapturedEnvironment captured = CapturedEnvironment.Capture(source);
        foreach (string name in KnownNames)
        {
            Assert.AreEqual(" raw." + name + " ", captured.GetValue(name));
            Assert.AreEqual(" raw." + name + " ", captured.GetValue(name));
        }

        Assert.IsNull(captured.GetValue("CORE_PASSWORD"));
        Assert.IsNull(captured.GetValue("DD_AGENT_HOST"));
        Assert.IsNull(captured.GetValue("core_service_name"));
        CollectionAssert.AreEqual(KnownNames, source.Reads);
    }

    [TestMethod]
    public void EachCaptureOwnsValuesAndDoesNotKeepAProcessSourceOrGlobalCache()
    {
        var source = new EnvironmentVariableStub();
        source.Values["CORE_SERVICE_NAME"] = "First";
        CapturedEnvironment first = CapturedEnvironment.Capture(source);
        source.Values["CORE_SERVICE_NAME"] = "Second";
        CapturedEnvironment second = CapturedEnvironment.Capture(source);
        source.Values.Clear();

        Assert.AreEqual("First", first.GetValue("CORE_SERVICE_NAME"));
        Assert.AreEqual("Second", second.GetValue("CORE_SERVICE_NAME"));
        Assert.AreEqual(42, source.Reads.Count);
        Assert.AreEqual(21, source.Reads.Take(21).Distinct().Count());
        Assert.AreEqual(21, source.Reads.Skip(21).Distinct().Count());
    }

    [TestMethod]
    [DataRow("DD_SERVICE")]
    [DataRow("CORE_LOG_LEVEL")]
    [DataRow("CORE_LOG_MAX_ENTRY_BYTES")]
    public void RequiredReadFailuresPropagateWithoutReturningAPartialCapture(string failingName)
    {
        var failure = new IOException("Controlled variable access failure");
        var source = new EnvironmentVariableStub { FailingName = failingName, Failure = failure };
        source.Values["CORE_SERVICE_NAME"] = "Valid.HigherPriority";
        CapturedEnvironment? captured = null;

        IOException actual = Assert.ThrowsExactly<IOException>(() => captured = CapturedEnvironment.Capture(source));

        Assert.AreSame(failure, actual);
        Assert.IsNull(captured);
        Assert.AreEqual(1, source.Reads.Count(name => name == failingName));
        Assert.AreEqual(failingName, source.Reads[^1]);
    }

    [TestMethod]
    [DynamicData(nameof(Settings))]
    public void EverySettingSelectsTheCoreTokenBeforeTypedLowerPriorityValues(string key)
    {
        var source = new EnvironmentVariableStub();
        source.Values[key] = " Selected.MixedCase ";
        CapturedEnvironment captured = CapturedEnvironment.Capture(source);
        ConfigurationSelection<string> selected = ConfigurationValueSelector.Select(captured, key,
            new ConfigurationCandidate<string>("Setup"), new ConfigurationCandidate<string>("Discovery"), new ConfigurationCandidate<string>("Default"));

        Assert.AreEqual(ConfigurationSource.Environment, selected.Source);
        Assert.AreEqual(key, selected.SourceKey);
        Assert.AreEqual("Selected.MixedCase", selected.Token);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = selected.Value);
        Assert.AreEqual(21, source.Reads.Count);
    }

    [TestMethod]
    [DataRow("CORE_SERVICE_NAME", "DD_SERVICE")]
    [DataRow("CORE_ENVIRONMENT", "DD_ENV")]
    [DataRow("CORE_SERVICE_VERSION", "DD_VERSION")]
    public void OnlyDocumentedIdentityAliasesOverrideSetup(string key, string alias)
    {
        var source = new EnvironmentVariableStub();
        source.Values[key] = " \t";
        source.Values[alias] = " Alias.MixedCase ";
        ConfigurationSelection<string> selected = ConfigurationValueSelector.Select(CapturedEnvironment.Capture(source), key,
            new ConfigurationCandidate<string>("Setup"), new ConfigurationCandidate<string>("Discovery"), new ConfigurationCandidate<string>("Default"));

        Assert.AreEqual(ConfigurationSource.Environment, selected.Source);
        Assert.AreEqual(alias, selected.SourceKey);
        Assert.AreEqual("Alias.MixedCase", selected.Token);
    }

    [TestMethod]
    [DataRow("Core", "Alias", "Setup", "Discovery", "Default", "Environment", "CORE_SERVICE_NAME", "Core")]
    [DataRow("bad\nCore", "Alias", "Setup", "Discovery", "Default", "Environment", "CORE_SERVICE_NAME", "bad\nCore")]
    [DataRow("Core", "bad\nAlias", "bad\nSetup", "Discovery", "Default", "Environment", "CORE_SERVICE_NAME", "Core")]
    [DataRow(null, "Alias", "Setup", "Discovery", "Default", "Environment", "DD_SERVICE", "Alias")]
    [DataRow(" \t", "bad\nAlias", "Setup", "Discovery", "Default", "Environment", "DD_SERVICE", "bad\nAlias")]
    [DataRow(null, "\u2003", " Setup.MixedCase ", "Discovery", "Default", "Setup", "EnvironmentSetup.ApplicationName", "Setup.MixedCase")]
    [DataRow(null, null, "bad\nSetup", "Discovery", "Default", "Setup", "EnvironmentSetup.ApplicationName", "bad\nSetup")]
    [DataRow(null, null, " ", " Discovery.MixedCase ", "Default", "Discovery", "ApplicationName", "Discovery.MixedCase")]
    [DataRow(null, null, null, " \t", " Default.MixedCase ", "Default", "CORE_SERVICE_NAME", "Default.MixedCase")]
    [DataRow(null, null, null, null, null, "Absent", "CORE_SERVICE_NAME", null)]
    public void FirstPresentSourceWinsWithoutValidationOrInvalidFallback(
        string? core, string? alias, string? setup, string? discovered, string? fallback,
        string expectedSourceName, string expectedKey, string? expectedValue)
    {
        var source = new EnvironmentVariableStub();
        source.Values["CORE_SERVICE_NAME"] = core;
        source.Values["DD_SERVICE"] = alias;
        ConfigurationSelection<string> selected = ConfigurationValueSelector.Select(CapturedEnvironment.Capture(source), "CORE_SERVICE_NAME",
            new ConfigurationCandidate<string>(setup), new ConfigurationCandidate<string>(discovered), new ConfigurationCandidate<string>(fallback));

        ConfigurationSource expectedSource = Enum.Parse<ConfigurationSource>(expectedSourceName);
        Assert.AreEqual(expectedSource, selected.Source);
        Assert.AreEqual(expectedKey, selected.SourceKey);
        if (expectedSource == ConfigurationSource.Environment)
        {
            Assert.AreEqual(expectedValue, selected.Token);
        }
        else if (expectedSource == ConfigurationSource.Absent)
        {
            Assert.IsNull(selected.Token);
            Assert.ThrowsExactly<InvalidOperationException>(() => _ = selected.Value);
        }
        else
        {
            Assert.AreEqual(expectedValue, selected.Value);
            Assert.IsNull(selected.Token);
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    public void ExplicitTypedNumbersKeepTheirValueIncludingInvalidBounds(int value)
    {
        ConfigurationSelection<int> selected = ConfigurationValueSelector.Select(CapturedEnvironment.Capture(new EnvironmentVariableStub()), "CORE_LOG_QUEUE_CAPACITY",
            new ConfigurationCandidate<int>(value), fallback: new ConfigurationCandidate<int>(1024));

        Assert.AreEqual(ConfigurationSource.Setup, selected.Source);
        Assert.AreEqual("LoggingSetup.QueueCapacity", selected.SourceKey);
        Assert.AreEqual(value, selected.Value);
    }

    [TestMethod]
    public void TypedLongAndUndefinedEnumsAreNotConvertedToEnvironmentTokens()
    {
        CapturedEnvironment captured = CapturedEnvironment.Capture(new EnvironmentVariableStub());
        ConfigurationSelection<long> bytes = ConfigurationValueSelector.Select(captured, "CORE_LOG_FILE_MAX_BYTES", new ConfigurationCandidate<long>(long.MaxValue));
        ConfigurationSelection<ConfigurationSeverity> undefined = ConfigurationValueSelector.Select(captured, "CORE_LOG_LEVEL", new ConfigurationCandidate<ConfigurationSeverity>((ConfigurationSeverity)999));

        Assert.AreEqual(long.MaxValue, bytes.Value);
        Assert.AreEqual((ConfigurationSeverity)999, undefined.Value);
        Assert.IsNull(bytes.Token);
        Assert.IsNull(undefined.Token);
    }

    [TestMethod]
    public void RepeatedSelectionAndVersionDiscoveryUseTheSameCapturedInputs()
    {
        var source = new EnvironmentVariableStub();
        source.Values["CORE_SERVICE_VERSION"] = "First.Label";
        CapturedEnvironment captured = CapturedEnvironment.Capture(source);
        source.Values["CORE_SERVICE_VERSION"] = "Changed.Label";
        var app = new ApplicationIdentity("App", "1.2.3+Actual.Build", null, null);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            ApplicationVersionSelection version = ApplicationVersionSelector.Select(app, variables: captured);
            Assert.AreEqual("First.Label", version.ServiceVersion);
            Assert.AreEqual("1.2.3+Actual.Build", version.BuildVersion);
            foreach (ConfigurationSetting setting in ConfigurationSettings.All)
            {
                _ = ConfigurationValueSelector.Select<string>(captured, setting.Key);
            }
        }

        CollectionAssert.AreEqual(KnownNames, source.Reads);
    }

    [TestMethod]
    [DataRow("CORE_PASSWORD")]
    [DataRow("DD_AGENT_HOST")]
    [DataRow("core_service_name")]
    public void UnknownSettingSelectionFailsBeforeReadingAProcessValue(string key)
    {
        var source = new EnvironmentVariableStub();
        ArgumentException failure = Assert.ThrowsExactly<ArgumentException>(() => ConfigurationValueSelector.Select<string>(source, key));
        Assert.AreEqual(0, source.Reads.Count);
        Assert.IsFalse(failure.Message.Contains(key, StringComparison.Ordinal));
    }

    [TestMethod]
    public void AbsentCandidatesAndNullArgumentsFailSafely()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = default(ConfigurationCandidate<int>).Value);
        Assert.ThrowsExactly<ArgumentNullException>(() => ConfigurationValueSelector.Select<string>(null!, "CORE_SERVICE_NAME"));
        Assert.ThrowsExactly<ArgumentNullException>(() => ConfigurationValueSelector.Select<string>(new EnvironmentVariableStub(), null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => CapturedEnvironment.Capture(new EnvironmentVariableStub()).GetValue(null!));
    }

    [TestMethod]
    public void RawSnapshotsAndSelectionsDoNotDumpValuesWhenFormatted()
    {
        const string raw = "secret-value-that-must-not-appear-in-diagnostics";
        var source = new EnvironmentVariableStub();
        source.Values["CORE_SERVICE_NAME"] = raw;
        CapturedEnvironment captured = CapturedEnvironment.Capture(source);
        ConfigurationSelection<string> selected = ConfigurationValueSelector.Select<string>(captured, "CORE_SERVICE_NAME");
        Assert.IsFalse(captured.ToString()!.Contains(raw, StringComparison.Ordinal));
        Assert.IsFalse(selected.ToString()!.Contains(raw, StringComparison.Ordinal));
    }

    private static string FindCatalog()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "contracts", "logging", "v1", "configuration.json");
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Cannot find the shared configuration catalog.");
    }
}
