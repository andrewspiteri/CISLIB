using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

internal static class ProviderReaderVerifier
{
    internal static async Task CheckAsync(string family, string mode, string control, string reason, TestContext context,
        Func<JsonObject, string, bool> change)
    {
        ProviderReportCapture captured = await ProviderReportProcess.RunAsync(family, mode, context);
        Validate(family, mode, captured.Report, captured.ConsoleText, captured.ProcessId);
        JsonObject bad = captured.Report.DeepClone().AsObject();
        string key = family == "provider-host-filter" ? "fileBytes" : "bytes";
        switch (control)
        {
            case "runtime-version": bad["runtime"] = ".NET 10.0.13"; break;
            case "actual-pid": bad["pid"] = captured.ProcessId + 1; break;
            case "pid-kind": bad["pid"] = true; break;
            case "blank-line": bad[key] = Convert.ToBase64String([.. Convert.FromBase64String(bad[key]!.GetValue<string>()), 10]); break;
            case "oversized": Wire(bad, row => row["message"] = new string('x', 16384), key: key); break;
            case "stop-shape": bad["result"]!["extra"] = true; break;
            case "schema-kind": Wire(bad, row => row["schema_version"] = JsonNode.Parse("1.0"), key: key); break;
            case "physical-pid": Wire(bad, row => row["resource"]!["process_id"] = 0, key: key); break;
            case "numeric-kind": bad["formatterCalls"] = JsonNode.Parse(bad["formatterCalls"]!.ToJsonString() + ".0"); break;
            case "attribute-kind": Wire(bad, row => row["attributes"]!["event_id"] = JsonNode.Parse("17.0"), key: key); break;
            default: if (!change(bad, control)) { throw new InvalidOperationException(control); } break;
        }
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            Validate(family, mode, bad, captured.ConsoleText, captured.ProcessId));
        Assert.AreEqual(reason, error.Message, family + "/" + control);
    }
    internal static void Validate(string family, string mode, JsonObject report, string consoleText, int pid)
    {
        switch (family)
        {
            case "provider-scope": ProviderScopeExpectations.Validate(mode, report, pid, physical: true); break;
            case "provider-operation": ProviderOperationExpectations.Validate(mode, report, pid, physical: true); break;
            case "provider-ownership": ProviderOwnershipExpectations.Validate(mode, report, pid); break;
            case "provider-host-filter": ProviderHostFilterExpectations.Validate(mode, report, consoleText, pid); break;
            case "provider-host-story": ProviderHostStoryExpectations.Validate(mode, report, pid); break;
            default: throw new InvalidOperationException(family);
        }
    }
    internal static string[] Modes(string family) => family switch
    {
        "provider-scope" => ProviderScopeExpectations.Modes,
        "provider-operation" => ProviderOperationExpectations.Modes,
        "provider-ownership" => ProviderOwnershipExpectations.Modes,
        "provider-host-filter" => ProviderHostFilterExpectations.Modes,
        "provider-host-story" => ProviderHostStoryExpectations.Modes,
        _ => throw new InvalidOperationException(family)
    };
    internal static void Wire(JsonObject report, Action<JsonObject> change, int index = 0, string key = "bytes")
    {
        string[] lines = Encoding.UTF8.GetString(Convert.FromBase64String(report[key]!.GetValue<string>()))[..^1].Split('\n');
        JsonObject[] rows = lines.Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        change(rows[index]);
        report[key] = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Concat(rows.Select(row => row.ToJsonString() + "\n"))));
    }
}
