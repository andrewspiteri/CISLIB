using System.Globalization;
using CIS.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Configuration;

[TestClass]
public sealed class ConfigurationMappingTests
{
    [TestMethod]
    [DataRow(false, false, 30, 5, true)]
    [DataRow(true, false, 60, 10, true)]
    [DataRow(false, true, 15, 5, false)]
    [DataRow(true, true, 15, 10, false)]
    public void EachPresentScopeOverlaysOnlyItsSuppliedMembers(bool common, bool application,
        int expectedInterval, int expectedTimeout, bool expectedEnabled)
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2OverlaySettings>();
        fixture.Baseline(common ? "{\"IntervalSeconds\":60,\"TimeoutSeconds\":10}" : null,
            application ? "{\"IntervalSeconds\":15,\"Enabled\":false}" : null);
        Assert.AreEqual((expectedInterval, expectedTimeout, expectedEnabled),
            (fixture.Root.IntervalSeconds, fixture.Root.TimeoutSeconds, fixture.Root.Enabled));
        Assert.AreEqual("default", fixture.Root.Label);
    }

    [TestMethod]
    public void FalseZeroEmptyAndAllowedNullArePresentValuesWhileCollectionsReplaceAsAWhole()
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2OverlaySettings>();
        fixture.Baseline("""
            {"IntervalSeconds":60,"Child":{"Left":7},"Items":[3,4],"Scores":{"common":8}}
            """, """
            {"IntervalSeconds":0,"Enabled":false,"Label":"","Optional":null,
             "Child":{"Right":9},"Items":[],"Scores":{"application":0}}
            """);
        Assert.AreEqual((0, false, "", (string?)null),
            (fixture.Root.IntervalSeconds, fixture.Root.Enabled, fixture.Root.Label, fixture.Root.Optional));
        Assert.IsNotNull(fixture.Root.Child);
        Assert.AreEqual((7, 9), (fixture.Root.Child.Left, fixture.Root.Child.Right));
        Assert.AreEqual(0, fixture.Root.Items.Count);
        CollectionAssert.AreEquivalent(new[] { "application" }, fixture.Root.Scores.Keys.ToArray());
        Assert.AreEqual(0, fixture.Root.Scores["application"]);
    }

    [TestMethod]
    public void AnUpperPartialObjectAfterALowerNullUsesTheDeclaredChildDefaults()
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2OverlaySettings>();
        fixture.Baseline("{\"Child\":null}", "{\"Child\":{\"Right\":9}}");
        Assert.IsNotNull(fixture.Root.Child);
        Assert.AreEqual((1, 9), (fixture.Root.Child.Left, fixture.Root.Child.Right));
        Assert.IsTrue(fixture.Pipeline.Apply(fixture.Entry, fixture.Prepare(application: "{\"Child\":null}"),
            CancellationToken.None, () => false));
        Assert.IsNull(fixture.Root.Child);
    }

    [TestMethod]
    public void RemovingRecordsRestoresTheCurrentLowerScopeThenCapturedDefaultsWithoutAccumulation()
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2OverlaySettings>();
        fixture.Baseline("{\"IntervalSeconds\":60,\"Scores\":{\"common\":4}}",
            "{\"IntervalSeconds\":15,\"Scores\":{\"application\":8}}");
        W2OverlaySettings retained = fixture.Root;
        Assert.IsTrue(fixture.Pipeline.Apply(fixture.Entry,
            fixture.Prepare("{\"IntervalSeconds\":90,\"Scores\":{\"latest\":9}}"), CancellationToken.None, () => false));
        Assert.AreEqual(90, retained.IntervalSeconds);
        CollectionAssert.AreEquivalent(new[] { "latest" }, retained.Scores.Keys.ToArray());
        Assert.IsTrue(fixture.Pipeline.Apply(fixture.Entry, fixture.Prepare(), CancellationToken.None, () => false));
        Assert.AreSame(retained, fixture.Root);
        Assert.AreEqual(30, retained.IntervalSeconds);
        CollectionAssert.AreEquivalent(new[] { "default" }, retained.Scores.Keys.ToArray());
        Assert.AreEqual(1, retained.Scores["default"]);
        Assert.AreEqual(2L, fixture.Entry.Revision);
    }

    [TestMethod]
    public void AMaskedCommonChangeIsUnchangedUntilTheOverrideIsRemovedUsingTheLatestCommonRecord()
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2OverlaySettings>();
        fixture.Baseline("{\"IntervalSeconds\":60}", "{\"IntervalSeconds\":15}");
        string? baseline = fixture.Entry.AcceptedChecksum;
        ConfigurationPreparedValues masked = fixture.Prepare("{\"IntervalSeconds\":90}", "{\"IntervalSeconds\":15}");
        Assert.AreEqual(baseline, masked.Checksum);
        Assert.IsFalse(fixture.Pipeline.Apply(fixture.Entry, masked, CancellationToken.None, () => false));
        Assert.AreEqual(0L, fixture.Entry.Revision);
        Assert.AreEqual(15, fixture.Root.IntervalSeconds);
        // The coordinator must supply its latest records. This proves composition,
        // not storage-token caching, watches, or notification delivery.
        Assert.IsTrue(fixture.Pipeline.Apply(fixture.Entry, fixture.Prepare("{\"IntervalSeconds\":90}"),
            CancellationToken.None, () => false));
        Assert.AreEqual(90, fixture.Root.IntervalSeconds);
        Assert.AreEqual(1L, fixture.Entry.Revision);
    }

    [TestMethod]
    public void DefaultRestorationUsesTheCapturedRootInsteadOfReevaluatingFactoryDefaultsOrLiveValues()
    {
        int constructions = 10;
        var fixture = ConfigurationMappingTestSupport.Create(factory: () => new W2SeedSettings { Seed = ++constructions });
        Assert.AreEqual(11, fixture.Root.Seed);
        fixture.Baseline(application: "{\"Seed\":99}");
        fixture.Root.Seed = 777;
        Assert.IsTrue(fixture.Pipeline.Apply(fixture.Entry, fixture.Prepare(), CancellationToken.None, () => false));
        Assert.AreEqual(11, fixture.Root.Seed);
        Assert.AreEqual(13, constructions, "Fresh validation candidates must not redefine captured defaults.");
    }

    [TestMethod]
    [DataRow("null", null)]
    [DataRow("[]", null)]
    [DataRow("42", null)]
    [DataRow("{\"IntervalSeconds\":\"secret-input\"}", null)]
    [DataRow("{\"IntervalSeconds\":2147483648}", null)]
    [DataRow("{\"IntervalSeconds\":1.5}", null)]
    [DataRow("{\"Enabled\":\"false\"}", null)]
    [DataRow("{\"Label\":12}", null)]
    [DataRow("{\"Label\":null}", "{\"Label\":\"valid\"}")]
    [DataRow("{\"IntervalSeconds\":\"secret-input\"}", "{\"IntervalSeconds\":15}")]
    [DataRow("{\"Items\":[null]}", null)]
    [DataRow("{\"Child\":[]}", null)]
    public void InvalidKnownSourceValuesRejectTheWholeCandidateEvenWhenAnUpperScopeMasksThem(string common, string? application)
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2OverlaySettings>();
        fixture.Baseline(application: "{\"IntervalSeconds\":44}");
        string? accepted = fixture.Entry.AcceptedChecksum;
        CoreConfigurationException error = ConfigurationMappingTestSupport.Issue("Configuration.Mapping",
            () => fixture.Prepare(common, application));
        Assert.AreEqual((44, 5, true, "default"),
            (fixture.Root.IntervalSeconds, fixture.Root.TimeoutSeconds, fixture.Root.Enabled, fixture.Root.Label));
        Assert.AreEqual(accepted, fixture.Entry.AcceptedChecksum);
        Assert.AreEqual(0L, fixture.Entry.Revision);
        Assert.IsFalse(fixture.Entry.Disabled);
        string diagnostic = error + string.Concat(error.Issues.Select(issue => issue.Key + issue.Explanation));
        Assert.IsFalse(diagnostic.Contains("secret-input", StringComparison.Ordinal));
        Assert.IsNull(error.InnerException);
    }

    [TestMethod]
    [DataRow("{\"Label\":\"a\",\"Label\":\"b\"}")]
    [DataRow("{\"unknown\":1,\"unknown\":2}")]
    [DataRow("{\"unknown\":[{\"nested\":{\"duplicate\":1,\"duplicate\":2}}]}")]
    [DataRow("{\"Child\":{\"Left\":1,\"Left\":2}}")]
    [DataRow("{\"Scores\":{\"key\":1,\"key\":2}}")]
    public void DuplicateOrdinalNamesAreRejectedAtEveryObjectAndDictionaryBoundary(string record)
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2OverlaySettings>();
        ConfigurationMappingTestSupport.Issue("Configuration.Mapping", () => fixture.Prepare(application: record));
        Assert.IsNull(fixture.Entry.AcceptedChecksum);
        Assert.AreEqual(30, fixture.Root.IntervalSeconds);
    }

    [TestMethod]
    public void UnknownNamesAreCaseSensitiveIgnoredAndReportedWithoutRawValuesOrUnboundedWarningCounts()
    {
        var warnings = new List<string>();
        var fixture = ConfigurationMappingTestSupport.Create<W2OverlaySettings>(diagnostic: (type, message) =>
        {
            Assert.AreSame(typeof(W2OverlaySettings), type);
            warnings.Add(message);
        });
        ConfigurationPreparedValues baseline = fixture.Prepare();
        string record = "{\"label\":\"secret-value\"," + string.Join(",", Enumerable.Range(0, 20)
            .Select(index => "\"unknown" + index.ToString(CultureInfo.InvariantCulture) + "\":\"secret-value\"")) + "}";
        ConfigurationPreparedValues ignored = fixture.Prepare(application: record);
        Assert.AreEqual(baseline.Checksum, ignored.Checksum);
        Assert.AreEqual(8, warnings.Count);
        Assert.IsTrue(warnings[0].Contains("label", StringComparison.Ordinal));
        Assert.IsTrue(warnings.All(message => !message.Contains("secret-value", StringComparison.Ordinal)));
        var failingObserver = ConfigurationMappingTestSupport.Create<W2OverlaySettings>(
            diagnostic: (_, _) => throw new InvalidOperationException("observer-secret"));
        Assert.AreEqual(failingObserver.Prepare().Checksum, failingObserver.Prepare(application: record).Checksum);
    }

    [TestMethod]
    public void NullableCollectionElementsAcceptExplicitNullWithoutRelaxingNonnullableElementRules()
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2NullableElements>();
        fixture.Baseline(application: "{\"Optional\":[null,\"kept\"],\"OptionalMap\":{\"k\":null}}");
        CollectionAssert.AreEqual(new string?[] { null, "kept" }, fixture.Root.Optional);
        Assert.IsTrue(fixture.Root.OptionalMap.ContainsKey("k"));
        Assert.IsNull(fixture.Root.OptionalMap["k"]);
        ConfigurationMappingTestSupport.Issue("Configuration.Mapping", () => fixture.Prepare(application: "{\"Required\":[null]}"));
        ConfigurationMappingTestSupport.Issue("Configuration.Mapping", () => fixture.Prepare(application: "{\"RequiredMap\":{\"k\":null}}"));
        CollectionAssert.AreEqual(new[] { "required" }, fixture.Root.Required);
        Assert.AreEqual("required", fixture.Root.RequiredMap["k"]);
    }

    [TestMethod]
    [DataRow("{\"Double\":1,\"Single\":1,\"Price\":1.2300}", "{\"Double\":1e0,\"Single\":1.0,\"Price\":1.23}")]
    [DataRow("{\"Double\":-0.0,\"Single\":-0.0,\"Price\":-0.00}", "{\"Double\":0,\"Single\":0,\"Price\":0}")]
    public void CanonicalNumbersDescribeTypedValuesInsteadOfTheirJsonSpelling(string first, string second)
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2Numbers>();
        ConfigurationPreparedValues firstValues = fixture.Prepare(application: first);
        ConfigurationPreparedValues secondValues = fixture.Prepare(application: second);
        Assert.AreEqual(firstValues.Checksum, secondValues.Checksum);
        Assert.IsFalse(fixture.Pipeline.Apply(fixture.Entry, firstValues, CancellationToken.None, () => false, initial: true));
        Assert.IsFalse(fixture.Pipeline.Apply(fixture.Entry, secondValues, CancellationToken.None, () => false));
        Assert.AreEqual(0L, fixture.Entry.Revision);
    }

    [TestMethod]
    [DataRow("{\"Count\":\"1\"}")]
    [DataRow("{\"Count\":true}")]
    [DataRow("{\"Count\":1.1}")]
    [DataRow("{\"Double\":1e400}")]
    [DataRow("{\"Single\":1e100}")]
    [DataRow("{\"Price\":79228162514264337593543950336}")]
    public void NumericConversionsRejectCoercionOverflowAndNonfiniteResults(string record)
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2Numbers>();
        ConfigurationMappingTestSupport.Issue("Configuration.Mapping", () => fixture.Prepare(application: record));
        Assert.AreEqual((0, 0d, 0f, 0m), (fixture.Root.Count, fixture.Root.Double, fixture.Root.Single, fixture.Root.Price));
    }

    [TestMethod]
    public void CanonicalObjectsAndDictionariesIgnoreMemberOrderWhileSequenceOrderRemainsSignificant()
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2OverlaySettings>();
        ConfigurationPreparedValues first = fixture.Prepare(application: "{\"Items\":[1,2],\"Scores\":{\"z\":9,\"a\":1},\"Label\":\"x\"}");
        ConfigurationPreparedValues reordered = fixture.Prepare(application: "{\"Label\":\"x\",\"Scores\":{\"a\":1,\"z\":9},\"Items\":[1,2]}");
        Assert.AreEqual(first.Checksum, reordered.Checksum);
        Assert.AreNotEqual(first.Checksum,
            fixture.Prepare(application: "{\"Items\":[2,1],\"Scores\":{\"a\":1,\"z\":9},\"Label\":\"x\"}").Checksum);
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow("en-US")]
    [DataRow("fr-FR")]
    [DataRow("tr-TR")]
    public void EffectiveChecksumMatchesAnIndependentLiteralTypedUtf8Sha256Vector(string cultureName)
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var fixture = ConfigurationMappingTestSupport.Create<W2ChecksumVector>();
            ConfigurationPreparedValues prepared = fixture.Prepare(application:
                "{\"When\":\"1970-01-01T00:00:00Z\",\"Price\":1.2500,\"Mode\":\"Ready\",\"Count\":2}");
            // SHA256 of the literal UTF8 bytes, no whitespace or BOM:
            // ["object",{"Count":["i32","2"],"Mode":["enum:i32","1"],"Price":["decimal","1.25"],"When":["datetime:utc","621355968000000000"]}]
            Assert.AreEqual("d46cedba454387ba4193f3a677cbe66db3e075a37e5e6a3dafcb3e5d81a389b6", prepared.Checksum);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [TestMethod]
    public void ExplicitDateOffsetsNormalizeToUtcAndEqualInstantsHaveEqualChecksums()
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2Dates>();
        ConfigurationPreparedValues utc = fixture.Prepare(application:
            "{\"When\":\"2024-04-05T10:15:30Z\",\"Offset\":\"2024-04-05T10:15:30+00:00\"}");
        ConfigurationPreparedValues offset = fixture.Prepare(application:
            "{\"When\":\"2024-04-05T12:15:30+02:00\",\"Offset\":\"2024-04-05T05:15:30-05:00\"}");
        Assert.AreEqual(utc.Checksum, offset.Checksum);
        Assert.IsFalse(fixture.Pipeline.Apply(fixture.Entry, offset, CancellationToken.None, () => false, initial: true));
        Assert.AreEqual(new DateTime(2024, 4, 5, 10, 15, 30, DateTimeKind.Utc), fixture.Root.When);
        Assert.AreEqual(DateTimeKind.Utc, fixture.Root.When.Kind);
        Assert.AreEqual(new DateTimeOffset(2024, 4, 5, 10, 15, 30, TimeSpan.Zero), fixture.Root.Offset);
        Assert.AreEqual(TimeSpan.Zero, fixture.Root.Offset.Offset);
    }

    [TestMethod]
    public void OffsetlessDateTimeRemainsUnspecifiedAndDateTimeOffsetRequiresAnExplicitOffset()
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2Dates>();
        ConfigurationPreparedValues unspecified = fixture.Prepare(application: "{\"When\":\"2024-04-05T10:15:30\"}");
        ConfigurationPreparedValues utc = fixture.Prepare(application: "{\"When\":\"2024-04-05T10:15:30Z\"}");
        Assert.AreNotEqual(unspecified.Checksum, utc.Checksum);
        Assert.IsFalse(fixture.Pipeline.Apply(fixture.Entry, unspecified, CancellationToken.None, () => false, initial: true));
        Assert.AreEqual(DateTimeKind.Unspecified, fixture.Root.When.Kind);
        Assert.AreEqual(new DateTime(2024, 4, 5, 10, 15, 30, DateTimeKind.Unspecified), fixture.Root.When);
        ConfigurationMappingTestSupport.Issue("Configuration.Mapping",
            () => fixture.Prepare(application: "{\"Offset\":\"2024-04-05T10:15:30\"}"));
    }

    [TestMethod]
    public void EnumNamesAndIntegralValuesShareMeaningWhileUnderlyingWidthsPermitUnnamedValues()
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2Enums>();
        Assert.AreEqual(fixture.Prepare(application: "{\"Mode\":\"Ready\"}").Checksum,
            fixture.Prepare(application: "{\"Mode\":1}").Checksum);
        fixture.Baseline(application: "{\"Mode\":99,\"ByteMode\":255,\"SignedMode\":-128,\"UnsignedMode\":18446744073709551615}");
        Assert.AreEqual((99, (byte)255, (sbyte)-128, ulong.MaxValue),
            ((int)fixture.Root.Mode, (byte)fixture.Root.ByteMode, (sbyte)fixture.Root.SignedMode, (ulong)fixture.Root.UnsignedMode));
        Assert.IsTrue(fixture.Pipeline.Apply(fixture.Entry, fixture.Prepare(), CancellationToken.None, () => false));
        Assert.AreEqual(0, (int)fixture.Root.Mode, "An undeclared enum default is valid representable data.");
    }

    [TestMethod]
    [DataRow("{\"Mode\":\"1\"}")]
    [DataRow("{\"Mode\":\"ready\"}")]
    [DataRow("{\"Mode\":\"Ready,Busy\"}")]
    [DataRow("{\"Mode\":1.5}")]
    [DataRow("{\"ByteMode\":256}")]
    [DataRow("{\"SignedMode\":-129}")]
    [DataRow("{\"UnsignedMode\":-1}")]
    [DataRow("{\"UnsignedMode\":18446744073709551616}")]
    public void EnumParsingRejectsUnapprovedStringFormsAndUnderlyingOverflow(string record)
    {
        var fixture = ConfigurationMappingTestSupport.Create<W2Enums>();
        ConfigurationMappingTestSupport.Issue("Configuration.Mapping", () => fixture.Prepare(application: record));
        Assert.AreEqual(0, (int)fixture.Root.Mode);
    }
}
