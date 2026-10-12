using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Logging;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class ConfigurationFilePathTests
{
    public static IEnumerable<object[]> ApplicableSharedCases => Read()["cases"]!.AsArray()
        .Where(row => row!["key"]!.GetValue<string>() == "CORE_LOG_FILE_PATH" && (row["lanes"] is not JsonArray lanes || lanes.Any(lane => lane!.GetValue<string>() == (OperatingSystem.IsWindows() ? "windows-x64" : "linux-ubuntu2404-x64"))))
        .Select(row => new object[] { row!["id"]!.GetValue<string>() });

    [TestMethod]
    [DynamicData(nameof(ApplicableSharedCases))]
    public void SharedPathVectorsExecuteForTheirNativePlatform(string id)
    {
        JsonObject row = ConfigurationFixtureAdapter.Case(Read(), id);
        JsonObject input = row["inputs"]!.AsObject();
        string initial = ConfigurationFixtureAdapter.Text(input, "workingDirectoryAtInitialization") ?? SyntheticBase();
        int reads = 0;
        (PreparedConfiguration? snapshot, CoreConfigurationException? error) = ConfigurationValidationAdapter.Execute(input, () => { reads++; return initial; });
        JsonNode expected = row["expected"]!;
        if (expected["status"]!.GetValue<string>() == "Invalid")
        {
            Assert.IsNotNull(error);
            Assert.AreEqual(ConfigurationIssueCodes.InvalidPath, error.Issues.Single().Code);
            Assert.AreEqual("LoggingOptions.FilePath", error.Issues.Single().Key);
        }
        else
        {
            Assert.IsNull(error);
            string? expectedPath = expected["value"] switch
            {
                null => null,
                JsonValue value => value.GetValue<string>(),
                JsonObject value => NativeExpected(initial, value["input"]!.GetValue<string>()),
                _ => throw new InvalidDataException("Unknown fixture expectation.")
            };
            initial = SyntheticBase("changed");
            Assert.AreEqual(expectedPath, snapshot!.Logging.FilePath);
        }
        Assert.AreEqual(1, reads);
    }

    [TestMethod]
    [DataRow("logs/app.jsonl", "logs/app.jsonl")]
    [DataRow("./logs/../log.jsonl", "log.jsonl")]
    [DataRow("../up.jsonl", "../up.jsonl")]
    [DataRow("logs/\u03a9-\U0001f642-e\u0301.jsonl", "logs/\u03a9-\U0001f642-e\u0301.jsonl")]
    public void NativeRelativeSyntaxPreservesUnicodeAndDoesNotCreateOutputs(string input, string expectedSuffix)
    {
        using var owned = new TemporaryDirectory();
        PreparedConfiguration result = Create(input, owned.Path);
        Assert.AreEqual(NativeExpected(owned.Path, expectedSuffix), result.Logging.FilePath);
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }

    [TestMethod]
    public void AbsoluteSyntaxIsIndependentOfCapturedBaseAndDoesNotRequireExistence()
    {
        string absolute = NativeExpected(SyntheticBase("absolute"), "never-created/log.jsonl");
        Assert.AreEqual(absolute, Create(absolute, SyntheticBase()).Logging.FilePath);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InvalidSetupPathStillValidatesForDisabledFileOutput(bool disabled)
    {
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => Create("private" + (char)0 + "path", SyntheticBase(), disabled));
        Assert.AreEqual(ConfigurationIssueCodes.InvalidPath, error.Issues.Single().Code);
        Assert.AreEqual("LoggingOptions.FilePath", error.Issues.Single().Key);
        Assert.IsFalse(error.ToString().Contains("private", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("relative")]
    [DataRow(" ")]
    public void InvalidInjectedDirectoryIsARequiredSourceFailure(string value)
    {
        Assert.ThrowsExactly<ArgumentException>(() => Create(null, value));
    }

    [TestMethod]
    public void RequiredDirectorySourceRunsOnceAndItsFailuresPropagate()
    {
        // The application supplies the base directory; logging never asks a source to resolve it again.
        string initial = SyntheticBase();
        PreparedConfiguration observation = Create("log.jsonl", initial);
        string changed = SyntheticBase("changed");
        Assert.AreNotEqual(initial, changed);
        Assert.AreEqual(NativeExpected(initial, "log.jsonl"), observation.Logging.FilePath);
    }

    [TestMethod]
    [DataRow("logs/~app.jsonl")]
    [DataRow("logs/\ue000-\ue001-~app.jsonl")]
    [DataRow("./~logs/../~app.jsonl")]
    public void LiteralTildesAndUnicodeMarkerCharactersRemainUnchanged(string input)
    {
        string initial = SyntheticBase("~initial");
        PreparedConfiguration result = Create(input, initial);
        Assert.AreEqual(NativeExpected(initial, input), result.Logging.FilePath);
    }

    [TestMethod]
    public void AbsoluteTildePathsPreserveLiteralTextIndependentlyOfTheBase()
    {
        string absolute = NativeExpected(SyntheticBase("~absolute"), "\ue000/~never-created/log.jsonl");
        PreparedConfiguration result = Create(absolute, SyntheticBase("~initial"));
        Assert.AreEqual(absolute, result.Logging.FilePath);
    }

    [TestMethod]
    public void PrivateUseMarkerExhaustionStillPreservesLiteralCharacters()
    {
        string privateUse = string.Concat(Enumerable.Range(0xe000, 0xfffd - 0xe000 + 1).Select(value => (char)value));
        string initial = SyntheticBase("~initial");
        string input = "~" + privateUse + ".jsonl";
        PreparedConfiguration result = Create(input, initial);
        Assert.AreEqual(NativeExpected(initial, input), result.Logging.FilePath);
    }

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
    private static PreparedConfiguration Create(string? path, string directory, bool disabled = false)
    {
        EnvironmentStoreTestData.AssignValidFacts();
        var logging = new LoggingOptions { FilePath = path, FileLevel = disabled ? CoreLogLevel.None : null };
        logging.Prepare(directory);
        return new PreparedConfiguration(EnvironmentStoreTestData.Values.CaptureMetadata(), logging);
    }
    private static JsonObject Read() => ConfigurationFixtureAdapter.ReadDocument(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "configuration-cases.json"));
}
