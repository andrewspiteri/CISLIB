using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CIS.Healthcheck;

namespace CIS.Tests.HealthcheckHttpHost;

// This test-only peer deliberately has no CurrentUserOnly restriction. Its
// response is independent of the formatter/codec in the instrumented library.
internal static class ForeignOwnerRawServer
{
    internal const string Request = "{\"protocolVersion\":1,\"view\":\"health\"}";
    internal const string Response = "{\"protocolVersion\":1,\"schemaVersion\":1,\"view\":\"health\",\"kind\":\"snapshot\",\"snapshot\":{\"schemaVersion\":1,\"revision\":\"42\",\"capturedAtUtc\":\"2026-10-21T08:00:00.0000000Z\",\"application\":{\"name\":\"foreign-owner-oracle\",\"instanceId\":\"10000000-0000-0000-0000-000000000001\",\"startupCompleted\":true,\"isStopping\":false},\"healthStatus\":\"Healthy\",\"ready\":true,\"monitorCounts\":{\"active\":1,\"critical\":1,\"failing\":0,\"criticalFailing\":0,\"pending\":0}}}";

    internal static async Task RunAsync(string[] arguments)
    {
        if (!OperatingSystem.IsLinux() || arguments.Length != 2) { throw new ArgumentException("The raw foreign-owner fixture requires Linux, a unique pipe and an owned journal directory."); }
        string pipe = arguments[0], journal = Path.GetFullPath(arguments[1]);
        if (!pipe.StartsWith("cis-foreign-", StringComparison.Ordinal) || pipe.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-') || !Directory.Exists(journal))
        { throw new ArgumentException("Unexpected foreign-owner fixture paths."); }
        string socket = Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + pipe);
        int connections = 0, requests = 0, expectedRequests = 0, emptyConnections = 0, responses = 0;
        string? failure = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        Task stop = ObserveStopAsync();
        try
        {
            using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            Write("ready.json", new
            {
                schema = "cis.healthcheck.foreign-peer-ready/v1",
                pid = Environment.ProcessId,
                uidLine = File.ReadLines("/proc/self/status").Single(line => line.StartsWith("Uid:", StringComparison.Ordinal)),
                runtime = RuntimeInformation.FrameworkDescription,
                temporaryRoot = Path.GetTempPath(),
                pipe,
                socket,
                fixtureSha256 = Hash(typeof(ForeignOwnerRawServer).Assembly.Location),
                coreSha256 = Hash(typeof(HealthcheckService).Assembly.Location),
                pipesSha256 = Hash(typeof(NamedPipeServerStream).Assembly.Location)
            });
            while (!stopping.IsCancellationRequested)
            {
                try { await server.WaitForConnectionAsync(stopping.Token); }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { break; }
                connections++;
                try
                {
                    byte[] header = new byte[4];
                    int first = await server.ReadAsync(header.AsMemory(0, 1), stopping.Token);
                    if (first == 0) { emptyConnections++; continue; }
                    await server.ReadExactlyAsync(header.AsMemory(1), stopping.Token);
                    uint length = BinaryPrimitives.ReadUInt32LittleEndian(header);
                    requests++;
                    if (length is 0 or > 65536) { continue; }
                    byte[] request = new byte[(int)length];
                    await server.ReadExactlyAsync(request, stopping.Token);
                    if (Encoding.UTF8.GetString(request) == Request) { expectedRequests++; }
                    byte[] response = Encoding.UTF8.GetBytes(Response);
                    BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)response.Length);
                    await server.WriteAsync(header, stopping.Token);
                    await server.WriteAsync(response, stopping.Token);
                    await server.FlushAsync(stopping.Token);
                    responses++;
                }
                // A mutated client may close or cancel at any protocol boundary.
                // That is a product observation, not a broken helper.
                catch (IOException) { }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
                finally
                {
                    server.Disconnect();
                    Write("progress.json", Counters());
                }
            }
            if (deadline.IsCancellationRequested && !File.Exists(Path.Combine(journal, "stop"))) { throw new TimeoutException("The raw foreign-owner fixture reached its independent lifetime bound."); }
        }
        catch (Exception error)
        {
            failure = error.ToString();
            Write("failure.json", new { schema = "cis.healthcheck.foreign-peer-failure/v1", pid = Environment.ProcessId, failure });
            throw;
        }
        finally
        {
            await stopping.CancelAsync();
            await stop;
            Write("terminal.json", new
            {
                schema = "cis.healthcheck.foreign-peer-terminal/v1",
                pid = Environment.ProcessId,
                healthy = failure is null,
                failure,
                socketRemoved = !File.Exists(socket),
                counters = Counters()
            });
        }

        object Counters() => new { connections, requests, expectedRequests, emptyConnections, responses };
        void Write(string name, object value)
        {
            string destination = Path.Combine(journal, name), pending = destination + ".pending";
            File.WriteAllText(pending, JsonSerializer.Serialize(value));
            File.Move(pending, destination, overwrite: true);
        }
        async Task ObserveStopAsync()
        {
            while (!stopping.IsCancellationRequested)
            {
                if (File.Exists(Path.Combine(journal, "stop"))) { await stopping.CancelAsync(); return; }
                try { await Task.Delay(20, stopping.Token); }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return; }
            }
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}

