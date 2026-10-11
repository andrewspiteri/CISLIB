using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CIS.Configuration;

namespace CIS.Tests.DiscoveryFixture;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "log-qualification")
        {
            return LogValueProcessFixture.Run(args[1], args[2], qualification: true);
        }

        if (args.Length == 3 && args[0] == "log-collection")
        {
            return LogValueProcessFixture.Run(args[1], args[2], collections: true);
        }

        if (args.Length == 3 && args[0] == "log-value")
        {
            return LogValueProcessFixture.Run(args[1], args[2]);
        }

        if (args.Length == 3 && args[0] == "startup-metadata")
        {
            return StartupQualificationProcessFixture.Metadata(args[1], args[2]);
        }

        if (args.Length == 2 && args[0] == "startup-independent")
        {
            return StartupQualificationProcessFixture.Independent(args[1]);
        }

        if (args.Length == 3 && args[0] == "startup-concurrency" && args[1] is "valid-2" or "valid-16" or "valid-32" or "mixed-16" or "invalid-then-valid" or "held-valid" or "held-invalid")
        {
            return StartupConcurrencyProcessFixture.Run(args[1], args[2]);
        }

        if (args.Length == 2 && args[0] == "startup-reentrancy" && args[1] is "reentrant-valid" or "reentrant-invalid" or "required-failure")
        {
            return StartupReentrancyProcessFixture.Run(args[1]);
        }

        if (args.Length == 3 && args[0] == "startup-ownership" && args[1] is "mutate" or "replace" or "sources" or "collected")
        {
            return StartupOwnershipProcessFixture.Run(args[1], args[2]);
        }

        if (args.Length == 3 && args[0] == "startup-facade" && args[1] is "before" or "null" or "defaults" or "explicit" or "overrides" or "retry")
        {
            return StartupFacadeProcessFixture.Run(args[1], args[2]);
        }

        if (args.Length == 4 && args[0] == "configuration-validation")
        {
            return ConfigurationValidationProcessFixture.Run(args[1], args[2], args[3]);
        }

        if (args.Length == 4 && args[0] == "configuration-path")
        {
            return ConfigurationPathProcessFixture.Run(args[1], args[2], args[3]);
        }

        if (args.Length == 3 && args[0] == "configuration")
        {
            return ConfigurationProcessFixture.Run(args[1], args[2]);
        }

        if (args.Length != 1 || args[0] is not ("native" or "assembly-version" or "unavailable"))
        {
            Console.Error.WriteLine("Expected native, assembly-version or unavailable.");
            return 2;
        }

        string scenario = args[0];
        ApplicationIdentity application = ApplicationIdentityDiscovery.Discover(
            new ApplicationIdentityInput(),
            scenario == "native" ? null : new LimitedAssemblyMetadata(scenario));
        CapturedEnvironment variables = CapturedEnvironment.Capture();
        ApplicationVersionSelection versions = ApplicationVersionSelector.Select(application, variables: variables);
        RuntimeMetadata runtime = RuntimeMetadataDiscovery.Discover(
            scenario == "unavailable" ? new UnavailableRuntimeMetadata() : null);
        DateTimeOffset beforeInitializationUtc = DateTimeOffset.UtcNow;
        InitializationIdentity initialization = new InitializationIdentityFactory().Create();
        DateTimeOffset afterInitializationUtc = DateTimeOffset.UtcNow;
        Assembly entry = Assembly.GetEntryAssembly()!;
        Assembly core = typeof(ApplicationIdentityDiscovery).Assembly;
        using Process process = Process.GetCurrentProcess();
        var result = new
        {
            Scenario = scenario,
            Application = application,
            Versions = versions,
            Runtime = runtime,
            Initialization = initialization,
            Observed = new
            {
                EntryName = entry.GetName().Name,
                EntryInformationalVersion = entry.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                EntryAssemblyVersion = entry.GetName().Version!.ToString(),
                EntryFileVersion = entry.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version,
                EntryPath = entry.Location,
                EntrySha256 = Hash(entry.Location),
                CoreName = core.GetName().Name,
                CoreInformationalVersion = core.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                CoreAssemblyVersion = core.GetName().Version!.ToString(),
                CorePath = core.Location,
                CoreSha256 = Hash(core.Location),
                HostName = System.Environment.MachineName,
                ProcessId = System.Environment.ProcessId,
                ProcessStartedAtUtc = new DateTimeOffset(process.StartTime.ToUniversalTime()),
                OSArchitecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                OSDescription = RuntimeInformation.OSDescription,
                RuntimeDescription = RuntimeInformation.FrameworkDescription,
                OperatingSystem = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "unknown",
                BeforeInitializationUtc = beforeInitializationUtc,
                AfterInitializationUtc = afterInitializationUtc
            }
        };
        Console.WriteLine(JsonSerializer.Serialize(result));
        return 0;
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class LimitedAssemblyMetadata(string scenario) : IApplicationAssemblyMetadata
    {
        private static RuntimeApplicationAssemblyMetadata Source => RuntimeApplicationAssemblyMetadata.Instance;
        public Assembly? GetEntryAssembly() => Source.GetEntryAssembly();
        public string? GetName(Assembly assembly) => Source.GetName(assembly);
        public string? GetInformationalVersion(Assembly assembly) => null;
        public string? GetVersion(Assembly assembly) => scenario == "unavailable" ? null : Source.GetVersion(assembly);
    }

    private sealed class UnavailableRuntimeMetadata : IRuntimeMetadataSource
    {
        public DateTimeOffset? GetProcessStartedAt() => null;
        public string? GetHostName() => null;
        public OSPlatform? GetOperatingSystem() => RuntimeMetadataSource.Instance.GetOperatingSystem();
        public string? GetOperatingSystemDescription() => null;
        public Architecture? GetOSArchitecture() => null;
        public Architecture? GetProcessArchitecture() => null;
        public string? GetRuntimeDescription() => null;
        public int? GetProcessId() => null;
    }
}
