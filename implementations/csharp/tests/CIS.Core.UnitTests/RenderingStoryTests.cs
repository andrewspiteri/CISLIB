using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class RenderingStoryTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void EveryViewPreservesTheSameStructurallyQualifiedCapture(string id)
    {
        JsonObject catalog = Catalog(), row = Row(catalog, id);
        RenderingStoryExpectations.Validate(catalog, row, RenderingStoryFixture.Execute(row));
    }
    [TestMethod]
    [DataRow("timestamp")]
    [DataRow("schema")]
    [DataRow("type")]
    [DataRow("exception")]
    [DataRow("array")]
    [DataRow("optional")]
    [DataRow("unsafe")]
    [DataRow("bom")]
    [DataRow("boundary")]
    [DataRow("copy")]
    [DataRow("boolean-number")]
    public void WholeStoryComparerRejectsChangedFactsTypesOrderAndUnsafeViews(string fault)
    {
        JsonObject catalog = Catalog(), row = Row(catalog, fault == "optional" ? "RENDER-ABSENT" : "RENDER-INFO"), actual = RenderingStoryFixture.Execute(row);
        if (fault is "timestamp" or "schema")
        {
            JsonObject source = JsonNode.Parse(Convert.FromBase64String(actual["wires"]![0]![0]!.GetValue<string>()))!.AsObject();
            if (fault == "timestamp") { source["timestamp"] = "2026-10-12T10:11:13.1234567Z"; } else { source["schema_version"] = 2; }
            ReplaceCapture(actual, 0, Encoding.UTF8.GetBytes(source.ToJsonString() + "\n"));
        }
        else if (fault == "copy") { Frame(actual, 1, 0)["jsonMemoryReused"] = false; }
        else
        {
            JsonObject frame = Frame(actual, 0, 1); byte[] line = Convert.FromBase64String(frame["line"]!.GetValue<string>());
            if (fault == "unsafe") { line[0] = 27; }
            else if (fault == "bom") { line = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(line).ToArray(); }
            else if (fault == "boundary") { line = line.Concat(new byte[] { 10 }).ToArray(); }
            else
            {
                Dictionary<string, JsonElement> fields = HumanRenderingExpectations.Read(line, 65536);
                var changed = new JsonObject(fields.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, JsonNode.Parse(pair.Value.GetRawText()))));
                if (fault == "type") { changed["attributes"]!["integer"] = "9007199254740991"; }
                else if (fault == "boolean-number") { changed["attributes"]!["true"] = 1; }
                else if (fault == "array") { changed["attributes"]!["array"] = new JsonArray((JsonNode)1, (JsonNode)2, null); }
                else if (fault == "exception") { changed["exception"]!["inner_exceptions"]![0]!["message"] = "changed"; }
                else { changed["operation_id"] = "invented"; }
                line = HumanLine(changed, " ");
            }
            frame["line"] = Convert.ToBase64String(line);
        }
        Assert.ThrowsExactly<InvalidDataException>(() => RenderingStoryExpectations.Validate(catalog, row, actual));
    }
    [TestMethod]
    [DataRow("json-layout")]
    [DataRow("human-layout")]
    [DataRow("integer-float")]
    [DataRow("number-notation")]
    public void InformationComparisonAllowsObjectAndDisplayOrderAndSpacing(string control)
    {
        JsonObject catalog = Catalog(), row = Row(catalog, "RENDER-INFO"), actual = RenderingStoryFixture.Execute(row);
        if (control is "json-layout" or "integer-float" or "number-notation")
        {
            for (int index = 0; index < 2; index++)
            {
                JsonNode source = JsonNode.Parse(Convert.FromBase64String(actual["wires"]![0]![index]!.GetValue<string>()))!;
                if (control is "integer-float" or "number-notation") { source["attributes"]![control == "integer-float" ? "integer" : "number"] = JsonNode.Parse(control == "integer-float" ? "9007199254740991.0" : "1.25e0"); }
                ReplaceCapture(actual, index, Encoding.UTF8.GetBytes(" " + ReverseMaps(source)!.ToJsonString() + " \n"));
            }
        }
        else
        {
            foreach (JsonNode? frame in actual["renderFrames"]!.AsArray().Where(frame => frame!["format"]!.GetValue<int>() == 0))
            {
                var fields = HumanRenderingExpectations.Read(Convert.FromBase64String(frame!["line"]!.GetValue<string>()), 65536);
                var node = new JsonObject(fields.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, JsonNode.Parse(pair.Value.GetRawText()))));
                frame["line"] = Convert.ToBase64String(HumanLine(ReverseMaps(node)!.AsObject(), "   ", padded: true));
            }
        }
        RenderingStoryExpectations.Validate(catalog, row, actual);
    }
    [TestMethod]
    public void SharedStoryMatrixIncludesAllCriteriaLevelsBoundaryModesAndFormats()
    {
        JsonObject catalog = Catalog(); CollectionAssert.AreEquivalent(new[] { "WP05-S01-AC01", "WP05-S01-AC02", "WP05-S01-AC03", "WP05-S01-AC04" }, catalog["criteria"]!.AsObject().Select(pair => pair.Key).ToArray());
        JsonArray rows = catalog["cases"]!.AsArray(); Assert.AreEqual(21, rows.Count); Assert.AreEqual(rows.Count, rows.Select(row => row!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count());
        CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5 }, rows.Select(row => row!["input"]!["level"]!.GetValue<int>()).Distinct().ToArray());
        CollectionAssert.AreEquivalent(new[] { "full", "absent", "empty", "cap" }, rows.Select(row => row!["input"]!["mode"]!.GetValue<string>()).Distinct().ToArray());
        CollectionAssert.AreEquivalent(new[] { 16384, 65536, 131072 }, rows.Select(row => row!["input"]!["maxEntryBytes"]!.GetValue<int>()).Distinct().ToArray());
        foreach (JsonNode? row in rows) { Assert.IsTrue(JsonNode.DeepEquals(row!["presentation"]!["formats"], JsonNode.Parse("[0,1]")) && JsonNode.DeepEquals(row["presentation"]!["details"], JsonNode.Parse("[0,1]"))); }
    }
    private static JsonObject Frame(JsonObject actual, int format, int detail) => actual["renderFrames"]!.AsArray().Single(frame => frame!["recordIndex"]!.GetValue<int>() == 0 && frame["format"]!.GetValue<int>() == format && frame["detail"]!.GetValue<int>() == detail)!.AsObject();
    private static void ReplaceCapture(JsonObject actual, int index, byte[] raw)
    {
        string value = Convert.ToBase64String(raw); actual["wires"]![0]![index] = value; actual["wires"]![1]![index] = value; actual["afterRendering"]![index] = value;
        foreach (JsonNode? frame in actual["renderFrames"]!.AsArray().Where(frame => frame!["recordIndex"]!.GetValue<int>() == index && frame["format"]!.GetValue<int>() == 1)) { frame!["line"] = value; }
        actual["stream"] = Convert.ToBase64String(actual["wires"]![0]!.AsArray().SelectMany(item => Convert.FromBase64String(item!.GetValue<string>())).ToArray());
    }
    private static JsonNode? ReverseMaps(JsonNode? node) => node switch
    {
        JsonObject value => new JsonObject(value.Reverse().Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, ReverseMaps(pair.Value)))),
        JsonArray value => new JsonArray(value.Select(ReverseMaps).ToArray()),
        _ => node?.DeepClone()
    };
    private static byte[] HumanLine(JsonObject fields, string separator, bool padded = false) => Encoding.UTF8.GetBytes((padded ? "  " : "") + string.Join(separator, fields.Select(pair => pair.Key + "=" + (padded ? "  " : "") + (pair.Value?.ToJsonString() ?? "null"))) + (padded ? "  " : "") + "\n");
    private static JsonObject Row(JsonObject catalog, string id) => catalog["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
    private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "rendering-story-cases.json")))!.AsObject();
}
