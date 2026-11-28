using System.Text;
using CIS.RuleEngine;

namespace CIS.Core.UnitTests.Management;

[TestClass]
public sealed class RuleDefinitionCodecTests
{
    private const string DefinitionJson = """
        {
          "rulesetId":"residence",
          "schemaRevision":"schema-1",
          "version":"version-1",
          "mode":"allMatching",
          "displayName":"Café 🌍",
          "metadataShape":{"kind":"object","nullable":true,"properties":[
            {"name":"risk","required":true,"shape":{"kind":"number","nullable":false}}
          ]},
          "columns":[
            {"id":"country","displayName":"Country","inputKind":"string","allowedPresetKinds":["string","stringList"],"exactScalarCount":3},
            {"id":"pensioner","displayName":"Pensioner","inputKind":"boolean","allowedPresetKinds":["boolean"]},
            {"id":"income","displayName":"Income","inputKind":"decimal","allowedPresetKinds":["decimal"],"minimumDecimal":"-1.2500"}
          ],
          "rows":[
            {"ruleId":"fallback","ordinal":20,"state":"test","weight":"0","metadata":null,"conditions":[
              {"columnId":"income","presetKind":"any"},
              {"columnId":"country","presetKind":"missing"},
              {"columnId":"pensioner","presetKind":"any"}
            ]},
            {"ruleId":"local","ordinal":5,"state":"enabled","weight":"-0.12500","comment":"Exact values",
             "metadata":{ "risk":1.25000000000000000000000e+40, "extra":"\u0041" },
             "conditions":[
              {"columnId":"income","presetKind":"decimal","operator":"greaterThanOrEqual","preset":{"kind":"decimal","value":"9007199254740993.0000000000010"}},
              {"columnId":"pensioner","presetKind":"boolean","operator":"equals","preset":{"kind":"boolean","value":false}},
              {"columnId":"country","presetKind":"stringList","operator":"isIn","preset":{"kind":"stringList","value":["MLT","ITA","MLT"]}}
            ]}
          ]
        }
        """;

    [TestMethod]
    public void DecodeOrdersAndDetachesRowsWhileEncodingPreservesExactWireValues()
    {
        byte[] input = Encoding.UTF8.GetBytes(DefinitionJson);
        RuleDefinition definition = RuleDefinitionCodec.Decode(input);
        Array.Clear(input);

        Assert.AreEqual("Café 🌍", definition.DisplayName);
        CollectionAssert.AreEqual(new[] { 5, 20 }, definition.Rows.Select(row => row.Ordinal).ToArray());
        RuleRow row = definition.Rows[0];
        CollectionAssert.AreEqual(new[] { "country", "pensioner", "income" }, row.Conditions.Select(cell => cell.ColumnId).ToArray());
        Assert.AreEqual("-0.12500", row.WeightText);
        Assert.IsFalse(row.Conditions[1].Preset!.BooleanValue);
        Assert.AreEqual("9007199254740993.0000000000010", row.Conditions[2].Preset!.DecimalText);
        Assert.AreEqual(-1.2500m, definition.Columns[2].MinimumDecimal!.Value);
        Assert.AreEqual("""{ "risk":1.25000000000000000000000e+40, "extra":"\u0041" }""", row.Metadata!.Value.GetRawText());
        Assert.AreEqual(System.Text.Json.JsonValueKind.Null, definition.Rows[1].Metadata!.Value.ValueKind);

        byte[] encoded = RuleDefinitionCodec.Encode(definition);
        CollectionAssert.AreEqual(encoded, RuleDefinitionCodec.Encode(RuleDefinitionCodec.Decode(encoded)));
    }

    [TestMethod]
    [DataRow("\"rulesetId\":\"residence\"", "\"rulesetId\":\"residence\",\"ruleset\\u0049d\":\"other\"")]
    [DataRow("\"mode\":\"allMatching\"", "\"mode\":\"AllMatching\"")]
    [DataRow("\"mode\":\"allMatching\"", "\"mode\":\"2\"")]
    [DataRow("\"mode\":\"allMatching\"", "\"mode\":\"allMatching\",\"surprise\":true")]
    [DataRow("\"minimumDecimal\":\"-1.2500\"", "\"minimumDecimal\":-1.25")]
    [DataRow("\"minimumDecimal\":\"-1.2500\"", "\"minimumDecimal\":\"-1e0\"")]
    [DataRow("\"weight\":\"-0.12500\"", "\"weight\":false")]
    [DataRow("\"presetKind\":\"missing\"", "\"presetKind\":\"missing\",\"operator\":\"none\"")]
    [DataRow("\"presetKind\":\"any\"", "\"presetKind\":\"any\",\"preset\":null")]
    [DataRow("\"value\":false", "\"value\":\"false\"")]
    [DataRow("\"value\":false", "\"value\":false,\"surprise\":true")]
    [DataRow("\"kind\":\"number\",\"nullable\":false", "\"kind\":\"number\",\"nullable\":false,\"surprise\":true")]
    [DataRow("\"extra\":\"\\u0041\"", "\"extra\":\"\\uD800\"")]
    public void DecodeRejectsDuplicateUnknownMalformedAndIncorrectlyTypedFields(string before, string after)
    {
        string malformed = DefinitionJson.Replace(before, after, StringComparison.Ordinal);
        Assert.AreNotEqual(DefinitionJson, malformed);
        Assert.ThrowsExactly<ArgumentException>(() => RuleDefinitionCodec.Decode(Encoding.UTF8.GetBytes(malformed)));
    }

    [TestMethod]
    public void DecodeRejectsTrailingContentAndOversizeBeforeReturningADefinition()
    {
        Assert.ThrowsExactly<ArgumentException>(() => RuleDefinitionCodec.Decode(Encoding.UTF8.GetBytes(DefinitionJson + "{}")));
        Assert.ThrowsExactly<ArgumentException>(() =>
            RuleDefinitionCodec.Decode(Encoding.UTF8.GetBytes(DefinitionJson + new string(' ', 128 * 1024))));
    }
}
