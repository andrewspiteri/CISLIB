using System.Globalization;
using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Logging.Configuration;
using CIS.Tests.DiscoveryFixture;
using CIS.Tests.UnitTests.Discovery;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ConfigurationOptionValidationTests
{
    private static readonly ApplicationIdentity Application = new("Fixture.App", "2.3.4-rc.1+build.8", null, null);
    private static readonly (string Key, long Minimum, long Maximum)[] Bounds =
    [
        ("CORE_LOG_QUEUE_CAPACITY", 1, 65536), ("CORE_LOG_FLUSH_INTERVAL_MS", 10, 60000),
        ("CORE_LOG_SHUTDOWN_TIMEOUT_MS", 1, 300000), ("CORE_LOG_FILE_MAX_BYTES", 65536, 1073741824),
        ("CORE_LOG_FILE_RETAINED_FILES", 1, 1000), ("CORE_LOG_SCHEMA_VERSION", 1, 1),
        ("CORE_LOG_MAX_ENTRY_BYTES", 16384, 16777216)
    ];
    public static IEnumerable<object[]> SharedOptions => ReadCases()["cases"]!.AsArray()
        .Where(row => Text(row!, "key").StartsWith("CORE_LOG_", StringComparison.Ordinal) && Text(row!, "key") != "CORE_LOG_FILE_PATH")
        .Select(row => new object[] { Text(row!, "id") });
    public static IEnumerable<object[]> SharedCrossOptions => ReadCases()["crossOptionCases"]!.AsArray().Select(row => new object[] { Text(row!, "id") });
    public static IEnumerable<object[]> TypedBounds => Bounds.SelectMany(bound => new[]
    {
        new object[] { bound.Key, bound.Minimum, true }, new object[] { bound.Key, bound.Maximum, true },
        new object[] { bound.Key, bound.Minimum - 1, false }, new object[] { bound.Key, bound.Maximum + 1, false }
    });

    [TestMethod]
    [DynamicData(nameof(SharedOptions))]
    public void SharedLoggingOptionVectorsExecuteAgainstTheRealValidator(string id)
    {
        JsonObject row = ReadCases()["cases"]!.AsArray().Single(row => Text(row!, "id") == id)!.AsObject();
        JsonObject input = row["inputs"]!.AsObject();
        JsonNode before = input.DeepClone();
        var source = new EnvironmentVariableStub();
        foreach ((string key, JsonNode? value) in input["environment"]!.AsObject())
        {
            source.Values[key] = value?.GetValue<string>();
        }
        JsonObject discovery = input["discovery"]!.AsObject();
        var application = new ApplicationIdentity(Text(discovery, "applicationName"), ConfigurationFixtureAdapter.Text(discovery, "buildVersion") ?? "unknown", null, null);
        ConfigurationResolution result = ConfigurationResolver.Resolve(ConfigurationFixtureAdapter.Setup(input), application, CapturedEnvironment.Capture(source));
        IReadOnlyList<ConfigurationProblem> issues = ConfigurationOptionValidator.Validate(result);
        JsonNode expected = row["expected"]!;
        if (Text(expected, "status") == "Invalid")
        {
            string[] expectedPairs = expected["issues"] is JsonArray many ? ExpectedIssues(many)
                : [Text(expected, "code") + ":" + (input["environment"]![Text(row, "key")] is not null ? Text(row, "key") : ConfigurationSettings.Find(Text(row, "key")).SetupKey)];
            CollectionAssert.AreEquivalent(expectedPairs, IssuePairs(issues), id);
        }
        else
        {
            Assert.AreEqual(0, issues.Count, id);
            JsonNode? value = ConfigurationFixtureAdapter.Project(result)["settings"]!.AsArray().Single(setting => Text(setting!, "key") == Text(row, "key"))!["value"];
            Assert.IsTrue(JsonNode.DeepEquals(expected["value"], value), id);
        }
        Assert.IsTrue(JsonNode.DeepEquals(before, input), id);
        Assert.AreEqual(21, source.Reads.Count);
    }

    [TestMethod]
    [DynamicData(nameof(SharedCrossOptions))]
    public void SharedCrossOptionVectorsValidateWithoutOutputSideEffects(string id)
    {
        JsonNode row = ReadCases()["crossOptionCases"]!.AsArray().Single(row => Text(row!, "id") == id)!;
        JsonObject input = row["inputs"]!.AsObject();
        var source = new EnvironmentVariableStub();
        foreach ((string key, JsonNode? value) in input)
        {
            source.Values[key] = value?.GetValue<string>();
        }
        var setup = new EnvironmentConfigurationInput { EnvironmentName = ConfigurationFixtureAdapter.Text(input, "hostEnvironment") };
        ConfigurationResolution result = Resolve(setup, source);
        IReadOnlyList<ConfigurationProblem> issues = ConfigurationOptionValidator.Validate(result);
        string[] expected = row["expectedIssues"] is JsonArray many ? ExpectedIssues(many)
            : row["expectedIssue"] is { } one ? [Text(one, "code") + ":" + Text(one, "key")] : [];
        // The older multiple-error fixture declares membership; catalog order is checked below.
        CollectionAssert.AreEquivalent(expected, IssuePairs(issues), id);
        switch (id)
        {
            case "CFG-INDEPENDENT-LEVELS":
                Assert.AreEqual(ConfigurationSeverity.Information, result.Level.Value);
                Assert.AreEqual(ConfigurationSeverity.Warning, result.ConsoleLevel.Value);
                Assert.AreEqual(ConfigurationSeverity.Debug, result.FileLevel.Value);
                Assert.AreEqual(true, result.FileEnabled);
                break;
            case "CFG-HOST-ENVIRONMENT-ISOLATION":
                Assert.AreEqual("live", result.EnvironmentName.Value);
                Assert.AreEqual("Production", setup.EnvironmentName);
                break;
            case "CFG-FILE-DISABLED-ENTRY-SIZE":
                Assert.AreEqual(131072, result.MaxEntryBytes.Value);
                Assert.AreEqual(false, result.FileEnabled);
                break;
        }
        Assert.IsFalse(source.Reads.Any(name => name == "UNRELATED_PASSWORD" || name == "CORE_FUTURE_OPTION" || name == "hostEnvironment"));
        if (row["diagnosticsMustExclude"] is JsonArray excluded)
        {
            string diagnostic = string.Join("|", issues.Select(issue => issue.Code + issue.Key + issue.Explanation));
            foreach (JsonNode? value in excluded)
            {
                Assert.IsFalse(diagnostic.Contains(value!.GetValue<string>(), StringComparison.Ordinal));
            }
        }
    }

    [TestMethod]
    public void ValidatorRangesAndVectorCoverageMatchThePortableCatalog()
    {
        JsonArray catalog = ConfigurationFixtureAdapter.ReadDocument(Path.Combine(Root(), "contracts", "logging", "v1", "configuration.json"))["settings"]!.AsArray();
        JsonNode[] numeric = catalog.Where(setting => Text(setting!, "kind") == "integer").Select(setting => setting!).ToArray();
        Assert.AreEqual(7, numeric.Length);
        Assert.AreEqual(276, SharedOptions.Count());
        Assert.AreEqual(9, SharedCrossOptions.Count());
        foreach (JsonNode setting in numeric)
        {
            ConfigurationIntegerRange range = ConfigurationIntegerRanges.All.Single(range => range.Key == Text(setting, "key"));
            Assert.AreEqual(setting["minimum"]!.GetValue<long>(), range.Minimum);
            Assert.AreEqual(setting["maximum"]!.GetValue<long>(), range.Maximum);
        }
        CollectionAssert.AreEquivalent(numeric.Select(setting => Text(setting, "key")).ToArray(), Bounds.Select(bound => bound.Key).ToArray());
        JsonObject kinds = ConfigurationFixtureAdapter.ReadDocument(Path.Combine(Root(), "implementations", "csharp", "verification", "contracts", "startup", "surface.json"))["enums"]!["CIS.EnvironmentKind"]!.AsObject();
        Assert.AreEqual(kinds.Count, Enum.GetValues<ConfigurationEnvironmentKind>().Length);
        foreach ((string name, JsonNode? value) in kinds)
        {
            Assert.AreEqual(value!.GetValue<int>(), (int)Enum.Parse<ConfigurationEnvironmentKind>(name));
        }
        Assert.AreEqual(14, ReadCases()["cases"]!.AsArray().Where(row => Text(row!, "key").StartsWith("CORE_LOG_", StringComparison.Ordinal) && Text(row!, "key") != "CORE_LOG_FILE_PATH").Select(row => Text(row!, "key")).Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    [DynamicData(nameof(TypedBounds))]
    public void TypedSetupUsesTheSameInclusiveNumericBounds(string key, long value, bool valid)
    {
        ConfigurationResolution result = Resolve(NumericSetup(key, value));
        IReadOnlyList<ConfigurationProblem> issues = ConfigurationOptionValidator.Validate(result);
        Assert.AreEqual(valid ? 0 : 1, issues.Count);
        if (!valid)
        {
            Assert.AreEqual(key == "CORE_LOG_SCHEMA_VERSION" ? "UnsupportedSchemaVersion" : "OutOfRange", issues[0].Code);
            Assert.AreEqual(ConfigurationSettings.Find(key).SetupKey, issues[0].Key);
        }
    }

    [TestMethod]
    [DataRow("level")]
    [DataRow("consoleLevel")]
    [DataRow("fileLevel")]
    [DataRow("consoleFormat")]
    [DataRow("fileFormat")]
    [DataRow("stream")]
    [DataRow("detail")]
    public void UndefinedTypedEnumsAreInvalidEvenForDisabledOutputs(string option)
    {
        LoggingConfigurationInput logging = option switch
        {
            "level" => new() { Level = (ConfigurationSeverity)99 },
            "consoleLevel" => new() { ConsoleLevel = (ConfigurationSeverity)99 },
            "fileLevel" => new() { FileLevel = (ConfigurationSeverity)99 },
            "consoleFormat" => new() { ConsoleLevel = ConfigurationSeverity.None, ConsoleFormat = (ConfigurationOutputFormat)99 },
            "fileFormat" => new() { FileLevel = ConfigurationSeverity.None, FileFormat = (ConfigurationOutputFormat)99 },
            "stream" => new() { ConsoleLevel = ConfigurationSeverity.None, ConsoleStream = (ConfigurationConsoleStream)99 },
            "detail" => new() { ConsoleLevel = ConfigurationSeverity.None, HumanDetail = (ConfigurationHumanDetail)99 },
            _ => throw new InvalidDataException("Unknown test option.")
        };
        IReadOnlyList<ConfigurationProblem> issues = ConfigurationOptionValidator.Validate(Resolve(new() { Logging = logging }));
        Assert.AreEqual(1, issues.Count);
        Assert.AreEqual("InvalidToken", issues[0].Code);
        Assert.IsTrue(issues[0].Key.StartsWith("LoggingSetup.", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(0, true)]
    [DataRow(1, true)]
    [DataRow(2, true)]
    [DataRow(3, true)]
    [DataRow(4, true)]
    [DataRow(-1, false)]
    [DataRow(5, false)]
    [DataRow(int.MaxValue, false)]
    public void EnvironmentKindAcceptsOnlyDefinedSetupValues(int kind, bool valid)
    {
        ConfigurationResolution result = Resolve(new() { EnvironmentKind = (ConfigurationEnvironmentKind)kind });
        Assert.AreEqual(kind, (int)result.EnvironmentKind);
        IReadOnlyList<ConfigurationProblem> issues = ConfigurationOptionValidator.Validate(result);
        Assert.AreEqual(valid ? 0 : 1, issues.Count);
        if (!valid)
        {
            Assert.AreEqual("InvalidEnvironmentKind", issues[0].Code);
            Assert.AreEqual("EnvironmentSetup.EnvironmentKind", issues[0].Key);
        }
    }

    [TestMethod]
    public void EnvironmentKindIsUnknownUnlessSetupSuppliesIt()
    {
        var source = new EnvironmentVariableStub();
        source.Values["CORE_ENVIRONMENT"] = "Production";
        source.Values["CORE_ENVIRONMENT_KIND"] = "Web";
        ConfigurationResolution result = Resolve(new() { EnvironmentName = "Service" }, source);
        Assert.AreEqual(ConfigurationEnvironmentKind.Unknown, result.EnvironmentKind);
        Assert.AreEqual("Production", result.EnvironmentName.Value);
        Assert.IsFalse(source.Reads.Contains("CORE_ENVIRONMENT_KIND", StringComparer.Ordinal));
        Assert.AreEqual(0, ConfigurationOptionValidator.Validate(result).Count);
    }

    [TestMethod]
    [DataRow(true, 65536L, 131072, 1)]
    [DataRow(true, 131072L, 131072, 0)]
    [DataRow(true, 262144L, 131072, 0)]
    [DataRow(false, 65536L, 131072, 0)]
    [DataRow(true, 1L, 131072, 1)]
    [DataRow(true, 65536L, 16777217, 1)]
    public void EnabledFileSizeRelationshipAvoidsDuplicateRangeErrors(bool enabled, long fileBytes, int entryBytes, int count)
    {
        using var directory = new TemporaryDirectory();
        string filePath = Path.Combine(directory.Path, "never-created", "log.jsonl");
        var setup = new EnvironmentConfigurationInput { Logging = new() { FilePath = filePath, FileLevel = enabled ? ConfigurationSeverity.Debug : ConfigurationSeverity.None, FileMaxBytes = fileBytes, MaxEntryBytes = entryBytes } };
        IReadOnlyList<ConfigurationProblem> issues = ConfigurationOptionValidator.Validate(Resolve(setup));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(directory.Path).Count());
        Assert.AreEqual(count, issues.Count);
        if (count != 0)
        {
            Assert.AreEqual("OutOfRange", issues[0].Code);
            Assert.AreEqual(entryBytes > 16777216 ? "LoggingSetup.MaxEntryBytes" : "LoggingSetup.FileMaxBytes", issues[0].Key);
        }
    }

    [TestMethod]
    [DataRow("CORE_LOG_FILE_MAX_BYTES", "invalid", "InvalidToken")]
    [DataRow("CORE_LOG_MAX_ENTRY_BYTES", "invalid", "InvalidToken")]
    [DataRow("CORE_LOG_FILE_MAX_BYTES", "999999999999999999999", "OutOfRange")]
    [DataRow("CORE_LOG_MAX_ENTRY_BYTES", "999999999999999999999", "OutOfRange")]
    public void UnparsedSizesDoNotCreateCascadingRelationshipProblems(string key, string token, string code)
    {
        var source = new EnvironmentVariableStub();
        source.Values[key] = token;
        ConfigurationResolution result = Resolve(new() { Logging = new() { FilePath = "never-created/log.jsonl", FileMaxBytes = 65536, MaxEntryBytes = 131072 } }, source);
        IReadOnlyList<ConfigurationProblem> issues = ConfigurationOptionValidator.Validate(result);
        Assert.AreEqual(1, issues.Count);
        Assert.AreEqual(code, issues[0].Code);
        Assert.AreEqual(key, issues[0].Key);
    }

    [TestMethod]
    [DataRow(60000, 1)]
    [DataRow(10, 300000)]
    public void FlushAndShutdownRangesRemainIndependent(int flush, int shutdown)
    {
        Assert.AreEqual(0, ConfigurationOptionValidator.Validate(Resolve(new() { Logging = new() { FlushIntervalMilliseconds = flush, ShutdownTimeoutMilliseconds = shutdown } })).Count);
    }

    [TestMethod]
    public void IndependentProblemsUseCatalogOrderThenKindAndSafeExplanations()
    {
        var source = new EnvironmentVariableStub();
        foreach (string key in new[] { "CORE_LOG_LEVEL", "CORE_LOG_CONSOLE_FORMAT", "CORE_LOG_FILE_FORMAT" })
        {
            source.Values[key] = "sensitive-selected-token";
        }
        source.Values["CORE_LOG_QUEUE_CAPACITY"] = "0";
        source.Values["CORE_LOG_FILE_MAX_BYTES"] = "1";
        source.Values["CORE_LOG_SCHEMA_VERSION"] = "2";
        source.Values["CORE_LOG_MAX_ENTRY_BYTES"] = "1";
        source.Values["UNRELATED_PASSWORD"] = "secret-must-stay-unread";
        ConfigurationResolution result = Resolve(new() { EnvironmentKind = (ConfigurationEnvironmentKind)99, Logging = new() { FilePath = "private-path/no-output.log" } }, source);
        IReadOnlyList<ConfigurationProblem> issues = ConfigurationOptionValidator.Validate(result);
        CollectionAssert.AreEqual(new[] { "CORE_LOG_LEVEL", "CORE_LOG_CONSOLE_FORMAT", "CORE_LOG_FILE_FORMAT", "CORE_LOG_QUEUE_CAPACITY", "CORE_LOG_FILE_MAX_BYTES", "CORE_LOG_SCHEMA_VERSION", "CORE_LOG_MAX_ENTRY_BYTES", "EnvironmentSetup.EnvironmentKind" }, issues.Select(issue => issue.Key).ToArray());
        string diagnostic = string.Join("|", issues.Select(issue => issue.Code + issue.Key + issue.Explanation));
        foreach (string secret in new[] { "sensitive-selected-token", "private-path", "secret-must-stay-unread", "UNRELATED_PASSWORD" })
        {
            Assert.IsFalse(diagnostic.Contains(secret, StringComparison.Ordinal));
        }
        Assert.IsTrue(issues.All(issue => !string.IsNullOrWhiteSpace(issue.Explanation)));
        Assert.IsFalse(source.Reads.Contains("UNRELATED_PASSWORD", StringComparer.Ordinal));
    }

    [TestMethod]
    public void InvalidInheritedThresholdReportsItsParentOnce()
    {
        var source = new EnvironmentVariableStub();
        source.Values["CORE_LOG_LEVEL"] = "invalid";
        ConfigurationResolution result = Resolve(new(), source);
        Assert.AreEqual(ConfigurationParseState.DependsOnInvalidSetting, result.ConsoleLevel.State);
        Assert.AreEqual(ConfigurationParseState.DependsOnInvalidSetting, result.FileLevel.State);
        CollectionAssert.AreEqual(new[] { "InvalidToken:CORE_LOG_LEVEL" }, IssuePairs(ConfigurationOptionValidator.Validate(result)));
    }

    [TestMethod]
    [DataRow("en-US")]
    [DataRow("tr-TR")]
    [DataRow("ar-SA")]
    public void ValidationDoesNotDependOnCurrentCulture(string cultureName)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var source = new EnvironmentVariableStub();
            source.Values["CORE_LOG_QUEUE_CAPACITY"] = " +65536 ";
            source.Values["CORE_LOG_FLUSH_INTERVAL_MS"] = "10";
            source.Values["CORE_LOG_FILE_MAX_BYTES"] = "65536";
            Assert.AreEqual(0, ConfigurationOptionValidator.Validate(Resolve(new(), source)).Count);
            source.Values["CORE_LOG_QUEUE_CAPACITY"] = "1,000";
            source.Values["CORE_LOG_FILE_MAX_BYTES"] = "65535";
            IReadOnlyList<ConfigurationProblem> issues = ConfigurationOptionValidator.Validate(Resolve(new(), source));
            CollectionAssert.AreEqual(new[] { "InvalidToken:CORE_LOG_QUEUE_CAPACITY", "OutOfRange:CORE_LOG_FILE_MAX_BYTES" }, IssuePairs(issues));
            Assert.AreEqual("Use an integer between 65536 and 1073741824, inclusive.", issues[1].Explanation);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    public void ProblemCollectionOwnsItsResultsAndRejectsMutationAndNullInput()
    {
        var source = new EnvironmentVariableStub();
        source.Values["CORE_LOG_QUEUE_CAPACITY"] = "0";
        IReadOnlyList<ConfigurationProblem> issues = ConfigurationOptionValidator.Validate(Resolve(new(), source));
        source.Values["CORE_LOG_QUEUE_CAPACITY"] = "1024";
        Assert.AreEqual(1, issues.Count);
        Assert.AreEqual(0, ConfigurationOptionValidator.Validate(Resolve(new(), source)).Count);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ConfigurationProblem>)issues).Clear());
        Assert.ThrowsExactly<ArgumentNullException>(() => ConfigurationOptionValidator.Validate(null!));
    }

    [TestMethod]
    public void OptionPhaseLeavesIdentityAndPathValidationForTheirOwnTasks()
    {
        ConfigurationResolution result = Resolve(new() { ApplicationName = "control" + (char)1, Logging = new() { FilePath = "nul" + (char)0 + "path" } });
        Assert.AreEqual(0, ConfigurationOptionValidator.Validate(result).Count);
    }

    private static EnvironmentConfigurationInput NumericSetup(string key, long value) => new()
    {
        Logging = key switch
        {
            "CORE_LOG_QUEUE_CAPACITY" => new() { QueueCapacity = checked((int)value) },
            "CORE_LOG_FLUSH_INTERVAL_MS" => new() { FlushIntervalMilliseconds = checked((int)value) },
            "CORE_LOG_SHUTDOWN_TIMEOUT_MS" => new() { ShutdownTimeoutMilliseconds = checked((int)value) },
            "CORE_LOG_FILE_MAX_BYTES" => new() { FileMaxBytes = value },
            "CORE_LOG_FILE_RETAINED_FILES" => new() { FileRetainedFiles = checked((int)value) },
            "CORE_LOG_SCHEMA_VERSION" => new() { SchemaVersion = checked((int)value) },
            "CORE_LOG_MAX_ENTRY_BYTES" => new() { MaxEntryBytes = checked((int)value) },
            _ => throw new InvalidDataException("Unknown test numeric setting.")
        }
    };
    private static ConfigurationResolution Resolve(EnvironmentConfigurationInput setup, EnvironmentVariableStub? source = null) =>
        ConfigurationResolver.Resolve(setup, Application, CapturedEnvironment.Capture(source ?? new EnvironmentVariableStub()));
    private static string[] IssuePairs(IReadOnlyList<ConfigurationProblem> issues) => issues.Select(issue => issue.Code + ":" + issue.Key).ToArray();
    private static string[] ExpectedIssues(JsonArray issues) => issues.Select(issue => Text(issue!, "code") + ":" + Text(issue!, "key")).ToArray();
    private static string Text(JsonNode node, string key) => ConfigurationFixtureAdapter.Text(node, key)!;
    private static string Root() => Directory.GetParent(Directory.GetParent(Directory.GetParent(ConfigurationFixtureAdapter.FindFixture())!.FullName)!.FullName)!.FullName;
    private static JsonObject ReadCases() => ConfigurationFixtureAdapter.ReadDocument(Path.Combine(Root(), "conformance", "logging", "configuration-cases.json"));
}
