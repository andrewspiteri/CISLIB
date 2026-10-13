using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileRuntimeStartupExpectations
{
    // Literal expected cases; no expected outcome is copied from the observed report.
    internal static readonly string[] Modes = ["jsonl", "restart", "file-display-loss", "human", "mixed-file-human", "mixed-console-human", "file-only", "console-only", "disabled", "truncated-file", "truncated-both", "frozen", "held-console", "existing-owner", "file-full", "rollback-missing", "rollback-format", "rollback-partial", "rollback-over-limit", "rollback-parent", "rollback-meta-directory", "rollback-retained", "rollback-console-readonly", "rollback-console-throw", "rollback-launch-console", "rollback-launch-file", "physical-console"];
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
    internal static void Validate(string mode, JsonObject actual, byte[]? physicalConsole = null)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal) && actual["mode"]!.GetValue<string>() == mode, "Case identity differs.");
        string? code = mode switch
        {
            "existing-owner" => "FileOwnershipUnavailable",
            "rollback-missing" => "FileInventoryMissing",
            "rollback-format" or "rollback-retained" => "FileInventoryIncompatible",
            "rollback-partial" => "FileRecordBoundaryInvalid",
            "rollback-over-limit" => "FileSizeLimitExceeded",
            "rollback-parent" => "FileInitializationFailed",
            "rollback-meta-directory" => "FileInventoryInitializationFailed",
            "rollback-console-readonly" => "ConsoleStreamNotWritable",
            "rollback-console-throw" => "ConsoleInitializationFailed",
            "rollback-launch-console" or "rollback-launch-file" => "WorkerInitializationFailed",
            _ => null
        };
        bool console = mode is not ("file-only" or "truncated-file" or "disabled"), file = mode is not ("console-only" or "disabled");
        int launches = mode == "rollback-launch-console" ? 1 : (console ? 1 : 0) + (file ? 1 : 0);
        int workers = launches - (mode is "rollback-launch-console" or "rollback-launch-file" ? 1 : 0);
        Require(actual["launches"]!.GetValue<int>() == launches && actual["workers"]!.GetValue<int>() == workers, "Worker acquisition differs.");
        Require(actual["selections"]!.GetValue<int>() == (console && mode != "physical-console" ? 1 : 0), "Console selection differs.");
        foreach (string key in new[] { "joinedAtEnd", "preservedAtStartup", "ownershipReleased", "standardWritable" }) { Require(actual[key]!.GetValue<bool>(), "Startup resource invariant differs: " + key); }
        Require(actual["borrowedDisposes"]!.GetValue<int>() == 0 && !actual["redirectedExists"]!.GetValue<bool>(), "Borrowed resource disposed or path redirected.");
        Require(actual["flowSuppressed"]!.AsArray().Count == workers && actual["flowSuppressed"]!.AsArray().All(x => x!.GetValue<bool>()), "Creator context reached workers.");
        Require(actual["joinedAtReturn"]!.AsArray().Count == workers && actual["joinedAtReturn"]!.AsArray().All(x => x!.GetValue<bool>() == (code is not null)), "Unpublished worker escaped startup.");
        Require(actual["existingRunning"]!.GetValue<bool>() == (mode == "existing-owner"), "Other runtime was stopped.");
        byte[] fileBytes = Convert.FromBase64String(actual["fileBytes"]!.GetValue<string>()), consoleBytes = Convert.FromBase64String(actual["consoleBytes"]!.GetValue<string>());
        if (code is not null)
        {
            string output = mode is "rollback-console-readonly" or "rollback-console-throw" or "rollback-launch-console" ? "console" : "file";
            var expected = new JsonObject { ["output"] = output, ["code"] = code, ["message"] = "Logging output '" + output + "' could not initialize (" + code + ").", ["inner"] = false, ["dataCount"] = 0 };
            Require(JsonNode.DeepEquals(expected, actual["error"]), "Safe startup error differs.");
            Require(actual["result"] is null && actual["diagnostics"] is null && actual["factories"]!.GetValue<int>() == 0 && consoleBytes.Length == 0, "Failed startup was published or emitted a record.");
            foreach (JsonNode? queue in actual["queues"]!.AsArray()) { Require(queue!["state"]!.GetValue<string>() == "Stopped" && queue["accepted"]!.GetValue<long>() == 0 && queue["pending"]!.GetValue<int>() == 0, "Failed worker retained work."); }
            if (mode == "existing-owner") { CheckRecords(fileBytes, false, ["file.prior", "file.after.failure"], ["prior record", "existing writer survives"]); }
            else if (mode is "rollback-missing" or "rollback-format" or "rollback-retained") { Require(fileBytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes("{\"prior\":true}\n")), "Pre-existing record changed."); }
            else if (mode == "rollback-partial") { Require(fileBytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes("{\"prior\":true")), "Partial preimage changed."); }
            else if (mode == "rollback-over-limit") { Require(fileBytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(new string('x', 131072) + "\n")), "Oversize preimage changed."); }
            else { Require(fileBytes.Length == 0, "Failed startup appended bytes."); }
            return;
        }
        Require(actual["error"] is null, "Unexpected startup failure.");
        bool truncated = mode is "truncated-file" or "truncated-both" or "frozen" or "file-display-loss";
        JsonObject result = actual["result"]!.AsObject();
        Require(result["outcome"]!.GetValue<string>() == (mode == "disabled" ? "Filtered" : "Accepted") && result["afterClose"]!.GetValue<string>() == "Stopped" && result["truncated"]!.GetValue<bool>() == truncated, "Write outcome differs.");
        Require(actual["factories"]!.GetValue<int>() == (mode == "disabled" ? 0 : 1), "Factory capture count differs.");
        string[] names = console && file ? ["console", "file"] : console ? ["console"] : file ? ["file"] : [];
        Require(result["outputs"]!.AsArray().Count == names.Length, "Enabled outputs differ.");
        JsonObject diagnostics = actual["diagnostics"]!.AsObject(); Require(diagnostics["state"]!.GetValue<string>() == "Stopped" && diagnostics["truncated"]!.GetValue<long>() == (truncated ? 1 : 0), "Terminal runtime facts differ.");
        Require(diagnostics["outputs"]!.AsArray().Count == names.Length, "Diagnostic outputs differ.");
        for (int n = 0; n < names.Length; n++)
        {
            JsonObject outcome = result["outputs"]![n]!.AsObject(), counts = diagnostics["outputs"]![n]!.AsObject();
            Require(outcome["name"]!.GetValue<string>() == names[n] && outcome["status"]!.GetValue<string>() == "Accepted" && outcome["ticket"]!.GetValue<long>() == 1, "Output ticket differs.");
            Require(counts["name"]!.GetValue<string>() == names[n] && counts["state"]!.GetValue<string>() == "Stopped" && counts["accepted"]!.GetValue<long>() == 1 && counts["written"]!.GetValue<long>() == 1 && counts["failed"]!.GetValue<long>() == 0 && counts["abandoned"]!.GetValue<long>() == 0 && counts["pending"]!.GetValue<long>() == 0 && counts["fault"] is null, "Output disposition differs.");
        }
        string message = mode == "file-display-loss" ? new string('\u200d', 8192) : truncated ? new string('z', 30000) : "owned \u03a9 \U0001f642";
        if (mode == "restart") { CheckRecords(fileBytes, false, ["file.prior", "file.event"], ["prior record", message]); }
        else if (file) { CheckRecords(fileBytes, mode is "human" or "mixed-file-human" or "file-display-loss", ["file.event"], [message], truncated, mode == "file-display-loss" ? 32768 : 16384); }
        else { Require(fileBytes.Length == 0 && !actual["fileExists"]!.GetValue<bool>() && !actual["inventoryExists"]!.GetValue<bool>(), "Disabled file performed I/O."); }
        if (mode == "file-full") { byte[] closed = Convert.FromBase64String(actual["closedBytes"]!.GetValue<string>()); Require(closed.Length == 65536, "Closed full segment changed size."); CheckRecords(closed, false, ["file.prior"], [new string('a', 100000)], true, 65536); }
        if (console && mode != "physical-console") { CheckRecords(consoleBytes, mode is "human" or "mixed-console-human", ["file.event"], [message], truncated && mode != "file-display-loss", mode == "file-display-loss" ? 32768 : 16384); }
        else { Require(consoleBytes.Length == 0, "Unselected controlled stream emitted bytes."); }
        if (mode == "physical-console" && physicalConsole is not null)
        {
            byte[] marker = Encoding.UTF8.GetBytes("CIS_BORROWED_CONSOLE_SURVIVES\n"); Require(physicalConsole.AsSpan().EndsWith(marker), "Borrowed standard stream was closed.");
            CheckRecords(physicalConsole[..^marker.Length], false, ["file.event"], [message]);
        }
        if (mode == "held-console") { Require(actual["held"]!["consolePending"]!.GetValue<long>() == 1 && actual["held"]!["fileWritten"]!.GetValue<long>() == 1 && actual["held"]!["fileBytes"]!.GetValue<string>() == actual["fileBytes"]!.GetValue<string>(), "Slow console blocked file delivery."); }
    }
    private static void CheckRecords(byte[] bytes, bool human, string[] events, string[] messages, bool truncated = false, int cap = 16384)
    {
        string text = new UTF8Encoding(false, true).GetString(bytes); string[] records = text.Split('\n');
        Require(records.Length == events.Length + 1 && records[^1] == "" && !text.StartsWith('\uFEFF'), "Record framing differs.");
        for (int n = 0; n < events.Length; n++)
        {
            byte[] raw = Encoding.UTF8.GetBytes(records[n] + "\n"); Require(raw.Length <= cap && !records[n].Contains('\r') && !records[n].Contains('\u001b'), "Record byte cap/control differs.");
            JsonObject value = human ? HumanRecord(raw) : JsonNode.Parse(raw)!.AsObject();
            foreach ((string key, string expected) in new[] { ("timestamp", "2026-10-13T10:11:12.0000000Z"), (human ? "severity" : "status", "info"), ("event_name", events[n]), ("component", "File.Component"), ("service", "Console.App"), ("env", "test"), ("version", "1.2.3") })
            { Require(value[key]!.GetValue<string>() == expected, "Record identity differs."); }
            string message = value["message"]!.GetValue<string>(); Require(truncated ? message.Length > 0 && message.Length < messages[n].Length && messages[n].StartsWith(message, StringComparison.Ordinal) : message == messages[n], "Record payload differs.");
            Require(truncated ? value["truncated"]!.GetValue<bool>() : value["truncated"] is null, "Record loss marker differs.");
            if (!human) { Require(value["schema_version"]!.GetValue<int>() == 1 && value["resource"]!["instance_id"]!.GetValue<string>() == "11111111-1111-1111-1111-111111111111", "Protected record facts differ."); }
        }
    }
    private static JsonObject HumanRecord(byte[] raw)
    {
        var result = new JsonObject(); int offset = 0;
        while (offset < raw.Length - 1)
        {
            while (offset < raw.Length - 1 && raw[offset] == 32) { offset++; }
            if (offset == raw.Length - 1) { break; }
            int equal = Array.IndexOf(raw, (byte)'=', offset); Require(equal >= offset, "Human field label missing.");
            string name = Encoding.UTF8.GetString(raw.AsSpan(offset, equal - offset)); var reader = new Utf8JsonReader(raw.AsSpan(equal + 1)); using JsonDocument field = JsonDocument.ParseValue(ref reader);
            Require(!result.ContainsKey(name), "Duplicate human field."); result[name] = JsonNode.Parse(field.RootElement.GetRawText()); offset = equal + 1 + (int)reader.BytesConsumed;
            Require(offset == raw.Length - 1 || raw[offset] == 32, "Human field separation differs.");
        }
        return result;
    }
}
