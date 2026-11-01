using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ProviderBehaviorVerifierTests
{
#if !CIS_MUTATION_TESTS
    // Retain only text from a real capture after its cleanup has completed.
    private static readonly Dictionary<string, Lazy<Task<string>>> Observations = ProviderBehaviorFixture.Modes.ToDictionary(
        mode => mode, mode => new Lazy<Task<string>>(async () => (await ProviderBehaviorFixture.ExecuteAsync(mode)).ToJsonString()), StringComparer.Ordinal);
#endif

    [TestMethod]
    [DataRow("pid", "float", "actual child PID")]
    [DataRow("lifetime", "float", "physical lifetime")]
    [DataRow("formatter", "filtered", "formatter calls")]
    [DataRow("copy", "stopped", "state traversal")]
    [DataRow("full-interest", "full", "interest gates")]
    [DataRow("all-faulted", "all-faulted", "formatter calls")]
    [DataRow("invalid", "rejection", "invalid accounting")]
    [DataRow("truncation", "template-tail", "truncation accounting")]
    [DataRow("exception", "formatter-failure", "formatter exception identity")]
    [DataRow("gap", "full", "output accounting")]
    [DataRow("fault", "partial-fault", "safe fault code")]
    [DataRow("stop", "full", "stop boundary")]
    [DataRow("template", "template-tail", "template after budget")]
    [DataRow("prefix", "template-tail", "bounded retained prefix")]
    [DataRow("prefix-type", "template-tail", "bounded retained prefix")]
    [DataRow("float", "float", "exact Single widening")]
    [DataRow("duplicates", "duplicates", "last visited duplicates")]
    [DataRow("value-type", "full", "accepted prefix")]
    [DataRow("nested-type", "duplicates", "last visited duplicates")]
    [DataRow("root", "root-shape", "root shape policy")]
    [DataRow("partial", "rejection", "no partial admission")]
    [DataRow("blank-line", "float", "bounded record count")]
    [DataRow("service", "float", "core identity")]
    [DataRow("result-shape", "float", "result shape")]
    public async Task CorruptedReportsFailAtTheCorrespondingAssertion(string control, string mode, string reason)
    {
#if CIS_MUTATION_TESTS
        JsonObject good = await ProviderBehaviorFixture.ExecuteAsync(mode);
#else
        JsonObject good = JsonNode.Parse(await Observations[mode].Value)!.AsObject();
#endif
        int pid = System.Environment.ProcessId;
        ProviderBehaviorExpectations.Validate(mode, good, pid);
        JsonObject bad = good.DeepClone().AsObject();
        switch (control)
        {
            case "pid": bad["pid"] = pid + 1; break;
            case "lifetime": bad["leaseReleased"] = true; break;
            case "formatter": bad["formatterCalls"] = 1; break;
            case "copy": bad["stateCalls"] = 1; break;
            case "full-interest": bad["enabledAtProbe"] = false; break;
            case "all-faulted": bad["formatterCalls"] = 2; break;
            case "invalid": bad["invalid"] = "18"; break;
            case "truncation": bad["truncated"] = "0"; break;
            case "exception": bad["exceptionIdentity"] = false; break;
            case "gap": bad["outputs"]![0]!["accepted"] = "3"; break;
            case "fault": bad["outputs"]![0]!["fault"] = "private exception"; break;
            case "stop": bad["result"]!["outputs"]![0]!["boundary"] = "3"; break;
            case "template": Wire(bad, row => row.Remove("message_template")); break;
            case "prefix": Wire(bad, row => row["attributes"]!["Count"] = 7); break;
            case "prefix-type": Wire(bad, row => row["attributes"]!["Items"]![0] = false); break;
            case "float": Wire(bad, row => row["attributes"]!["number"] = 0.1); break;
            case "duplicates": Wire(bad, row => row["message_template"] = "first"); break;
            case "value-type": Wire(bad, row => row["attributes"]!["value"] = JsonNode.Parse("7.0")); break;
            case "nested-type": Wire(bad, row => row["attributes"]!["nested"]!["same"] = JsonNode.Parse("2.0")); break;
            case "root": Wire(bad, row => row["attributes"]!["ignored"] = 7); break;
            case "partial": Wire(bad, row => row["event_name"] = "invalid"); break;
            case "blank-line": bad["bytes"] = Convert.ToBase64String(Convert.FromBase64String(bad["bytes"]!.GetValue<string>()).Concat(new byte[] { 10 }).ToArray()); break;
            case "service": Wire(bad, row => row["service"] = "wrong-service"); break;
            case "result-shape": bad["result"]!["extra"] = true; break;
            default: throw new InvalidOperationException(control);
        }
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => ProviderBehaviorExpectations.Validate(mode, bad, pid));
        Assert.AreEqual(reason, error.Message);
    }

    [TestMethod]
    public void NativeModesMatchTheSharedCaseCatalogInOrder()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            string path = Path.Combine(current.FullName, "conformance", "logging", "provider-behavior-cases.json");
            if (!File.Exists(path)) { continue; }
            JsonObject catalog = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            CollectionAssert.AreEqual(ProviderBehaviorExpectations.Modes, catalog["cases"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).ToArray());
            CollectionAssert.AreEqual(ProviderBehaviorExpectations.Modes, ProviderBehaviorFixture.Modes);
            return;
        }
        Assert.Fail("Shared provider behavior catalog was not found.");
    }

    private static void Wire(JsonObject report, Action<JsonObject> change)
    {
        string[] lines = Encoding.UTF8.GetString(Convert.FromBase64String(report["bytes"]!.GetValue<string>()))[..^1].Split('\n');
        JsonObject[] rows = lines.Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        change(rows[0]);
        report["bytes"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Concat(rows.Select(row => row.ToJsonString() + "\n"))));
    }
}
