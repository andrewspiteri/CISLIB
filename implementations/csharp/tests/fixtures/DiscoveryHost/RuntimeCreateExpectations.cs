using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class RuntimeCreateExpectations
{
    internal static readonly string[] Modes = ["file", "disabled", "invalid-settings", "invalid-metadata", "contended"];
    private static void Require(bool value, string reason) { if (!value) { throw new InvalidOperationException(reason); } }
    internal static void Validate(string mode, JsonObject a, int? processId = null)
    {
        LifecycleReportChecks.Identity(mode, a, processId);
        Require(Modes.Contains(mode, StringComparer.Ordinal) && a["mode"]!.GetValue<string>() == mode && a["runtime"]!.GetValue<string>() == ".NET 10.0.12", "creation mode/runtime");
        if (mode is "invalid-settings" or "invalid-metadata")
        {
            Require((string?)a["error"] == "CoreConfigurationException" && a["noFileArtifacts"]!.GetValue<bool>(), "creation validation"); return;
        }
        Require(a["result"]!["completion"]!.GetValue<string>() == "Completed" && a["result"]!["succeeded"]!.GetValue<bool>() && !a["result"]!["loss"]!.GetValue<bool>(), "creation stop");
        Require(a["sameTask"]!.GetValue<bool>() && a["preserved"]!.GetValue<bool>() && a["factPid"]!.GetValue<int>() == a["pid"]!.GetValue<int>(), "creation process facts");
        Require(a["contention"]!.GetValue<bool>() == (mode == "contended") && a["released"]!.GetValue<bool>() == (mode != "disabled") && a["frozen"]!.GetValue<bool>() == (mode != "disabled"), "creation ownership");
        Require(mode == "contended" ? a["error"]!.GetValue<string>() == "FileOwnershipUnavailable" : a["error"] is null, "creation contention");
        byte[] bytes = Convert.FromBase64String(a["bytes"]!.GetValue<string>());
        if (mode == "disabled") { Require(a["noFileArtifacts"]!.GetValue<bool>() && bytes.Length == 0 && a["result"]!["outputs"]!.AsArray().Count == 0, "creation disabled output"); }
        else
        {
            Require(!a["noFileArtifacts"]!.GetValue<bool>() && bytes.Length > 0 && bytes[^1] == (byte)'\n', "creation record framing");
            string[] lines = new UTF8Encoding(false, true).GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries); Require(lines.Length == 1, "creation record count");
            JsonObject record = JsonNode.Parse(lines[0])!.AsObject();
            Require(record["event_name"]!.GetValue<string>() == "factory" && record["attributes"]!["producer_pid"]!.GetValue<int>() == a["pid"]!.GetValue<int>() && record["message"]!.GetValue<string>() == "Ω 🙂", "creation original record");
            Require(a["application"]!.GetValue<string>() == "CIS.Core.DiscoveryFixture", "creation application");
        }
        JsonObject result = a["result"]!.AsObject(); LifecycleReportChecks.Result(result, globalCounts: false);
        LifecycleReportChecks.Names(result["outputs"]!.AsArray(), mode == "disabled" ? [] : ["file"]);
        JsonObject[] records = LifecycleReportChecks.Records(a["bytes"], a["pid"]!.GetValue<int>());
        Require(records.Select(record => (string)record["event_name"]!).SequenceEqual(mode == "disabled" ? Array.Empty<string>() : ["factory"]), "creation record selection");
        Require(records.All(record => (string?)record["status"] == "info"), "creation wire severity");
        if (mode != "disabled")
        {
            JsonNode output = result["outputs"]![0]!;
            Require(LifecycleReportChecks.Number(output["boundary"]) == 1 && LifecycleReportChecks.Number(output["written"]) == 1
                && new[] { "failed", "abandoned", "pending" }.All(key => LifecycleReportChecks.Number(output[key]) == 0) && (string?)output["flush"] == "Succeeded", "creation delivery");
        }
    }
}
