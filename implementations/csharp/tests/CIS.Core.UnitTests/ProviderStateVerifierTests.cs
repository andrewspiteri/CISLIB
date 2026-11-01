using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ProviderStateVerifierTests
{
#if !CIS_MUTATION_TESTS
    // Retain only text from a real capture after its cleanup has completed.
    private static readonly Dictionary<string, Lazy<Task<string>>> Observations = ProviderStateFixture.Modes.ToDictionary(
        mode => mode, mode => new Lazy<Task<string>>(async () => (await ProviderStateFixture.ExecuteAsync(mode)).ToJsonString()), StringComparer.Ordinal);
#endif

    [TestMethod]
    [DataRow("pid", "extensions", "actual child PID")]
    [DataRow("runtime", "extensions", "actual runtime")]
    [DataRow("lease", "extensions", "physical lifetime")]
    [DataRow("formatter", "scalars", "formatter calls")]
    [DataRow("identity", "scalars", "original state and null exception")]
    [DataRow("ToString", "unsupported", "no arbitrary ToString")]
    [DataRow("filtered-traversal", "filtered", "state enumeration")]
    [DataRow("bounded-traversal", "bounds", "bounded traversal")]
    [DataRow("invalid", "unsupported", "invalid accounting")]
    [DataRow("truncated", "bounds", "truncation accounting")]
    [DataRow("shape", "extensions", "result shape")]
    [DataRow("summary", "unsupported", "stop summary")]
    [DataRow("ticket", "extensions", "core delivery")]
    [DataRow("framing", "extensions", "actual framing")]
    [DataRow("event-name", "extensions", "event names")]
    [DataRow("identity-facts", "extensions", "automatic core facts")]
    [DataRow("provider-id", "precedence", "provider numeric ID")]
    [DataRow("marker", "bounds", "truncation marker")]
    [DataRow("framework-template", "extensions", "framework template/message")]
    [DataRow("framework-fields", "extensions", "framework fields")]
    [DataRow("rendered", "scalars", "supplied rendered message")]
    [DataRow("scalar-type", "scalars", "scalar normalization")]
    [DataRow("decimal", "scalars", "scalar normalization")]
    [DataRow("format", "scalars", "original format")]
    [DataRow("copied-array", "collections", "copied collections")]
    [DataRow("nested-bool-type", "collections", "copied collections")]
    [DataRow("precedence", "precedence", "event/scope precedence")]
    [DataRow("duplicates", "precedence", "event/scope precedence")]
    [DataRow("array-count", "bounds", "array bound")]
    [DataRow("depth-value", "bounds", "depth bound")]
    [DataRow("field-prefix", "bounds", "field bound")]
    [DataRow("trace", "correlation", "frozen Activity")]
    [DataRow("span", "correlation", "frozen Activity")]
    [DataRow("template-size", "template-size", "template bound")]
    [DataRow("blank-line", "extensions", "actual bounded records")]
    [DataRow("oversized", "extensions", "actual bounded records")]
    [DataRow("formatter-kind", "scalars", "formatter calls")]
    [DataRow("pid-kind", "extensions", "actual child PID")]
    [DataRow("shape-nodes", "bounds", "attribute shape")]
    [DataRow("shape-depth", "collections", "attribute shape")]
    [DataRow("array-value", "bounds", "array bound")]
    [DataRow("field-value", "bounds", "field bound")]
    public async Task CorruptedReportsFailAtTheCorrespondingAssertion(string control, string mode, string reason)
    {
#if CIS_MUTATION_TESTS
        JsonObject good = await ProviderStateFixture.ExecuteAsync(mode);
#else
        JsonObject good = JsonNode.Parse(await Observations[mode].Value)!.AsObject();
#endif
        int pid = System.Environment.ProcessId;
        ProviderStateExpectations.Validate(mode, good, pid);
        JsonObject bad = good.DeepClone().AsObject();
        switch (control)
        {
            case "pid": bad["pid"] = pid + 1; break;
            case "runtime": bad["runtime"] = ".NET 10.0.13"; break;
            case "lease": bad["leaseReleased"] = true; break;
            case "formatter": bad["formatterCalls"] = 0; break;
            case "identity": bad["stateIdentityChecks"] = 0; break;
            case "ToString": bad["arbitraryToStringCalls"] = 1; break;
            case "filtered-traversal": bad["stateEnumerationCalls"] = 1; break;
            case "bounded-traversal": bad["arrayEnumerated"] = 2000; break;
            case "invalid": bad["invalid"] = "0"; break;
            case "truncated": bad["truncated"] = "0"; break;
            case "shape": bad["result"]!["extra"] = true; break;
            case "summary": bad["result"]!["loss"] = false; break;
            case "ticket": bad["result"]!["outputs"]![0]!["boundary"] = "0"; break;
            case "framing": bad["bytes"] = Convert.ToBase64String(Convert.FromBase64String(bad["bytes"]!.GetValue<string>())[..^1]); break;
            case "event-name": Wire(bad, row => row["event_name"] = "wrong"); break;
            case "identity-facts": Wire(bad, row => row["service"] = "fake"); break;
            case "provider-id": Wire(bad, row => row["attributes"]!["event_id"] = 999); break;
            case "marker": Wire(bad, row => row.Remove("truncated")); break;
            case "framework-template": Wire(bad, row => row["message_template"] = "wrong"); break;
            case "framework-fields": Wire(bad, row => row["attributes"]!["Count"] = "3"); break;
            case "rendered": Wire(bad, row => row["message"] = "wrong"); break;
            case "scalar-type": Wire(bad, row => row["attributes"]!["bool"] = 1); break;
            case "decimal": Wire(bad, row => row["attributes"]!["decimal"] = 1.2); break;
            case "format": Wire(bad, row => row["message_template"] = "wrong"); break;
            case "copied-array": Wire(bad, row => row["attributes"]!["nested"]!["array"]![0] = 999); break;
            case "nested-bool-type": Wire(bad, row => row["attributes"]!["nested"]!["array"]![2] = 1); break;
            case "precedence": Wire(bad, row => row["attributes"]!["common"] = "outer"); break;
            case "duplicates": Wire(bad, row => row["attributes"]!["choice"] = 1); break;
            case "array-count": Wire(bad, row => row["attributes"]!["array"]!.AsArray().RemoveAt(1020)); break;
            case "depth-value": Wire(bad, row => row["attributes"]!["deep"] = new JsonArray(), 1); break;
            case "field-prefix": Wire(bad, row => row["attributes"]!.AsObject().Remove("k1021"), 2); break;
            case "trace": Wire(bad, row => row["trace_id"] = new string('3', 32)); break;
            case "span": Wire(bad, row => row["span_id"] = new string('0', 16)); break;
            case "template-size": Wire(bad, row => row["message_template"] = new string('x', 8191)); break;
            case "blank-line": bad["bytes"] = Convert.ToBase64String([.. Convert.FromBase64String(bad["bytes"]!.GetValue<string>()), 10]); break;
            case "oversized": Wire(bad, row => row["message"] = new string('x', 16384)); break;
            case "formatter-kind": bad["formatterCalls"] = JsonNode.Parse("1.0"); break;
            case "pid-kind": bad["pid"] = true; break;
            case "shape-nodes": Wire(bad, row => row["attributes"]!["extra"] = 1); break;
            case "shape-depth":
                Wire(bad, row =>
                {
                    JsonArray deep = [];
                    for (int i = 0; i < 9; i++) { deep = new JsonArray(deep); }
                    row["attributes"]!["deep"] = deep;
                });
                break;
            case "array-value": Wire(bad, row => row["attributes"]!["array"]![0] = -1); break;
            case "field-value": Wire(bad, row => row["attributes"]!["k0000"] = -1, 2); break;
            default: throw new InvalidOperationException(control);
        }
        Assert.AreEqual(reason, Assert.ThrowsExactly<InvalidDataException>(() => ProviderStateExpectations.Validate(mode, bad, pid)).Message);
    }

    [TestMethod]
    public void NativeModesMatchTheSharedCaseCatalogInOrder()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            string path = Path.Combine(current.FullName, "conformance/logging/provider-state-cases.json");
            if (!File.Exists(path)) { continue; }
            JsonObject catalog = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            CollectionAssert.AreEqual(ProviderStateExpectations.Modes, catalog["cases"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).ToArray());
            CollectionAssert.AreEqual(ProviderStateExpectations.Modes, ProviderStateFixture.Modes);
            return;
        }
        Assert.Fail("Shared provider state catalog was not found.");
    }

    private static void Wire(JsonObject report, Action<JsonObject> change, int index = 0)
    {
        string[] lines = Encoding.UTF8.GetString(Convert.FromBase64String(report["bytes"]!.GetValue<string>()))[..^1].Split('\n');
        JsonObject[] rows = lines.Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        change(rows[index]);
        report["bytes"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Concat(rows.Select(row => row.ToJsonString() + "\n"))));
    }
}
