using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Logging;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

internal static class RuntimeCreateFixture
{
    internal static readonly string[] Modes = ["file", "disabled", "invalid-settings", "invalid-metadata", "contended"];
    internal static int Run(string mode, string root)
    {
        Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0;
    }
    private static byte[] ReadLive(string path)
    {
        // A reader must also share the active writer's existing write access on Windows.
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var bytes = new MemoryStream(); input.CopyTo(bytes); return bytes.ToArray();
    }
    private static async Task<JsonObject> ExecuteAsync(string mode, string root)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal)); Directory.CreateDirectory(Path.Combine(root, "logs Ω"));
        string path = Path.Combine(root, "logs Ω", "active.log");
        LoggingOptions options = EnvironmentOptions.Logging; options.ConsoleLevel = CoreLogLevel.None; options.FileLevel = mode == "disabled" ? CoreLogLevel.None : CoreLogLevel.Information;
        options.FilePath = Path.Combine("logs Ω", "active.log"); options.FileFormat = LogOutputFormat.JsonLines;
        options.MaxEntryBytes = mode == "invalid-settings" ? 0 : 16384; options.ShutdownTimeoutMilliseconds = 5000;
        if (mode == "invalid-metadata") { EnvironmentOptions.CommitId = "invalid\0commit"; }
        CoreLogRuntime? runtime = null; string? error = null; bool preserved = true, contention = false, released = false, frozen = false;
        try
        {
            try { runtime = CoreLogRuntime.Create(); }
            catch (CoreConfigurationException) when (mode is "invalid-settings" or "invalid-metadata") { error = "CoreConfigurationException"; }
            if (error is not null)
            {
                Require(!File.Exists(path) && !File.Exists(path + ".cis.lock"));
                JsonObject failed = Common(mode); failed["error"] = error; failed["noFileArtifacts"] = true; return failed;
            }
            Require(runtime is not null && Path.IsPathFullyQualified(options.FilePath!) && options.FilePath == path);
            CoreLogRuntime actual = runtime!;
            if (mode != "disabled")
            {
                if (mode == "file") { options.FileLevel = CoreLogLevel.None; options.FilePath = Path.Combine(root, "must-not-create.log"); }
                Accepted(actual.CreateLogger("PublicFactory").Write(CoreLogLevel.Information, Data("factory")));
                LogFlushResult flushed = await actual.FlushAsync(Guard, CancellationToken.None).WaitAsync(Guard); Require(flushed.Succeeded);
                byte[] before = ReadLive(path);
                if (mode == "contended")
                {
                    try { await CoreLogRuntime.Create().DisposeAsync(); throw new InvalidOperationException("Second owner was admitted."); }
                    catch (CoreLogInitializationException failure) { Require(failure.OutputName == "file"); error = failure.ErrorCode; contention = true; }
                    preserved = before.SequenceEqual(ReadLive(path)); Require(preserved);
                }
                frozen = ReadLive(path).Length > 0 && !File.Exists(Path.Combine(root, "must-not-create.log")); Require(frozen);
            }
            LogStopResult result = await actual.StopAsync(CancellationToken.None).WaitAsync(Guard); Require(result.Succeeded);
            Task shared = actual.DisposeAsync().AsTask(); Require(ReferenceEquals(shared, actual.StopAsync(CancellationToken.None))); await shared.WaitAsync(Guard);
            Require(ReferenceEquals(result, actual.GetDiagnostics().LastStopResult));
            if (mode != "disabled")
            {
                using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); released = true;
            }
            else { Require(actual.GetDiagnostics().Outputs.Count == 0 && !File.Exists(path) && !File.Exists(path + ".cis.lock")); }
            JsonObject report = Common(mode); report["result"] = StopJoinFixture.Json(result); report["error"] = error; report["contention"] = contention;
            report["preserved"] = preserved; report["released"] = released; report["frozen"] = frozen; report["noFileArtifacts"] = mode == "disabled";
            report["bytes"] = Convert.ToBase64String(mode == "disabled" ? [] : ReadLive(path)); report["application"] = EnvironmentOptions.ApplicationName;
            report["factPid"] = EnvironmentOptions.ProcessId; report["sameTask"] = true; return report;
        }
        finally { if (runtime is not null) { await runtime.DisposeAsync(); } }
    }
}
