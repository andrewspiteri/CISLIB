import assert from "node:assert/strict";
import test from "node:test";
import { captureConfigurationDraft, captureConfigurationReason, configurationDraftBytes, configurationFields, configurationNumberToken, formatConfigurationJson,
  configurationStringValue, omitConfigurationValue, setConfigurationValue } from "../src/configuration-draft.ts";

const shape = { kind: "object", nullable: false, members: [
  { name: "Enabled", required: true, shape: { kind: "boolean", nullable: false } },
  { name: "Count", required: true, shape: { kind: "integer", nullable: false } },
  { name: "Group", required: false, shape: { kind: "object", nullable: true, members: [
    { name: "Text", required: false, shape: { kind: "string", nullable: true } }
  ] } }
] };

test("scalar token patches preserve every unrelated large number Unicode escape false zero and null byte", () => {
  const raw = ' { "Huge" : 9007199254740993, "Exponent":1e300, "Unicode":"\\u00e9", "Enabled":false, "Zero":0, "Null":null, "Group" : { "Text" : "before", "Unknown":42 } } ';
  const changed = setConfigurationValue(raw, ["Group", "Text"], JSON.stringify("Café 日本語"));
  assert.equal(changed, raw.replace('"before"', '"Café 日本語"'));
  assert.equal(setConfigurationValue(changed, ["Enabled"], "true"), changed.replace('"Enabled":false', '"Enabled":true'));
  assert.equal(configurationNumberToken("9007199254740993"), "9007199254740993");
  assert.equal(configurationNumberToken("1e300"), "1e300");
});

test("pretty formatting changes whitespace only and preserves exact numeric string and Unicode tokens", () => {
  const raw = String.raw` {"Huge":9007199254740993,"Tiny":1e-999,"NegativeZero":-0,"Text":"\u00e9 / 日本語 \\\" {[,]}","Array":[false,null,{},[],{"n":1.2300e+40}]} `;
  const formatted = formatConfigurationJson(raw);
  assert.equal(formatted.valid, true);
  assert.equal(formatted.json, String.raw`{
  "Huge": 9007199254740993,
  "Tiny": 1e-999,
  "NegativeZero": -0,
  "Text": "\u00e9 / 日本語 \\\" {[,]}",
  "Array": [
    false,
    null,
    {},
    [],
    {
      "n": 1.2300e+40
    }
  ]
}`);
  assert.deepEqual(formatConfigurationJson(formatted.json), formatted, "Formatting is idempotent without re-encoding any value token.");
});

test("format refuses incomplete duplicate or oversized results without returning a replacement draft", () => {
  for (const invalid of ['{"x":', '{"x":1,"x":2}', '{"x":"\ud800"}', '\ufeff{}']) {
    const result = formatConfigurationJson(invalid); assert.equal(result.valid, false); assert.equal("json" in result, false);
  }
  const compact = '{"Items":[' + "0,".repeat(39999) + "0]}";
  assert.equal(captureConfigurationDraft(compact), compact, "The original compact source is valid and within its native byte limit.");
  const overflow = formatConfigurationJson(compact);
  assert.equal(overflow.valid, false); assert.equal("json" in overflow, false);
  assert.equal(formatConfigurationJson("{ \n \t }").json, "{}");
});

test("omitting first middle last or only members changes no remaining token and never prunes the parent", () => {
  const raw = '{ "a":1, "b":9007199254740993, "c":null }';
  assert.equal(omitConfigurationValue(raw, ["a"]), '{ "b":9007199254740993, "c":null }');
  assert.equal(omitConfigurationValue(raw, ["b"]), '{ "a":1, "c":null }');
  assert.equal(omitConfigurationValue(raw, ["c"]), '{ "a":1, "b":9007199254740993 }');
  assert.equal(omitConfigurationValue('{"Group":{"Text":"one"},"Unknown":0}', ["Group", "Text"]), '{"Group":{},"Unknown":0}');
  assert.equal(setConfigurationValue('{"Unknown":0}', ["Group", "Text"], '"new"'), '{"Unknown":0,"Group":{"Text":"new"}}');
});

test("explicit null stays distinct from omission and blocks implicit child-object replacement", () => {
  const fields = configurationFields(shape, '{"Group":null,"Enabled":false,"Count":0}').fields;
  assert.equal(fields.find(field => field.path.join("/") === "Group").rawValue, "null");
  assert.equal(fields.find(field => field.path.join("/") === "Group/Text").blocked, true);
  assert.throws(() => setConfigurationValue('{"Group":null}', ["Group", "Text"], '"new"'));
  assert.equal(omitConfigurationValue('{"Group":null}', ["Group"]), "{}");
  assert.equal(configurationFields(shape, "{}").fields.find(field => field.path[0] === "Enabled").present, false);
});

test("finite forms expose declared fields only and fall back for collections or excessive depth/count", () => {
  const fields = configurationFields(shape, '{"Enabled":false,"Count":9007199254740993,"Unknown":"not a form field"}');
  assert.equal(fields.fields.some(field => field.path[0] === "Unknown"), false);
  assert.equal(fields.fields.find(field => field.path[0] === "Count").rawValue, "9007199254740993");
  const collection = { kind: "object", nullable: false, members: [{ name: "Items", required: false, shape: { kind: "array", nullable: false, element: { kind: "number", nullable: false } } }] };
  assert.equal(configurationFields(collection, '{"Items":[9007199254740993]}').fallback, true);
  const many = { kind: "object", nullable: false, members: Array.from({ length: 140 }, (_, index) => ({ name: "f" + index, required: false, shape: { kind: "string", nullable: false } })) };
  assert.equal(configurationFields(many, "{}").fields.length, 128);
  assert.equal(configurationFields(many, "{}").fallback, true);
});

test("existing native lexical validation bounds exact raw drafts and newly inserted values", () => {
  const maximum = '{"Text":"' + "x".repeat(262133) + '"}';
  assert.equal(configurationDraftBytes(maximum), 262144); assert.equal(captureConfigurationDraft(maximum), maximum);
  assert.throws(() => captureConfigurationDraft(maximum + " "));
  for (const invalid of ['{"a":1,"a":2}', '{"x":"\ud800"}', "[]", '\ufeff{}']) { assert.throws(() => captureConfigurationDraft(invalid)); }
  for (const invalid of ["01", "NaN", "Infinity", "1,2", "", "0x10"]) { assert.throws(() => configurationNumberToken(invalid)); }
  assert.throws(() => setConfigurationValue('{"Text":"before"}', ["Text"], '"bad\ud800"'));
  assert.equal(configurationStringValue('"\\u00e9"'), "é");
  assert.equal(setConfigurationValue("{}", ["__proto__"], "false"), '{"__proto__":false}');
  assert.equal({}.polluted, undefined);
  assert.equal(captureConfigurationReason("🌍".repeat(256)), "🌍".repeat(256));
  assert.throws(() => captureConfigurationReason("🌍".repeat(257)));
  assert.throws(() => captureConfigurationReason("line\nbreak"));
  assert.throws(() => captureConfigurationReason("   "));
});
