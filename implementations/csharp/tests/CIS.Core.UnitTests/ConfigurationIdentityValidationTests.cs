using System.Reflection;
using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Logging;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class ConfigurationIdentityValidationTests
{
    private static readonly string[] Fields = ["ApplicationName", "EnvironmentName", "ServiceVersion", "BuildVersion", "CommitId", "BuildId"];
    public static IEnumerable<object[]> UnicodeCases => Fields.SelectMany(identityField => new[] { "maximum", "too-long", "combining-maximum", "combining-too-long", "high-surrogate", "low-surrogate", "reversed-pair", "interrupted-pair" }.Select(scenario => new object[] { identityField, scenario }));
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
        EnvironmentStoreTestData.AssignValidFacts();
        typeof(FixtureEnvironmentValues).GetProperty(identityField)!.SetValue(EnvironmentStoreTestData.Values, value);
        bool valid = scenario is "maximum" or "combining-maximum";
        if (valid)
        {
            EventMetadata snapshot = EnvironmentStoreTestData.Values.CaptureMetadata();
            Assert.AreEqual(value, typeof(EventMetadata).GetProperty(identityField)!.GetValue(snapshot));
        }
        else
        {
            CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => EnvironmentStoreTestData.Values.CaptureMetadata());
            Assert.AreEqual(ConfigurationIssueCodes.InvalidIdentity, error.Issues.Single().Code);
            Assert.AreEqual("EnvironmentOptions." + identityField, error.Issues.Single().Key);
        }
    }
}
