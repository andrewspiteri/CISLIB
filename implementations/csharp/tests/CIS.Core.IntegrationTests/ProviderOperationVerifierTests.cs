using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.IntegrationTests.ProviderReaderVerifier;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ProviderOperationVerifierTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    [DataRow("pid", "owned", "actual child PID")]
    [DataRow("lease", "host", "physical lifetime")]
    [DataRow("formatter", "owned", "formatter calls")]
    [DataRow("owned-errors", "invalid", "owned ID errors")]
    [DataRow("callback-loss", "callback-errors", "invalid accounting")]
    [DataRow("omitted-loss", "host-omitted", "invalid accounting")]
    [DataRow("ticket", "event", "core delivery")]
    [DataRow("names", "nested-owned", "event names")]
    [DataRow("schema", "owned", "core wire mapping")]
    [DataRow("service", "protected", "automatic core facts")]
    [DataRow("marker", "host-omitted", "truncation marker")]
    [DataRow("owned-copy", "owned", "operation and attributes")]
    [DataRow("host-copy", "host", "operation and attributes")]
    [DataRow("nested", "nested-owned", "operation and attributes")]
    [DataRow("blank", "nested-host", "operation and attributes")]
    [DataRow("duplicate", "duplicates", "operation and attributes")]
    [DataRow("case", "duplicates", "operation and attributes")]
    [DataRow("nonstring", "nonstring", "operation and attributes")]
    [DataRow("explicit-value", "nonstring", "owned ID errors")]
    [DataRow("event-override", "event", "operation and attributes")]
    [DataRow("trace", "protected", "protected identity and Activity")]
    [DataRow("bound-id", "bounds", "operation and attributes")]
    [DataRow("late-id", "bounds", "operation and attributes")]
    [DataRow("max-id", "invalid", "operation and attributes")]
    [DataRow("omitted-parent", "host-omitted", "operation and attributes")]
    [DataRow("runtime-version", "owned", "actual runtime")]
    [DataRow("pid-kind", "owned", "actual child PID")]
    [DataRow("blank-line", "owned", "bounded records")]
    [DataRow("oversized", "owned", "bounded records")]
    [DataRow("stop-shape", "owned", "result shape")]
    [DataRow("schema-kind", "owned", "core wire mapping")]
    [DataRow("physical-pid", "owned", "automatic core facts")]
    [DataRow("numeric-kind", "owned", "formatter calls")]
    [DataRow("attribute-kind", "owned", "operation and attributes")]
    public Task CorruptedReportsFailAtTheCorrespondingAssertion(string control, string mode, string reason) =>
        CheckAsync("provider-operation", mode, control, reason, TestContext, Change);

    private static bool Change(JsonObject a, string control)
    {
        switch (control)
        {
            case "pid": a["pid"] = a["pid"]!.GetValue<int>() + 1; break;
            case "lease": a["leaseReleased"] = false; break;
            case "formatter": a["formatterCalls"] = 0; break;
            case "owned-errors": a["ownedErrors"] = 0; break;
            case "callback-loss": a["invalid"] = "0"; break;
            case "omitted-loss": a["invalid"] = "0"; break;
            case "ticket": a["result"]!["outputs"]![0]!["boundary"] = "1"; break;
            case "names": Wire(a, row => row["event_name"] = "wrong"); break;
            case "schema": Wire(a, row => row["schema_version"] = true); break;
            case "service": Wire(a, row => row["service"] = "event.fake"); break;
            case "marker": Wire(a, row => row["truncated"] = true); break;
            case "owned-copy": Wire(a, row => row["operation_id"] = "changed"); break;
            case "host-copy": Wire(a, row => row["attributes"]!["common"] = "before"); break;
            case "nested": Wire(a, row => row["operation_id"] = "outer", 1); break;
            case "blank": Wire(a, row => row["operation_id"] = "", 2); break;
            case "duplicate": Wire(a, row => row["operation_id"] = "one"); break;
            case "case": Wire(a, row => row["attributes"]!["operation_id"] = "ordinary", 3); break;
            case "nonstring": Wire(a, row => row["operation_id"] = "7"); break;
            case "explicit-value": a["ownedErrors"] = 1; break;
            case "event-override": Wire(a, row => row["operation_id"] = "scope", 1); break;
            case "trace": Wire(a, row => row["trace_id"] = "event.fake"); break;
            case "bound-id": Wire(a, row => row.Remove("operation_id")); break;
            case "late-id": Wire(a, row => row["operation_id"] = "not-visited", 1); break;
            case "max-id": Wire(a, row => row["operation_id"] = new string('x', 254)); break;
            case "omitted-parent": Wire(a, row => row["operation_id"] = "parent"); break;
            default: return false;
        }
        return true;
    }
}
