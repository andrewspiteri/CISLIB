using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.IntegrationTests.ProviderReaderVerifier;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ProviderHostStoryVerifierTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    [DataRow("formatter-identity", "factory-formatter", "formatter exception identity")]
    [DataRow("cohort", "async-owned", "callbacks and cohort")]
    [DataRow("formatter", "async-external", "callbacks and cohort")]
    [DataRow("shared-task", "shutdown", "observed stop and attention")]
    [DataRow("shared-result", "shutdown", "observed stop and attention")]
    [DataRow("finish", "shutdown", "observed stop and attention")]
    [DataRow("loss-attention", "shutdown-loss", "observed stop and attention")]
    [DataRow("runtime", "factory-lifetime", "factory and Core ownership")]
    [DataRow("guards", "factory-lifetime", "factory and Core ownership")]
    [DataRow("invalid", "shutdown-loss", "actual accounting and lease")]
    [DataRow("lease", "shutdown", "actual accounting and lease")]
    [DataRow("ordering", "shutdown", "shutdown ordering")]
    [DataRow("loss-result", "shutdown-loss", "actual Core stop result")]
    [DataRow("ticket", "async-owned", "exact drain delivery")]
    [DataRow("owned-copy", "async-owned", "exact event attributes")]
    [DataRow("external-copy", "async-external", "exact event attributes")]
    [DataRow("identity", "factory-precedence", "automatic identity")]
    [DataRow("Activity", "factory-precedence", "protected Activity and identity")]
    [DataRow("factory-scope", "factory-lifetime", "operation category message")]
    [DataRow("runtime-version", "async-owned", "actual runtime")]
    [DataRow("pid-kind", "async-owned", "actual child PID")]
    [DataRow("blank-line", "async-owned", "bounded records")]
    [DataRow("oversized", "async-owned", "bounded records")]
    [DataRow("stop-shape", "async-owned", "result shape")]
    [DataRow("schema-kind", "async-owned", "wire schema/status/truncation")]
    [DataRow("physical-pid", "async-owned", "automatic identity")]
    [DataRow("numeric-kind", "async-owned", "callbacks and cohort")]
    [DataRow("attribute-kind", "async-owned", "exact event attributes")]
    [DataRow("actual-pid", "async-owned", "actual child PID")]
    public Task CorruptedReportsFailAtTheCorrespondingAssertion(string control, string mode, string reason) =>
        CheckAsync("provider-host-story", mode, control, reason, TestContext, Change);

    private static bool Change(JsonObject a, string control)
    {
        switch (control)
        {
            case "formatter-identity": a["formatterExceptionPreserved"] = false; break;
            case "cohort": a["arrivals"] = 1; break;
            case "formatter": a["formatterCalls"] = 10; break;
            case "shared-task": a["sameStopTask"] = false; break;
            case "shared-result": a["sameStopResult"] = false; break;
            case "finish": a["workFinishedAtStop"] = false; break;
            case "loss-attention": a["ownerAttention"] = false; break;
            case "runtime": a["sharedLiveAfterFactoryDispose"] = false; break;
            case "guards": a["disposedGuards"] = 0; break;
            case "invalid": a["invalid"] = "0"; break;
            case "lease": a["leaseReleased"] = false; break;
            case "ordering": a["phases"] = new JsonArray(a["phases"]!.AsArray().Reverse().Select(item => item?.DeepClone()).ToArray()); break;
            case "loss-result": a["result"]!["loss"] = false; break;
            case "ticket": a["result"]!["outputs"]![0]!["boundary"] = "8"; break;
            case "owned-copy": Wire(a, row => row["attributes"]!["phase"] = "changed"); break;
            case "external-copy": Wire(a, row => row["attributes"]!["phase"] = "initial"); break;
            case "identity": Wire(a, row => row["service"] = "fake"); break;
            case "Activity": Wire(a, row => row["trace_id"] = "fake"); break;
            case "factory-scope": Wire(a, row => row["operation_id"] = "host.fake", 1); break;
            default: return false;
        }
        return true;
    }
}
