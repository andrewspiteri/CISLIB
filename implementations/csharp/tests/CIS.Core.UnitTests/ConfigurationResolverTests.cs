using System.Globalization;
using CIS.Configuration;
using CIS.Logging.Configuration;
using CIS.Tests.UnitTests.Discovery;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ConfigurationResolverTests
{
    private static readonly ApplicationIdentity Application = new("Discovered.App", "2.3.4-rc.1+Actual.Build", "Commit", "BuildId");
    private static readonly string[] IntegerKeys = ["CORE_LOG_QUEUE_CAPACITY", "CORE_LOG_FLUSH_INTERVAL_MS", "CORE_LOG_SHUTDOWN_TIMEOUT_MS", "CORE_LOG_FILE_MAX_BYTES", "CORE_LOG_FILE_RETAINED_FILES", "CORE_LOG_SCHEMA_VERSION", "CORE_LOG_MAX_ENTRY_BYTES"];

    public static IEnumerable<object[]> SeverityAliases
    {
        get
        {
            (string Token, string Expected)[] values = [("Trace", "Trace"), ("TRC", "Trace"), ("Debug", "Debug"), ("DBG", "Debug"), ("Information", "Information"), ("INF", "Information"), ("INFO", "Information"), ("Warning", "Warning"), ("WRN", "Warning"), ("WARN", "Warning"), ("Error", "Error"), ("ERR", "Error"), ("Critical", "Critical"), ("CRT", "Critical"), ("CRIT", "Critical"), ("None", "None")];
            foreach (string key in new[] { "CORE_LOG_LEVEL", "CORE_LOG_CONSOLE_LEVEL", "CORE_LOG_FILE_LEVEL" })
            {
                foreach ((string token, string expected) in values)
                {
                    yield return [key, " " + token.ToLowerInvariant() + " ", expected];
                }
            }
        }
    }

    [TestMethod]
    public void AllDefaultsAndCatalogOrderMatchTheDeclaredContract()
    {
        ConfigurationResolution result = Resolve();
        Assert.IsFalse(result.HasParseErrors);
        Assert.AreEqual(18, result.Settings.Count);
        Assert.AreEqual("Discovered.App", result.ApplicationName.Value);
        Assert.AreEqual("unknown", result.EnvironmentName.Value);
        Assert.AreEqual(Application.BuildVersion, result.BuildVersion);
        Assert.AreEqual(Application.BuildVersion, result.ServiceVersion.Value);
        Assert.AreEqual(ConfigurationSeverity.Information, result.Level.Value);
        Assert.AreEqual(ConfigurationSeverity.Information, result.ConsoleLevel.Value);
        Assert.AreEqual(ConfigurationSeverity.Information, result.FileLevel.Value);
        Assert.AreEqual("CORE_LOG_LEVEL", result.ConsoleLevel.InheritedFrom);
        Assert.AreEqual("CORE_LOG_LEVEL", result.FileLevel.InheritedFrom);
        Assert.AreEqual(ConfigurationOutputFormat.Human, result.ConsoleFormat.Value);
        Assert.AreEqual(ConfigurationOutputFormat.JsonLines, result.FileFormat.Value);
        Assert.AreEqual(ConfigurationConsoleStream.StandardError, result.ConsoleStream.Value);
        Assert.AreEqual(ConfigurationHumanDetail.Normal, result.HumanDetail.Value);
        Assert.AreEqual(ConfigurationParseState.Absent, result.FilePath.State);
        Assert.AreEqual(1024, result.QueueCapacity.Value);
        Assert.AreEqual(1000, result.FlushIntervalMilliseconds.Value);
        Assert.AreEqual(TimeSpan.FromSeconds(1), result.FlushInterval);
        Assert.AreEqual(5000, result.ShutdownTimeoutMilliseconds.Value);
        Assert.AreEqual(TimeSpan.FromSeconds(5), result.ShutdownTimeout);
        Assert.AreEqual(10485760L, result.FileMaxBytes.Value);
        Assert.AreEqual(10, result.FileRetainedFiles.Value);
        Assert.AreEqual(1, result.SchemaVersion.Value);
        Assert.AreEqual(65536, result.MaxEntryBytes.Value);
        Assert.AreEqual(true, result.ConsoleEnabled);
        Assert.AreEqual(false, result.FileEnabled);
        CollectionAssert.AreEqual(ConfigurationSettings.All.Select(s => s.Key).ToArray(), result.Settings.Select(s => s.Key).ToArray());
    }

    [TestMethod]
    [DataRow("CORE_SERVICE_NAME", " New.App ", "New.App")]
    [DataRow("CORE_ENVIRONMENT", " MixedCase.Production ", "MixedCase.Production")]
    [DataRow("CORE_SERVICE_VERSION", " Deploy+Exact.Case ", "Deploy+Exact.Case")]
    [DataRow("CORE_LOG_LEVEL", " dBg ", "Debug")]
    [DataRow("CORE_LOG_CONSOLE_FORMAT", " JSONL ", "JsonLines")]
    [DataRow("CORE_LOG_CONSOLE_LEVEL", " wRn ", "Warning")]
    [DataRow("CORE_LOG_CONSOLE_STREAM", " STDOUT ", "StandardOutput")]
    [DataRow("CORE_LOG_HUMAN_DETAIL", " VERBOSE ", "Verbose")]
    [DataRow("CORE_LOG_FILE_PATH", " MixedCase/Relative.log ", "MixedCase/Relative.log")]
    [DataRow("CORE_LOG_FILE_FORMAT", " HUMAN ", "Human")]
    [DataRow("CORE_LOG_FILE_LEVEL", " trc ", "Trace")]
    [DataRow("CORE_LOG_QUEUE_CAPACITY", " +002048 ", "2048")]
    [DataRow("CORE_LOG_FLUSH_INTERVAL_MS", " 2000 ", "2000")]
    [DataRow("CORE_LOG_SHUTDOWN_TIMEOUT_MS", " 10000 ", "10000")]
    [DataRow("CORE_LOG_FILE_MAX_BYTES", " 20971520 ", "20971520")]
    [DataRow("CORE_LOG_FILE_RETAINED_FILES", " 20 ", "20")]
    [DataRow("CORE_LOG_SCHEMA_VERSION", " +0001 ", "1")]
    [DataRow("CORE_LOG_MAX_ENTRY_BYTES", " 131072 ", "131072")]
    public void EverySettingAcceptsItsDocumentedEnvironmentOverride(string key, string token, string expected)
    {
        ConfigurationResolution result = Resolve(new Dictionary<string, string?> { [key] = token });
        Assert.IsFalse(result.HasParseErrors);
        Assert.AreEqual(expected, FormatValue(Value(result, key)));
        Assert.AreEqual(ConfigurationSource.Environment, result.Settings.Single(s => s.Key == key).Source);
        Assert.AreEqual(key, result.Settings.Single(s => s.Key == key).SourceKey);
        Assert.AreEqual(Application.BuildVersion, result.BuildVersion);
    }

    [TestMethod]
    public void TypedSetupResolvesEveryOptionWithoutConvertingItToAToken()
    {
        var setup = new EnvironmentConfigurationInput
        {
            ApplicationName = " Setup.App ",
            EnvironmentName = " Host.Framework ",
            ServiceVersion = " Setup.Release ",
            Logging = new LoggingConfigurationInput
            {
                Level = ConfigurationSeverity.Warning,
                ConsoleFormat = ConfigurationOutputFormat.JsonLines,
                ConsoleLevel = ConfigurationSeverity.Error,
                ConsoleStream = ConfigurationConsoleStream.StandardOutput,
                HumanDetail = ConfigurationHumanDetail.Verbose,
                FilePath = " relative.log ",
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
        ConfigurationResolution result = Resolve(setup: setup);
        string[] expected = ["Setup.App", "Host.Framework", "Setup.Release", "Warning", "JsonLines", "Error", "StandardOutput", "Verbose", "relative.log", "Human", "Debug", "2048", "2000", "10000", "20971520", "20", "1", "131072"];
        CollectionAssert.AreEqual(expected, result.Settings.Select(s => FormatValue(Value(result, s.Key))).ToArray());
        Assert.IsTrue(result.Settings.All(s => s.Source == ConfigurationSource.Setup));
        Assert.AreEqual("BuildId", Application.BuildId);
        Assert.AreEqual(Application.BuildVersion, result.BuildVersion);
        Assert.AreEqual(true, result.FileEnabled);
    }

    [TestMethod]
    [DynamicData(nameof(SeverityAliases))]
    public void ExistingSeverityNamesAndAbbreviationsApplyToEveryThreshold(string key, string token, string expected)
    {
        ConfigurationResolution result = Resolve(new Dictionary<string, string?> { [key] = token });
        Assert.AreEqual(expected, FormatValue(Value(result, key)));
        Assert.IsFalse(result.HasParseErrors);
    }

    [TestMethod]
    [DataRow("Information", "Warning", "Debug", "Warning", "Debug")]
    [DataRow("Debug", null, null, "Debug", "Debug")]
    [DataRow("None", "Debug", null, "Debug", "None")]
    [DataRow("None", null, "Trace", "None", "Trace")]
    [DataRow("Critical", "Trace", "Debug", "Trace", "Debug")]
    [DataRow("Information", "None", "Trace", "None", "Trace")]
    [DataRow("Warning", " ", "\t", "Warning", "Warning")]
    [DataRow("None", null, null, "None", "None")]
    public void OutputLevelsInheritOnlyWhenAbsentAndGlobalLevelIsNotACap(string level, string? console, string? file, string expectedConsole, string expectedFile)
    {
        ConfigurationResolution result = Resolve(new Dictionary<string, string?> { ["CORE_LOG_LEVEL"] = level, ["CORE_LOG_CONSOLE_LEVEL"] = console, ["CORE_LOG_FILE_LEVEL"] = file, ["CORE_LOG_FILE_PATH"] = "relative.log" });
        Assert.AreEqual(expectedConsole, result.ConsoleLevel.Value.ToString());
        Assert.AreEqual(expectedFile, result.FileLevel.Value.ToString());
        Assert.AreEqual(expectedConsole != "None", result.ConsoleEnabled);
        Assert.AreEqual(expectedFile != "None", result.FileEnabled);
        Assert.IsFalse(result.HasParseErrors);
    }

    [TestMethod]
    [DataRow(null, "Debug", false)]
    [DataRow(" \t", "Debug", false)]
    [DataRow("relative.log", "None", false)]
    [DataRow("relative.log", "Debug", true)]
    [DataRow("relative.log", "Trace", true)]
    [DataRow("relative.log", "Critical", true)]
    public void FileEnablementRequiresBothAPathAndAThreshold(string? path, string level, bool expected)
    {
        ConfigurationResolution result = Resolve(new Dictionary<string, string?> { ["CORE_LOG_FILE_PATH"] = path, ["CORE_LOG_FILE_LEVEL"] = level });
        Assert.AreEqual(expected, result.FileEnabled);
    }

    [TestMethod]
    public void InvalidGlobalLevelDoesNotCreateAUsableInheritedFallback()
    {
        ConfigurationResolution blocked = Resolve(new Dictionary<string, string?> { ["CORE_LOG_LEVEL"] = "not-a-level", ["CORE_LOG_FILE_PATH"] = "relative.log" });
        Assert.AreEqual(ConfigurationParseState.InvalidToken, blocked.Level.State);
        Assert.AreEqual(ConfigurationParseState.DependsOnInvalidSetting, blocked.ConsoleLevel.State);
        Assert.AreEqual(ConfigurationParseState.DependsOnInvalidSetting, blocked.FileLevel.State);
        Assert.IsNull(blocked.ConsoleEnabled);
        Assert.IsNull(blocked.FileEnabled);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = blocked.ConsoleLevel.Value);
        ConfigurationResolution independent = Resolve(new Dictionary<string, string?> { ["CORE_LOG_LEVEL"] = "not-a-level", ["CORE_LOG_CONSOLE_LEVEL"] = "Warning", ["CORE_LOG_FILE_LEVEL"] = "Debug", ["CORE_LOG_FILE_PATH"] = "relative.log" });
        Assert.IsTrue(independent.HasParseErrors);
        Assert.AreEqual(ConfigurationSeverity.Warning, independent.ConsoleLevel.Value);
        Assert.AreEqual(ConfigurationSeverity.Debug, independent.FileLevel.Value);
        Assert.AreEqual(true, independent.ConsoleEnabled);
        Assert.AreEqual(true, independent.FileEnabled);
    }

    [TestMethod]
    [DataRow("1.0")]
    [DataRow("1e3")]
    [DataRow("0x10")]
    [DataRow("1_000")]
    [DataRow("1,000")]
    [DataRow("1 2")]
    [DataRow("+")]
    [DataRow("-")]
    [DataRow("++1")]
    [DataRow("--1")]
    [DataRow("+-1")]
    [DataRow("+ 1")]
    [DataRow("NaN")]
    [DataRow("true")]
    [DataRow("\u0661")]
    [DataRow("\uff11\uff12")]
    [DataRow("2\n3")]
    public void EveryIntegerRejectsNonInvariantDecimalGrammar(string token)
    {
        ConfigurationResolution result = Resolve(IntegerKeys.ToDictionary(key => key, _ => (string?)token));
        foreach (string key in IntegerKeys)
        {
            Assert.AreEqual(ConfigurationParseState.InvalidToken, result.Settings.Single(s => s.Key == key).State, key);
        }
    }

    [TestMethod]
    [DataRow("2147483647", "Parsed", "Parsed")]
    [DataRow("-2147483648", "Parsed", "Parsed")]
    [DataRow("2147483648", "OutOfRange", "Parsed")]
    [DataRow("-2147483649", "OutOfRange", "Parsed")]
    [DataRow("9223372036854775808", "OutOfRange", "OutOfRange")]
    [DataRow("-9223372036854775809", "OutOfRange", "OutOfRange")]
    public void NumericDomainOverflowNeverFallsThroughToDefaults(string token, string expectedInt, string expectedLong)
    {
        ConfigurationResolution result = Resolve(new Dictionary<string, string?> { ["CORE_LOG_QUEUE_CAPACITY"] = token, ["CORE_LOG_FILE_MAX_BYTES"] = token });
        Assert.AreEqual(expectedInt, result.QueueCapacity.State.ToString());
        Assert.AreEqual(expectedLong, result.FileMaxBytes.State.ToString());
        Assert.AreEqual(ConfigurationSource.Environment, result.QueueCapacity.Source);
        Assert.AreEqual(ConfigurationSource.Environment, result.FileMaxBytes.Source);
    }

    [TestMethod]
    [DataRow("fr-FR")]
    [DataRow("ar-SA")]
    [DataRow("tr-TR")]
    public void ParsingIsInvariantWhileIdentityRetainsCase(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            ConfigurationResolution result = Resolve(new Dictionary<string, string?> { ["CORE_LOG_QUEUE_CAPACITY"] = "+0002048", ["CORE_LOG_LEVEL"] = "information", ["CORE_SERVICE_NAME"] = " MiXeD.App " });
            Assert.AreEqual(2048, result.QueueCapacity.Value);
            Assert.AreEqual(ConfigurationSeverity.Information, result.Level.Value);
            Assert.AreEqual("MiXeD.App", result.ApplicationName.Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    [DataRow("1", "Parsed")]
    [DataRow("+001", "Parsed")]
    [DataRow("0", "UnsupportedSchemaVersion")]
    [DataRow("-1", "UnsupportedSchemaVersion")]
    [DataRow("2", "UnsupportedSchemaVersion")]
    [DataRow("2147483648", "OutOfRange")]
    public void SchemaSelectionOnlyAcceptsVersionOne(string token, string expected)
    {
        ConfigurationResolution result = Resolve(new Dictionary<string, string?> { ["CORE_LOG_SCHEMA_VERSION"] = token });
        Assert.AreEqual(expected, result.SchemaVersion.State.ToString());
    }

    [TestMethod]
    public void UndefinedTypedEnumsAndUnsupportedTokensStaySelectedErrors()
    {
        var setup = new EnvironmentConfigurationInput { Logging = new LoggingConfigurationInput { Level = (ConfigurationSeverity)999, ConsoleFormat = (ConfigurationOutputFormat)999, ConsoleStream = (ConfigurationConsoleStream)999, HumanDetail = (ConfigurationHumanDetail)999 } };
        ConfigurationResolution result = Resolve(setup: setup);
        foreach (string key in new[] { "CORE_LOG_LEVEL", "CORE_LOG_CONSOLE_FORMAT", "CORE_LOG_CONSOLE_STREAM", "CORE_LOG_HUMAN_DETAIL" })
        {
            Assert.AreEqual(ConfigurationParseState.InvalidToken, result.Settings.Single(s => s.Key == key).State);
            Assert.AreEqual(ConfigurationSource.Setup, result.Settings.Single(s => s.Key == key).Source);
        }

        foreach (string token in new[] { "0", "JsonLines", "xml", "stderr" })
        {
            Assert.AreEqual(ConfigurationParseState.InvalidToken, Resolve(new Dictionary<string, string?> { ["CORE_LOG_CONSOLE_FORMAT"] = token }).ConsoleFormat.State);
        }
    }

    [TestMethod]
    public void MalformedProvidedOptionsAreStillParsedWhenBothOutputsAreDisabled()
    {
        ConfigurationResolution result = Resolve(new Dictionary<string, string?>
        {
            ["CORE_LOG_CONSOLE_LEVEL"] = "None",
            ["CORE_LOG_FILE_LEVEL"] = "None",
            ["CORE_LOG_CONSOLE_FORMAT"] = "xml",
            ["CORE_LOG_FILE_FORMAT"] = "xml",
            ["CORE_LOG_CONSOLE_STREAM"] = "left",
            ["CORE_LOG_HUMAN_DETAIL"] = "wide",
            ["CORE_LOG_FILE_MAX_BYTES"] = "one"
        });
        Assert.AreEqual(false, result.ConsoleEnabled);
        Assert.AreEqual(false, result.FileEnabled);
        Assert.IsTrue(result.HasParseErrors);
        foreach (string key in new[] { "CORE_LOG_CONSOLE_FORMAT", "CORE_LOG_FILE_FORMAT", "CORE_LOG_CONSOLE_STREAM", "CORE_LOG_HUMAN_DETAIL", "CORE_LOG_FILE_MAX_BYTES" })
        {
            Assert.AreEqual(ConfigurationParseState.InvalidToken, result.Settings.Single(s => s.Key == key).State);
        }
    }

    [TestMethod]
    public void ValidHigherPrioritySourcesShadowInvalidTypedSetupAndPreserveAliasProvenance()
    {
        var setup = new EnvironmentConfigurationInput { EnvironmentName = "Host.Framework", ServiceVersion = "Setup.Release", Logging = new LoggingConfigurationInput { Level = (ConfigurationSeverity)999, ConsoleFormat = (ConfigurationOutputFormat)999 } };
        ConfigurationResolution result = Resolve(new Dictionary<string, string?> { ["CORE_LOG_LEVEL"] = "INF", ["CORE_LOG_CONSOLE_FORMAT"] = "human", ["DD_ENV"] = " Deployed.MixedCase ", ["DD_VERSION"] = " Alias.Release " }, setup);
        Assert.IsFalse(result.HasParseErrors);
        Assert.AreEqual("Deployed.MixedCase", result.EnvironmentName.Value);
        Assert.AreEqual("DD_ENV", result.EnvironmentName.SourceKey);
        Assert.AreEqual("Alias.Release", result.ServiceVersion.Value);
        Assert.AreEqual("DD_VERSION", result.ServiceVersion.SourceKey);
        Assert.AreEqual("Host.Framework", setup.EnvironmentName);
        Assert.AreEqual(Application.BuildVersion, result.BuildVersion);
    }

    [TestMethod]
    public void ReusingCapturedInputsOwnsAllSelectedValuesAndCreatesNoFile()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "missing", "relative.log");
        var source = new EnvironmentVariableStub();
        source.Values["CORE_LOG_FILE_PATH"] = path;
        source.Values["CORE_LOG_MAX_ENTRY_BYTES"] = "131072";
        CapturedEnvironment captured = CapturedEnvironment.Capture(source);
        ConfigurationResolution result = ConfigurationResolver.Resolve(new EnvironmentConfigurationInput(), Application, captured);
        source.Values.Clear();
        ConfigurationResolution second = ConfigurationResolver.Resolve(new EnvironmentConfigurationInput(), Application, captured);
        Assert.AreEqual(131072, result.MaxEntryBytes.Value);
        Assert.AreEqual(131072, second.MaxEntryBytes.Value);
        Assert.AreEqual(path, result.FilePath.Value);
        Assert.AreEqual(21, source.Reads.Count);
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(path)));
        Assert.IsFalse(File.Exists(path));
        Assert.IsTrue(((IList<IParsedConfigurationSetting>)result.Settings).IsReadOnly);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<IParsedConfigurationSetting>)result.Settings)[0] = result.Settings[1]);
    }

    [TestMethod]
    public void ParsedValuesDoNotPretendToSatisfyPendingRangesIdentityAndPathValidation()
    {
        ConfigurationResolution result = Resolve(new Dictionary<string, string?> { ["CORE_LOG_QUEUE_CAPACITY"] = "0", ["CORE_LOG_FILE_MAX_BYTES"] = "1", ["CORE_SERVICE_NAME"] = "bad\u0001identity", ["CORE_LOG_FILE_PATH"] = "bad\0path", ["CORE_LOG_MAX_ENTRY_BYTES"] = "1" });
        Assert.AreEqual(0, result.QueueCapacity.Value);
        Assert.AreEqual(1L, result.FileMaxBytes.Value);
        Assert.AreEqual(1, result.MaxEntryBytes.Value);
        Assert.AreEqual("bad\u0001identity", result.ApplicationName.Value);
        Assert.AreEqual("bad\0path", result.FilePath.Value);
        Assert.IsFalse(result.HasParseErrors);
    }

    [TestMethod]
    public void NullInputsFailBeforeResolutionAndAbsentRequiredIdentityStaysAbsent()
    {
        CapturedEnvironment variables = CapturedEnvironment.Capture(new EnvironmentVariableStub());
        Assert.ThrowsExactly<ArgumentNullException>(() => ConfigurationResolver.Resolve(null!, Application, variables));
        Assert.ThrowsExactly<ArgumentNullException>(() => ConfigurationResolver.Resolve(new EnvironmentConfigurationInput(), null!, variables));
        Assert.ThrowsExactly<ArgumentNullException>(() => ConfigurationResolver.Resolve(new EnvironmentConfigurationInput(), Application, null!));
        ConfigurationResolution result = ConfigurationResolver.Resolve(new EnvironmentConfigurationInput(), new ApplicationIdentity(null, "unknown", null, null), variables);
        Assert.AreEqual(ConfigurationParseState.Absent, result.ApplicationName.State);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = result.ApplicationName.Value);
    }

    private static ConfigurationResolution Resolve(Dictionary<string, string?>? values = null, EnvironmentConfigurationInput? setup = null)
    {
        var source = new EnvironmentVariableStub();
        if (values is not null)
        {
            foreach ((string key, string? value) in values)
            {
                source.Values[key] = value;
            }
        }

        return ConfigurationResolver.Resolve(setup ?? new EnvironmentConfigurationInput(), Application, CapturedEnvironment.Capture(source));
    }

    private static string FormatValue(object? value) => value is IFormattable formatted ? formatted.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "<absent>";

    private static object? Value(ConfigurationResolution result, string key) => key switch
    {
        "CORE_SERVICE_NAME" => result.ApplicationName.Value,
        "CORE_ENVIRONMENT" => result.EnvironmentName.Value,
        "CORE_SERVICE_VERSION" => result.ServiceVersion.Value,
        "CORE_LOG_LEVEL" => result.Level.Value,
        "CORE_LOG_CONSOLE_FORMAT" => result.ConsoleFormat.Value,
        "CORE_LOG_CONSOLE_LEVEL" => result.ConsoleLevel.Value,
        "CORE_LOG_CONSOLE_STREAM" => result.ConsoleStream.Value,
        "CORE_LOG_HUMAN_DETAIL" => result.HumanDetail.Value,
        "CORE_LOG_FILE_PATH" => result.FilePath.HasValue ? result.FilePath.Value : null,
        "CORE_LOG_FILE_FORMAT" => result.FileFormat.Value,
        "CORE_LOG_FILE_LEVEL" => result.FileLevel.Value,
        "CORE_LOG_QUEUE_CAPACITY" => result.QueueCapacity.Value,
        "CORE_LOG_FLUSH_INTERVAL_MS" => result.FlushIntervalMilliseconds.Value,
        "CORE_LOG_SHUTDOWN_TIMEOUT_MS" => result.ShutdownTimeoutMilliseconds.Value,
        "CORE_LOG_FILE_MAX_BYTES" => result.FileMaxBytes.Value,
        "CORE_LOG_FILE_RETAINED_FILES" => result.FileRetainedFiles.Value,
        "CORE_LOG_SCHEMA_VERSION" => result.SchemaVersion.Value,
        "CORE_LOG_MAX_ENTRY_BYTES" => result.MaxEntryBytes.Value,
        _ => throw new ArgumentException("Unknown test setting.", nameof(key))
    };
}
