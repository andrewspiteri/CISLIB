using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging.Exceptions;

namespace CIS.Tests.DiscoveryFixture;

internal static class ExceptionQualificationProcessFixture
{
    internal static int Run(string inputPath, string id)
    {
        byte[] inputBytes = File.ReadAllBytes(inputPath);
        JsonObject catalog = JsonNode.Parse(inputBytes)!.AsObject();
        JsonObject row = ConfigurationFixtureAdapter.Case(catalog, id);
        JsonObject result = ExceptionQualificationFixture.Execute(row, catalog["adapterObservations"]!["csharp"]!.AsObject());
        result["observed"] = new JsonObject
        {
            ["processId"] = System.Environment.ProcessId,
            ["lifecycleApiPresent"] = (typeof(EnvironmentOptions).GetMethod("Initialize") is not null),
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux",
            ["fixtureSha256"] = Hash(typeof(ExceptionQualificationProcessFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(ExceptionProjection).Assembly.Location),
            ["inputsSha256"] = Convert.ToHexStringLower(SHA256.HashData(inputBytes))
        };
        Console.WriteLine(result.ToJsonString());
        return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
