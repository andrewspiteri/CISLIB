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
        if (args.Length == 4 && args[0] == "file-retention") { return FileRetentionFixture.Run(args[1], args[2], args[3]); }

        if (args.Length == 3 && args[0] == "file-rotation") { return FileRotationFixture.Run(args[1], args[2]); }

        if (args.Length == 3 && args[0] == "file-runtime-group") { return FileGroupConsumerFixture.Run(args[1], args[2]); }
        if (args.Length == 3 && args[0] == "file-runtime-startup") { return FileRuntimeStartupFixture.Run(args[1], args[2]); }
        if (args.Length == 3 && args[0] == "file-inventory") { return FileInventoryFixture.Run(args[1], args[2]); }
        if (args.Length == 3 && args[0] == "file-acquisition") { return FileAcquisitionFixture.Run(args[1], args[2]); }
        if (args.Length == 3 && args[0] == "console-composition") { return ConsoleCompositionFixture.Run(args[1], args[2]); }
        if (args.Length == 3 && args[0] == "console-ownership") { return ConsoleOwnershipFixture.Run(args[1], args[2]); }
        if (args.Length == 3 && args[0] == "console-consumer") { return ConsoleConsumerFixture.Run(args[1], args[2]); }
        if (args.Length == 3 && args[0] == "console-startup") { return ConsoleStartupFixture.Run(args[1], args[2]); }
        if (args.Length == 3 && args[0] == "diagnostics") { return DiagnosticsFixture.Run(args[1], args[2]); }
        if (args.Length == 3 && args[0] == "output-states") { return OutputStateFixture.Run(args[1], args[2]); }
        if (args.Length == 3 && args[0] == "delivery-observation") { return DeliveryObservationFixture.Run(args[1], args[2]); }
        if (args.Length == 3 && args[0] == "output-accounting") { return OutputAccountingFixture.Run(args[1], args[2]); }
        if (args.Length == 3 && args[0] == "queued-fanout") { return QueuedFanoutFixture.Run(args[1], args[2]); }
        if (args.Length == 3 && args[0] == "output-queue")
        {
            return OutputQueueFixture.Run(args[1], args[2]);
        }
        if (args.Length == 3 && args[0] == "rendering-story")
        {
            return RenderingStoryFixture.Run(args[1], args[2]);
        }
        if (args.Length == 3 && args[0] == "presentation")
        {
            return PresentationFixture.Run(args[1], args[2]);
        }
        if (args.Length == 3 && args[0] == "human-rendering")
        {
            return HumanRenderingFixture.Run(args[1], args[2]);
        }
        if (args.Length == 3 && args[0] == "jsonl-reuse")
        {
            return JsonlReuseProcessFixture.Run(args[1], args[2]);
        }
        if (args.Length == 3 && args[0] == "logger-outcome")
        {
            return LoggerOutcomeProcessFixture.Run(args[1], args[2]);
        }
        if (args.Length == 3 && args[0] == "logger-capture")
        {
            return LoggerCaptureProcessFixture.Run(args[1], args[2]);
        }
        if (args.Length == 3 && args[0] == "logger-decision")
        {
            return LoggerDecisionProcessFixture.Run(args[1], args[2]);
        }
        if (args.Length == 3 && args[0] == "logger-foundation")
        {
            return LoggerFoundationProcessFixture.Run(args[1], args[2]);
        }
        if (args.Length == 3 && args[0] == "event-qualification")
        {
            return EventQualificationProcessFixture.RunAsync(args[1], args[2]).GetAwaiter().GetResult();
        }
        if (args.Length == 3 && args[0] == "event-encoding")
        {
            return EventEncodingProcessFixture.Run(args[1], args[2]);
        }
        if (args.Length == 2 && args[0] == "constructor-options")
        {
            return ConstructorOptionsProcessFixture.Run(args[1]);
        }
        if (args.Length == 3 && args[0] == "event-correlation")
        {
            return EventCorrelationProcessFixture.Run(args[1], args[2]);
        }
        if (args.Length == 3 && args[0] == "event-precedence")
        {
            return EventPrecedenceProcessFixture.Run(args[1], args[2]);
        }
        if (args.Length == 3 && args[0] == "event-projection")
        {
            return EventProjectionProcessFixture.Run(args[1], args[2]);
        }
        if (args.Length == 3 && args[0] == "scope-qualification")
        {
            return OperationScopeQualificationProcessFixture.RunAsync(args[1], args[2]).GetAwaiter().GetResult();
        }
        if (args.Length == 2 && args[0] == "scope-disposal")
        {
            return OperationScopeDisposalProcessFixture.RunAsync(args[1]).GetAwaiter().GetResult();
        }

        if (args.Length == 2 && args[0] == "scope-merge")
        {
            return OperationScopeMergeProcessFixture.Run(args[1]);
        }

        if (args.Length == 2 && args[0] == "scope-foundation")
        {
            return OperationScopeProcessFixture.RunAsync(args[1]).GetAwaiter().GetResult();
        }

        if (args.Length == 3 && args[0] == "exception-qualification")
        {
            return ExceptionQualificationProcessFixture.Run(args[1], args[2]);
        }

        if (args.Length == 2 && args[0] == "exception-content")
        {
            return ExceptionProjectionProcessFixture.Content(args[1]);
        }

        if (args.Length == 2 && args[0] == "exception-boundary")
        {
            return ExceptionProjectionProcessFixture.Boundary(args[1]);
        }

        if (args.Length == 2 && args[0] == "exception-projection")
        {
            return ExceptionProjectionProcessFixture.Run(args[1]);
        }

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
        DateTimeOffset beforeInitializationUtc = DateTimeOffset.UtcNow;
        Assembly application = Assembly.GetEntryAssembly()!;
        CIS.Logging.EventMetadata snapshot;
        if (scenario == "native")
        {
            snapshot = new CIS.Logging.EventMetadata();
        }
        else
        {
            // Historical fallback/unavailable projection bindings use controlled test inputs.
            // Native constructor discovery is the separate native mode; no readonly fact is reassigned.
            using Process suppliedProcess = Process.GetCurrentProcess();
            ConfigurationFixtureAdapter.Facts = new FixtureEnvironmentValues
            {
                ApplicationName = application.GetName().Name!,
                HostName = System.Environment.MachineName,
                OperatingSystem = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "unknown",
                OperatingSystemDescription = RuntimeInformation.OSDescription,
                OSArchitecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                RuntimeDescription = RuntimeInformation.FrameworkDescription,
                ProcessId = System.Environment.ProcessId,
                ProcessStartedAtUtc = new DateTimeOffset(suppliedProcess.StartTime.ToUniversalTime()),
                InstanceId = Guid.NewGuid().ToString("D"),
                InitializedAtUtc = DateTimeOffset.UtcNow
            };
            FixtureEnvironmentValues facts = ConfigurationFixtureAdapter.Facts;
            facts.BuildVersion = scenario == "assembly-version" ? application.GetName().Version!.ToString() : "unknown";
            if (scenario == "unavailable")
            {
                facts.ProcessStartedAtUtc = null;
                facts.HostName = null;
                facts.OperatingSystemDescription = null;
                facts.OSArchitecture = null;
                facts.ProcessArchitecture = null;
                facts.RuntimeDescription = null;
                facts.ProcessId = null;
            }
            facts.ServiceVersion = facts.BuildVersion;
            ConfigurationFixtureAdapter.ApplyOverrides(System.Environment.GetEnvironmentVariable);
            snapshot = facts.CaptureMetadata();
        }
        DateTimeOffset afterInitializationUtc = DateTimeOffset.UtcNow;
        Assembly entry = Assembly.GetEntryAssembly()!;
        Assembly core = typeof(EnvironmentOptions).Assembly;
        using Process process = Process.GetCurrentProcess();
        var result = new
        {
            Scenario = scenario,
            FactBinding = scenario == "native" ? "static-constructor" : "controlled-internal-projection",
            Application = new { snapshot.ApplicationName, snapshot.BuildVersion, snapshot.CommitId, snapshot.BuildId },
            Versions = new { snapshot.BuildVersion, snapshot.ServiceVersion },
            Runtime = new { snapshot.ProcessStartedAtUtc, snapshot.HostName, snapshot.OperatingSystem, snapshot.OperatingSystemDescription, snapshot.OSArchitecture, snapshot.ProcessArchitecture, snapshot.RuntimeDescription, snapshot.ProcessId },
            Initialization = new { snapshot.InstanceId, snapshot.InitializedAtUtc },
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

}
