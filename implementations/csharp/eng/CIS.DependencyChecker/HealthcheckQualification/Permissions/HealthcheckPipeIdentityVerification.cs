using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

// Actual Linux identities and the existing packaged production transports only.
internal sealed class HealthcheckPipeIdentityVerification
{
    private const string OriginPrefix = "CIS_HEALTHCHECK_PACKAGE_ORIGIN ";
    private readonly string host, probe, coreHash, output, dotnet, runuser, identity, stat, temporary;
    private readonly string? hostUmask;
    private readonly Dictionary<string, string> pins = new(StringComparer.Ordinal);
    private readonly JsonObject report;

    private HealthcheckPipeIdentityVerification(TypeManagerVerificationInputs input)
    {
        host = input.PathOf("host"); probe = input.PathOf("probe"); output = input.PathOf("output"); coreHash = input.Get("core-sha256");
        hostUmask = input.Optional("host-umask");
        dotnet = ResolveExecutable(input.Optional("dotnet") ?? "dotnet");
        runuser = SystemTool("runuser", "/usr/sbin/runuser", "/sbin/runuser", "/usr/bin/runuser");
        identity = SystemTool("id", "/usr/bin/id", "/bin/id"); stat = SystemTool("stat", "/usr/bin/stat", "/bin/stat");
        temporary = Path.GetFullPath(Path.GetTempPath());
        report = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["complete"] = false,
            ["passed"] = false,
            ["scope"] = "Actual existing Linux identities using the supplied package-bound host/probe and Core hash; no published-package, privileged-root-denial or full-release claim",
            ["platform"] = RuntimeInformation.OSDescription,
            ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["expectedCoreSha256"] = coreHash,
            ["processes"] = new JsonArray(),
            ["hostUmask"] = hostUmask ?? "inherited",
            ["verifierSha256"] = Hash(typeof(HealthcheckPipeIdentityVerification).Assembly.Location)
        };
    }

    internal static async Task<int> RunAsync(string[] arguments)
    {
        Require(OperatingSystem.IsLinux(), "Different-identity socket qualification must actually execute on Linux.");
        var input = new TypeManagerVerificationInputs(arguments, ["host", "probe", "core-sha256", "output"], ["host-umask"]);
        Require(input.Optional("host-umask") is null or "000", "Only the explicit optional --host-umask 000 lane is supported.");
        string output = input.PathOf("output");
        Require(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(), "Use a new or empty output directory; failed attempts are retained.");
        Require(input.Get("core-sha256") is { Length: 64 } hash && hash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'), "Use the actual lowercase SHA-256 of the supplied package Core asset.");
        Directory.CreateDirectory(Path.Combine(output, "logs"));
        return await new HealthcheckPipeIdentityVerification(input).VerifyAsync().ConfigureAwait(false);
    }

    private async Task<int> VerifyAsync()
    {
        int exit = 1;
        try
        {
            if (!OperatingSystem.IsLinux()) { throw new InvalidDataException("Actual Linux execution is required."); }
            foreach (string file in new[] { dotnet, runuser, identity, stat }) { Pin(file); }
            if (hostUmask == "000") { Pin("/bin/sh"); }
            foreach (string assembly in new[] { host, probe })
            {
                Require(File.Exists(assembly) && Path.GetExtension(assembly) == ".dll", "Supply the real built package-consumer DLLs.");
                foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(assembly)!, "*", SearchOption.TopDirectoryOnly)
                    .Where(file => Path.GetExtension(file) is ".dll" or ".json")) { Pin(file); }
                Require(Pin(Path.Combine(Path.GetDirectoryName(assembly)!, "CIS.Core.dll")) == coreHash, "Supplied consumer contains different Core bytes.");
            }
            await ExpectAsync("invoking-root", identity, ["-u"], "0").ConfigureAwait(false);
            await ExpectAsync("owner-user", runuser, ["-u", "user", "--", identity, "-u"], "1000").ConfigureAwait(false);
            await ExpectAsync("nonowner-nobody", runuser, ["-u", "nobody", "--", identity, "-u"], "65534").ConfigureAwait(false);
            await ExpectAsync("sdk", dotnet, ["--version"], "10.0.401").ConfigureAwait(false);

            await using var server = new Child(this, "owner-host", runuser, HostArguments(), Path.GetDirectoryName(host)!);
            JsonObject ready = await server.ReadObjectAsync().ConfigureAwait(false);
            JsonObject hostOrigin = await server.ReadOriginAsync().ConfigureAwait(false);
            ValidateOrigin(hostOrigin, host);
            Require((string?)ready["status"] == "ready" && (string?)ready["runtime"] == ".NET 10.0.12", "Actual owner host did not start on the pinned runtime.");
            int serverPid = (int?)ready["pid"] ?? throw new InvalidDataException("Missing actual host PID.");
            string processStatus = File.ReadAllText("/proc/" + serverPid.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/status");
            string uidLine = processStatus.Split('\n').Single(line => line.StartsWith("Uid:", StringComparison.Ordinal));
            string[] uids = uidLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[1..];
            Require(uids.Length == 4 && uids.All(value => value == "1000"), "Actual host process is not running with the existing owner's real/effective/saved/filesystem UID.");
            Write(Path.Combine(output, "host.proc-status.txt"), processStatus);
            string pipe = (string?)ready["pipeName"] ?? "";
            Require(pipe.StartsWith("cis-fixture-", StringComparison.Ordinal) && pipe.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'), "Unexpected real fixture pipe identity.");
            string socket = Path.Combine(temporary, "CoreFxPipe_" + pipe);
            UnixFileMode mode = File.GetUnixFileMode(socket);
            Require((mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite)) == (UnixFileMode.UserRead | UnixFileMode.UserWrite),
                "Actual production socket does not permit its owner to read/write.");
            Observation metadata = await RunAsync("socket-stat", stat, ["--format=%u:%g:%a:%F", "--", socket]).ConfigureAwait(false);
            string[] fields = metadata.Stdout.Trim().Split(':');
            Require(metadata.ExitCode == 0 && !metadata.TimedOut && fields.Length == 4 && fields[0] == "1000" && fields[3] == "socket", "Actual endpoint is not an owner-UID1000 Unix socket.");
            if (hostUmask == "000")
            {
                Require(fields[2] == "777" && (int)mode == 511, "The permissive umask000 lane must actually observe socket mode0777 before denial can qualify peer identity enforcement.");
            }
            report["host"] = new JsonObject { ["startup"] = ready, ["origin"] = hostOrigin, ["actualUidLine"] = uidLine };
            report["socket"] = new JsonObject { ["path"] = socket, ["managedMode"] = mode.ToString(), ["managedModeNumeric"] = (int)mode, ["stat"] = metadata.Stdout.Trim() };
            var address = new Uri((string)ready["address"]!);
            Require(address.Scheme == "http" && address.Host == "127.0.0.1", "Host must use its actual loopback HTTP endpoint.");
            using var http = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(15) };
            await HealthyHttpAsync(http, "before-denial").ConfigureAwait(false);
            await ProbeAsync("same-owner-before", "user", pipe, 0).ConfigureAwait(false);
            JsonObject before = await server.CommandAsync("state").ConfigureAwait(false);
            await ProbeAsync("different-user-denied", "nobody", pipe, 2).ConfigureAwait(false);
            JsonObject after = await server.CommandAsync("state").ConfigureAwait(false);
            Require((int?)before["captures"] == 2 && (int?)after["captures"] == (int?)before["captures"]
                && (int?)before["businessCalls"] == 0 && (int?)after["businessCalls"] == 0,
                "Different-user attempt reached report capture or changed business behavior.");
            await HealthyHttpAsync(http, "after-denial").ConfigureAwait(false);
            await ProbeAsync("same-owner-after", "user", pipe, 0).ConfigureAwait(false);
            JsonObject recovered = await server.CommandAsync("state").ConfigureAwait(false);
            Require((int?)recovered["captures"] == 4 && (int?)recovered["businessCalls"] == 0, "Actual owner/reporting did not remain usable after denied access.");
            report["captureProof"] = new JsonObject { ["beforeOtherIdentity"] = before, ["afterOtherIdentity"] = after, ["afterHealthyHttpAndOwnerRetry"] = recovered };
            await server.SendAsync("exit").ConfigureAwait(false);
            Observation stopped = await server.FinishAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            Require(stopped.ExitCode == 0 && !stopped.TimedOut, "Actual owner server did not finish normal bounded shutdown.");
            Require(StripOrigin(stopped.Stderr).Length == 0 && stopped.Stderr.Split('\n').Count(line => line.StartsWith(OriginPrefix, StringComparison.Ordinal)) == 1,
                "Actual server emitted unexpected stderr or duplicate loaded-origin observations.");
            report["complete"] = true; report["passed"] = true; exit = 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or JsonException or TimeoutException or System.ComponentModel.Win32Exception or System.Net.Http.HttpRequestException or OperationCanceledException)
        { report["failure"] = error.ToString(); }
        finally
        {
            try { foreach ((string path, string hash) in pins) { Require(Hash(path) == hash, "Qualification input changed: " + path); } report["inputsVerifiedAfter"] = true; }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            { report["integrityFailure"] = error.ToString(); report["complete"] = false; report["passed"] = false; exit = 1; }
            report["inputPins"] = JsonSerializer.SerializeToNode(pins); report["exitCode"] = exit; Save();
        }
        Console.WriteLine(new JsonObject { ["passed"] = report["passed"]!.DeepClone(), ["exitCode"] = exit }.ToJsonString());
        return exit;
    }

    private async Task ProbeAsync(string name, string user, string pipe, int expected)
    {
        Observation result = await RunAsync(name, runuser, AsUser(user, Execute(probe, "--pipe", pipe, "--view", "health", "--timeout-ms", "1000"))).ConfigureAwait(false);
        Require(result.ExitCode == expected && !result.TimedOut, "Actual " + user + " probe returned a different exit: " + name);
        string[] markers = result.Stderr.Split('\n').Where(line => line.StartsWith(OriginPrefix, StringComparison.Ordinal)).ToArray();
        Require(markers.Length == 1, "Actual probe did not report exactly one loaded Core origin.");
        JsonObject origin = JsonNode.Parse(markers[0][OriginPrefix.Length..])!.AsObject(); ValidateOrigin(origin, probe);
        string error = StripOrigin(result.Stderr);
        if (expected == 2)
        {
            Require(result.Stdout.Length == 0 && error is "cis-health-probe: TransportOrProtocolFailure" or "cis-health-probe: TimeoutOrCanceled",
                "Nonowner must fail through the actual bounded probe transport path, not a host/launch/runtime failure.");
        }
        else
        {
            JsonObject snapshot = JsonNode.Parse(result.Stdout)!.AsObject();
            Require(error.Length == 0 && (string?)snapshot["healthStatus"] == "Healthy" && (bool?)snapshot["ready"] == true, "Same-owner probe did not receive an actual healthy report.");
        }
        report[name] = new JsonObject { ["user"] = user, ["exitCode"] = result.ExitCode, ["origin"] = origin }; Save();
    }

    private async Task HealthyHttpAsync(HttpClient client, string name)
    {
        using HttpResponseMessage response = await client.GetAsync("/_health").ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Write(Path.Combine(output, "logs", name + ".http.json"), body);
        JsonObject snapshot = JsonNode.Parse(body)!.AsObject();
        Require(response.StatusCode == HttpStatusCode.OK && (string?)snapshot["healthStatus"] == "Healthy" && (bool?)snapshot["ready"] == true,
            "Real HTTP reporting was unhealthy around the denied pipe attempt.");
        report[name + "-http"] = new JsonObject { ["status"] = (int)response.StatusCode, ["bodySha256"] = Hash(Path.Combine(output, "logs", name + ".http.json")) }; Save();
    }

    private void ValidateOrigin(JsonObject origin, string assembly) => Require((bool?)origin["exact"] == true && (string?)origin["sha256"] == coreHash
        && (string?)origin["runtime"] == ".NET 10.0.12" && (string?)origin["architecture"] == "X64"
        && Path.GetFullPath((string)origin["path"]!) == Path.Combine(Path.GetDirectoryName(assembly)!, "CIS.Core.dll"), "Actual process loaded different Core bytes/location/runtime.");
    private async Task ExpectAsync(string name, string executable, string[] arguments, string expected)
    {
        Observation result = await RunAsync(name, executable, arguments).ConfigureAwait(false);
        Require(result.ExitCode == 0 && !result.TimedOut && result.Stdout.Trim() == expected && result.Stderr.Length == 0, "Existing identity/tool requirement failed: " + name);
    }
    private async Task<Observation> RunAsync(string name, string executable, string[] arguments)
    {
        await using var child = new Child(this, name, executable, arguments, Path.GetDirectoryName(host)!);
        return await child.FinishAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
    }
    private string[] AsUser(string user, string[] arguments) => ["-u", user, "--", dotnet, .. arguments];
    private string[] HostArguments() => hostUmask == "000"
        ? ["-u", "user", "--", "/bin/sh", "-c", "umask 000; exec \"$@\"", "healthcheck-host", dotnet, .. Execute(host, "pipe-live")]
        : AsUser("user", Execute(host, "pipe-live"));
    private static string[] Execute(string assembly, params string[] arguments) => ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", assembly, .. arguments];
    private string Pin(string path) { string hash = Hash(path); if (pins.TryGetValue(path, out string? previous)) { Require(previous == hash, "Input changed: " + path); } else { pins.Add(path, hash); } return hash; }
    private void Save() => WriteJson(Path.Combine(output, "results.json"), report);
    private static string StripOrigin(string value) => string.Join('\n', value.Split('\n').Where(line => !line.StartsWith(OriginPrefix, StringComparison.Ordinal))).Trim();
    private static string SystemTool(string name, params string[] choices) => choices.FirstOrDefault(File.Exists) ?? throw new InvalidDataException("Required existing Linux tool is absent: " + name);
    private static string ResolveExecutable(string value)
    {
        string? path = Path.IsPathFullyQualified(value) ? value : (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, value)).FirstOrDefault(File.Exists);
        Require(path is not null && File.Exists(path), "Provide the existing pinned Linux dotnet executable."); return Path.GetFullPath(path!);
    }

    private sealed record Observation(int ExitCode, bool TimedOut, string Stdout, string Stderr);

    private sealed class Child : IAsyncDisposable
    {
        private readonly HealthcheckPipeIdentityVerification owner;
        private readonly string name, executable, cwd;
        private readonly string[] arguments;
        private readonly Process process;
        private readonly StringBuilder stdout = new(), stderr = new();
        private readonly object outputGate = new(), errorGate = new();
        private readonly Channel<string> lines = Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource<JsonObject> origin = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task outputTask, errorTask;
        private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
        private Observation? result;

        internal Child(HealthcheckPipeIdentityVerification owner, string name, string executable, string[] arguments, string cwd)
        {
            this.owner = owner; this.name = name; this.executable = executable; this.arguments = arguments; this.cwd = cwd;
            var start = new ProcessStartInfo(executable)
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
            foreach (string key in start.Environment.Keys.Where(key => key.StartsWith("CIS_HEALTHCHECK_", StringComparison.OrdinalIgnoreCase)).ToArray()) { start.Environment.Remove(key); }
            start.Environment["TMPDIR"] = owner.temporary; start.Environment["DOTNET_ROLL_FORWARD"] = "Disable";
            start.Environment["LC_ALL"] = "C";
            process = new Process { StartInfo = start };
            try { Require(process.Start(), "Could not start owned identity qualification process."); }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                owner.report["processes"]!.AsArray().Add(new JsonObject { ["name"] = name, ["executable"] = executable, ["arguments"] = JsonSerializer.SerializeToNode(arguments), ["startFailure"] = error.ToString() });
                owner.Save(); process.Dispose(); throw;
            }
            outputTask = DrainAsync(process.StandardOutput, stdout, outputGate, isError: false);
            errorTask = DrainAsync(process.StandardError, stderr, errorGate, isError: true);
        }

        internal Task<JsonObject> ReadOriginAsync() => origin.Task.WaitAsync(TimeSpan.FromSeconds(15));
        internal async Task<JsonObject> ReadObjectAsync() => JsonNode.Parse(await lines.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false))!.AsObject();
        internal async Task<JsonObject> CommandAsync(string value) { await SendAsync(value).ConfigureAwait(false); return await ReadObjectAsync().ConfigureAwait(false); }
        internal async Task SendAsync(string value)
        {
            await process.StandardInput.WriteLineAsync(value).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        internal async Task<Observation> FinishAsync(TimeSpan timeout, bool terminate = false)
        {
            if (result is not null) { return result; }
            bool timedOut = false, forced = terminate && !process.HasExited;
            try
            {
                if (forced) { Kill(); }
                await process.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(false);
            }
            catch (TimeoutException) { timedOut = true; forced = true; Kill(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
            finally
            {
                try { await Task.WhenAll(outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
                finally
                {
                    string output, error; lock (outputGate) { output = stdout.ToString(); }
                    lock (errorGate) { error = stderr.ToString(); }
                    result = new Observation(process.HasExited ? process.ExitCode : -1, timedOut, output, error);
                    string log = Path.Combine(owner.output, "logs", name); Write(log + ".stdout.log", output); Write(log + ".stderr.log", error);
                    var row = new JsonObject
                    {
                        ["name"] = name,
                        ["executable"] = executable,
                        ["arguments"] = JsonSerializer.SerializeToNode(arguments),
                        ["workingDirectory"] = cwd,
                        ["pid"] = process.Id,
                        ["startedUtc"] = started,
                        ["finishedUtc"] = DateTimeOffset.UtcNow,
                        ["exitCode"] = result.ExitCode,
                        ["timedOut"] = timedOut,
                        ["forcedTermination"] = forced,
                        ["stdoutSha256"] = Hash(log + ".stdout.log"),
                        ["stderrSha256"] = Hash(log + ".stderr.log")
                    };
                    WriteJson(log + ".process.json", row); owner.report["processes"]!.AsArray().Add(row.DeepClone()); owner.Save();
                }
            }
            return result;
        }
        private void Kill() { try { if (!process.HasExited) { process.Kill(entireProcessTree: true); } } catch (InvalidOperationException) when (process.HasExited) { } }
        private async Task DrainAsync(StreamReader reader, StringBuilder capture, object gate, bool isError)
        {
            var pending = new StringBuilder(); var buffer = new char[4096];
            try
            {
                int count;
                while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) != 0)
                {
                    lock (gate) { capture.Append(buffer, 0, count); }
                    for (int index = 0; index < count; index++)
                    {
                        if (buffer[index] != '\n') { pending.Append(buffer[index]); continue; }
                        string line = pending.ToString().TrimEnd('\r'); pending.Clear();
                        if (!isError) { lines.Writer.TryWrite(line); }
                        else if (line.StartsWith(OriginPrefix, StringComparison.Ordinal)) { origin.TrySetResult(JsonNode.Parse(line[OriginPrefix.Length..])!.AsObject()); }
                    }
                }
                if (!isError && pending.Length > 0) { lines.Writer.TryWrite(pending.ToString()); }
            }
            finally { if (!isError) { lines.Writer.TryComplete(); } }
        }
        public async ValueTask DisposeAsync() { try { await FinishAsync(TimeSpan.FromSeconds(10), terminate: true).ConfigureAwait(false); } finally { process.Dispose(); } }
    }
}
