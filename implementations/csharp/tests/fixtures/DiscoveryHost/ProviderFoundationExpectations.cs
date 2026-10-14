using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderFoundationExpectations
{
    internal static readonly string[] Modes = ["forward", "extensions", "categories", "filtered", "nonowning", "stopped", "typed-scope", "formatter-error"];
    private static void Require(bool condition) { if (!condition) { throw new InvalidDataException("Provider foundation expectation failed."); } }
    internal static void Validate(string mode, JsonObject a)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal) && a["mode"]!.GetValue<string>() == mode && a["runtime"]!.GetValue<string>() == ".NET 10.0.12");
        bool physical = a["physical"]!.GetValue<bool>(); Require(a["borrowedConsoleOpen"]!.GetValue<bool>() && a["leaseReleased"]!.GetValue<bool>() == physical && a["invalid"]!.GetValue<string>() == "0");
        Require(a["sharedRuntimeLiveAfterProviderDispose"]!.GetValue<bool>() == (mode == "nonowning") && a["formatterErrorPreserved"]!.GetValue<bool>() == (mode == "formatter-error") && a["scopeCopied"]!.GetValue<bool>() == (mode == "typed-scope"));
        Require(a["formatterExceptionReceived"]!.GetValue<bool>() == (mode == "forward"));
        int count = mode is "filtered" or "stopped" or "formatter-error" ? 0 : mode is "categories" or "nonowning" ? 2 : 1;
        Require(a["formatterCalls"]!.GetValue<int>() == (mode is "forward" or "nonowning" or "typed-scope" or "formatter-error" ? 1 : mode == "categories" ? 2 : 0));
        Require(a["result"]!["completion"]!.GetValue<string>() == "Completed" && a["result"]!["succeeded"]!.GetValue<bool>() && !a["result"]!["loss"]!.GetValue<bool>());
        JsonArray outputs = a["result"]!["outputs"]!.AsArray(); Require(outputs.Count == 1); JsonNode output = outputs[0]!;
        Require(output["name"]!.GetValue<string>() == (physical ? "file" : "console") && long.Parse(output["boundary"]!.GetValue<string>(), CultureInfo.InvariantCulture) == count && long.Parse(output["written"]!.GetValue<string>(), CultureInfo.InvariantCulture) == count && output["failed"]!.GetValue<string>() == "0" && output["abandoned"]!.GetValue<string>() == "0" && output["pending"]!.GetValue<string>() == "0" && output["flush"]!.GetValue<string>() == "Succeeded");
        byte[] bytes = Convert.FromBase64String(a["bytes"]!.GetValue<string>());
        if (count == 0) { Require(bytes.Length == 0); return; }
        Require(bytes[^1] == (byte)'\n' && !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
        JsonObject[] rows = new UTF8Encoding(false, true).GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => JsonNode.Parse(s)!.AsObject()).ToArray(); Require(rows.Length == count);
        string[] names = mode switch { "categories" => ["first", "second"], "nonowning" => ["direct", "other"], "extensions" => ["log.message"], "typed-scope" => ["scoped"], _ => ["forward"] };
        string[] components = mode switch { "categories" => ["Orders.First", "Orders.Second"], "nonowning" => ["Direct", "Other.Provider"], _ => ["Provider.Foundation"] };
        Require(rows.Select(x => x["event_name"]!.GetValue<string>()).SequenceEqual(names));
        Require(rows.Select(x => x["component"]!.GetValue<string>()).SequenceEqual(components));
        if (mode == "extensions") { Require(rows[0]["message"]!.GetValue<string>() == "Orders 3"); }
        if (mode == "forward")
        {
            Require(rows[0]["message"]!.GetValue<string>() == "Ω 🙂" && rows[0]["status"]!.GetValue<string>() == "error");
            JsonObject error = rows[0]["exception"]!.AsObject();
            Require(error["type"]!.GetValue<string>() == "System.InvalidOperationException" && error["message"]!.GetValue<string>() == "controlled provider failure");
            Require(error.All(x => x.Key is "type" or "message" or "stack_trace" or "inner_exceptions" or "truncated"));
            Require(!rows[0].ToJsonString().Contains("private_data", StringComparison.Ordinal) && !rows[0].ToJsonString().Contains("must not be projected", StringComparison.Ordinal));
        }
        if (mode == "typed-scope") { Require(rows[0]["operation_id"]!.GetValue<string>() == "operation.original" && rows[0]["attributes"]!["scope_attribute"]!.GetValue<string>() == "original"); }
    }
}
