using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CIS.Configuration;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class StartupOwnershipProcessFixture
{
    public static int Run(string scenario, string afterDirectory)
    {
        string beforeDirectory = Directory.GetCurrentDirectory();
        Publication state = Publish(retainInputs: scenario != "collected");
        EnvironmentSnapshot snapshot = state.Snapshot;
        string stored = JsonSerializer.Serialize(snapshot);
        string instance = snapshot.InstanceId;
        LoggingSetup? originalLogging = state.Setup?.Logging;
        int changedFields = 0;
        int changedVariables = 0;
        if (scenario is "mutate" or "replace")
        {
            EnvironmentSetup setup = state.Setup!;
            setup.ApplicationAssembly = typeof(EnvironmentOptions).Assembly;
            setup.ApplicationName = "Changed.App"; setup.BuildVersion = "changed-build"; setup.ServiceVersion = "changed-release";
            setup.EnvironmentName = "Changed.Live"; setup.EnvironmentKind = (EnvironmentKind)99; setup.CommitId = "changed-commit"; setup.BuildId = "changed-id";
            originalLogging!.Level = (CoreLogLevel)99; originalLogging.ConsoleFormat = (LogOutputFormat)99;
            originalLogging.ConsoleLevel = (CoreLogLevel)99; originalLogging.ConsoleStream = (ConsoleOutputStream)99;
            originalLogging.HumanDetail = (HumanOutputDetail)99; originalLogging.FilePath = "invalid" + (char)0;
            originalLogging.FileFormat = (LogOutputFormat)99; originalLogging.FileLevel = (CoreLogLevel)99;
            originalLogging.QueueCapacity = 0; originalLogging.FlushIntervalMilliseconds = 0; originalLogging.ShutdownTimeoutMilliseconds = 0;
            originalLogging.FileMaxBytes = 0; originalLogging.FileRetainedFiles = 0; originalLogging.SchemaVersion = 0; originalLogging.MaxEntryBytes = 0;
            changedFields = 23;
            if (scenario == "replace") { setup.Logging = new() { QueueCapacity = 0 }; changedFields++; }
        }
        else if (scenario == "sources")
        {
            foreach (ConfigurationSetting setting in ConfigurationSettings.All)
            {
                Environment.SetEnvironmentVariable(setting.Key, "CIS_CHANGED_SOURCE"); changedVariables++;
                if (setting.Alias is { } alias) { Environment.SetEnvironmentVariable(alias, "CIS_CHANGED_SOURCE"); changedVariables++; }
            }
            Environment.SetEnvironmentVariable("DD_AGENT_HOST", "CIS_CHANGED_COLLECTOR");
        }
        else if (scenario == "collected")
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            Require(!state.SetupReference.IsAlive && !state.LoggingReference.IsAlive, "published snapshot retained mutable caller inputs");
        }
        Directory.SetCurrentDirectory(afterDirectory);
        Require(EnvironmentOptions.IsInitialized, "publication was lost");
        Require(ReferenceEquals(snapshot, EnvironmentOptions.Current), "Current identity changed");
        Require(ReferenceEquals(snapshot.Logging, EnvironmentOptions.Logging), "logging identity changed");
        Require(ReferenceEquals(snapshot.EventMetadata, EnvironmentOptions.EventMetadata), "metadata identity changed");
        Require(stored == JsonSerializer.Serialize(EnvironmentOptions.Current), "complete stored projection changed");
        Require(instance == EnvironmentOptions.Current.InstanceId, "instance changed");
        Dictionary<string, object?> convenience = new[] { "ApplicationName", "BuildVersion", "ServiceVersion", "EnvironmentName", "HostName", "OperatingSystem", "Logging", "EventMetadata" }
            .ToDictionary(name => name, name => typeof(EnvironmentOptions).GetProperty(name)!.GetValue(null), StringComparer.Ordinal);
        foreach ((string name, object? value) in convenience) { Require(Equals(value, typeof(EnvironmentSnapshot).GetProperty(name)!.GetValue(snapshot)), "convenience getter changed: " + name); }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            scenario,
            snapshot = EnvironmentOptions.Current,
            convenience,
            observed = new
            {
                beforeDirectory,
                afterDirectory = Directory.GetCurrentDirectory(),
                changedFields,
                changedVariables,
                callerInputsCollected = scenario == "collected" && !state.SetupReference.IsAlive && !state.LoggingReference.IsAlive,
                storedProjectionStable = stored == JsonSerializer.Serialize(EnvironmentOptions.Current),
                instanceStable = instance == snapshot.InstanceId,
                snapshotReferenceStable = ReferenceEquals(snapshot, EnvironmentOptions.Current),
                loggingReferenceStable = ReferenceEquals(snapshot.Logging, EnvironmentOptions.Logging),
                metadataReferenceStable = ReferenceEquals(snapshot.EventMetadata, EnvironmentOptions.EventMetadata),
                metadataFieldCount = typeof(EventMetadata).GetProperties().Length,
                runtimeDescription = RuntimeInformation.FrameworkDescription,
                operatingSystem = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "unknown",
                fixtureSha256 = Hash(typeof(StartupOwnershipProcessFixture).Assembly.Location),
                coreSha256 = Hash(typeof(EnvironmentOptions).Assembly.Location)
            }
        }));
        GC.KeepAlive(state.Setup); GC.KeepAlive(originalLogging);
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Publication Publish(bool retainInputs)
    {
        var setup = new EnvironmentSetup
        {
            ApplicationAssembly = typeof(StartupOwnershipProcessFixture).Assembly,
            ApplicationName = " Owned.App ",
            BuildVersion = "1.2.3-rc.4+abc",
            ServiceVersion = " owned-release ",
            EnvironmentName = " Owned.Live ",
            EnvironmentKind = EnvironmentKind.Function,
            CommitId = " commit-owned ",
            BuildId = " build-owned ",
            Logging = new()
            {
                Level = CoreLogLevel.Warning,
                ConsoleFormat = LogOutputFormat.JsonLines,
                ConsoleLevel = CoreLogLevel.Trace,
                ConsoleStream = ConsoleOutputStream.StandardOutput,
                HumanDetail = HumanOutputDetail.Verbose,
                FilePath = "logs/~owned.jsonl",
                FileFormat = LogOutputFormat.Human,
                FileLevel = CoreLogLevel.Critical,
                QueueCapacity = 17,
                FlushIntervalMilliseconds = 31,
                ShutdownTimeoutMilliseconds = 61,
                FileMaxBytes = 131072,
                FileRetainedFiles = 7,
                SchemaVersion = 1,
                MaxEntryBytes = 65536
            }
        };
        var setupReference = new WeakReference(setup);
        var loggingReference = new WeakReference(setup.Logging);
        EnvironmentSnapshot snapshot = EnvironmentOptions.Initialize(setup);
        return new(snapshot, retainInputs ? setup : null, setupReference, loggingReference);
    }
    private sealed record Publication(EnvironmentSnapshot Snapshot, EnvironmentSetup? Setup, WeakReference SetupReference, WeakReference LoggingReference);
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Require(bool value, string message) { if (!value) { throw new InvalidDataException(message); } }
}
