using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationPreFreshMarker;

namespace CIS.Tests.ArchitectureTests;

// Synthetic receiving controls: actual marker/TRX captures remain private evidence.
[TestClass]
public sealed class HealthcheckMutationPreFreshMarkerTests
{
    private const string Timestamp = "2026-10-23T18:00:00.1234567+00:00";
    private const string OutputRoot = "/data/cis-healthcheck-receiving-controls/synthetic-complete-partition79-run01";
    private const string CoreRoot = OutputRoot + "/source/implementations/csharp/src/CIS.Core";
    private const string OriginalOrigin = OutputRoot + "/before-fresh-marker.json";

    [TestMethod]
    [DataRow("unit-runtime", "controller79-unit")]
    [DataRow("pipes", "controller79-transport")]
    [DataRow("aspnet", "controller79-transport")]
    public void ExactSourceWriterShapesRetainTheOriginalTimestampAndOutputOrigin(string group, string expectedFormat)
    {
        Parsed parsed = Parse(Marker(group), group);
        Assert.AreEqual(expectedFormat, parsed.Format);
        Assert.AreEqual(Timestamp, parsed.Observation.Raw);
        Assert.AreEqual(OriginalOrigin, parsed.RequiredInventoryOrigin);
        Assert.AreEqual((byte)0, parsed.Observation.SubTickNanoseconds);
    }

    [TestMethod]
    public void PlainLegacyObservationPreservesItsOriginalNanosecondsAndLegacyOriginSemantics()
    {
        const string raw = "2026-10-22T11:37:10,654533965+00:00";
        Parsed parsed = Read(raw + "\n", null, "pipes", CoreRoot);
        Assert.AreEqual("legacy-plain", parsed.Format);
        Assert.AreEqual(raw, parsed.Observation.Raw);
        Assert.AreEqual((byte)65, parsed.Observation.SubTickNanoseconds);
        Assert.IsNull(parsed.RequiredInventoryOrigin);
    }

    [TestMethod]
    public void RichLegacyObservationRetainsItsOffsetSpellingAndExistingMetadata()
    {
        const string raw = "2026-10-22T13:37:10.6545339+02:00";
        JsonObject document = new() { ["observedBeforeFreshUtc"] = raw, ["oldMetadata"] = "retained" };
        Parsed parsed = Parse(document, "aspnet");
        Assert.AreEqual("legacy-rich", parsed.Format);
        Assert.AreEqual(raw, parsed.Observation.Raw);
        Assert.IsNull(parsed.RequiredInventoryOrigin);
    }

    [TestMethod]
    [DataRow("2026-10-23T18:00:00.1234567+00:00")]
    [DataRow("2026-10-21T00:00:00Z")]
    public void BothTimestampAliasesAreAmbiguousEvenWhenTheirValuesAreEqual(string secondTimestamp)
    {
        JsonObject document = Marker("aspnet");
        document["observedBeforeFreshUtc"] = secondTimestamp;
        Assert.ThrowsExactly<InvalidDataException>(() => Parse(document, "aspnet"));
    }

    [TestMethod]
    [DataRow("observedUtc", "null")]
    [DataRow("observedUtc", "123")]
    [DataRow("observedUtc", "true")]
    [DataRow("observedUtc", "[]")]
    [DataRow("observedUtc", "{}")]
    [DataRow("observedBeforeFreshUtc", "null")]
    [DataRow("observedBeforeFreshUtc", "123")]
    [DataRow("observedBeforeFreshUtc", "true")]
    [DataRow("observedBeforeFreshUtc", "[]")]
    [DataRow("observedBeforeFreshUtc", "{}")]
    public void AJsonTimestampMustBeAnActualString(string timestampField, string literal)
    {
        JsonObject document = timestampField == "observedUtc" ? Marker("aspnet") : new JsonObject();
        document[timestampField] = JsonNode.Parse(literal);
        Assert.ThrowsExactly<InvalidDataException>(() => Parse(document, "aspnet"));
    }

    [TestMethod]
    [DataRow(" 2026-10-23T18:00:00.1234567+00:00")]
    [DataRow("2026-10-23T18:00:00.1234567+00:00 ")]
    [DataRow("2026-02-30T18:00:00.1234567+00:00")]
    public void ControllerTimestampIsNeitherTrimmedNorRepaired(string timestamp)
    {
        JsonObject document = Marker("aspnet");
        document["observedUtc"] = timestamp;
        Assert.ThrowsExactly<InvalidDataException>(() => Parse(document, "aspnet"));
    }

