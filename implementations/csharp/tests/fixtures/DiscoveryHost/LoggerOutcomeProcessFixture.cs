using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class LoggerOutcomeProcessFixture
{
    internal static int Run(string path, string id)
    {
        byte[] inputBytes = File.ReadAllBytes(path);
        JsonObject row = JsonNode.Parse(inputBytes)!["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = LoggerOutcomeFixture.Execute(row); LoggerOutcomeExpectations.Validate(row, actual);
        actual["observed"] = new JsonObject
        {
            ["processId"] = System.Environment.ProcessId,
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "unknown",
            ["fixtureSha256"] = Hash(typeof(LoggerOutcomeProcessFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(LogEventData).Assembly.Location),
            ["inputsSha256"] = Convert.ToHexStringLower(SHA256.HashData(inputBytes))
        };
        Console.WriteLine(actual.ToJsonString()); return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
