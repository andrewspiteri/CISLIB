using System.Globalization;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class DiagnosticsExpectations
{
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
    private static DateTimeOffset Utc(string token)
    {
        Require(DateTimeOffset.TryParseExact(token, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset value) && value.Offset == TimeSpan.Zero && token == value.ToString("O", CultureInfo.InvariantCulture), "Noncanonical UTC label."); return value;
    }
    internal static void Validate(JsonObject row, JsonObject actual)
    {
        Require(JsonNode.DeepEquals(row["id"], actual["caseId"]) && JsonNode.DeepEquals(row["sourceCaseId"], actual["source"]!["caseId"]), "Diagnostics/source identity differs.");
        JsonObject source = actual["source"]!.AsObject(); JsonArray points = source["diagnostics"]!.AsArray(), expected = row["expected"]!.AsArray();
        Require(points.Count == expected.Count, "Public boundary count differs.");
        DateTimeOffset before = Utc(source["diagnosticsBeforeUtc"]!.GetValue<string>()), after = Utc(source["diagnosticsAfterUtc"]!.GetValue<string>());
        string? shutdown = null; var faults = new Dictionary<string, string>(StringComparer.Ordinal); var previous = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal); long invalid = 0, truncated = 0;
        long Count(JsonObject values, string key)
        {
            string token = values[key]!.GetValue<string>(); Require(long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out long number) && number >= 0 && token == number.ToString(CultureInfo.InvariantCulture), "Noncanonical Int64."); return number;
        }
        for (int index = 0; index < points.Count; index++)
        {
            JsonObject point = points[index]!.AsObject(), wanted = expected[index]!.AsObject();
            foreach (string key in new[] { "phase", "state", "closed", "invalid", "truncated" }) { Require(JsonNode.DeepEquals(point[key], wanted[key]), "Literal public runtime observation differs: " + key); }
            DateTimeOffset observed = Utc(point["observedAtUtc"]!.GetValue<string>()); Require(observed >= before && observed <= after, "Observation time not captured in operation.");
            if (point["closed"]!.GetValue<bool>())
            {
                Require(point["shutdownStartedAtUtc"] is JsonValue, "Closed runtime has no shutdown timestamp.");
                string token = point["shutdownStartedAtUtc"]!.GetValue<string>(); DateTimeOffset at = Utc(token); Require(at >= before && at <= after, "Closure time not captured in operation.");
                if (shutdown is not null) { Require(token == shutdown, "Closure time reset."); } else { shutdown = token; }
            }
            else { Require(point["shutdownStartedAtUtc"] is null, "Open runtime has a shutdown timestamp."); }
            long nextInvalid = Count(point, "invalid"), nextTruncated = Count(point, "truncated"); Require(nextInvalid >= invalid && nextTruncated >= truncated, "Global count decreased."); invalid = nextInvalid; truncated = nextTruncated;
            JsonArray outputs = point["outputs"]!.AsArray(), wantedOutputs = wanted["outputs"]!.AsArray(); Require(outputs.Count == wantedOutputs.Count, "Enabled public output set differs.");
            for (int outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
            {
                JsonObject values = outputs[outputIndex]!.AsObject(), expectedValues = wantedOutputs[outputIndex]!.AsObject();
                foreach (var field in expectedValues) { Require(JsonNode.DeepEquals(values[field.Key], field.Value), "Literal public output observation differs: " + field.Key); }
                string name = values["name"]!.GetValue<string>();
                var counts = new[] { "sequence", "filtered", "attempts", "accepted", "queueFull", "unavailable", "written", "failed", "abandoned", "pending" }.ToDictionary(key => key, key => Count(values, key));
                Require(counts["accepted"] == counts["written"] + counts["failed"] + counts["abandoned"] + counts["pending"] && counts["sequence"] == counts["accepted"], "Public accepted partition/ticket differs.");
                long rejected = counts["queueFull"] > long.MaxValue - counts["unavailable"] ? long.MaxValue : counts["queueFull"] + counts["unavailable"];
                Require(counts["attempts"] == (counts["accepted"] > long.MaxValue - rejected ? long.MaxValue : counts["accepted"] + rejected), "Public admission sum differs.");
                if (previous.TryGetValue(name, out Dictionary<string, long>? prior)) { Require(counts.All(field => field.Key == "pending" || field.Value >= prior[field.Key]), "Public cumulative counter decreased."); }
                previous[name] = counts;
                if (values["fault"] is null) { Require(values["faultAtUtc"] is null, "Nonfaulted timestamp appeared."); }
                else
                {
                    Require(values["faultAtUtc"] is JsonValue, "Faulted output has no fault timestamp.");
                    string token = values["faultAtUtc"]!.GetValue<string>(); DateTimeOffset at = Utc(token); Require(at >= before && at <= after, "Fault time differs.");
                    if (faults.TryGetValue(name, out string? priorFault)) { Require(token == priorFault, "Last fault time lost/reset."); } else { faults[name] = token; }
                }
            }
        }
    }
    internal static void Damage(JsonObject actual, string fault)
    {
        JsonArray points = actual["source"]!["diagnostics"]!.AsArray(); JsonObject final = points.Last()!.AsObject(), output = final["outputs"]![0]!.AsObject();
        switch (fault)
        {
            case "runtime-state": final["state"] = "Running"; break;
            case "closure": final["closed"] = false; break;
            case "observed-time": final["observedAtUtc"] = "2001-01-01T00:00:00.0000000+00:00"; break;
            case "shutdown-time": final["shutdownStartedAtUtc"] = "2001-01-01T00:00:00.0000000+00:00"; break;
            case "global": final["invalid"] = "99"; break;
            case "written": output["written"] = "99"; break;
            case "ticket": output["sequence"] = "99"; break;
            case "fault": output["fault"] = "private/path/payload"; break;
            case "fault-time": output["faultAtUtc"] = "2001-01-01T00:00:00.0000000+00:00"; break;
            case "owned":
                points[0]!["invalid"] = final["invalid"]!.DeepClone();
                points[0]!["truncated"] = final["truncated"]!.DeepClone();
                points[0]!["outputs"] = final["outputs"]!.DeepClone(); break;
            case "shutdown-retention": final["shutdownStartedAtUtc"] = actual["source"]!["diagnosticsBeforeUtc"]!.DeepClone(); break;
            case "fault-retention": points[3]!["outputs"]![0]!["faultAtUtc"] = actual["source"]!["diagnosticsBeforeUtc"]!.DeepClone(); break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }
}
