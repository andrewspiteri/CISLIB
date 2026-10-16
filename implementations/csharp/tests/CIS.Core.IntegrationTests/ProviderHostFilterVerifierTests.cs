using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.IntegrationTests.ProviderReaderVerifier;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ProviderHostFilterVerifierTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    [DataRow("formatter", "broad", "skipped callback work")]
    [DataRow("enumeration", "core-off", "skipped callback work")]
    [DataRow("narrow-interest", "narrow-host", "effective host interest")]
    [DataRow("lease", "provider-rule", "physical lifetime")]
    [DataRow("threshold", "trace-file", "core thresholds")]
    [DataRow("stop", "broad", "actual stop result")]
    [DataRow("ticket", "broad", "separate core delivery")]
    [DataRow("live", "category-rule", "live delivery accounting")]
    [DataRow("missing-debug", "broad", "destination severity records")]
    [DataRow("runtime-version", "broad", "actual runtime")]
    [DataRow("pid-kind", "broad", "actual child PID")]
    [DataRow("blank-line", "broad", "bounded records")]
    [DataRow("oversized", "broad", "bounded records")]
    [DataRow("stop-shape", "broad", "result shape")]
    [DataRow("schema-kind", "broad", "wire level")]
    [DataRow("physical-pid", "broad", "automatic facts")]
    [DataRow("numeric-kind", "broad", "skipped callback work")]
    [DataRow("attribute-kind", "broad", "structured state")]
    [DataRow("actual-pid", "broad", "actual child PID")]
    public Task CorruptedReportsFailAtTheCorrespondingAssertion(string control, string mode, string reason) =>
        CheckAsync("provider-host-filter", mode, control, reason, TestContext, Change);

    private static bool Change(JsonObject a, string control)
    {
        switch (control)
        {
            case "formatter": a["formatterCalls"] = 6; break;
            case "enumeration": a["stateEnumerations"] = 1; break;
            case "narrow-interest": a["hostDebug"] = true; break;
            case "lease": a["leaseReleased"] = false; break;
            case "threshold": a["fileLevel"] = "Debug"; break;
            case "stop": a["result"]!["loss"] = true; break;
            case "ticket": a["result"]!["outputs"]![1]!["boundary"] = "4"; break;
            case "live": a["outputs"]![1]!["accepted"] = "4"; break;
            case "missing-debug":
                string[] lines = Encoding.UTF8.GetString(Convert.FromBase64String(a["fileBytes"]!.GetValue<string>()))[..^1].Split('\n');
                a["fileBytes"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('\n', lines.Skip(1)) + "\n"));
                break;
            default: return false;
        }
        return true;
    }
}
