using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class RuntimeCreateExpectations
{
    internal static readonly string[] Modes = ["file", "disabled", "invalid-settings", "invalid-metadata", "contended"];
    private static void Require(bool value) { if (!value) { throw new InvalidOperationException("Public creation expectation failed."); } }
    internal static void Validate(string mode, JsonObject a)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal) && a["mode"]!.GetValue<string>() == mode && a["runtime"]!.GetValue<string>() == ".NET 10.0.12");
        if (mode is "invalid-settings" or "invalid-metadata")
        {
            Require(a["error"]!.GetValue<string>() == "CoreConfigurationException" && a["noFileArtifacts"]!.GetValue<bool>()); return;
        }
        Require(a["result"]!["completion"]!.GetValue<string>() == "Completed" && a["result"]!["succeeded"]!.GetValue<bool>() && !a["result"]!["loss"]!.GetValue<bool>());
        Require(a["sameTask"]!.GetValue<bool>() && a["preserved"]!.GetValue<bool>() && a["factPid"]!.GetValue<int>() == a["pid"]!.GetValue<int>());
        Require(a["contention"]!.GetValue<bool>() == (mode == "contended") && a["released"]!.GetValue<bool>() == (mode != "disabled") && a["frozen"]!.GetValue<bool>() == (mode != "disabled"));
        Require(mode == "contended" ? a["error"]!.GetValue<string>() == "FileOwnershipUnavailable" : a["error"] is null);
        byte[] bytes = Convert.FromBase64String(a["bytes"]!.GetValue<string>());
        if (mode == "disabled") { Require(a["noFileArtifacts"]!.GetValue<bool>() && bytes.Length == 0 && a["result"]!["outputs"]!.AsArray().Count == 0); }
        else
        {
            Require(!a["noFileArtifacts"]!.GetValue<bool>() && bytes[^1] == (byte)'\n');
            string[] lines = new UTF8Encoding(false, true).GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries); Require(lines.Length == 1);
            JsonObject record = JsonNode.Parse(lines[0])!.AsObject();
            Require(record["event_name"]!.GetValue<string>() == "factory" && record["attributes"]!["producer_pid"]!.GetValue<int>() == a["pid"]!.GetValue<int>() && record["message"]!.GetValue<string>() == "Ω 🙂");
            Require(a["application"]!.GetValue<string>() == "CIS.Core.DiscoveryFixture");
        }
    }
}
