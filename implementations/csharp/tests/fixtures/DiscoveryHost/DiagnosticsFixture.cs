using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class DiagnosticsFixture
{
    internal static JsonObject SourceRow(string folder, JsonObject row) => JsonNode.Parse(File.ReadAllText(Path.Combine(folder, row["sourceCatalog"]!.GetValue<string>())))!["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == row["sourceCaseId"]!.GetValue<string>())!.AsObject();
    internal static async Task<JsonObject> ExecuteAsync(string folder, JsonObject row)
    {
        JsonObject sourceRow = SourceRow(folder, row);
        bool delivery = row["profile"]!.GetValue<string>() == "delivery";
        JsonObject source = delivery ? await DeliveryObservationFixture.ExecuteAsync(sourceRow, captureDiagnostics: true) : await OutputStateFixture.ExecuteAsync(sourceRow, captureDiagnostics: true);
        if (delivery) { DeliveryObservationExpectations.Validate(sourceRow, source); } else { OutputStateExpectations.Validate(sourceRow, source); }
        return new JsonObject { ["caseId"] = row["id"]!.DeepClone(), ["source"] = source };
    }
    internal static int Run(string path, string id)
    {
        JsonObject row = JsonNode.Parse(File.ReadAllText(path))!["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = ExecuteAsync(Path.GetDirectoryName(path)!, row).GetAwaiter().GetResult(); DiagnosticsExpectations.Validate(row, actual);
        actual["observed"] = new JsonObject { ["processId"] = System.Environment.ProcessId, ["runtimeDescription"] = RuntimeInformation.FrameworkDescription, ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux", ["fixtureSha256"] = Hash(typeof(DiagnosticsFixture).Assembly.Location), ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location), ["inputsSha256"] = Hash(path), ["sourceInputsSha256"] = Hash(Path.Combine(Path.GetDirectoryName(path)!, row["sourceCatalog"]!.GetValue<string>())) };
        Console.WriteLine(actual.ToJsonString()); return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
