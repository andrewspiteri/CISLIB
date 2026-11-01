using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ProviderFoundationVerifierTests
{
#if !CIS_MUTATION_TESTS
    // Retain only text from a real capture after its cleanup has completed.
    private static readonly Dictionary<string, Lazy<Task<string>>> Observations = ProviderFoundationFixture.Modes.ToDictionary(
        mode => mode, mode => new Lazy<Task<string>>(async () => (await ProviderFoundationFixture.ExecuteAsync(mode)).ToJsonString()), StringComparer.Ordinal);
#endif

    [TestMethod]
    [DataRow("pid", "forward", "actual child PID")]
    [DataRow("exception-identity", "forward", "formatter exception identity")]
    [DataRow("exception-type", "forward", "exception type")]
    [DataRow("exception-message", "forward", "exception message")]
    [DataRow("exception-data", "forward", "exception excludes Data")]
    [DataRow("runtime", "forward", "actual runtime")]
    [DataRow("lease", "forward", "physical lifetime")]
    [DataRow("invalid", "forward", "core invalid count")]
    [DataRow("owner", "nonowning", "provider ownership/error/scope facts")]
    [DataRow("formatter-error", "formatter-error", "provider ownership/error/scope facts")]
    [DataRow("scope-copy", "typed-scope", "provider ownership/error/scope facts")]
    [DataRow("filtered", "filtered", "deferred formatter calls")]
    [DataRow("stopped", "stopped", "deferred formatter calls")]
    [DataRow("shape", "forward", "result shape")]
    [DataRow("summary", "forward", "stop summary")]
    [DataRow("ticket", "forward", "core delivery")]
    [DataRow("framing", "forward", "actual framing")]
    [DataRow("names", "forward", "event names")]
    [DataRow("category", "categories", "category components")]
    [DataRow("facts", "forward", "automatic core facts")]
    [DataRow("message", "forward", "rendered message")]
    [DataRow("extension", "extensions", "framework formatter")]
    [DataRow("scope-value", "typed-scope", "copied typed scope")]
    [DataRow("operation-id", "typed-scope", "copied typed scope")]
    [DataRow("blank-line", "forward", "actual bounded records")]
    [DataRow("oversized", "forward", "actual bounded records")]
    [DataRow("formatter-kind", "forward", "deferred formatter calls")]
    [DataRow("pid-kind", "forward", "actual child PID")]
    public async Task CorruptedReportsFailAtTheCorrespondingAssertion(string control, string mode, string reason)
    {
#if CIS_MUTATION_TESTS
        JsonObject good = await ProviderFoundationFixture.ExecuteAsync(mode);
#else
        JsonObject good = JsonNode.Parse(await Observations[mode].Value)!.AsObject();
#endif
        int pid = System.Environment.ProcessId;
        ProviderFoundationExpectations.Validate(mode, good, pid);
        JsonObject bad = good.DeepClone().AsObject();
        switch (control)
        {
            case "pid": bad["pid"] = pid + 1; break;
            case "exception-identity": bad["formatterExceptionReceived"] = false; break;
            case "exception-type": Wire(bad, row => row["exception"]!["type"] = "wrong"); break;
            case "exception-message": Wire(bad, row => row["exception"]!["message"] = "wrong"); break;
            case "exception-data": Wire(bad, row => row["exception"]!["Data"] = new JsonObject { ["private_data"] = "must not be projected" }); break;
            case "runtime": bad["runtime"] = ".NET 10.0.13"; break;
            case "lease": bad["leaseReleased"] = true; break;
            case "invalid": bad["invalid"] = "1"; break;
            case "owner": bad["sharedRuntimeLiveAfterProviderDispose"] = false; break;
            case "formatter-error": bad["formatterErrorPreserved"] = false; break;
            case "scope-copy": bad["scopeCopied"] = false; break;
            case "filtered" or "stopped": bad["formatterCalls"] = 1; break;
            case "shape": bad["result"]!["extra"] = true; break;
            case "summary": bad["result"]!["succeeded"] = false; break;
            case "ticket": bad["result"]!["outputs"]![0]!["boundary"] = "0"; break;
            case "framing": bad["bytes"] = Convert.ToBase64String(Convert.FromBase64String(bad["bytes"]!.GetValue<string>())[..^1]); break;
            case "names": Wire(bad, row => row["event_name"] = "other"); break;
            case "category": Wire(bad, row => row["component"] = "wrong"); break;
            case "facts": Wire(bad, row => row["service"] = "wrong"); break;
            case "message": Wire(bad, row => row["message"] = "wrong"); break;
            case "extension": Wire(bad, row => row["message"] = "Orders {Count}"); break;
            case "scope-value": Wire(bad, row => row["attributes"]!["scope_attribute"] = "changed"); break;
            case "operation-id": Wire(bad, row => row["operation_id"] = "changed"); break;
            case "blank-line": bad["bytes"] = Convert.ToBase64String([.. Convert.FromBase64String(bad["bytes"]!.GetValue<string>()), 10]); break;
            case "oversized": Wire(bad, row => row["message"] = new string('x', 16384)); break;
            case "formatter-kind": bad["formatterCalls"] = JsonNode.Parse("1.0"); break;
            case "pid-kind": bad["pid"] = true; break;
            default: throw new InvalidOperationException(control);
        }
        Assert.AreEqual(reason, Assert.ThrowsExactly<InvalidDataException>(() => ProviderFoundationExpectations.Validate(mode, bad, pid)).Message);
    }

    [TestMethod]
    public void NativeModesMatchTheSharedCaseCatalogInOrder()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            string path = Path.Combine(current.FullName, "conformance/logging/provider-foundation-cases.json");
            if (!File.Exists(path)) { continue; }
            JsonObject catalog = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            CollectionAssert.AreEqual(ProviderFoundationExpectations.Modes, catalog["cases"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).ToArray());
            CollectionAssert.AreEqual(ProviderFoundationExpectations.Modes, ProviderFoundationFixture.Modes);
            return;
        }
        Assert.Fail("Shared provider foundation catalog was not found.");
    }

    private static void Wire(JsonObject report, Action<JsonObject> change)
    {
        string[] lines = Encoding.UTF8.GetString(Convert.FromBase64String(report["bytes"]!.GetValue<string>()))[..^1].Split('\n');
        JsonObject[] rows = lines.Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        change(rows[0]);
        report["bytes"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Concat(rows.Select(row => row.ToJsonString() + "\n"))));
    }
}
