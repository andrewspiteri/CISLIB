using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CIS.Configuration;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class StartupFacadeProcessFixture
{
    public static int Run(string scenario, string afterDirectory)
    {
        string beforeDirectory = Directory.GetCurrentDirectory();
        Require(!EnvironmentOptions.IsInitialized, "fresh process must be uninitialized");
        RequireUninitializedReads();
        EnvironmentSnapshot? snapshot = null;
        string[] issuePairs = [];
        DateTimeOffset beforeInitialization = DateTimeOffset.UtcNow;
        if (scenario == "null")
        {
            RequireNullGuard();
            Require(!EnvironmentOptions.IsInitialized, "null must not publish");
            RequireUninitializedReads();
        }
        else if (scenario != "before")
        {
            EnvironmentSetup setup = scenario == "defaults" ? new() : new()
            {
                ApplicationName = " Setup.App ",
                BuildVersion = "1.2.3-rc.4+app.5",
                ServiceVersion = "setup-release",
                EnvironmentName = " Setup.Live ",
                EnvironmentKind = EnvironmentKind.Service,
                CommitId = " commit-8 ",
                BuildId = " build-9 ",
                Logging = new()
                {
                    Level = CoreLogLevel.Warning,
                    ConsoleLevel = CoreLogLevel.None,
                    ConsoleFormat = LogOutputFormat.JsonLines,
                    FilePath = "logs/~app.jsonl",
                    FileLevel = CoreLogLevel.Debug,
                    FileFormat = LogOutputFormat.JsonLines,
                    ConsoleStream = ConsoleOutputStream.StandardOutput,
                    HumanDetail = HumanOutputDetail.Verbose,
                    QueueCapacity = 2,
                    FlushIntervalMilliseconds = 11,
                    ShutdownTimeoutMilliseconds = 12,
                    FileMaxBytes = 1048576,
                    FileRetainedFiles = 3,
                    MaxEntryBytes = 65536
                }
            };
            if (scenario == "retry")
            {
                setup.Logging!.QueueCapacity = 0;
                setup.Logging.FilePath = "bad" + (char)0;
                try { EnvironmentOptions.Initialize(setup); throw new InvalidDataException("invalid setup unexpectedly succeeded"); }
                catch (CoreConfigurationException error) { issuePairs = error.Issues.Select(issue => issue.Code + ":" + issue.Key).ToArray(); }
                Require(!EnvironmentOptions.IsInitialized, "failed validation must not publish");
                RequireUninitializedReads();
                setup.Logging.QueueCapacity = 2;
                setup.Logging.FilePath = "logs/~app.jsonl";
            }
            snapshot = EnvironmentOptions.Initialize(setup);
            Require(EnvironmentOptions.IsInitialized, "valid setup must publish");
            Require(ReferenceEquals(snapshot, EnvironmentOptions.Current), "return and Current must be identical");
            RequireNullGuard();
            try
            {
                EnvironmentOptions.Initialize(new() { ApplicationName = "replacement", Logging = new() { QueueCapacity = 0 } });
                throw new InvalidDataException("second call unexpectedly succeeded");
            }
            catch (InvalidOperationException) { }
            Require(ReferenceEquals(snapshot, EnvironmentOptions.Current), "later call must preserve winner");
            RequirePublishedReads(snapshot);
        }
        DateTimeOffset afterInitialization = DateTimeOffset.UtcNow;
        string? stored = snapshot is null ? null : JsonSerializer.Serialize(snapshot);
        Directory.SetCurrentDirectory(afterDirectory);
        if (snapshot is not null)
        {
            Require(stored == JsonSerializer.Serialize(EnvironmentOptions.Current), "stored facts must survive cwd change");
            RequirePublishedReads(snapshot);
        }
        else { RequireUninitializedReads(); }
        using Process process = Process.GetCurrentProcess();
        Assembly fixture = typeof(StartupFacadeProcessFixture).Assembly;
        Assembly core = typeof(EnvironmentOptions).Assembly;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            scenario,
            snapshot,
            issuePairs,
            initialized = EnvironmentOptions.IsInitialized,
            observed = new
            {
                beforeDirectory,
                afterDirectory = Directory.GetCurrentDirectory(),
                beforeInitialization,
                afterInitialization,
                processId = Environment.ProcessId,
                processStartedAtUtc = new DateTimeOffset(process.StartTime.ToUniversalTime()),
                hostName = Environment.MachineName,
                operatingSystem = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "unknown",
                osDescription = RuntimeInformation.OSDescription,
                osArchitecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                runtimeDescription = RuntimeInformation.FrameworkDescription,
                fixtureName = fixture.GetName().Name,
                fixtureBuild = fixture.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                fixtureSha256 = Hash(fixture.Location),
                coreSha256 = Hash(core.Location),
                storedProjectionStable = stored == (snapshot is null ? null : JsonSerializer.Serialize(EnvironmentOptions.Current))
            }
        }));
        return 0;
    }

    private static void RequireUninitializedReads()
    {
        foreach (string name in new[] { "Current", "ApplicationName", "BuildVersion", "ServiceVersion", "EnvironmentName", "HostName", "OperatingSystem", "Logging", "EventMetadata" })
        {
            try { typeof(EnvironmentOptions).GetProperty(name)!.GetValue(null); throw new InvalidDataException("uninitialized getter succeeded: " + name); }
            catch (TargetInvocationException error) when (error.InnerException?.GetType() == typeof(InvalidOperationException)) { }
        }
    }

    private static void RequireNullGuard()
    {
        try { EnvironmentOptions.Initialize(null!); throw new InvalidDataException("null unexpectedly succeeded"); }
        catch (ArgumentNullException error) { Require(error.ParamName == "setup", "null parameter name"); }
    }

    private static void RequirePublishedReads(EnvironmentSnapshot snapshot)
    {
        foreach (string name in new[] { "ApplicationName", "BuildVersion", "ServiceVersion", "EnvironmentName", "HostName", "OperatingSystem", "Logging", "EventMetadata" })
        {
            Require(Equals(typeof(EnvironmentSnapshot).GetProperty(name)!.GetValue(snapshot), typeof(EnvironmentOptions).GetProperty(name)!.GetValue(null)), "convenience getter: " + name);
        }
        foreach (PropertyInfo property in typeof(EventMetadata).GetProperties())
        {
            Require(Equals(property.GetValue(snapshot.EventMetadata), typeof(EnvironmentSnapshot).GetProperty(property.Name)!.GetValue(snapshot)), "metadata fact: " + property.Name);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidDataException(message); }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
