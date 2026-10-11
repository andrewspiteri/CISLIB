using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Configuration;

namespace CIS.Tests.DiscoveryFixture;

internal static class ConfigurationProcessFixture
{
    public static int Run(string path, string id)
    {
        JsonObject row = ConfigurationFixtureAdapter.Case(ConfigurationFixtureAdapter.ReadDocument(path), id);
        JsonObject input = row["inputs"]!.AsObject();
        JsonNode hostBefore = input["host"]!.DeepClone();
        string? frameworkBefore = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        ApplicationIdentity application = ApplicationIdentityDiscovery.Discover(new ApplicationIdentityInput());
        JsonObject discovery = input["discovery"]!.AsObject();
        if (application.ApplicationName != ConfigurationFixtureAdapter.Text(discovery, "applicationName") || application.BuildVersion != ConfigurationFixtureAdapter.Text(discovery, "buildVersion"))
        {
            throw new InvalidDataException("The fixture discovery values do not match this application host.");
        }
        var source = new RecordingSource();
        CapturedEnvironment variables = CapturedEnvironment.Capture(source);
        ConfigurationResolution resolution = ConfigurationResolver.Resolve(ConfigurationFixtureAdapter.Setup(input), application, variables);
        var result = new JsonObject
        {
            ["caseId"] = id,
            ["configuration"] = ConfigurationFixtureAdapter.Project(resolution),
            ["hostBefore"] = hostBefore,
            ["hostAfter"] = input["host"]!.DeepClone(),
            ["frameworkVariableBefore"] = frameworkBefore,
            ["frameworkVariableAfter"] = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            ["observed"] = new JsonObject
            {
                ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "unknown",
                ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
                ["outputRedirected"] = Console.IsOutputRedirected,
                ["errorRedirected"] = Console.IsErrorRedirected,
                ["debuggerAttached"] = Debugger.IsAttached,
                ["processId"] = System.Environment.ProcessId,
                ["fixtureSha256"] = Hash(typeof(ConfigurationProcessFixture).Assembly.Location),
                ["coreSha256"] = Hash(typeof(ConfigurationResolver).Assembly.Location),
                ["inputsSha256"] = Hash(path),
                ["recognizedReads"] = new JsonArray(source.Reads.Select(name => (JsonNode)JsonValue.Create(name)!).ToArray())
            }
        };
        Console.WriteLine(result.ToJsonString());
        return 0;
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class RecordingSource : IEnvironmentVariableSource
    {
        public List<string> Reads { get; } = [];
        public string? GetValue(string name)
        {
            Reads.Add(name);
            return System.Environment.GetEnvironmentVariable(name);
        }
    }
}
