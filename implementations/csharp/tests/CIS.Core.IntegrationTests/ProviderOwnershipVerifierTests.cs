using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.IntegrationTests.ProviderReaderVerifier;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ProviderOwnershipVerifierTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    [DataRow("runtime", "shared", "application runtime ownership")]
    [DataRow("lease", "shared", "application runtime ownership")]
    [DataRow("source-disposal", "resources", "borrowed source and disposed guards")]
    [DataRow("guards", "shared", "borrowed source and disposed guards")]
    [DataRow("formatter", "in-flight", "disposal callback work")]
    [DataRow("handles", "shared", "caller scope lifetime")]
    [DataRow("lifo", "scope-handles", "caller scope lifetime")]
    [DataRow("retention", "resources", "source reference release")]
    [DataRow("stop", "resources", "actual Core stop")]
    [DataRow("ticket", "in-flight", "exact Core delivery")]
    [DataRow("operation-shared", "shared", "event ownership facts")]
    [DataRow("operation-scope-handles", "scope-handles", "event ownership facts")]
    [DataRow("operation-in-flight", "in-flight", "event ownership facts")]
    [DataRow("runtime-version", "shared", "actual runtime")]
    [DataRow("pid-kind", "shared", "actual child PID")]
    [DataRow("blank-line", "shared", "bounded records")]
    [DataRow("oversized", "shared", "bounded records")]
    [DataRow("stop-shape", "shared", "result shape")]
    [DataRow("schema-kind", "shared", "wire mapping")]
    [DataRow("physical-pid", "shared", "automatic wire facts")]
    [DataRow("numeric-kind", "shared", "disposal callback work")]
    [DataRow("attribute-kind", "shared", "exact attributes")]
    [DataRow("actual-pid", "shared", "actual child PID")]
    public Task CorruptedReportsFailAtTheCorrespondingAssertion(string control, string mode, string reason) =>
        CheckAsync("provider-ownership", mode, control, reason, TestContext, Change);

    private static bool Change(JsonObject a, string control)
    {
        switch (control)
        {
            case "runtime": a["runtimeLiveAfterProviderDispose"] = false; break;
            case "lease": a["leaseHeldAfterProviderDispose"] = false; break;
            case "source-disposal": a["hostDisposals"] = 1; break;
            case "guards": a["disposedGuards"] = 2; break;
            case "formatter": a["formatterCalls"] = 2; break;
            case "handles": a["hostHandleSurvived"] = false; break;
            case "lifo": a["lifoErrors"] = 0; break;
            case "retention": a["sourceReleasedAfter"] = false; break;
            case "stop": a["result"]!["succeeded"] = false; break;
            case "ticket": a["result"]!["outputs"]![0]!["boundary"] = "1"; break;
            case "operation-shared": Wire(a, row => row["operation_id"] = "host.operation", 1); break;
            case "operation-scope-handles": Wire(a, row => row["operation_id"] = "outer"); break;
            case "operation-in-flight": Wire(a, row => row.Remove("operation_id")); break;
            default: return false;
        }
        return true;
    }
}
