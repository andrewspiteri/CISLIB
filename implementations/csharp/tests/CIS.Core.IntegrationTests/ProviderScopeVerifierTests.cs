using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.IntegrationTests.ProviderReaderVerifier;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ProviderScopeVerifierTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    [DataRow("pid", "owned", "actual child PID")]
    [DataRow("lease", "external", "physical lifetime")]
    [DataRow("formatter", "filtered", "formatter calls")]
    [DataRow("owner-error", "invalid", "owned scope errors")]
    [DataRow("copy-work", "bounds", "bounded scope visits")]
    [DataRow("host-work", "filtered", "filtered host capture")]
    [DataRow("invalid", "depth", "invalid accounting")]
    [DataRow("loss", "bounds", "truncation accounting")]
    [DataRow("stop", "owned", "core delivery")]
    [DataRow("facts", "owned", "automatic core facts")]
    [DataRow("schema-type", "owned", "core wire mapping")]
    [DataRow("owned", "owned", "owned snapshot")]
    [DataRow("host-copy", "external", "host capture snapshot")]
    [DataRow("host-precedence", "hybrid", "host overlay")]
    [DataRow("event-precedence", "hybrid", "event overlay")]
    [DataRow("direct-context", "hybrid", "direct core scope")]
    [DataRow("extension", "extensions", "Microsoft scope fields")]
    [DataRow("async", "async-external", "isolated async scopes")]
    [DataRow("popped", "async-owned", "popped async scopes")]
    [DataRow("prefix-type", "bounds", "bounded owned prefix")]
    [DataRow("nested-depth", "bounds", "bounded host depth")]
    [DataRow("trace", "correlation", "frozen Activity")]
    [DataRow("incompatible-fields", "host-incompatible", "incompatible frame rejected")]
    [DataRow("incompatible-marker", "host-incompatible", "truncation marker")]
    [DataRow("replacement", "replacement", "replacement and ownership")]
    [DataRow("runtime-version", "owned", "actual runtime")]
    [DataRow("pid-kind", "owned", "actual child PID")]
    [DataRow("blank-line", "owned", "bounded records")]
    [DataRow("oversized", "owned", "bounded records")]
    [DataRow("stop-shape", "owned", "result shape")]
    [DataRow("schema-kind", "owned", "core wire mapping")]
    [DataRow("physical-pid", "owned", "automatic core facts")]
    [DataRow("numeric-kind", "owned", "formatter calls")]
    [DataRow("attribute-kind", "owned", "owned snapshot")]
    public Task CorruptedReportsFailAtTheCorrespondingAssertion(string control, string mode, string reason) =>
        CheckAsync("provider-scope", mode, control, reason, TestContext, Change);

    private static bool Change(JsonObject a, string control)
    {
        switch (control)
        {
            case "pid": a["pid"] = a["pid"]!.GetValue<int>() + 1; break;
            case "lease": a["leaseReleased"] = false; break;
            case "formatter": a["formatterCalls"] = 1; break;
            case "owner-error": a["ownedErrors"] = 0; break;
            case "copy-work": a["fieldVisits"] = 2000; break;
            case "host-work": a["filteredHostCalls"] = 1; break;
            case "invalid": a["invalid"] = "1"; break;
            case "loss": a["truncated"] = "0"; break;
            case "stop": a["result"]!["outputs"]![0]!["boundary"] = "0"; break;
            case "facts": Wire(a, row => row["service"] = "fake"); break;
            case "schema-type": Wire(a, row => row["schema_version"] = true); break;
            case "owned": Wire(a, row => row["attributes"]!["owner"] = "changed"); break;
            case "host-copy": Wire(a, row => row["attributes"]!["owner"] = "after"); break;
            case "host-precedence": Wire(a, row => row["attributes"]!["common"] = "core"); break;
            case "event-precedence": Wire(a, row => row["attributes"]!["common"] = "host", 1); break;
            case "direct-context": Wire(a, row => row["attributes"]!["host"] = true, 2); break;
            case "extension": Wire(a, row => row["attributes"]!["Owner"] = "wrong"); break;
            case "async": Wire(a, row => row["operation_id"] = "other"); break;
            case "popped": Wire(a, row => row["operation_id"] = "leak", 2); break;
            case "prefix-type": Wire(a, row => row["attributes"]!["k0000"] = false); break;
            case "nested-depth": Wire(a, row => row["attributes"]!["deep"] = new JsonArray(), 1); break;
            case "trace": Wire(a, row => row["trace_id"] = new string('3', 32)); break;
            case "incompatible-fields": Wire(a, row => row["attributes"]!["HttpMethod"] = "GET"); break;
            case "incompatible-marker": Wire(a, row => row["truncated"] = true); break;
            case "replacement": Wire(a, row => row["attributes"]!["owner"] = "first", 1); break;
            default: return false;
        }
        return true;
    }
}
