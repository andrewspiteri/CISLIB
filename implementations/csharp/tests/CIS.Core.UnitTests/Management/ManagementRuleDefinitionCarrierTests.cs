using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using CIS.Management;

namespace CIS.Core.UnitTests.Management;

[TestClass]
public sealed class ManagementRuleDefinitionCarrierTests
{
    private const int ChunkBytes = 48 * 1024;
    private const int DefinitionLimit = 128 * 1024;
    private const string DefinitionJson = """
        {"rulesetId":"residence","schemaRevision":"schema-1","version":"version-01","mode":"allMatching",
         "columns":[{"id":"income","displayName":"Income 🌍","inputKind":"decimal","allowedPresetKinds":["decimal"]}],
         "rows":[{"ruleId":"standard","ordinal":0,"state":"enabled","weight":"-1.2500",
          "metadata":{"display":"\u0041","risk":9.00000000000000000000000001e+10},
          "conditions":[{"columnId":"income","presetKind":"decimal","operator":"greaterThan",
          "preset":{"kind":"decimal","value":"9007199254740993.0000000000010"}}]}]}
        """;

    [TestMethod]
    public void RoundTripReturnsValidatedDefinitionAndDefendsOriginalUtf8()
    {
        byte[] input = Encoding.UTF8.GetBytes(DefinitionJson);
        string source = ManagementRuleDefinitionCarrier.Encode(input);
        ManagementRuleDefinitionCarrier carrier = ManagementRuleDefinitionCarrier.Decode(source);

        CollectionAssert.AreEqual(input, carrier.GetDefinitionBytes());
        Array.Clear(carrier.GetDefinitionBytes());
        CollectionAssert.AreEqual(input, carrier.GetDefinitionBytes());
        Assert.AreEqual("version-01", carrier.Definition.Version);
        Assert.AreEqual("-1.2500", carrier.Definition.Rows[0].WeightText);
        Assert.AreEqual("9007199254740993.0000000000010", carrier.Definition.Rows[0].Conditions[0].Preset!.DecimalText);
        Assert.AreEqual("""{"display":"\u0041","risk":9.00000000000000000000000001e+10}""",
            carrier.Definition.Rows[0].Metadata!.Value.GetRawText());
    }

    [TestMethod]
    public void FullDefinitionLimitFitsThreeBoundedSourceStrings()
    {
        byte[] version = DefinitionOfSize(DefinitionLimit);
        string source = ManagementRuleDefinitionCarrier.Encode(version);

        using JsonDocument json = JsonDocument.Parse(source);
        int[] characters = json.RootElement.GetProperty("definitionChunks").EnumerateArray()
            .Select(chunk => chunk.GetString()!.Length).ToArray();
        CollectionAssert.AreEqual(new[] { 65_536, 65_536, 43_691 }, characters);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(source) <= 256 * 1024);
        CollectionAssert.AreEqual(version, ManagementRuleDefinitionCarrier.Decode(source).GetDefinitionBytes());
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("""{"definitionChunks":[]}""")]
    [DataRow("""{"definitionChunks":null}""")]
    [DataRow("""{"definitionChunks":[""]}""")]
    [DataRow("""{"definitionChunks":[0]}""")]
    [DataRow("""{"definitionChunks":["Zg=="]}""")]
    [DataRow("""{"definitionChunks":["Zg "]}""")]
    [DataRow("""{"definitionChunks":["+/"]}""")]
    [DataRow("""{"definitionChunks":["a","b","c","d"]}""")]
    [DataRow("""{"definitionChunks":["Zg"],"extra":true}""")]
    [DataRow("""{"definitionChunks":["Zg"],"definition\u0043hunks":["Zg"]}""")]
    [DataRow("""{"definitionChunks":["Zg"]}{}""")]
    public void DecodeRejectsMalformedOrEmptyCarrier(string source)
        => Assert.ThrowsExactly<ArgumentException>(() => ManagementRuleDefinitionCarrier.Decode(source));

    [TestMethod]
    public void DecodeRejectsAlternativeChunkBoundariesAndNonCanonicalTailBits()
    {
        byte[] definition = Encoding.UTF8.GetBytes(DefinitionJson);
        string split = Source(Base64Url.EncodeToString(definition.AsSpan(0, 3)),
            Base64Url.EncodeToString(definition.AsSpan(3)));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementRuleDefinitionCarrier.Decode(split));

        byte[] padded = DefinitionOfSize(definition.Length + (4 - definition.Length % 3) % 3);
        string encoded = Base64Url.EncodeToString(padded);
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        char alternate = alphabet[alphabet.IndexOf(encoded[^1]) + 1];
        Assert.ThrowsExactly<ArgumentException>(() => ManagementRuleDefinitionCarrier.Decode(Source(encoded[..^1] + alternate)));
    }

    [TestMethod]
    public void BothEntriesRejectOversizedOrInvalidDefinitions()
    {
        byte[] oversized = DefinitionOfSize(DefinitionLimit + 1);
        Assert.ThrowsExactly<ArgumentException>(() => ManagementRuleDefinitionCarrier.Encode(oversized));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementRuleDefinitionCarrier.Decode(Source(
            Base64Url.EncodeToString(oversized.AsSpan(0, ChunkBytes)),
            Base64Url.EncodeToString(oversized.AsSpan(ChunkBytes, ChunkBytes)),
            Base64Url.EncodeToString(oversized.AsSpan(2 * ChunkBytes)))));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementRuleDefinitionCarrier.Decode(Source(new string('A', 65_537))));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementRuleDefinitionCarrier.Decode(Source("Zg") + new string(' ', 256 * 1024)));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementRuleDefinitionCarrier.Encode("{}"u8));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementRuleDefinitionCarrier.Decode(Source(Base64Url.EncodeToString("{}"u8))));
    }

    private static string Source(params string[] chunks) => JsonSerializer.Serialize(new { definitionChunks = chunks });

    private static byte[] DefinitionOfSize(int length)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(DefinitionJson);
        int original = bytes.Length;
        Array.Resize(ref bytes, length);
        bytes.AsSpan(original).Fill((byte)' ');
        return bytes;
    }
}
