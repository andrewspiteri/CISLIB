using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class EventCorrelationProcessFixture
{
    internal static int Run(string path, string id)
    {
        JsonObject catalog = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        JsonObject row = catalog["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = EventCorrelationFixture.Execute(row);
        EventCorrelationExpectations.Validate(row, actual);
        actual["observed"] = new JsonObject
        {
            ["lifecycleApiPresent"] = (typeof(EnvironmentOptions).GetMethod("Initialize") is not null),
            ["processId"] = System.Environment.ProcessId,
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux"
                : OperatingSystem.IsMacOS() ? "macos" : "unknown",
            ["fixtureSha256"] = Hash(typeof(EventCorrelationProcessFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(LogEventData).Assembly.Location),
            ["inputsSha256"] = Hash(path)
        };
        Console.WriteLine(actual.ToJsonString());
        return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