    [TestMethod]
    [DataRow("unit-runtime", "pipes")]
    [DataRow("unit-runtime", "aspnet")]
    [DataRow("pipes", "unit-runtime")]
    [DataRow("aspnet", "unit-runtime")]
    public void AControllerShapeCannotBeSubstitutedForAnotherGroupFamily(string writerGroup, string receivingGroup) =>
        Assert.ThrowsExactly<InvalidDataException>(() => Parse(Marker(writerGroup), receivingGroup));

    [TestMethod]
    [DataRow("")]
    [DataRow("invented-group")]
    public void UnknownGroupsCannotSelectAControllerOrLegacyFormat(string group)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Parse(Marker("aspnet"), group));
        Assert.ThrowsExactly<InvalidDataException>(() => Read(Timestamp, null, group, CoreRoot));
    }

    [TestMethod]
    [DataRow("unit-runtime", "actualNativeCoreDllRestorationVerified", "false")]
    [DataRow("unit-runtime", "explicitControllerPdbCorrection", "\"true\"")]
    [DataRow("unit-runtime", "historicalLoadedSymbolClaim", "true")]
    [DataRow("pipes", "nativeCoreDllRestorationVerified", "false")]
    [DataRow("pipes", "explicitControllerSymbolRepair", "\"true\"")]
    [DataRow("pipes", "historicalLoadedSymbolsClaim", "true")]
    [DataRow("aspnet", "nativeCoreDllRestorationVerified", "false")]
    [DataRow("aspnet", "explicitControllerSymbolRepair", "\"true\"")]
    [DataRow("aspnet", "historicalLoadedSymbolsClaim", "true")]
    public void FixedSourceWriterFieldsCannotBeChangedOrCoerced(string group, string field, string literal)
    {
        JsonObject document = Marker(group);
        document[field] = JsonNode.Parse(literal);
        Assert.ThrowsExactly<InvalidDataException>(() => Parse(document, group));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("0.0")]
    [DataRow("\"0\"")]
    [DataRow("137")]
    public void NativeExitRetainsTheTerminalIntegerTypeAndAcceptedValue(string literal)
    {
        JsonObject document = Marker("aspnet");
        document["nativeExit"] = JsonNode.Parse(literal);
        Assert.ThrowsExactly<InvalidDataException>(() => Parse(document, "aspnet"));
    }

    [TestMethod]
    [DataRow("unit-runtime")]
    [DataRow("pipes")]
    [DataRow("aspnet")]
    public void TheEntireExactShapeIsRequiredAndAnApprovalFlagCannotReplaceIt(string group)
    {
        JsonObject extra = Marker(group);
        extra["approved"] = true;
        Assert.ThrowsExactly<InvalidDataException>(() => Parse(extra, group));

        JsonObject missingTimestamp = Marker(group);
        Assert.IsTrue(missingTimestamp.Remove("observedUtc"));
        Assert.ThrowsExactly<InvalidDataException>(() => Parse(missingTimestamp, group));

        JsonObject missingExit = Marker(group);
        Assert.IsTrue(missingExit.Remove("nativeExit"));
        Assert.ThrowsExactly<InvalidDataException>(() => Parse(missingExit, group));

        Assert.ThrowsExactly<InvalidDataException>(() => Parse(new JsonObject { ["observedUtc"] = Timestamp }, group));
    }

    [TestMethod]
    [DataRow(OutputRoot + "/not-a-source")]
    [DataRow(OutputRoot + "/../foreign/source/implementations/csharp/src/CIS.Core")]
    [DataRow("relative/source/implementations/csharp/src/CIS.Core")]
    public void ControllerOriginMustComeFromTheExactAbsoluteNativeSourceLayout(string nativeCoreRoot)
    {
        JsonObject document = Marker("aspnet");
        Assert.ThrowsExactly<InvalidDataException>(() => Read(document.ToJsonString(), document, "aspnet", nativeCoreRoot));
    }

    [TestMethod]
    public void OnlyTheExactOriginalNativeOriginAndOriginalPhysicalHashSatisfyTheControllerPin()
    {
        JsonObject document = Marker("aspnet");
        Parsed parsed = Parse(document, "aspnet");
        string originalBytes = document.ToJsonString();
        string originalHash = Hash(originalBytes);
        Dictionary<string, string> inventory = new(StringComparer.Ordinal) { [OriginalOrigin] = originalHash };

        RequireInventoryPin(parsed, inventory, originalHash);
        Assert.ThrowsExactly<InvalidDataException>(() => RequireInventoryPin(parsed,
            new Dictionary<string, string> { [OutputRoot + "/invented-marker.json"] = originalHash }, originalHash));
        Assert.ThrowsExactly<InvalidDataException>(() => RequireInventoryPin(parsed, new Dictionary<string, string>(), originalHash));

        // A different physical serialization with the same timestamp is still different evidence.
        string changedBytes = originalBytes + "\n";
        string changedHash = Hash(changedBytes);
        Assert.AreNotEqual(originalHash, changedHash);
        Assert.AreEqual(parsed.Observation, Read(changedBytes, document, "aspnet", CoreRoot).Observation);
        Assert.ThrowsExactly<InvalidDataException>(() => RequireInventoryPin(parsed, inventory, changedHash));
    }

    [TestMethod]
    [DataRow("approved")]
    [DataRow("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void AnInventoryValueCannotMakeAMalformedPhysicalHashValid(string physicalHash)
    {
        Parsed parsed = Parse(Marker("aspnet"), "aspnet");
        Dictionary<string, string> inventory = new() { [OriginalOrigin] = physicalHash };
        Assert.ThrowsExactly<InvalidDataException>(() => RequireInventoryPin(parsed, inventory, physicalHash));
    }

    [TestMethod]
    public void ExistingLegacyInventoryPinRemainsRequiredWithoutInventingAControllerOrigin()
    {
        Parsed parsed = Read(Timestamp, null, "pipes", CoreRoot);
        string hash = Hash(Timestamp);
        RequireInventoryPin(parsed, new Dictionary<string, string> { ["legacy-capture.txt"] = hash }, hash);
        Assert.ThrowsExactly<InvalidDataException>(() => RequireInventoryPin(parsed, new Dictionary<string, string>(), hash));
    }

    [TestMethod]
    [DataRow("2026-10-23T18:00:00.1234566+00:00", false)]
    [DataRow("2026-10-23T18:00:00.1234567+00:00", false)]
    [DataRow("2026-10-23T18:00:00.1234568+00:00", true)]
    public void OriginalObservationMustStrictlyPrecedeTheFreshTrxStart(string freshStart, bool expected)
    {
        Parsed parsed = Parse(Marker("unit-runtime"), "unit-runtime");
        DateTimeOffset fresh = DateTimeOffset.ParseExact(freshStart, "O", CultureInfo.InvariantCulture);
        Assert.AreEqual(expected, parsed.Observation.IsBefore(fresh));
    }

    [TestMethod]
    public void NativeNanosecondsAfterTheFreshWholeTickCannotBeRoundedIntoAcceptance()
    {
        const string raw = "2026-10-22T11:37:10,654533965+00:00";
        JsonObject document = Marker("pipes");
        document["observedUtc"] = raw;
        Parsed parsed = Parse(document, "pipes");
        Assert.AreEqual(raw, parsed.Observation.Raw);
        Assert.IsFalse(parsed.Observation.IsBefore(DateTimeOffset.ParseExact(
            "2026-10-22T11:37:10.6545339+00:00", "O", CultureInfo.InvariantCulture)));
        Assert.IsTrue(parsed.Observation.IsBefore(DateTimeOffset.ParseExact(
            "2026-10-22T11:37:10.6545340+00:00", "O", CultureInfo.InvariantCulture)));
    }

    private static Parsed Parse(JsonObject document, string group) => Read(document.ToJsonString(), document, group, CoreRoot);

    private static JsonObject Marker(string group)
    {
        JsonObject document = new() { ["observedUtc"] = Timestamp, ["nativeExit"] = group == "unit-runtime" ? 2 : 0 };
        if (group == "unit-runtime")
        {
            document["actualNativeCoreDllRestorationVerified"] = true;
            document["explicitControllerPdbCorrection"] = true;
            document["historicalLoadedSymbolClaim"] = false;
        }
        else
        {
            document["nativeCoreDllRestorationVerified"] = true;
            document["explicitControllerSymbolRepair"] = true;
            document["historicalLoadedSymbolsClaim"] = false;
        }
        return document;
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
