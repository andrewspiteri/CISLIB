using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class JsonlReuseTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void ActualLoggerReusesOwnedJsonlWithOneBoundedLinePerCapture(string id)
    {
        JsonObject catalog = Catalog(), row = Row(catalog, id);
        JsonlReuseExpectations.Validate(catalog, row, JsonlReuseFixture.Execute(row));
    }
    [TestMethod]
    [DataRow("bom")]
    [DataRow("crlf")]
    [DataRow("banner")]
    [DataRow("ansi")]
    [DataRow("utf8")]
    [DataRow("type")]
    [DataRow("stream")]
    [DataRow("raw-ls")]
    [DataRow("underfill")]
    public void ComparatorRejectsFramingEncodingTypeAndBatchCorruption(string fault)
    {
        JsonObject catalog = Catalog(), row = Row(catalog, fault == "raw-ls" ? "JSONL-NEWLINE-LS" : fault == "underfill" ? "JSONL-CAP-DEFAULT" : "JSONL-INFO"), actual = JsonlReuseFixture.Execute(row);
        JsonlReuseExpectations.Validate(catalog, row, actual);
        byte[] raw = Convert.FromBase64String(actual["wires"]![0]![0]!.GetValue<string>());
        switch (fault)
        {
            case "bom": raw = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(raw).ToArray(); break;
            case "crlf": raw = raw[..^1].Concat(new byte[] { 13, 10 }).ToArray(); break;
            case "banner": raw = Encoding.UTF8.GetBytes("banner\n").Concat(raw).ToArray(); break;
            case "ansi": raw[0] = 27; break;
            case "utf8": raw[0] = 0xFF; break;
            case "type":
                JsonObject record = JsonNode.Parse(raw)!.AsObject(); record["attributes"]!["integer"] = "9007199254740991";
                raw = Encoding.UTF8.GetBytes(record.ToJsonString() + "\n"); break;
            case "raw-ls": raw = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(raw).Replace("\\u2028", "\u2028", StringComparison.Ordinal)); break;
            case "underfill":
                JsonObject shortRecord = JsonNode.Parse(raw)!.AsObject(); shortRecord["attributes"]!["zPayload"] = new string('\u0001', 10);
                raw = Encoding.UTF8.GetBytes(shortRecord.ToJsonString() + "\n"); break;
            case "stream": actual["stream"] = Convert.ToBase64String(raw); break;
        }
        if (fault != "stream")
        {
            actual["wires"]![0]![0] = Convert.ToBase64String(raw); actual["wires"]![1]![0] = Convert.ToBase64String(raw);
        }
        if (fault is "raw-ls" or "underfill")
        {
            actual["stream"] = Convert.ToBase64String(actual["wires"]![0]!.AsArray().SelectMany(item => Convert.FromBase64String(item!.GetValue<string>())).ToArray());
        }
        if (fault == "utf8") { Assert.ThrowsExactly<DecoderFallbackException>(() => JsonlReuseExpectations.Validate(catalog, row, actual)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => JsonlReuseExpectations.Validate(catalog, row, actual)); }
    }
    [TestMethod]
    public void StructuralComparerAcceptsDifferentJsonMemberOrderAndWhitespace()
    {
        JsonObject catalog = Catalog(), row = Row(catalog, "JSONL-INFO"), actual = JsonlReuseFixture.Execute(row);
        var stream = new List<byte>();
        for (int index = 0; index < 2; index++)
        {
            JsonObject old = JsonNode.Parse(Convert.FromBase64String(actual["wires"]![0]![index]!.GetValue<string>()))!.AsObject();
            var reordered = new JsonObject(old.Reverse().Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone())));
            byte[] raw = Encoding.UTF8.GetBytes(" " + reordered.ToJsonString() + " \n"); stream.AddRange(raw);
            actual["wires"]![0]![index] = Convert.ToBase64String(raw); actual["wires"]![1]![index] = Convert.ToBase64String(raw);
        }
        actual["stream"] = Convert.ToBase64String(stream.ToArray()); JsonlReuseExpectations.Validate(catalog, row, actual);
    }
    private static JsonObject Row(JsonObject catalog, string id) => catalog["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
    private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "jsonl-reuse-cases.json")))!.AsObject();
}
