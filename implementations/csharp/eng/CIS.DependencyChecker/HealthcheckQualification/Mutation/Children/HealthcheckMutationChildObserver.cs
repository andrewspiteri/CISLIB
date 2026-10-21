using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using static CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildProtocol;

namespace CIS.Verification.HealthcheckMutationChildren;

internal static class HealthcheckMutationChildObserver
{
    // This file is linked only into the real test fixtures, never shipping CIS.Core.
    // The tool also compiles it, but the parent session alone cannot activate it.
    [ModuleInitializer]
    internal static void Observe()
    {
        string? launchPath = Environment.GetEnvironmentVariable(LaunchVariable);
        if (string.IsNullOrEmpty(launchPath)) { return; }
        string receiptPath = Path.Combine(Path.GetDirectoryName(launchPath)!, "receipt.json");
        try
        {
            Launch launch = Read<Launch>(launchPath);
            Session session = Read<Session>(launch.SessionPath);
            Require(launch.Schema == Schema && session.Schema == Schema && session.CoverageAnalysisOff, "Unexpected child proof protocol.");
            Require(Hash(launch.SessionPath) == launch.SessionSha256 && Under(session.OutputRoot, launchPath), "The child session input changed.");
            Require(Under(Path.Combine(session.OutputRoot, "launches"), launchPath) && Path.GetFileName(launchPath) == "launch.json",
                "Unexpected child receipt context.");
            Require(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STRYKER_MUTANT_FILE"))
                && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STRYKER_COVERAGE_FILE")), "A child inherited mutable file-based control or coverage IPC.");
            Require(Environment.GetEnvironmentVariable("STRYKER_MUTANT_ID_CONTROL_VAR") == SelectorVariable
                && Environment.GetEnvironmentVariable(SelectorVariable) == launch.ActiveMutant.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "The child did not inherit its frozen selector.");
            VerifyFiles(launch.Stage.Directory, launch.Stage.DeployedFiles);
            Assembly entry = Assembly.GetEntryAssembly() ?? throw new InvalidOperationException("Missing child entry assembly.");
            Require(string.Equals(Path.GetFullPath(entry.Location), Path.GetFullPath(Path.Combine(launch.Stage.Directory, launch.Stage.AssemblyFile)), PathComparison),
                "The child started a different executable.");
            Require(entry.GetName().Name == launch.Stage.AssemblyName, "The child executable identity changed; probe friend access must remain CisHealthProbe.");
            ValidateRuntimeConfiguration(entry.Location);
            ValidateDependencyGraph(entry.Location);
            Assembly core = Assembly.Load(new AssemblyName("CIS.Core")); // Use normal deps resolution, not an arbitrary explicit load path.
            string expectedCore = Path.Combine(AppContext.BaseDirectory, "CIS.Core.dll");
            string coreHash = Hash(core.Location);
            Require(string.Equals(Path.GetFullPath(core.Location), Path.GetFullPath(expectedCore), PathComparison)
                && coreHash == launch.ParentCoreSha256 && coreHash == launch.Stage.CoreSha256,
                "The actual child loaded a stale or different Core.");
            Guid mvid = core.ManifestModule.ModuleVersionId;
            Require(mvid == launch.ParentCoreMvid && mvid == launch.Stage.CoreMvid && mvid == Mvid(core.Location),
                "The child Core module identity differs from the loaded parent.");
            Type? controller = Controller(core);
            Require((controller is not null) == launch.Instrumented && controller?.FullName == launch.ControllerType,
                "The child did not receive the exact generated Stryker controller.");
            int active = controller is null ? -1 : Active(controller);
            Require(active == launch.ActiveMutant, "The selected child mutant differs from its parent.");
            string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Missing actual child runtime host path.");
            Require(Hash(processPath) == session.DotnetSha256 && string.Equals(Path.GetFullPath(processPath), Path.GetFullPath(session.DotnetPath), PathComparison),
                "The child used a different dotnet executable.");
            string runtime = RuntimeInformation.FrameworkDescription;
            Require(runtime == ".NET 10.0.12" && Hash(typeof(object).Assembly.Location) == session.CoreLibrarySha256,
                "The child did not use the pinned actual CLR/runtime.");
            Write(receiptPath, new Receipt(Schema, launch.Id, Hash(launchPath), DateTimeOffset.UtcNow, Environment.ProcessId,
                entry.Location, entry.GetName().Name!, Hash(entry.Location), core.Location, coreHash, mvid, controller?.FullName,
                active, controller is not null, runtime, processPath, Hash(processPath), Hash(typeof(object).Assembly.Location),
                Environment.GetCommandLineArgs(), null));
            AppDomain.CurrentDomain.UnhandledException += (_, failure) =>
            {
                try
                {
                    Require(failure.IsTerminating && failure.ExceptionObject is ArgumentException
                        && failure.ExceptionObject.GetType() == typeof(ArgumentException), "Only an exact terminating ArgumentException is attributed.");
                    var exception = (ArgumentException)failure.ExceptionObject;
                    MethodBase method = new StackTrace(exception, fNeedFileInfo: false).GetFrame(0)?.GetMethod()
                        ?? throw new InvalidOperationException("Missing first managed throw frame.");
                    Require(launch.Instrumented && launch.ActiveMutant >= 0 && launch.Stage.AssemblyFile == "CIS.Healthcheck.HttpFixture.dll"
                        && method.Module.Assembly == core && method.Module.ModuleVersionId == mvid
                        && string.Equals(method.Module.Assembly.Location, core.Location, PathComparison),
                        "The actual first throw frame is not the selected verified Core HTTP fixture.");
                    Require(Hash(core.Location) == coreHash && Active(controller!) == launch.ActiveMutant, "The terminating Core origin/selector changed.");
                    string type = method.DeclaringType?.FullName ?? "";
                    VerifyRegistrationFrame(core.Location, mvid, method.MetadataToken, type, method.Name);
                    VerifyFiles(launch.Stage.Directory, launch.Stage.DeployedFiles);
                    Write(Path.Combine(Path.GetDirectoryName(launchPath)!, "managed-failure.json"), new ManagedFailureReceipt(
                        Schema, launch.Id, Hash(launchPath), Hash(receiptPath), Environment.ProcessId, DateTimeOffset.UtcNow,
                        failure.IsTerminating, typeof(ArgumentException).FullName!, core.Location, coreHash, mvid,
                        method.MetadataToken, type, method.Name, 0, launch.ActiveMutant));
                }
                catch (Exception error)
                {
                    Write(Path.Combine(Path.GetDirectoryName(launchPath)!, "managed-failure-rejected.json"), new
                    { error = error.ToString(), exceptionType = failure.ExceptionObject.GetType().FullName, failure.IsTerminating, pid = Environment.ProcessId });
                }
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try
                {
                    VerifyFiles(launch.Stage.Directory, launch.Stage.DeployedFiles);
                    Write(Path.Combine(Path.GetDirectoryName(launchPath)!, "exit.json"),
                        new ExitReceipt(launch.Id, Environment.ProcessId, Environment.ExitCode, DateTimeOffset.UtcNow));
                }
                catch (Exception error)
                {
                    Write(Path.Combine(Path.GetDirectoryName(launchPath)!, "exit-failure.json"), new { error = error.ToString() });
                }
            };
        }
        catch (Exception error)
        {
            Write(Path.Combine(Path.GetDirectoryName(launchPath)!, "observer-failure.json"), new { error = error.ToString(), pid = Environment.ProcessId });
            throw;
        }
    }

    private static void ValidateRuntimeConfiguration(string assembly)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(assembly, ".runtimeconfig.json")));
        JsonElement options = document.RootElement.GetProperty("runtimeOptions");
        Require(options.GetProperty("tfm").GetString() == "net10.0" && options.GetProperty("rollForward").GetString() == "Disable",
            "The real child runtime roll-forward/framework pins changed.");
        JsonElement[] frameworks = options.TryGetProperty("frameworks", out JsonElement many) ? many.EnumerateArray().ToArray() : [options.GetProperty("framework")];
        Require(frameworks.Length is > 0 and <= 2 && frameworks.All(framework => framework.GetProperty("version").GetString() == "10.0.12")
            && frameworks.Any(framework => framework.GetProperty("name").GetString() == "Microsoft.NETCore.App"), "The real child framework pins changed.");
    }

    private static void ValidateDependencyGraph(string assembly)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(assembly, ".deps.json")));
        JsonElement target = document.RootElement.GetProperty("targets").EnumerateObject().Single().Value;
        JsonProperty[] core = target.EnumerateObject().Where(item => item.Name.StartsWith("CIS.Core/", StringComparison.Ordinal)).ToArray();
        Require(core.Length == 1 && core[0].Value.GetProperty("runtime").EnumerateObject().Select(item => item.Name).SequenceEqual(["CIS.Core.dll"]),
            "The real child deps graph does not resolve the one actual Core asset.");
        Require(!target.EnumerateObject().Any(item => item.Name.StartsWith("Stryker", StringComparison.OrdinalIgnoreCase)
            || item.Name.StartsWith("Microsoft.NET.Test.Sdk/", StringComparison.OrdinalIgnoreCase)), "Test/tool dependencies leaked into a real child fixture graph.");
    }
}
