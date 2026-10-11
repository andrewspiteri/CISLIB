using System.Globalization;
using CIS.Configuration;
using CIS.Logging.Configuration;
using CIS.Tests.UnitTests.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ConfigurationNormalizationTests
{
    private static readonly ApplicationIdentity Application = new("Discovered.App.Mixed", "9.8.7-RC.2+Sha.MixedCASE", null, null);
    private static readonly string[] Cultures = ["en-US", "tr-TR", "ar-SA"];

    public static IEnumerable<object?[]> BlankValues =>
        new string?[] { null, "", " ", "\t\r\n", "\u00a0", "\u2003\u202f", "\u3000" }.Select(value => new object?[] { value });

    [TestMethod]
    [DynamicData(nameof(BlankValues))]
    public void BlankCoreValuesUseTypedSetupForEverySetting(string? blank)
    {
        var source = new EnvironmentVariableStub();
        foreach (ConfigurationSetting setting in ConfigurationSettings.All)
        {
            source.Values[setting.Key] = blank;
        }

        EnvironmentConfigurationInput setup = CompleteSetup();
        ConfigurationResolution result = Resolve(source, setup);
        Assert.IsFalse(result.HasParseErrors);
        Assert.IsTrue(result.Settings.All(setting => setting.Source == ConfigurationSource.Setup && setting.State == ConfigurationParseState.Parsed));
        Assert.AreEqual("Setup.App.Mixed", result.ApplicationName.Value);
        Assert.AreEqual("Host.Framework.Mixed", result.EnvironmentName.Value);
        Assert.AreEqual("Setup.Release+Exact.CASE", result.ServiceVersion.Value);
        Assert.AreEqual("Relative.Mixed/Output.log", result.FilePath.Value);
        Assert.AreEqual(ConfigurationSeverity.Error, result.Level.Value);
        Assert.AreEqual(ConfigurationSeverity.Warning, result.ConsoleLevel.Value);
        Assert.AreEqual(ConfigurationSeverity.Debug, result.FileLevel.Value);
        Assert.AreEqual(131072, result.MaxEntryBytes.Value);
        Assert.AreEqual(true, result.ConsoleEnabled);
        Assert.AreEqual(true, result.FileEnabled);
        Assert.AreEqual(21, source.Reads.Count);
    }

    [TestMethod]
    [DynamicData(nameof(BlankValues))]
    public void BlankAliasesAndSetupTextUseDiscoveryOrDefaults(string? blank)
    {
        var source = new EnvironmentVariableStub();
        foreach (ConfigurationSetting setting in ConfigurationSettings.All.Where(setting => setting.Alias is not null))
        {
            source.Values[setting.Key] = blank;
            source.Values[setting.Alias!] = blank;
        }

        var setup = new EnvironmentConfigurationInput
        {
            ApplicationName = blank,
            EnvironmentName = blank,
            ServiceVersion = blank,
            Logging = new LoggingConfigurationInput { FilePath = blank }
        };
        ConfigurationResolution result = Resolve(source, setup);
        Assert.AreEqual(Application.ApplicationName, result.ApplicationName.Value);
        Assert.AreEqual(ConfigurationSource.Discovery, result.ApplicationName.Source);
        Assert.AreEqual("unknown", result.EnvironmentName.Value);
        Assert.AreEqual(ConfigurationSource.Default, result.EnvironmentName.Source);
        Assert.AreEqual(Application.BuildVersion, result.ServiceVersion.Value);
        Assert.AreEqual(ConfigurationSource.Discovery, result.ServiceVersion.Source);
        Assert.AreEqual(ConfigurationParseState.Absent, result.FilePath.State);
        Assert.AreEqual(false, result.FileEnabled);
        Assert.IsFalse(result.HasParseErrors);
    }

    [TestMethod]
    [DataRow("core")]
    [DataRow("alias")]
    [DataRow("setup")]
    public void IdentityLayersPreserveTrimmedCaseAndUnicodeSequences(string layer)
    {
        const string rawName = "\u2003 App.\u0130.I.\u0131.i.A\u030a \U0001f680 \u00a0";
        const string rawEnvironment = "\t Host.Mixed CASE.\u00d6 \u3000";
        const string rawVersion = "\u202f Release.RC+Sha.MixedCASE.A\u030a \n";
        string[] raw = [rawName, rawEnvironment, rawVersion];
        var source = new EnvironmentVariableStub();
        ConfigurationSetting[] identities = ConfigurationSettings.All.Where(setting => setting.Alias is not null).ToArray();
        for (int index = 0; index < identities.Length; index++)
        {
            if (layer is "core" or "alias")
            {
                source.Values[layer == "core" ? identities[index].Key : identities[index].Alias!] = raw[index];
            }
        }

        var setup = new EnvironmentConfigurationInput { ApplicationName = rawName, EnvironmentName = rawEnvironment, ServiceVersion = rawVersion };
        ConfigurationResolution result = Resolve(source, setup);
        Assert.AreEqual("App.\u0130.I.\u0131.i.A\u030a \U0001f680", result.ApplicationName.Value);
        Assert.AreEqual("Host.Mixed CASE.\u00d6", result.EnvironmentName.Value);
        Assert.AreEqual("Release.RC+Sha.MixedCASE.A\u030a", result.ServiceVersion.Value);
        Assert.AreEqual(Application.BuildVersion, result.BuildVersion);
        for (int index = 0; index < identities.Length; index++)
        {
            IParsedConfigurationSetting selected = result.Settings[index];
            Assert.AreEqual(layer == "setup" ? ConfigurationSource.Setup : ConfigurationSource.Environment, selected.Source);
            Assert.AreEqual(layer switch { "core" => identities[index].Key, "alias" => identities[index].Alias, _ => identities[index].SetupKey }, selected.SourceKey);
        }

        Assert.AreEqual(rawName, setup.ApplicationName);
        Assert.AreEqual(rawEnvironment, setup.EnvironmentName);
        Assert.AreEqual(rawVersion, setup.ServiceVersion);
    }

    [TestMethod]
    [DataRow("CORE_LOG_LEVEL", "information", "Information")]
    [DataRow("CORE_LOG_LEVEL", "info", "Information")]
    [DataRow("CORE_LOG_CONSOLE_LEVEL", "warning", "Warning")]
    [DataRow("CORE_LOG_CONSOLE_LEVEL", "wrn", "Warning")]
    [DataRow("CORE_LOG_FILE_LEVEL", "debug", "Debug")]
    [DataRow("CORE_LOG_FILE_LEVEL", "dbg", "Debug")]
    [DataRow("CORE_LOG_CONSOLE_FORMAT", "human", "Human")]
    [DataRow("CORE_LOG_CONSOLE_FORMAT", "jsonl", "JsonLines")]
    [DataRow("CORE_LOG_FILE_FORMAT", "human", "Human")]
    [DataRow("CORE_LOG_FILE_FORMAT", "jsonl", "JsonLines")]
    [DataRow("CORE_LOG_CONSOLE_STREAM", "stderr", "StandardError")]
    [DataRow("CORE_LOG_CONSOLE_STREAM", "stdout", "StandardOutput")]
    [DataRow("CORE_LOG_HUMAN_DETAIL", "normal", "Normal")]
    [DataRow("CORE_LOG_HUMAN_DETAIL", "verbose", "Verbose")]
    public void TokensMatchOrdinalCaseAcrossCultures(string key, string token, string expected)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in Cultures)
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                string alternating = string.Concat(token.Select((character, index) => index % 2 == 0 ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character)));
                foreach (string variant in new[] { token, token.ToUpperInvariant(), alternating })
                {
                    var source = new EnvironmentVariableStub();
                    source.Values[key] = "\u2003\t" + variant + "\u00a0\u3000";
                    source.Values["CORE_SERVICE_NAME"] = " Identity.\u0130.I.\u0131.i ";
                    ConfigurationResolution result = Resolve(source, CompleteSetup());
                    IParsedConfigurationSetting selected = result.Settings.Single(setting => setting.Key == key);
                    Assert.AreEqual(ConfigurationParseState.Parsed, selected.State, culture);
                    Assert.AreEqual(expected, TokenValue(result, key), culture);
                    Assert.AreEqual(ConfigurationSource.Environment, selected.Source);
                    Assert.AreEqual("Identity.\u0130.I.\u0131.i", result.ApplicationName.Value);
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    [DataRow("CORE_LOG_LEVEL", "I NFO")]
    [DataRow("CORE_LOG_CONSOLE_FORMAT", "js onl")]
    [DataRow("CORE_LOG_FILE_FORMAT", "hu\tman")]
    [DataRow("CORE_LOG_CONSOLE_STREAM", "std out")]
    [DataRow("CORE_LOG_HUMAN_DETAIL", "ver\nbose")]
    [DataRow("CORE_LOG_QUEUE_CAPACITY", "1\u00a00")]
    [DataRow("CORE_LOG_FLUSH_INTERVAL_MS", "1\t0")]
    [DataRow("CORE_LOG_LEVEL", "\u200b")]
    [DataRow("CORE_LOG_CONSOLE_FORMAT", "\ufeffhuman")]
    [DataRow("CORE_LOG_SCHEMA_VERSION", "\u200b1")]
    public void NonblankMalformedTokensAreNotRepairedOrReplaced(string key, string raw)
    {
        var source = new EnvironmentVariableStub();
        source.Values[key] = "\u2003" + raw + "\u3000";
        ConfigurationResolution result = Resolve(source, CompleteSetup());
        IParsedConfigurationSetting selected = result.Settings.Single(setting => setting.Key == key);
        Assert.AreEqual(ConfigurationParseState.InvalidToken, selected.State);
        Assert.AreEqual(ConfigurationSource.Environment, selected.Source);
        Assert.AreEqual(key, selected.SourceKey);
        Assert.IsTrue(result.HasParseErrors);
    }

    [TestMethod]
    [DataRow("\u200b")]
    [DataRow("\ufeff")]
    public void NonWhitespaceFormatCharactersRemainSelectedText(string text)
    {
        var source = new EnvironmentVariableStub();
        source.Values["CORE_SERVICE_NAME"] = text;
        source.Values["CORE_LOG_FILE_PATH"] = text;
        ConfigurationResolution result = Resolve(source, CompleteSetup());
        Assert.AreEqual(text, result.ApplicationName.Value);
        Assert.AreEqual(text, result.FilePath.Value);
        Assert.AreEqual(ConfigurationSource.Environment, result.ApplicationName.Source);
        Assert.AreEqual(ConfigurationSource.Environment, result.FilePath.Source);
        // Text selection does not replace the later identity/path validator.
        Assert.AreEqual(ConfigurationParseState.Parsed, result.ApplicationName.State);
    }

    [TestMethod]
    [DataRow("CORE_PASSWORD")]
    [DataRow("CORE_LOG_LEVEL_EXTRA")]
    [DataRow("CORE_LOG_NLOG_LEVEL")]
    [DataRow("DD_AGENT_HOST")]
    [DataRow("core_log_level")]
    [DataRow("C\u041eRE_LOG_LEVEL")]
    public void UnknownNamesAreNeverReadCapturedOrFormatted(string unknown)
    {
        const string raw = "unknown-value-must-not-be-read-or-dumped";
        var source = new EnvironmentVariableStub { FailingName = unknown, Failure = new InvalidOperationException(raw) };
        source.Values[unknown] = raw;
        CapturedEnvironment captured = CapturedEnvironment.Capture(source);
        ConfigurationResolution result = ConfigurationResolver.Resolve(new EnvironmentConfigurationInput(), Application, captured);
        Assert.IsNull(captured.GetValue(unknown));
        Assert.AreEqual(21, source.Reads.Count);
        Assert.IsFalse(source.Reads.Contains(unknown, StringComparer.Ordinal));
        Assert.AreEqual(ConfigurationSeverity.Information, result.Level.Value);
        Assert.IsFalse(result.HasParseErrors);
        Assert.IsFalse(captured.ToString()!.Contains(raw, StringComparison.Ordinal));
        Assert.IsFalse(result.ToString()!.Contains(raw, StringComparison.Ordinal));
        Assert.AreEqual(raw, source.Values[unknown]);
    }

    [TestMethod]
    public void ParsedResultsAndValueGuardsDoNotDumpRawInputs()
    {
        const string raw = "recognized-value-must-not-be-auto-formatted";
        var source = new EnvironmentVariableStub();
        source.Values["CORE_SERVICE_NAME"] = raw;
        source.Values["CORE_LOG_LEVEL"] = raw;
        source.Values["CORE_PASSWORD"] = raw;
        CapturedEnvironment captured = CapturedEnvironment.Capture(source);
        ConfigurationResolution result = ConfigurationResolver.Resolve(new EnvironmentConfigurationInput(), Application, captured);
        Assert.AreEqual(raw, result.ApplicationName.Value);
        foreach (object value in new object[] { captured, result }.Concat(result.Settings))
        {
            Assert.IsFalse(value.ToString()!.Contains(raw, StringComparison.Ordinal));
        }

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() => _ = result.Level.Value);
        Assert.IsFalse(error.Message.Contains(raw, StringComparison.Ordinal));
        Assert.IsFalse(error.ToString().Contains(raw, StringComparison.Ordinal));
    }

    [TestMethod]
    public void NormalizationLeavesCallerInputsAndHostLabelsUnchanged()
    {
        const string rawFrameworkLabel = " Host.Framework.MixedCASE ";
        var setup = new EnvironmentConfigurationInput { EnvironmentName = rawFrameworkLabel };
        var source = new EnvironmentVariableStub();
        source.Values["CORE_ENVIRONMENT"] = "\u2003";
        source.Values["DD_ENV"] = " Telemetry.MixedCASE ";
        source.Values["ASPNETCORE_ENVIRONMENT"] = rawFrameworkLabel;
        KeyValuePair<string, string?>[] before = source.Values.ToArray();
        ConfigurationResolution result = Resolve(source, setup);
        Assert.AreEqual("Telemetry.MixedCASE", result.EnvironmentName.Value);
        Assert.AreEqual("DD_ENV", result.EnvironmentName.SourceKey);
        Assert.AreEqual(rawFrameworkLabel, setup.EnvironmentName);
        CollectionAssert.AreEqual(before, source.Values.ToArray());
        Assert.IsFalse(source.Reads.Contains("ASPNETCORE_ENVIRONMENT", StringComparer.Ordinal));
        Assert.AreEqual(21, source.Reads.Count);
    }

    private static ConfigurationResolution Resolve(EnvironmentVariableStub source, EnvironmentConfigurationInput setup) =>
        ConfigurationResolver.Resolve(setup, Application, CapturedEnvironment.Capture(source));

    private static EnvironmentConfigurationInput CompleteSetup() => new()
    {
        ApplicationName = " Setup.App.Mixed ",
        EnvironmentName = " Host.Framework.Mixed ",
        ServiceVersion = " Setup.Release+Exact.CASE ",
        Logging = new LoggingConfigurationInput
        {
            Level = ConfigurationSeverity.Error,
            ConsoleFormat = ConfigurationOutputFormat.JsonLines,
            ConsoleLevel = ConfigurationSeverity.Warning,
            ConsoleStream = ConfigurationConsoleStream.StandardOutput,
            HumanDetail = ConfigurationHumanDetail.Verbose,
            FilePath = " Relative.Mixed/Output.log ",
            FileFormat = ConfigurationOutputFormat.Human,
            FileLevel = ConfigurationSeverity.Debug,
            QueueCapacity = 2048,
            FlushIntervalMilliseconds = 2000,
            ShutdownTimeoutMilliseconds = 10000,
            FileMaxBytes = 20971520L,
            FileRetainedFiles = 20,
            SchemaVersion = 1,
            MaxEntryBytes = 131072
        }
    };

    private static string TokenValue(ConfigurationResolution result, string key) => key switch
    {
        "CORE_LOG_LEVEL" => result.Level.Value.ToString(),
        "CORE_LOG_CONSOLE_LEVEL" => result.ConsoleLevel.Value.ToString(),
        "CORE_LOG_FILE_LEVEL" => result.FileLevel.Value.ToString(),
        "CORE_LOG_CONSOLE_FORMAT" => result.ConsoleFormat.Value.ToString(),
        "CORE_LOG_FILE_FORMAT" => result.FileFormat.Value.ToString(),
        "CORE_LOG_CONSOLE_STREAM" => result.ConsoleStream.Value.ToString(),
        "CORE_LOG_HUMAN_DETAIL" => result.HumanDetail.Value.ToString(),
        _ => throw new ArgumentException("Unknown test token setting.", nameof(key))
    };
}
