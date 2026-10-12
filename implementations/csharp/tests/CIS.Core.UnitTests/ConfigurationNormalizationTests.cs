using System.Globalization;
using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Logging;
using CIS.Tests.DiscoveryFixture;
using CIS.Tests.UnitTests.Discovery;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class ConfigurationNormalizationTests
{
    private static readonly string[] Cultures = ["en-US", "tr-TR", "ar-SA"];
    public static IEnumerable<object?[]> BlankValues => new string?[] { null, "", " ", "\t\r\n", "\u00a0", "\u2003\u202f", "\u3000" }.Select(value => new object?[] { value });

    [TestMethod]
    [DynamicData(nameof(BlankValues))]
    public void BlankCoreValuesUseTypedSetupForEverySetting(string? blank)
    {
        JsonObject input = ConfigurationFixtureAdapter.Case(Read(), "CFG-RESOLVE-SETUP-ALL")["inputs"]!.DeepClone().AsObject();
        foreach (JsonNode? setting in Contract()["settings"]!.AsArray())
        {
            input["environment"]![setting!["key"]!.GetValue<string>()] = blank;
        }
        using var directory = new TemporaryDirectory();
        (PreparedConfiguration? snapshot, CoreConfigurationException? error) = ConfigurationValidationAdapter.Execute(input, () => directory.Path);
        Assert.IsNull(error);
        foreach ((string name, JsonNode? expected) in input["setup"]!.AsObject())
        {
            JsonNode? actual = ConfigurationFixtureAdapter.Values(snapshot!)[name];
            if (name == "Logging.FilePath")
            {
                Assert.AreEqual(Path.Combine(directory.Path, expected!.GetValue<string>().Trim().Replace('/', Path.DirectorySeparatorChar)), snapshot!.Logging.FilePath);
            }
            else
            {
                Assert.IsTrue(JsonNode.DeepEquals(expected, actual), name);
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(BlankValues))]
    public void BlankAliasesAndSetupTextUseDiscoveryOrDefaults(string? blank)
    {
        EnvironmentStoreTestData.AssignValidFacts();
        // Caller supplies discovered facts before the helper applies only nonblank overrides.
        EnvironmentStoreTestData.Values.ApplicationName = "Discovered.App";
        EnvironmentStoreTestData.Values.BuildVersion = "discovered-build";
        EnvironmentStoreTestData.Values.ServiceVersion = EnvironmentStoreTestData.Values.BuildVersion;
        EnvironmentStoreTestData.Values.Logging.FilePath = blank;
        ConfigurationFixtureAdapter.ApplyOverrides(_ => blank);
        PreparedConfiguration snapshot = EnvironmentStoreTestData.Observe();
        Assert.AreEqual("Discovered.App", snapshot.ApplicationName);
        Assert.AreEqual("unknown", snapshot.EnvironmentName);
        Assert.AreEqual("discovered-build", snapshot.ServiceVersion);
        Assert.IsNull(snapshot.Logging.FilePath);
        Assert.IsFalse(snapshot.Logging.FileEnabled);
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
                    PreparedConfiguration result = Create(source);
                    Assert.AreEqual(expected, TokenValue(result, key), culture);
                    Assert.AreEqual("Identity.\u0130.I.\u0131.i", result.ApplicationName);
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
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => Create(source));
        Assert.AreEqual(ConfigurationIssueCodes.InvalidToken, error.Issues.Single().Code);
        Assert.AreEqual(key, error.Issues.Single().Key);
    }

    [TestMethod]
    [DataRow("\u200b")]
    [DataRow("\ufeff")]
    public void NonWhitespaceFormatCharactersRemainSelectedText(string text)
    {
        var source = new EnvironmentVariableStub();
        source.Values["CORE_SERVICE_NAME"] = text;
        source.Values["CORE_LOG_FILE_PATH"] = text;
        PreparedConfiguration result = Create(source);
        Assert.AreEqual(text, result.ApplicationName);
        Assert.AreEqual(Path.Combine(Path.GetTempPath(), text), result.Logging.FilePath);
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
        var source = new EnvironmentVariableStub { FailingName = unknown, Failure = new InvalidOperationException("unknown value") };
        source.Values[unknown] = "unknown-value-must-not-be-read-or-dumped";
        PreparedConfiguration snapshot = Create(source);
        Assert.IsFalse(source.Reads.Contains(unknown, StringComparer.Ordinal));
        Assert.AreEqual(CoreLogLevel.Information, snapshot.Logging.Level);
    }

    [TestMethod]
    [DataRow("core")]
    [DataRow("alias")]
    [DataRow("setup")]
    public void IdentityLayersPreserveTrimmedCaseAndUnicodeSequences(string layer)
    {
        string[] raw = ["\u2003 App.\u0130.I.\u0131.i.A\u030a \U0001f680 \u00a0", "\t Host.Mixed CASE.\u00d6 \u3000", "\u202f Release.RC+Sha.MixedCASE.A\u030a \n"];
        var source = new EnvironmentVariableStub();
        EnvironmentStoreTestData.AssignValidFacts();
        EnvironmentStoreTestData.Values.ApplicationName = raw[0].Trim();
        EnvironmentStoreTestData.Values.EnvironmentName = raw[1].Trim();
        EnvironmentStoreTestData.Values.ServiceVersion = raw[2].Trim();
        EnvironmentStoreTestData.Values.BuildVersion = "Actual.MixedBuild";
        string[] keys = layer == "core" ? ["CORE_SERVICE_NAME", "CORE_ENVIRONMENT", "CORE_SERVICE_VERSION"] : ["DD_SERVICE", "DD_ENV", "DD_VERSION"];
        if (layer != "setup")
        {
            for (int index = 0; index < keys.Length; index++)
            {
                source.Values[keys[index]] = raw[index];
            }
        }
        ConfigurationFixtureAdapter.ApplyOverrides(source.GetValue);
        PreparedConfiguration snapshot = EnvironmentStoreTestData.Observe();
        Assert.AreEqual(raw[0].Trim(), snapshot.ApplicationName);
        Assert.AreEqual(raw[1].Trim(), snapshot.EnvironmentName);
        Assert.AreEqual(raw[2].Trim(), snapshot.ServiceVersion);
        Assert.AreEqual("Actual.MixedBuild", snapshot.BuildVersion);
    }

    private static PreparedConfiguration Create(EnvironmentVariableStub source)
    {
        EnvironmentStoreTestData.AssignValidFacts();
        ConfigurationFixtureAdapter.ApplyOverrides(source.GetValue);
        return EnvironmentStoreTestData.Observe();
    }
    private static JsonObject Read() => ConfigurationFixtureAdapter.ReadDocument(ConfigurationFixtureAdapter.FindFixture());
    private static JsonObject Contract() => ConfigurationFixtureAdapter.ReadDocument(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "..", "..", "contracts", "logging", "v1", "configuration.json"));
    private static string TokenValue(PreparedConfiguration snapshot, string key) => key switch
    {
        "CORE_LOG_LEVEL" => snapshot.Logging.Level.ToString(),
        "CORE_LOG_CONSOLE_LEVEL" => snapshot.Logging.EffectiveConsoleLevel.ToString(),
        "CORE_LOG_FILE_LEVEL" => snapshot.Logging.EffectiveFileLevel.ToString(),
        "CORE_LOG_CONSOLE_FORMAT" => snapshot.Logging.ConsoleFormat.ToString(),
        "CORE_LOG_FILE_FORMAT" => snapshot.Logging.FileFormat.ToString(),
        "CORE_LOG_CONSOLE_STREAM" => snapshot.Logging.ConsoleStream.ToString(),
        "CORE_LOG_HUMAN_DETAIL" => snapshot.Logging.HumanDetail.ToString(),
        _ => throw new ArgumentException("Unknown test setting.", nameof(key))
    };
}
