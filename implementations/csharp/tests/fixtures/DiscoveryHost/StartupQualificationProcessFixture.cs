using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class StartupQualificationProcessFixture
{
    public static int Metadata(string inputPath, string id)
    {
        JsonObject row = ConfigurationFixtureAdapter.Case(ConfigurationFixtureAdapter.ReadDocument(inputPath), id);
        JsonObject result = StartupMetadataFixtureAdapter.Execute(row, Directory.GetCurrentDirectory());
        StartupMetadataFixtureAdapter.Validate(row, result);
        result["observed"]!["fixtureSha256"] = Hash(typeof(StartupQualificationProcessFixture).Assembly.Location);
        result["observed"]!["coreSha256"] = Hash(typeof(EnvironmentOptions).Assembly.Location);
        result["observed"]!["inputsSha256"] = Hash(inputPath);
        result["observed"]!["runtimeDescription"] = RuntimeInformation.FrameworkDescription;
        result["observed"]!["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux";
        Console.WriteLine(result.ToJsonString());
        return 0;
    }

    public static int Independent(string afterDirectory)
    {
        string beforeDirectory = Directory.GetCurrentDirectory();
        Require(!EnvironmentOptions.IsInitialized, "Fresh application inherited publication.");
        var setup = new EnvironmentSetup
        {
            ApplicationName = "Independent.App",
            BuildVersion = "2.3.4+sha.MixedCase",
            ServiceVersion = "deployment-release",
            EnvironmentName = "Independent.Live",
            EnvironmentKind = EnvironmentKind.Service,
            Logging = new LoggingSetup { QueueCapacity = 1024, FilePath = "logs/independent.jsonl" }
        };
        DateTimeOffset beforeInitialization = DateTimeOffset.UtcNow;
        EnvironmentSnapshot snapshot = EnvironmentOptions.Initialize(setup);
        DateTimeOffset afterInitialization = DateTimeOffset.UtcNow;
        string original = JsonSerializer.Serialize(snapshot);
        string instance = snapshot.InstanceId;
        Dictionary<string, object?> getters = Getters(snapshot);
        setup.ApplicationName = "Changed.App";
        setup.Logging.QueueCapacity = 1;
        Environment.SetEnvironmentVariable("CORE_SERVICE_NAME", "Changed.Environment");
        Directory.SetCurrentDirectory(afterDirectory);
        int checks = CheckStoredReads(snapshot, original, instance);
        using Process process = Process.GetCurrentProcess();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            scenario = "independent",
            snapshot,
            getters,
            observed = new
            {
                beforeDirectory,
                afterDirectory = Directory.GetCurrentDirectory(),
                beforeInitialization,
                afterInitialization,
                storedReadChecks = checks,
                instanceStable = instance == EnvironmentOptions.Current.InstanceId,
                completeSnapshotStable = original == JsonSerializer.Serialize(EnvironmentOptions.Current),
                referenceStable = ReferenceEquals(snapshot, EnvironmentOptions.Current),
                noResetApi = typeof(EnvironmentOptions).GetMethod("Reset", BindingFlags.Public | BindingFlags.Static) is null,
                processId = Environment.ProcessId,
                processStartedAtUtc = new DateTimeOffset(process.StartTime.ToUniversalTime()),
                hostName = Environment.MachineName,
                operatingSystem = OperatingSystem.IsWindows() ? "windows" : "linux",
                osArchitecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                runtimeDescription = RuntimeInformation.FrameworkDescription,
                fixtureSha256 = Hash(typeof(StartupQualificationProcessFixture).Assembly.Location),
                coreSha256 = Hash(typeof(EnvironmentOptions).Assembly.Location)
            }
        }));
        return 0;
    }

    private static Dictionary<string, object?> Getters(EnvironmentSnapshot snapshot)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (string name in new[] { "ApplicationName", "BuildVersion", "ServiceVersion", "EnvironmentName", "HostName", "OperatingSystem", "Logging", "EventMetadata" })
        {
            object? value = typeof(EnvironmentOptions).GetProperty(name)!.GetValue(null);
            Require(Equals(value, typeof(EnvironmentSnapshot).GetProperty(name)!.GetValue(snapshot)), "Inconsistent getter: " + name);
            values.Add(name, value);
        }
        return values;
    }

    private static int CheckStoredReads(EnvironmentSnapshot snapshot, string original, string instance)
    {
        for (int index = 0; index < 128; index++)
        {
            Require(EnvironmentOptions.IsInitialized && ReferenceEquals(snapshot, EnvironmentOptions.Current), "Published reference changed.");
            Require(original == JsonSerializer.Serialize(EnvironmentOptions.Current), "Caller/source mutation changed snapshot.");
            Require(instance == EnvironmentOptions.Current.InstanceId, "Instance changed within application run.");
            Getters(snapshot);
            foreach (PropertyInfo property in typeof(EventMetadata).GetProperties())
            {
                Require(Equals(property.GetValue(snapshot.EventMetadata), typeof(EnvironmentSnapshot).GetProperty(property.Name)!.GetValue(snapshot)), "Metadata differs.");
            }
        }
        return 128;
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidDataException(message);
        }
    }
}
