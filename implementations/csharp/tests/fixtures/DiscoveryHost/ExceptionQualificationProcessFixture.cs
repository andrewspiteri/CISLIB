using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging.Exceptions;

namespace CIS.Tests.DiscoveryFixture;

internal static class ExceptionQualificationProcessFixture
{
    internal static int Run(string inputPath, string id)
    {
        JsonObject catalog = ConfigurationFixtureAdapter.ReadDocument(inputPath);
        JsonObject row = ConfigurationFixtureAdapter.Case(catalog, id);
        JsonObject result = ExceptionQualificationFixture.Execute(row, catalog["adapterObservations"]!["csharp"]!.AsObject());
        result["observed"] = new JsonObject
        {
            ["publicFacadeInitialized"] = EnvironmentOptions.IsInitialized,
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux",
            ["fixtureSha256"] = Hash(typeof(ExceptionQualificationProcessFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(ExceptionProjection).Assembly.Location),
            ["inputsSha256"] = Hash(inputPath)
        };
        Console.WriteLine(result.ToJsonString());
        return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
