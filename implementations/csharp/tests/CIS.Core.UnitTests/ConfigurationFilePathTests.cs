using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Tests.DiscoveryFixture;
using CIS.Tests.UnitTests.Discovery;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ConfigurationFilePathTests
{
    private static readonly ApplicationIdentity Application = new("Fixture.App", "2.3.4-rc.1+build.8", null, null);
    public static IEnumerable<object[]> ApplicableSharedCases => Read()["cases"]!.AsArray()
        .Where(row => row!["key"]!.GetValue<string>() == "CORE_LOG_FILE_PATH" && Applies(row))
        .Select(row => new object[] { row!["id"]!.GetValue<string>() });

    [TestMethod]
    [DynamicData(nameof(ApplicableSharedCases))]
    public void SharedPathVectorsExecuteForTheirNativePlatform(string id)
    {
        JsonNode row = Read()["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!;
        JsonObject input = row["inputs"]!.AsObject();
        JsonNode before = input.DeepClone();
        var source = new EnvironmentVariableStub();
        foreach ((string key, JsonNode? value) in input["environment"]!.AsObject())
        {
            source.Values[key] = value?.GetValue<string>();
        }
        string initial = input["workingDirectoryAtInitialization"]?.GetValue<string>() ?? SyntheticBase();
        int reads = 0;
        InitializationWorkingDirectory directory = InitializationWorkingDirectory.Capture(() => { reads++; return initial; });
        ConfigurationResolution selected = ConfigurationResolver.Resolve(ConfigurationFixtureAdapter.Setup(input), Application, CapturedEnvironment.Capture(source));
        JsonNode expected = row["expected"]!;
        if (expected["status"]!.GetValue<string>() == "Invalid")
        {
            CoreConfigurationException exception = Assert.ThrowsExactly<CoreConfigurationException>(() => ConfigurationValidator.Validate(selected, directory));
            Assert.AreEqual(1, exception.Issues.Count);
            Assert.AreEqual(ConfigurationIssueCodes.InvalidPath, exception.Issues[0].Code);
            Assert.AreEqual("CORE_LOG_FILE_PATH", exception.Issues[0].Key);
        }
        else
        {
            ValidatedConfiguration result = ConfigurationValidator.Validate(selected, directory);
            string? expectedPath = expected["value"] switch
            {
                null => null,
                JsonValue value => value.GetValue<string>(),
                JsonObject value => NativeExpected(initial, value["input"]!.GetValue<string>()),
                _ => throw new InvalidDataException("Unknown fixture expectation.")
            };
            initial = input["workingDirectoryAfterInitialization"]?.GetValue<string>() ?? SyntheticBase("changed");
            Assert.AreEqual(expectedPath, result.FilePath);
            Assert.AreEqual(expectedPath, result.FilePath);
            Assert.AreEqual(directory.Value, result.WorkingDirectory);
        }
        Assert.AreEqual(1, reads);
        Assert.AreEqual(21, source.Reads.Count);
        Assert.IsTrue(JsonNode.DeepEquals(before, input));
    }

    [TestMethod]
    public void PathFixtureCoverageDeclaresBothPlatformsWithoutClaimingSkippedExecution()
    {
        JsonNode[] rows = Read()["cases"]!.AsArray().Where(row => row!["key"]!.GetValue<string>() == "CORE_LOG_FILE_PATH").Select(row => row!).ToArray();
        Assert.AreEqual(14, rows.Length);
        Assert.AreEqual(13, ApplicableSharedCases.Count());
        Assert.AreEqual(2, rows.Count(row => row["lanes"] is not null));
    }

    [TestMethod]
    [DataRow("logs/app.jsonl", "logs/app.jsonl")]
    [DataRow("./logs/../log.jsonl", "log.jsonl")]
    [DataRow("../up.jsonl", "../up.jsonl")]
    [DataRow("logs/\u03a9-\U0001f642-e\u0301.jsonl", "logs/\u03a9-\U0001f642-e\u0301.jsonl")]
    public void NativeRelativeSyntaxPreservesUnicodeAndDoesNotCreateOutputs(string input, string expectedSuffix)
    {
        using var owned = new TemporaryDirectory();
        InitializationWorkingDirectory directory = InitializationWorkingDirectory.Capture(() => owned.Path);
        ValidatedConfiguration result = ConfigurationValidator.Validate(Resolve(input), directory);
        Assert.AreEqual(NativeExpected(owned.Path, expectedSuffix), result.FilePath);
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }

    [TestMethod]
    public void AbsoluteSyntaxIsIndependentOfCapturedBaseAndDoesNotRequireExistence()
    {
        string absolute = NativeExpected(SyntheticBase("absolute"), "never-created/log.jsonl");
        ValidatedConfiguration result = ConfigurationValidator.Validate(Resolve(absolute), InitializationWorkingDirectory.Capture(() => SyntheticBase()));
        Assert.AreEqual(absolute, result.FilePath);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InvalidSetupPathStillValidatesForDisabledFileOutput(bool disabled)
    {
        ConfigurationResolution selected = Resolve("private" + (char)0 + "path", disabled);
        CoreConfigurationException exception = Assert.ThrowsExactly<CoreConfigurationException>(() => ConfigurationValidator.Validate(selected, InitializationWorkingDirectory.Capture(() => SyntheticBase())));
        Assert.AreEqual(1, exception.Issues.Count);
        Assert.AreEqual(ConfigurationIssueCodes.InvalidPath, exception.Issues[0].Code);
        Assert.AreEqual("LoggingSetup.FilePath", exception.Issues[0].Key);
        Assert.IsFalse(exception.ToString().Contains("private", StringComparison.Ordinal));
        Assert.IsFalse(exception.Issues[0].Explanation.Contains("private", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PathProblemsJoinIndependentIssuesAtTheirCatalogPosition()
    {
        var source = new EnvironmentVariableStub();
        source.Values["CORE_LOG_CONSOLE_FORMAT"] = "invalid";
        source.Values["CORE_LOG_FILE_PATH"] = "bad" + (char)0;
        source.Values["CORE_LOG_FILE_FORMAT"] = "invalid";
        source.Values["CORE_LOG_QUEUE_CAPACITY"] = "0";
        source.Values["CORE_LOG_FILE_LEVEL"] = "None";
        ConfigurationResolution selected = ConfigurationResolver.Resolve(new(), Application, CapturedEnvironment.Capture(source));
        CoreConfigurationException exception = Assert.ThrowsExactly<CoreConfigurationException>(() => ConfigurationValidator.Validate(selected, InitializationWorkingDirectory.Capture(() => SyntheticBase())));
        CollectionAssert.AreEqual(new[] { "CORE_LOG_CONSOLE_FORMAT", "CORE_LOG_FILE_PATH", "CORE_LOG_FILE_FORMAT", "CORE_LOG_QUEUE_CAPACITY" }, exception.Issues.Select(issue => issue.Key).ToArray());
    }

    [TestMethod]
    public void RequiredDirectorySourceRunsOnceAndItsFailuresPropagate()
    {
        int reads = 0;
        string current = SyntheticBase();
        InitializationWorkingDirectory captured = InitializationWorkingDirectory.Capture(() => { reads++; return current; });
        current = SyntheticBase("changed");
        ValidatedConfiguration result = ConfigurationValidator.Validate(Resolve("log.jsonl"), captured);
        Assert.AreEqual(NativeExpected(SyntheticBase(), "log.jsonl"), result.FilePath);
        Assert.AreEqual(1, reads);
        var expected = new IOException("controlled directory source failure");
        Assert.AreSame(expected, Assert.ThrowsExactly<IOException>(() => InitializationWorkingDirectory.Capture(() => throw expected)));
        Assert.ThrowsExactly<OperationCanceledException>(() => InitializationWorkingDirectory.Capture(() => throw new OperationCanceledException()));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("relative")]
    [DataRow(" ")]
    public void InvalidInjectedDirectoryIsARequiredSourceFailure(string value)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => InitializationWorkingDirectory.Capture(() => value));
    }

    [TestMethod]
    public void NativeCaptureAndArgumentGuardsAreExplicit()
    {
        Assert.AreEqual(Directory.GetCurrentDirectory(), InitializationWorkingDirectory.Capture().Value);
        InitializationWorkingDirectory directory = InitializationWorkingDirectory.Capture(() => SyntheticBase());
        Assert.ThrowsExactly<ArgumentNullException>(() => ConfigurationValidator.Validate(null!, directory));
        Assert.ThrowsExactly<ArgumentNullException>(() => ConfigurationValidator.Validate(Resolve(null), null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ConfigurationFilePathResolution.Resolve(null!, directory));
        Assert.ThrowsExactly<ArgumentNullException>(() => ConfigurationFilePathResolution.Resolve(Resolve(null).FilePath, null!));
    }


    [TestMethod]
    [DataRow("logs/~app.jsonl")]
    [DataRow("logs/\ue000-\ue001-~app.jsonl")]
    [DataRow("./~logs/../~app.jsonl")]
    public void LiteralTildesAndUnicodeMarkerCharactersRemainUnchanged(string input)
    {
        string initial = SyntheticBase("~initial");
        InitializationWorkingDirectory directory = InitializationWorkingDirectory.Capture(() => initial);
        ValidatedConfiguration result = ConfigurationValidator.Validate(Resolve(input), directory);
        Assert.AreEqual(initial, directory.Value);
        Assert.AreEqual(NativeExpected(initial, input), result.FilePath);
    }

    [TestMethod]
    public void AbsoluteTildePathsPreserveLiteralTextIndependentlyOfTheBase()
    {
        string absolute = NativeExpected(SyntheticBase("~absolute"), "\ue000/~never-created/log.jsonl");
        ValidatedConfiguration result = ConfigurationValidator.Validate(Resolve(absolute), InitializationWorkingDirectory.Capture(() => SyntheticBase("~initial")));
        Assert.AreEqual(absolute, result.FilePath);
    }

    [TestMethod]
    public void PrivateUseMarkerExhaustionStillPreservesLiteralCharacters()
    {
        string privateUse = string.Concat(Enumerable.Range(0xe000, 0xfffd - 0xe000 + 1).Select(value => (char)value));
        string initial = SyntheticBase("~initial");
        string input = "~" + privateUse + ".jsonl";
        ValidatedConfiguration result = ConfigurationValidator.Validate(Resolve(input), InitializationWorkingDirectory.Capture(() => initial));
        Assert.AreEqual(NativeExpected(initial, input), result.FilePath);
    }

    private static bool Applies(JsonNode row) => row["lanes"] is not JsonArray lanes || lanes.Any(lane => lane!.GetValue<string>() == (OperatingSystem.IsWindows() ? "windows-x64" : "linux-ubuntu2404-x64"));
    private static string SyntheticBase(string name = "initial") => OperatingSystem.IsWindows() ? "C:\\cislib-path-fixtures\\" + name : "/cislib-path-fixtures/" + name;
    private static string NativeExpected(string directory, string relative)
    {
        // Expectations for these simple declared segments use independent join/collapse logic.
        var parts = directory.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (string part in relative.Split('/'))
        {
            if (part == "..")
            {
                parts.RemoveAt(parts.Count - 1);
            }
            else if (part is not ("." or ""))
            {
                parts.Add(part);
            }
        }
        return OperatingSystem.IsWindows() ? string.Join('\\', parts) : "/" + string.Join('/', parts);
    }
    private static ConfigurationResolution Resolve(string? filePath, bool disabled = false) => ConfigurationResolver.Resolve(new() { Logging = new() { FilePath = filePath, FileLevel = disabled ? CIS.Logging.Configuration.ConfigurationSeverity.None : null } }, Application, CapturedEnvironment.Capture(new EnvironmentVariableStub()));
    private static JsonObject Read() => ConfigurationFixtureAdapter.ReadDocument(Path.Combine(Directory.GetParent(ConfigurationFixtureAdapter.FindFixture())!.FullName, "configuration-cases.json"));
}
