using System.Globalization;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileFailureExpectations
{
    internal static void Validate(string mode, JsonObject actual)
    {
        bool render = mode.StartsWith("render", StringComparison.Ordinal); string code = render ? "RenderTooLarge" : "RetentionDeleteFailed";
        void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
        Require(actual["mode"]!.GetValue<string>() == mode && actual["composition"]!.GetValue<string>() == (render ? "trusted-internal-small-segment" : "configured-runtime"), "composition");
        Require(actual["segmentMaxBytes"]!.GetValue<int>() == (render ? 16 : 65536), "segment limit");
        if (render) { byte[] line = Convert.FromBase64String(actual["attemptedFileBytes"]!.GetValue<string>()); Require(line.Length > 16 && line.Length <= 16384 && line[^1] == 10 && actual["writeAttempts"]!.GetValue<int>() == 1, "single oversized Human attempt"); }
        else { Require(actual["attemptedFileBytes"] is null && actual["writeAttempts"] is null, "configured writer composition"); }
        JsonObject error = actual["error"]!.AsObject(); Require(error["code"]!.GetValue<string>() == code && error["message"]!.GetValue<string>() == "Logging file output failed (" + code + ")." && !error["inner"]!.GetValue<bool>() && error["dataCount"]!.GetValue<int>() == 0, "safe fixed error");
        Require(actual["leaseReleased"]!.GetValue<bool>() && actual["activeReleased"]!.GetValue<bool>(), "owned handle release"); Require(actual["borrowedDisposes"]!.GetValue<int>() == 0 && actual["launches"]!.GetValue<int>() == 2 && actual["factories"]!.GetValue<int>() == 4 && actual["stopped"]!.GetValue<string>() == "Stopped", "lifecycle/borrowed");
        Require(JsonNode.DeepEquals(actual["before"], actual["after"]) && JsonNode.DeepEquals(actual["namesBefore"], actual["namesAfter"]), "no further growth/preimage");
        JsonArray results = actual["results"]!.AsArray(); Require(results.Count == 4, "result count");
        for (int index = 0; index < 4; index++)
        {
            JsonObject result = results[index]!.AsObject(); Require(result["outcome"]!.GetValue<string>() == (index == 3 ? "PartiallyAccepted" : "Accepted") && !result["truncated"]!.GetValue<bool>(), "write outcome"); JsonArray outputs = result["outputs"]!.AsArray(); Require(outputs.Count == 2, "output fanout");
            foreach (string name in new[] { "console", "file" })
            {
                JsonObject output = outputs.Single(x => x!["name"]!.GetValue<string>() == name)!.AsObject(); bool failed = index == 3 && name == "file";
                Require(output["status"]!.GetValue<string>() == (failed ? "Faulted" : "Accepted") && (failed ? output["sequence"] is null : output["sequence"]!.GetValue<long>() == index + 1), "admission tickets");
            }
        }
        foreach (string phase in new[] { "fault", "final" })
        {
            JsonObject diagnostics = actual[phase]!.AsObject(); Require(diagnostics["invalid"]!.GetValue<long>() == 0 && diagnostics["truncated"]!.GetValue<long>() == 0, "loss counters");
            JsonObject file = diagnostics["outputs"]!.AsArray().Single(x => x!["name"]!.GetValue<string>() == "file")!.AsObject(); Require(file["state"]!.GetValue<string>() == (phase == "final" ? "Stopped" : "Faulted") && file["faultCode"]!.GetValue<string>() == code, "terminal fault code");
            DateTimeOffset at = DateTimeOffset.ParseExact(file["faultAt"]!.GetValue<string>(), "O", CultureInfo.InvariantCulture); Require(at.Offset == TimeSpan.Zero, "fault UTC");
            Require(file["accepted"]!.GetValue<long>() == 3 && file["written"]!.GetValue<long>() == 0 && file["failed"]!.GetValue<long>() == 1 && file["abandoned"]!.GetValue<long>() == 2 && file["pending"]!.GetValue<long>() == 0 && file["unavailable"]!.GetValue<long>() == (phase == "final" ? 1 : 0), "file loss accounting");
            JsonObject console = diagnostics["outputs"]!.AsArray().Single(x => x!["name"]!.GetValue<string>() == "console")!.AsObject(); Require(console["state"]!.GetValue<string>() == (phase == "final" ? "Stopped" : "Running") && console["faultCode"] is null, "healthy console state");
            if (phase == "final") { Require(console["accepted"]!.GetValue<long>() == 4 && console["written"]!.GetValue<long>() == 4 && console["failed"]!.GetValue<long>() == 0 && console["abandoned"]!.GetValue<long>() == 0 && console["pending"]!.GetValue<long>() == 0, "console progress"); }
        }
    }
}
