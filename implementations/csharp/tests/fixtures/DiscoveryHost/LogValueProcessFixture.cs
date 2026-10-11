using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class LogValueProcessFixture
{
    public static int Run(string inputPath, string id, bool collections = false, bool qualification = false)
    {
        JsonObject row = ConfigurationFixtureAdapter.Case(ConfigurationFixtureAdapter.ReadDocument(inputPath), id);
        JsonObject result;
        if (qualification)
        {
            result = LogValueQualificationFixtureAdapter.Execute(row);
        }
        else if (collections)
        {
            result = LogValueCollectionFixtureAdapter.Execute(row);
        }
        else
        {
            result = LogValueFixtureAdapter.Execute(row);
            LogValueFixtureAdapter.Validate(row, result);
        }
        result["observed"] = new JsonObject
        {
            ["publicFacadeInitialized"] = EnvironmentOptions.IsInitialized,
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux",
            ["fixtureSha256"] = Hash(typeof(LogValueProcessFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(LogValue).Assembly.Location),
            ["inputsSha256"] = Hash(inputPath)
        };
        Console.WriteLine(result.ToJsonString());
        return 0;
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
