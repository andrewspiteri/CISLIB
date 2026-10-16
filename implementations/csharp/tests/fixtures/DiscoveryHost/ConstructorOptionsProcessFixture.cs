using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class ConstructorOptionsProcessFixture
{
    internal static int Run(string scenario)
    {
        JsonNode? values = null;
        EventMetadata? metadata = null;
        CoreConfigurationException? failure = null;
        bool repeatedFailure = false;
        DateTimeOffset before = DateTimeOffset.UtcNow;
        try
        {
            // First access invokes the actual static constructor.
            _ = EnvironmentOptions.ApplicationName;
            if (scenario == "explicit")
            {
                EnvironmentOptions.EnvironmentKind = EnvironmentKind.Console;
                EnvironmentOptions.CommitId = "caller-commit";
                EnvironmentOptions.BuildId = "caller-build";
            }
            EnvironmentOptions.Logging.Prepare(Directory.GetCurrentDirectory());
            metadata = new EventMetadata();
            values = JsonSerializer.SerializeToNode(new
            {
                EnvironmentOptions.ApplicationName,
                EnvironmentOptions.BuildVersion,
                EnvironmentOptions.ServiceVersion,
                EnvironmentOptions.EnvironmentName,
                EnvironmentOptions.EnvironmentKind,
                EnvironmentOptions.InstanceId,
                EnvironmentOptions.ProcessStartedAtUtc,
                EnvironmentOptions.InitializedAtUtc,
                EnvironmentOptions.HostName,
                EnvironmentOptions.OperatingSystem,
                EnvironmentOptions.OperatingSystemDescription,
                EnvironmentOptions.OSArchitecture,
                EnvironmentOptions.ProcessArchitecture,
                EnvironmentOptions.RuntimeDescription,
                EnvironmentOptions.ProcessId,
                EnvironmentOptions.CommitId,
                EnvironmentOptions.BuildId,
                EnvironmentOptions.Logging
            });
        }
        catch (TypeInitializationException error) when (error.InnerException is CoreConfigurationException configuration)
        {
            failure = configuration;
            try { _ = EnvironmentOptions.ProcessId; }
            catch (TypeInitializationException again) { repeatedFailure = ReferenceEquals(error.InnerException, again.InnerException); }
        }
        catch (CoreConfigurationException error) { failure = error; }
        DateTimeOffset after = DateTimeOffset.UtcNow;
        string? selected = System.Environment.GetEnvironmentVariable("CORE_SERVICE_NAME");
        Console.WriteLine(new JsonObject
        {
            ["observed"] = new JsonObject { ["processId"] = System.Environment.ProcessId, ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux", ["runtimeDescription"] = RuntimeInformation.FrameworkDescription, ["fixtureSha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(ConstructorOptionsProcessFixture).Assembly.Location))), ["coreSha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(EnvironmentOptions).Assembly.Location))) },
            ["scenario"] = scenario,
            ["values"] = values,
            ["metadata"] = metadata is null ? null : JsonSerializer.SerializeToNode(metadata),
            ["observedPid"] = System.Environment.ProcessId,
            ["before"] = before,
            ["after"] = after,
            ["nameInterned"] = metadata is not null && ReferenceEquals(metadata.ApplicationName, string.IsInterned(metadata.ApplicationName)),
            ["invalidIdentityInterned"] = failure is not null && selected is not null && string.IsInterned(selected) is not null,
            ["repeatedFailure"] = repeatedFailure,
            ["readonlyFacts"] = typeof(EnvironmentOptions).GetProperties().Where(property => property.Name is not ("EnvironmentKind" or "CommitId" or "BuildId")).All(property => property.SetMethod is null),
            ["issues"] = new JsonArray((failure?.Issues ?? []).Select(issue => (JsonNode)new JsonObject { ["code"] = issue.Code, ["key"] = issue.Key }).ToArray()),
            ["lifecycleApiPresent"] = typeof(EnvironmentOptions).GetMethod("Initialize") is not null
        }.ToJsonString());
        return 0;
    }
}
