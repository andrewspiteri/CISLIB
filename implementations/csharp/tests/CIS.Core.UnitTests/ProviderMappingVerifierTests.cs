using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ProviderMappingVerifierTests
{
    [TestMethod]
    [DataRow("pid", "levels", "actual child PID")]
    [DataRow("runtime", "levels", "actual runtime")]
    [DataRow("lease", "levels", "physical lifetime")]
    [DataRow("invalid", "levels", "core invalid count")]
    [DataRow("formatter", "levels", "supplied formatter calls")]
    [DataRow("none", "levels", "None formatter not called")]
    [DataRow("exception-identity", "payload", "formatter exception identity")]
    [DataRow("shape", "levels", "result shape")]
    [DataRow("summary", "levels", "stop summary")]
    [DataRow("ticket", "levels", "core delivery")]
    [DataRow("framing", "levels", "actual framing")]
    [DataRow("severity", "levels", "mapped severity")]
    [DataRow("category", "names", "category component")]
    [DataRow("name", "names", "mapped event name")]
    [DataRow("message", "payload", "supplied rendered message")]
    [DataRow("event-id-value", "ids", "numeric event ID")]
    [DataRow("event-id-type", "ids", "numeric event ID")]
    [DataRow("facts", "levels", "automatic core facts")]
    [DataRow("exception-type", "payload", "exception type")]
    [DataRow("exception-message", "payload", "exception message")]
    [DataRow("exception-data", "payload", "exception excludes Data")]
    [DataRow("timestamp", "structured", "valid capture timestamp")]
    [DataRow("date", "structured", "valid capture timestamp")]
    [DataRow("structured-number-type", "structured", "equivalent structured values")]
    [DataRow("structured-bool", "structured", "equivalent structured values")]
    [DataRow("structured-template", "structured", "equivalent structured template")]
    [DataRow("structured-trace", "structured", "equivalent correlation")]
    [DataRow("direct-equivalence", "payload", "equivalent core record")]
    [DataRow("blank-line", "levels", "actual bounded records")]
    [DataRow("oversized", "levels", "actual bounded records")]
    [DataRow("formatter-kind", "levels", "supplied formatter calls")]
    [DataRow("pid-kind", "levels", "actual child PID")]
    [DataRow("span-zero", "structured", "equivalent correlation")]
    [DataRow("span-kind", "structured", "equivalent correlation")]
    [DataRow("schema-kind", "levels", "automatic core facts")]
    [DataRow("event-id-float", "ids", "numeric event ID")]
    public async Task CorruptedReportsFailAtTheCorrespondingAssertion(string control, string mode, string reason)
    {
        JsonObject good = await ProviderMappingFixture.ExecuteAsync(mode);
        int pid = System.Environment.ProcessId;
        ProviderMappingExpectations.Validate(mode, good, pid);
        JsonObject bad = good.DeepClone().AsObject();
        switch (control)
        {
            case "pid": bad["pid"] = pid + 1; break;
            case "runtime": bad["runtime"] = ".NET 10.0.13"; break;
            case "lease": bad["leaseReleased"] = true; break;
            case "invalid": bad["invalid"] = "1"; break;
            case "formatter": bad["formatterCalls"] = 0; break;
            case "none": bad["noneFormatterCalls"] = 1; break;
            case "exception-identity": bad["formatterExceptionReceived"] = false; break;
            case "shape": bad["result"]!["extra"] = true; break;
            case "summary": bad["result"]!["succeeded"] = false; break;
            case "ticket": bad["result"]!["outputs"]![0]!["boundary"] = "0"; break;
            case "framing": bad["bytes"] = Convert.ToBase64String(Convert.FromBase64String(bad["bytes"]!.GetValue<string>())[..^1]); break;
            case "severity": Wire(bad, row => row["status"] = "info"); break;
            case "category": Wire(bad, row => row["component"] = "wrong"); break;
            case "name": Wire(bad, row => row["event_name"] = "wrong"); break;
            case "message": Wire(bad, row => row["message"] = "unrendered input"); break;
            case "event-id-value": Wire(bad, row => row["attributes"]!["event_id"] = 99); break;
            case "event-id-type": Wire(bad, row => row["attributes"]!["event_id"] = "0"); break;
            case "facts": Wire(bad, row => row["service"] = "wrong"); break;
            case "exception-type": Wire(bad, row => row["exception"]!["type"] = "wrong"); break;
            case "exception-message": Wire(bad, row => row["exception"]!["message"] = "wrong"); break;
            case "exception-data": Wire(bad, row => row["exception"]!["Data"] = new JsonObject { ["private_data"] = "must not be projected" }); break;
            case "timestamp": Wire(bad, row => row["timestamp"] = "ignored-invalid-time"); break;
            case "date": Wire(bad, row => row["timestamp"] = "2026-02-30T00:00:00.0000000Z"); break;
            case "structured-number-type": Wire(bad, row => row["attributes"]!["Count"] = JsonNode.Parse("3.0")); break;
            case "structured-bool": Wire(bad, row => row["attributes"]!["Items"]![2] = 1); break;
            case "structured-template": Wire(bad, row => row.Remove("message_template")); break;
            case "structured-trace": Wire(bad, row => row["trace_id"] = new string('0', 32)); break;
            case "direct-equivalence": Wire(bad, row => row["env"] = "changed", 1); break;
            case "blank-line": bad["bytes"] = Convert.ToBase64String([.. Convert.FromBase64String(bad["bytes"]!.GetValue<string>()), 10]); break;
            case "oversized": Wire(bad, row => row["message"] = new string('x', 16384)); break;
            case "formatter-kind": bad["formatterCalls"] = JsonNode.Parse("6.0"); break;
            case "pid-kind": bad["pid"] = true; break;
            case "span-zero": Wire(bad, row => row["span_id"] = new string('0', 16)); break;
            case "span-kind": Wire(bad, row => row["span_id"] = new string('G', 16)); break;
            case "schema-kind": Wire(bad, row => row["schema_version"] = JsonNode.Parse("1.0")); break;
            case "event-id-float": Wire(bad, row => row["attributes"]!["event_id"] = JsonNode.Parse("0.0")); break;
            default: throw new InvalidOperationException(control);
        }
        Assert.AreEqual(reason, Assert.ThrowsExactly<InvalidDataException>(() => ProviderMappingExpectations.Validate(mode, bad, pid)).Message);
    }

    [TestMethod]
    public void NativeModesMatchTheSharedCaseCatalogInOrder()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            string path = Path.Combine(current.FullName, "conformance/logging/provider-mapping-cases.json");
            if (!File.Exists(path)) { continue; }
            JsonObject catalog = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            CollectionAssert.AreEqual(ProviderMappingExpectations.Modes, catalog["cases"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).ToArray());
            CollectionAssert.AreEqual(ProviderMappingExpectations.Modes, ProviderMappingFixture.Modes);
            return;
        }
        Assert.Fail("Shared provider mapping catalog was not found.");
    }

    [TestMethod]
    public async Task EquivalentStructuredPropertyOrderIsAccepted()
    {
        JsonObject report = await ProviderMappingFixture.ExecuteAsync("structured");
        Wire(report, row =>
        {
            JsonObject attributes = row["attributes"]!.AsObject();
            var reordered = new JsonObject();
            foreach (KeyValuePair<string, JsonNode?> item in attributes.Reverse()) { reordered[item.Key] = item.Value?.DeepClone(); }
            row["attributes"] = reordered;
        });
        ProviderMappingExpectations.Validate("structured", report, System.Environment.ProcessId);
    }

    private static void Wire(JsonObject report, Action<JsonObject> change, int index = 0)
    {
        string[] lines = Encoding.UTF8.GetString(Convert.FromBase64String(report["bytes"]!.GetValue<string>()))[..^1].Split('\n');
        JsonObject[] rows = lines.Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        change(rows[index]);
        report["bytes"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Concat(rows.Select(row => row.ToJsonString() + "\n"))));
    }
}
