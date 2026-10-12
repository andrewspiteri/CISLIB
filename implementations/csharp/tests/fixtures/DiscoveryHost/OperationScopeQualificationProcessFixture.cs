using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging.Scopes;

namespace CIS.Tests.DiscoveryFixture;

internal static class OperationScopeQualificationProcessFixture
{
    internal static async Task<int> RunAsync(string path, string id)
    {
        JsonObject catalog = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        JsonObject row = catalog["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject result = await OperationScopeQualificationFixture.ExecuteAsync(row).ConfigureAwait(false);
        OperationScopeQualificationExpectations.Validate(row, catalog["nativeBindings"]!.AsObject(), result);
        result["observed"] = new JsonObject
        {
            ["lifecycleApiPresent"] = (typeof(EnvironmentOptions).GetMethod("Initialize") is not null),
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux"
                : OperatingSystem.IsMacOS() ? "macos" : "unknown",
            ["fixtureSha256"] = Hash(typeof(OperationScopeQualificationProcessFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(OperationScopeStore).Assembly.Location),
            ["inputsSha256"] = Hash(path)
        };
        Console.WriteLine(result.ToJsonString());
        return 0;
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
