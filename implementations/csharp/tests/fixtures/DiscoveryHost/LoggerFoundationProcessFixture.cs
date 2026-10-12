using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class LoggerFoundationProcessFixture
{
    internal static int Run(string path, string id)
    {
        JsonObject row = JsonNode.Parse(File.ReadAllText(path))!["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = LoggerFoundationFixture.Execute(row); LoggerFoundationExpectations.Validate(row, actual);
        actual["observed"] = new JsonObject
        {
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "unknown",
            ["fixtureSha256"] = Hash(typeof(LoggerFoundationProcessFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(LogEventData).Assembly.Location),
            ["inputsSha256"] = Hash(path)
        };
        Console.WriteLine(actual.ToJsonString()); return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