// The same immutable binary first proves this unrestricted client's valid
// exchange with an unrestricted root peer, then observes the instrumented peer.
internal static class ForeignOwnerRawClient
{
    internal static async Task RunAsync(string[] arguments)
    {
        if (!OperatingSystem.IsLinux() || arguments.Length != 3) { throw new ArgumentException("The raw foreign client requires Linux, two owned pipe names and a journal directory."); }
        string positivePipe = arguments[0], targetPipe = arguments[1], journal = Path.GetFullPath(arguments[2]);
        if (new[] { positivePipe, targetPipe }.Any(pipe => !pipe.StartsWith("cis-foreign-", StringComparison.Ordinal)
            || pipe.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')) || !Directory.Exists(journal))
        { throw new ArgumentException("Unexpected raw-client fixture inputs."); }
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        string? failure = null;
        try
        {
            Write("ready.json", new
            {
                schema = "cis.healthcheck.foreign-client-ready/v1",
                pid = Environment.ProcessId,
                uidLine = File.ReadLines("/proc/self/status").Single(line => line.StartsWith("Uid:", StringComparison.Ordinal)),
                runtime = RuntimeInformation.FrameworkDescription,
                temporaryRoot = Path.GetTempPath(),
                positivePipe,
                targetPipe,
                fixtureSha256 = Hash(typeof(ForeignOwnerRawClient).Assembly.Location),
                coreSha256 = Hash(typeof(HealthcheckService).Assembly.Location),
                pipesSha256 = Hash(typeof(NamedPipeClientStream).Assembly.Location)
            });
            Write("positive.json", await ExchangeAsync(positivePipe));
            while (!File.Exists(Path.Combine(journal, "target-go")) && !File.Exists(Path.Combine(journal, "stop"))) { await Task.Delay(10, lifetime.Token); }
            if (File.Exists(Path.Combine(journal, "target-go"))) { Write("target.json", await ExchangeAsync(targetPipe)); }
            while (!File.Exists(Path.Combine(journal, "stop"))) { await Task.Delay(10, lifetime.Token); }
        }
        catch (Exception error)
        {
            failure = error.ToString();
            Write("failure.json", new { schema = "cis.healthcheck.foreign-client-failure/v1", pid = Environment.ProcessId, failure });
            throw;
        }
        finally
        {
            Write("terminal.json", new { schema = "cis.healthcheck.foreign-client-terminal/v1", pid = Environment.ProcessId, healthy = failure is null, failure });
        }

        async Task<object> ExchangeAsync(string pipe)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            budget.CancelAfter(TimeSpan.FromSeconds(2));
            bool connected = false, healthy = false;
            string outcome = "rejected", error = "";
            try
            {
                using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(budget.Token);
                connected = true;
                byte[] payload = Encoding.UTF8.GetBytes(ForeignOwnerRawServer.Request), header = new byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);
                await client.WriteAsync(header, budget.Token);
                await client.WriteAsync(payload, budget.Token);
                await client.FlushAsync(budget.Token);
                await client.ReadExactlyAsync(header, budget.Token);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(header);
                if (size is 0 or > 65536) { throw new InvalidDataException("Invalid response frame size."); }
                payload = new byte[(int)size];
                await client.ReadExactlyAsync(payload, budget.Token);
                using JsonDocument response = JsonDocument.Parse(payload);
                healthy = response.RootElement.TryGetProperty("snapshot", out JsonElement snapshot)
                    && snapshot.TryGetProperty("healthStatus", out JsonElement status) && status.GetString() == "Healthy";
                outcome = healthy ? "healthy" : "invalid-response";
            }
            // These are observations of the peer under test, never fixture
            // failures. The immutable positive exchange must independently pass.
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { outcome = "timeout"; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or System.Net.Sockets.SocketException or InvalidOperationException)
            { error = exception.GetType().Name; }
            return new
            {
                schema = "cis.healthcheck.foreign-client-outcome/v1",
                pipe,
                connected,
                healthy,
                outcome,
                error,
                requestSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ForeignOwnerRawServer.Request))).ToLowerInvariant()
            };
        }
        void Write(string name, object value)
        {
            string destination = Path.Combine(journal, name), pending = destination + ".pending";
            File.WriteAllText(pending, JsonSerializer.Serialize(value));
            File.Move(pending, destination, overwrite: true);
        }
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
