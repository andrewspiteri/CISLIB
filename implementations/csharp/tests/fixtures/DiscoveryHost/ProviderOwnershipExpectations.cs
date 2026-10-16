using System.Text.Json.Nodes;
using static CIS.Tests.DiscoveryFixture.ProviderReportChecks;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderOwnershipExpectations
{
    internal static readonly string[] Modes = ["shared", "resources", "scope-handles", "in-flight"];
    internal static void Validate(string mode, JsonObject a, int? expectedPid = null)
    {
        Header(mode, Modes, a, expectedPid);
        Require(Same(a["runtimeLiveAfterProviderDispose"], "true") && Same(a["leaseHeldAfterProviderDispose"], "true") &&
            Same(a["leaseReleasedAfterCoreStop"], "true"), "application runtime ownership");
        Require(Integer(a["hostDisposals"], 0) && Integer(a["disposedGuards"], 3), "borrowed source and disposed guards");
        Require((string?)a["invalid"] == "0" && (string?)a["truncated"] == "0", "valid untruncated events");
        Require(Integer(a["formatterCalls"], mode == "shared" ? 2 : mode == "in-flight" ? 1 : 0) &&
            Integer(a["hostCaptureCalls"], mode is "shared" or "in-flight" ? 1 : 0), "disposal callback work");
        Require(Same(a["hostHandleSurvived"], JsonValue.Create(mode is "shared" or "in-flight")) &&
            Integer(a["lifoErrors"], mode == "scope-handles" ? 1 : 0), "caller scope lifetime");
        if (mode == "resources")
        {
            Require(Same(a["sourceRetainedBefore"], "true") && Same(a["sourceReleasedAfter"], "true"), "source reference release");
        }
        else { Require(a["sourceRetainedBefore"] is null && a["sourceReleasedAfter"] is null, "GC probe applicability"); }
        string[] names = mode switch
        {
            "shared" => ["before", "direct.scoped", "other"],
            "resources" => ["after.release"],
            "scope-handles" => ["inner", "outer", "after"],
            _ => ["in.flight", "after.flight"]
        };
        string?[] operations = mode switch
        {
            "shared" => ["host.operation", "owned.operation", null],
            "scope-handles" => ["inner", "outer", null],
            "in-flight" => ["in.flight", null],
            _ => [null]
        };
        JsonObject[] rows = Rows(a, names.Length);
        for (int i = 0; i < rows.Length; i++)
        {
            JsonObject row = rows[i]; string name = names[i]; bool adapted = name is "before" or "other" or "in.flight";
            Require((string?)row["event_name"] == name && (string?)row["operation_id"] == operations[i] &&
                (string?)row["message"] == (adapted ? "rendered" : "direct"), "event ownership facts");
            Require((string?)row["component"] == (name == "other" ? "Other.Ownership" : adapted ? "Provider.Ownership" : "Direct.Ownership") &&
                (string?)row["status"] == "info" && Integer(row["schema_version"], 1), "wire mapping");
            Identity(row, a, true, "automatic wire facts");
            Require(Same(row["attributes"], adapted ? """{"event_id":17}""" : "{}"), "exact attributes");
            Require(!row.ContainsKey("truncated"), "untruncated wire");
        }
        Stop(a, names.Length, true, false, "actual Core stop", "exact Core delivery");
    }
}
