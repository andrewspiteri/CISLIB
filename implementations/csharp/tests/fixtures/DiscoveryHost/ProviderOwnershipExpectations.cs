using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderOwnershipExpectations
{
    internal static readonly string[] Modes = ["shared", "resources", "scope-handles", "in-flight"];
    internal static void Validate(string mode, JsonObject report)
    {
        void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
        Require((string?)report["mode"] == mode && (string?)report["runtime"] == ".NET 10.0.12", "Actual case/runtime.");
        Require(report["runtimeLiveAfterProviderDispose"]!.GetValue<bool>() && report["leaseHeldAfterProviderDispose"]!.GetValue<bool>() && report["leaseReleasedAfterCoreStop"]!.GetValue<bool>(), "Application owns runtime and physical output.");
        Require(report["hostDisposals"]!.GetValue<int>() == 0 && report["disposedGuards"]!.GetValue<int>() == 3 && (string?)report["invalid"] == "0" && (string?)report["truncated"] == "0", "Borrowed source and disposed guards.");
        int calls = mode == "shared" ? 2 : mode == "in-flight" ? 1 : 0;
        Require(report["formatterCalls"]!.GetValue<int>() == calls && report["hostCaptureCalls"]!.GetValue<int>() == (mode is "shared" or "in-flight" ? 1 : 0), "Disposed callbacks skipped; in-flight call can finish.");
        Require(report["hostHandleSurvived"]!.GetValue<bool>() == (mode is "shared" or "in-flight") && report["lifoErrors"]!.GetValue<int>() == (mode == "scope-handles" ? 1 : 0), "Caller-owned scope handles.");
        if (mode == "resources") { Require(report["sourceRetainedBefore"]!.GetValue<bool>() && report["sourceReleasedAfter"]!.GetValue<bool>(), "Borrowed source reference released."); }
        else { Require(report["sourceRetainedBefore"] is null && report["sourceReleasedAfter"] is null, "GC probe applicability."); }
        string[] names = mode switch { "shared" => ["before", "direct.scoped", "other"], "resources" => ["after.release"], "scope-handles" => ["inner", "outer", "after"], _ => ["in.flight", "after.flight"] };
        string?[] operations = mode switch { "shared" => ["host.operation", "owned.operation", null], "scope-handles" => ["inner", "outer", null], "in-flight" => ["in.flight", null], _ => [null] };
        byte[] data = Convert.FromBase64String(report["bytes"]!.GetValue<string>()); string text = Encoding.UTF8.GetString(data);
        Require(text.EndsWith('\n') && !text.StartsWith('\uFEFF'), "Actual JSONL framing.");
        JsonObject[] rows = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        Require(rows.Length == names.Length, "Actual output count.");
        for (int i = 0; i < rows.Length; i++)
        {
            JsonObject row = rows[i]; string name = names[i]; bool adapted = name is "before" or "other" or "in.flight";
            Require((string?)row["event_name"] == name && (string?)row["operation_id"] == operations[i] && (string?)row["message"] == (adapted ? "rendered" : "direct"), "Owned/direct/in-flight event facts.");
            Require((string?)row["component"] == (name == "other" ? "Other.Ownership" : adapted ? "Provider.Ownership" : "Direct.Ownership") && (string?)row["status"] == "info" && row["schema_version"]!.GetValue<int>() == 1 && row["resource"]!["process_id"]!.GetValue<int>() == report["pid"]!.GetValue<int>(), "Actual provider/Core wire mapping.");
            if (adapted) { Require(row["attributes"]!.AsObject().Count == 1 && row["attributes"]!["event_id"]!.GetValue<int>() == 17, "Provider event ID."); }
            else { Require(row["attributes"]!.AsObject().Count == 0, "Empty direct attributes."); }
        }
        JsonObject result = report["result"]!.AsObject();
        Require((string?)result["completion"] == "Completed" && result["succeeded"]!.GetValue<bool>() && !result["loss"]!.GetValue<bool>(), "Actual Core stop.");
        JsonArray outputs = result["outputs"]!.AsArray(); Require(outputs.Count == 1, "File output.");
        JsonObject output = outputs[0]!.AsObject(); string count = names.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Require((string?)output["name"] == "file" && (string?)output["boundary"] == count && (string?)output["written"] == count && (string?)output["failed"] == "0" && (string?)output["abandoned"] == "0" && (string?)output["pending"] == "0" && (string?)output["flush"] == "Succeeded", "Separate provider/runtime lifetimes.");
    }
}
