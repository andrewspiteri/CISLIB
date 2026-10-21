using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal sealed partial class HealthcheckPackageVerification
{
    private sealed record ProcessObservation(int ExitCode, bool TimedOut, string StandardOutput, string StandardError);

    private async Task<ProcessObservation> RunChildAsync(string name, string[] arguments, string cwd)
    {
        await using var child = new OwnedProcess(this, name, arguments, cwd);
        return await child.FinishAsync(TimeSpan.FromMinutes(4)).ConfigureAwait(false);
    }

    private JsonObject ValidateOrigin(ProcessObservation process, Consumer consumer)
    {
        string[] lines = process.StandardError.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.StartsWith(OriginPrefix, StringComparison.Ordinal)).ToArray();
        Require(lines.Length == 1, consumer.Name + " did not report exactly one actual loaded Core origin.");
        JsonObject origin = JsonNode.Parse(lines[0][OriginPrefix.Length..])!.AsObject();
        Require((bool?)origin["exact"] == true && (string?)origin["sha256"] == coreHash
            && (string?)origin["runtime"] == ".NET 10.0.12" && (string?)origin["architecture"] == "X64"
            && ((string?)origin["assembly"])?.StartsWith("CIS.Core, Version=", StringComparison.Ordinal) == true
            && SamePath((string)origin["path"]!, Path.Combine(consumer.Output, "CIS.Core.dll")), consumer.Name + " loaded a different Core origin or runtime.");
        return origin;
    }

    private static string[] Execute(Consumer consumer, params string[] arguments) =>
        ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Path.Combine(consumer.Output, consumer.Assembly + ".dll"), .. arguments];

    // The fixture needs an interactive stdin while both streams keep draining.
    // Every owned process is bounded, observed and retained even on timeout.
    private sealed class OwnedProcess : IAsyncDisposable
    {
        private readonly HealthcheckPackageVerification owner;
        private readonly string name, cwd;
        private readonly string[] arguments;
        private readonly Process process;
        private readonly StringBuilder stdout = new(), stderr = new();
        private readonly object stdoutGate = new(), stderrGate = new();
        private readonly Channel<string> lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        private readonly Task output, error;
        private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
        private ProcessObservation? result;

        internal OwnedProcess(HealthcheckPackageVerification owner, string name, string[] arguments, string cwd)
        {
            this.owner = owner; this.name = name; this.arguments = arguments; this.cwd = cwd;
            var start = new ProcessStartInfo(owner.input.Dotnet)
            {
                WorkingDirectory = cwd,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false, true),
                StandardErrorEncoding = new UTF8Encoding(false, true)
            };
            foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
            foreach ((string key, string? value) in owner.environment)
            {
                if (value is null) { start.Environment.Remove(key); } else { start.Environment[key] = value; }
            }
            process = new Process { StartInfo = start };
            try { Require(process.Start(), "Could not start owned qualification process."); }
            catch (Exception failure) when (failure is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                owner.report["processes"]!.AsArray().Add(new JsonObject
                {
                    ["name"] = name,
                    ["executable"] = owner.input.Dotnet,
                    ["arguments"] = JsonSerializer.SerializeToNode(arguments),
                    ["workingDirectory"] = cwd,
                    ["startedUtc"] = started,
                    ["startFailure"] = failure.ToString()
                });
                owner.Save(); process.Dispose(); throw;
            }
            output = DrainAsync(process.StandardOutput, stdout, stdoutGate, lines.Writer);
            error = DrainAsync(process.StandardError, stderr, stderrGate, null);
        }

        internal async Task<JsonObject> ReadObjectAsync()
        {
            string line = await lines.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            return JsonNode.Parse(line)!.AsObject();
        }

        internal async Task<JsonObject> CommandAsync(string command)
        {
            await SendAsync(command).ConfigureAwait(false);
            return await ReadObjectAsync().ConfigureAwait(false);
        }

        internal async Task SendAsync(string command)
        {
            await process.StandardInput.WriteLineAsync(command).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        internal async Task<ProcessObservation> FinishAsync(TimeSpan timeout, bool terminate = false)
        {
            if (result is not null) { return result; }
            bool timedOut = false, forced = terminate && !process.HasExited;
            try
            {
                if (forced) { Kill(); }
                await process.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                timedOut = true; forced = true; Kill();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            finally
            {
                try { await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
                finally
                {
                    string standardOutput, standardError;
                    lock (stdoutGate) { standardOutput = stdout.ToString(); }
                    lock (stderrGate) { standardError = stderr.ToString(); }
                    int exit = process.HasExited ? process.ExitCode : -1;
                    result = new ProcessObservation(exit, timedOut, standardOutput, standardError);
                    string log = Path.Combine(owner.root, "logs", name);
                    Write(log + ".stdout.log", standardOutput); Write(log + ".stderr.log", standardError);
                    var record = new JsonObject
                    {
                        ["name"] = name,
                        ["executable"] = owner.input.Dotnet,
                        ["arguments"] = JsonSerializer.SerializeToNode(arguments),
                        ["workingDirectory"] = cwd,
                        ["pid"] = process.Id,
                        ["startedUtc"] = started,
                        ["finishedUtc"] = DateTimeOffset.UtcNow,
                        ["exitCode"] = exit,
                        ["timedOut"] = timedOut,
                        ["forcedTermination"] = forced,
                        ["streamRepresentation"] = "UTF-8 decoded process streams, preserving newline characters",
                        ["stdoutSha256"] = Hash(log + ".stdout.log"),
                        ["stderrSha256"] = Hash(log + ".stderr.log")
                    };
                    WriteJson(log + ".process.json", record); owner.report["processes"]!.AsArray().Add(record.DeepClone()); owner.Save();
                }
            }
            return result;
        }

        private void Kill()
        {
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
            catch (InvalidOperationException) when (process.HasExited) { }
        }

        private static async Task DrainAsync(StreamReader reader, StringBuilder capture, object gate, ChannelWriter<string>? writer)
        {
            var pending = new StringBuilder(); var buffer = new char[4096];
            try
            {
                int count;
                while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) != 0)
                {
                    lock (gate) { capture.Append(buffer, 0, count); }
                    if (writer is null) { continue; }
                    for (int index = 0; index < count; index++)
                    {
                        if (buffer[index] == '\n') { writer.TryWrite(pending.ToString().TrimEnd('\r')); pending.Clear(); }
                        else { pending.Append(buffer[index]); }
                    }
                }
                if (writer is not null && pending.Length > 0) { writer.TryWrite(pending.ToString()); }
            }
            finally { writer?.TryComplete(); }
        }

        public async ValueTask DisposeAsync()
        {
            try { await FinishAsync(TimeSpan.FromSeconds(10), terminate: true).ConfigureAwait(false); }
            finally { process.Dispose(); }
        }
    }
}
