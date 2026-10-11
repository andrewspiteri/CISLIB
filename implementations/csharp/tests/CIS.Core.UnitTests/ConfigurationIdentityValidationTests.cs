using System.Reflection;
using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Tests.DiscoveryFixture;
using CIS.Tests.UnitTests.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ConfigurationIdentityValidationTests
{
    private static readonly string[] Fields = ["ApplicationName", "EnvironmentName", "ServiceVersion", "BuildVersion", "CommitId", "BuildId"];
    public static IEnumerable<object[]> SharedIdentities => ReadCases()["cases"]!.AsArray().Where(row => !Text(row!, "key").StartsWith("CORE_LOG_", StringComparison.Ordinal)).Select(row => new object[] { Text(row!, "id") });
    public static IEnumerable<object[]> UnicodeCases => Fields.SelectMany(identityField => new[] { "maximum", "too-long", "combining-maximum", "combining-too-long", "high-surrogate", "low-surrogate", "reversed-pair", "interrupted-pair" }.Select(scenario => new object[] { identityField, scenario }));
    public static IEnumerable<object[]> Controls => Enumerable.Range(0, 32).Concat(Enumerable.Range(127, 33)).Select(value => new object[] { value });

    [TestMethod]
    [DynamicData(nameof(SharedIdentities))]
    public void SharedIdentityVectorsExecuteAgainstTheRealValidator(string id)
    {
        JsonNode row = ReadCases()["cases"]!.AsArray().Single(row => Text(row!, "id") == id)!;
        JsonObject input = row["inputs"]!.AsObject();
        JsonNode before = input.DeepClone();
        var source = new EnvironmentVariableStub();
        foreach ((string key, JsonNode? value) in input["environment"]!.AsObject())
        {
            source.Values[key] = value?.GetValue<string>();
        }
        JsonNode discovery = input["discovery"]!;
        var application = new ApplicationIdentity(Text(discovery, "applicationName"), Text(discovery, "buildVersion"), null, null);
        ConfigurationResolution result = ConfigurationResolver.Resolve(ConfigurationFixtureAdapter.Setup(input), application, CapturedEnvironment.Capture(source));
        IReadOnlyList<ConfigurationIssue> issues = ConfigurationValidator.CollectIdentityAndOptionIssues(result);
        JsonNode expected = row["expected"]!;
        if (Text(expected, "status") == "Invalid")
        {
            CollectionAssert.AreEqual(expected["issues"]!.AsArray().Select(issue => Text(issue!, "code") + ":" + Text(issue!, "key")).ToArray(), issues.Select(issue => issue.Code + ":" + issue.Key).ToArray(), id);
            CoreConfigurationException exception = Assert.ThrowsExactly<CoreConfigurationException>(() => ConfigurationValidator.ThrowIfIdentityOrOptionsInvalid(result));
            Assert.AreEqual(issues.Count, exception.Issues.Count);
        }
        else
        {
            Assert.AreEqual(0, issues.Count, id);
            ConfigurationValidator.ThrowIfIdentityOrOptionsInvalid(result);
            JsonNode? value = ConfigurationFixtureAdapter.Project(result)["settings"]!.AsArray().Single(setting => Text(setting!, "key") == Text(row, "key"))!["value"];
            Assert.IsTrue(JsonNode.DeepEquals(expected["value"], value), id);
        }
        Assert.IsTrue(JsonNode.DeepEquals(before, input));
        Assert.AreEqual(21, source.Reads.Count);
    }

    [TestMethod]
    [DynamicData(nameof(UnicodeCases))]
    public void IdentityBoundsCountScalarsAndRejectMalformedUnicode(string identityField, string scenario)
    {
        int maximum = identityField is "ApplicationName" or "EnvironmentName" ? 255 : 512;
        string AtLength(int count) => string.Concat(Enumerable.Repeat("\U0001f642", count));
        string Combined(int count) => string.Concat(Enumerable.Repeat("e\u0301", count / 2)) + (count % 2 == 0 ? "" : "e");
        string value = scenario switch
        {
            "maximum" => AtLength(maximum),
            "too-long" => AtLength(maximum + 1),
            "combining-maximum" => Combined(maximum),
            "combining-too-long" => Combined(maximum + 1),
            "high-surrogate" => "x" + (char)0xd800,
            "low-surrogate" => "x" + (char)0xdc00,
            "reversed-pair" => "x" + (char)0xdc00 + (char)0xd800,
            "interrupted-pair" => "x" + (char)0xd800 + "a" + (char)0xdc00,
            _ => throw new InvalidDataException("Unknown scenario.")
        };
        IReadOnlyList<ConfigurationIssue> issues = ConfigurationValidator.CollectIdentityAndOptionIssues(ForField(identityField, value));
        bool valid = scenario is "maximum" or "combining-maximum";
        Assert.AreEqual(valid ? 0 : 1, issues.Count);
        if (!valid)
        {
            Assert.AreEqual(ConfigurationIssueCodes.InvalidIdentity, issues[0].Code);
            Assert.AreEqual(SourceKey(identityField), issues[0].Key);
        }
    }

    [TestMethod]
    [DynamicData(nameof(Controls))]
    public void EveryC0AndC1ControlIsRejectedInAllIdentityFields(int scalar)
    {
        foreach (string identityField in Fields)
        {
            IReadOnlyList<ConfigurationIssue> issues = ConfigurationValidator.CollectIdentityAndOptionIssues(ForField(identityField, "start" + (char)scalar + "end"));
            Assert.AreEqual(1, issues.Count, identityField);
            Assert.AreEqual(ConfigurationIssueCodes.InvalidIdentity, issues[0].Code, identityField);
            Assert.AreEqual(SourceKey(identityField), issues[0].Key, identityField);
        }
    }

    [TestMethod]
    public void CombinedErrorsUseCatalogThenMetadataThenSetupOrderWithoutRawValues()
    {
        var source = new EnvironmentVariableStub();
        source.Values["DD_SERVICE"] = "private" + (char)1;
        source.Values["CORE_ENVIRONMENT"] = "private" + (char)1;
        source.Values["CORE_SERVICE_VERSION"] = "private" + (char)1;
        source.Values["CORE_LOG_CONSOLE_FORMAT"] = "private-token";
        source.Values["CORE_LOG_QUEUE_CAPACITY"] = "0";
        source.Values["UNRELATED_PASSWORD"] = "private-password";
        var setup = new EnvironmentConfigurationInput { EnvironmentKind = (ConfigurationEnvironmentKind)99 };
        var application = new ApplicationIdentity("safe-app", "private" + (char)1, "private" + (char)1, "private" + (char)1);
        ConfigurationResolution result = ConfigurationResolver.Resolve(setup, application, CapturedEnvironment.Capture(source));
        CoreConfigurationException exception = Assert.ThrowsExactly<CoreConfigurationException>(() => ConfigurationValidator.ThrowIfIdentityOrOptionsInvalid(result));
        CollectionAssert.AreEqual(new[] { "DD_SERVICE", "CORE_ENVIRONMENT", "CORE_SERVICE_VERSION", "CORE_LOG_CONSOLE_FORMAT", "CORE_LOG_QUEUE_CAPACITY", "BuildVersion", "CommitId", "BuildId", "EnvironmentSetup.EnvironmentKind" }, exception.Issues.Select(issue => issue.Key).ToArray());
        string diagnostic = exception.ToString() + string.Join("|", exception.Issues.Select(issue => issue.Code + issue.Key + issue.Explanation));
        Assert.IsFalse(diagnostic.Contains("private", StringComparison.Ordinal));
        Assert.IsFalse(diagnostic.Contains("UNRELATED_PASSWORD", StringComparison.Ordinal));
        Assert.IsFalse(source.Reads.Contains("UNRELATED_PASSWORD", StringComparer.Ordinal));
    }

    [TestMethod]
    public void TelemetryOverrideCannotHideInvalidBuildIdentityAndInheritedFailureIsReportedOnce()
    {
        var badApplication = new ApplicationIdentity("valid", "invalid" + (char)1, null, null);
        ConfigurationResolution result = Resolve(new() { ServiceVersion = "Telemetry.Release" }, badApplication);
        CollectionAssert.AreEqual(new[] { "BuildVersion" }, ConfigurationValidator.CollectIdentityAndOptionIssues(result).Select(issue => issue.Key).ToArray());
        result = Resolve(new(), badApplication);
        CollectionAssert.AreEqual(new[] { "BuildVersion" }, ConfigurationValidator.CollectIdentityAndOptionIssues(result).Select(issue => issue.Key).ToArray());
    }

    [TestMethod]
    public void MissingApplicationNameAndOptionalBuildIdentifiersFollowDiscoveryAbsenceRules()
    {
        ConfigurationResolution result = Resolve(new(), new(null, "unknown", null, null));
        IReadOnlyList<ConfigurationIssue> issues = ConfigurationValidator.CollectIdentityAndOptionIssues(result);
        Assert.AreEqual(1, issues.Count);
        Assert.AreEqual(ConfigurationIssueCodes.MissingApplicationName, issues[0].Code);
        Assert.AreEqual("CORE_SERVICE_NAME", issues[0].Key);
        var identity = ApplicationIdentityDiscovery.Discover(new() { ApplicationName = " explicit ", BuildVersion = " Release+Case ", CommitId = " ", BuildId = "\t" });
        result = Resolve(new(), identity);
        Assert.AreEqual(0, ConfigurationValidator.CollectIdentityAndOptionIssues(result).Count);
        Assert.AreEqual("Release+Case", result.BuildVersion);
        Assert.IsNull(result.CommitId);
        Assert.IsNull(result.BuildId);
    }

    [TestMethod]
    public void ValidIdentitiesPreserveCaseCombiningMarksAndNonControlFormatCharacters()
    {
        string value = "Mixed.Case-e\u0301-\u200b-\ufeff";
        foreach (string identityField in Fields)
        {
            ConfigurationResolution result = ForField(identityField, value);
            Assert.AreEqual(0, ConfigurationValidator.CollectIdentityAndOptionIssues(result).Count);
            string actual = identityField switch
            {
                "ApplicationName" => result.ApplicationName.Value,
                "EnvironmentName" => result.EnvironmentName.Value,
                "ServiceVersion" => result.ServiceVersion.Value,
                "BuildVersion" => result.BuildVersion,
                "CommitId" => result.CommitId!,
                "BuildId" => result.BuildId!,
                _ => throw new InvalidDataException()
            };
            Assert.AreEqual(value, actual);
        }
    }

    [TestMethod]
    public void KnownValidationCollectsOwnedResultsAndDoesNotClaimPathOrPublicationValidation()
    {
        ConfigurationResolution result = Resolve(new() { Logging = new() { FilePath = "nul" + (char)0 } });
        ConfigurationValidator.ThrowIfIdentityOrOptionsInvalid(result);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ConfigurationIssue>)ConfigurationValidator.CollectIdentityAndOptionIssues(result)).Clear());
        Assert.ThrowsExactly<ArgumentNullException>(() => ConfigurationValidator.CollectIdentityAndOptionIssues(null!));
    }

    private static string SourceKey(string identityField) => identityField switch
    {
        "ApplicationName" => "EnvironmentSetup.ApplicationName",
        "EnvironmentName" => "EnvironmentSetup.EnvironmentName",
        "ServiceVersion" => "EnvironmentSetup.ServiceVersion",
        _ => identityField
    };
    private static ConfigurationResolution ForField(string identityField, string value)
    {
        var setup = new EnvironmentConfigurationInput { ApplicationName = identityField == "ApplicationName" ? value : null, EnvironmentName = identityField == "EnvironmentName" ? value : null, ServiceVersion = identityField == "ServiceVersion" ? value : "safe-telemetry" };
        return Resolve(setup, new("safe-app", identityField == "BuildVersion" ? value : "safe-build", identityField == "CommitId" ? value : null, identityField == "BuildId" ? value : null));
    }
    private static ConfigurationResolution Resolve(EnvironmentConfigurationInput setup, ApplicationIdentity? identity = null) => ConfigurationResolver.Resolve(setup, identity ?? new("safe-app", "safe-build", null, null), CapturedEnvironment.Capture(new EnvironmentVariableStub()));
    private static string Text(JsonNode node, string key) => ConfigurationFixtureAdapter.Text(node, key)!;
    private static string Root() => Directory.GetParent(Directory.GetParent(Directory.GetParent(ConfigurationFixtureAdapter.FindFixture())!.FullName)!.FullName)!.FullName;
    private static JsonObject ReadCases() => ConfigurationFixtureAdapter.ReadDocument(Path.Combine(Root(), "conformance", "logging", "configuration-cases.json"));
}
