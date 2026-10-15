using System.Diagnostics;
using System.Reflection;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CIS;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Compiled only by the integration harness, using an exact CIS.Core PackageReference.
internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    private static CoreLogRuntime? runtime;
    private static CoreLogger? logger;
    private static string mode = "";
    private static string reportPath = "";
    private static int factories;
    private static StreamReader? pipeInput;
    private static StreamWriter? pipeOutput;
    private static readonly List<LogWriteResult> receipts = [];

    private static object AssemblyOrigin(Assembly assembly) => new { name = assembly.FullName, path = assembly.Location, sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))), mvid = assembly.ManifestModule.ModuleVersionId };
    private static JsonObject Observe(string stage) => new()
    {
        ["stage"] = stage, ["mode"] = mode, ["pid"] = Environment.ProcessId,
        ["runtime"] = RuntimeInformation.FrameworkDescription,
        ["os"] = OperatingSystem.IsWindows() ? "windows" : "linux", ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
        ["core"] = JsonSerializer.SerializeToNode(AssemblyOrigin(typeof(CoreLogRuntime).Assembly), Json),
        ["logging"] = JsonSerializer.SerializeToNode(AssemblyOrigin(typeof(ILogger).Assembly), Json),
        ["di"] = JsonSerializer.SerializeToNode(AssemblyOrigin(typeof(IServiceCollection).Assembly), Json),
        ["consumer"] = JsonSerializer.SerializeToNode(AssemblyOrigin(typeof(Program).Assembly), Json),
        ["instanceId"] = EnvironmentOptions.InstanceId, ["factories"] = factories,
        ["diagnostics"] = runtime is null ? null : JsonSerializer.SerializeToNode(runtime.GetDiagnostics(), Json),
        ["receipts"] = JsonSerializer.SerializeToNode(receipts, Json)
    };
    private static void Publish(JsonObject value)
    {
        File.WriteAllText(reportPath, value.ToJsonString());
        if (mode is "interactive") { Console.WriteLine(value.ToJsonString()); }
        else if (mode is "burst" or "deadline") { pipeOutput!.WriteLine(value.ToJsonString()); }
    }
    private static LogWriteResult Write(string name, int payload = 0, CoreLogLevel level = CoreLogLevel.Information)
    {
        LogWriteResult result = logger!.Write(level, new() { EventName = name, Message = payload == 0 ? "Unicode Ω🙂 \"quoted\"\nline\tend" : new string('x', payload / 2) + "Ω🙂", Attributes = payload == 0 ? null : new Dictionary<string, LogValue> { ["payload"] = LogValue.String(new string('y', payload / 2)) }, Exception = level == CoreLogLevel.Error ? new InvalidOperationException("candidate failure") : null });
        receipts.Add(result); return result;
    }
    private static async Task<JsonObject> Stop()
    {
        Task<LogStopResult> shared = runtime!.StopAsync(CancellationToken.None);
        LogStopResult stop = await shared.WaitAsync(TimeSpan.FromSeconds(15));
        bool same = ReferenceEquals(shared, runtime.StopAsync(CancellationToken.None));
        string frozen = JsonSerializer.Serialize(stop, Json);
        await runtime.DisposeAsync(); await runtime.DisposeAsync();
        LogWriteResult stopped = logger!.WriteLazy(CoreLogLevel.None, () => { factories++; throw new InvalidOperationException("Stopped factory executed."); });
        JsonObject value = Observe("stopped"); value["stop"] = JsonSerializer.SerializeToNode(stop, Json); value["sameStopTask"] = same;
        value["stoppedOutcome"] = stopped.Outcome.ToString(); value["frozenStop"] = frozen;
        return value;
    }
    private static async Task Until(Func<bool> condition)
    {
        async Task Wait() { while (!condition()) { await Task.Yield(); } }
        await Wait().WaitAsync(TimeSpan.FromSeconds(10));
    }
    private static async Task BlockedPipe()
    {
        // The parent deliberately holds stdout unread; a separate BCL pipe carries control replies.
        // No elapsed sleep is used to declare pressure: a registered flush must time out with pending work.
        for (int round = 0; round < 64; round++)
        {
            for (int i = 0; i < 32; i++) { Write("burst." + round + "." + i, 15000); }
            LogFlushResult flush = await runtime!.FlushAsync(TimeSpan.FromMilliseconds(20), CancellationToken.None);
            LogOutputDiagnostics output = runtime.GetDiagnostics().Outputs.Single();
            if (flush.Completion == LogCompletion.TimedOut && output.Pending > 0 && output.QueueFull > 0)
            {
                JsonObject held = Observe("blocked"); held["flush"] = JsonSerializer.SerializeToNode(flush, Json); Publish(held);
                if (mode == "deadline") { Publish(await Stop()); }
                if (await pipeInput!.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)) != "drain") { throw new InvalidDataException("Missing drain signal."); }
                if (mode == "burst") { Publish(await Stop()); }
                else
                {
                    string frozen = JsonSerializer.Serialize(runtime.GetDiagnostics().LastStopResult, Json);
                    await Until(() => runtime.GetDiagnostics().Outputs.All(o => o.Pending == 0));
                    JsonObject late = Observe("settled"); late["sameFrozenStop"] = frozen == JsonSerializer.Serialize(runtime.GetDiagnostics().LastStopResult, Json); Publish(late);
                }
                return;
            }
        }
        throw new InvalidOperationException("Could not establish actual bounded pipe pressure.");
    }
    private static async Task Interact()
    {
        Publish(Observe("ready"));
        for (int request = 0; request < 8; request++)
        {
            string? command = await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (command == "stop") { Publish(await Stop()); return; }
            if (command is null) { throw new EndOfStreamException(); }
            JsonObject input = JsonNode.Parse(command)!.AsObject();
            int count = input["count"]!.GetValue<int>(), payload = input["payload"]!.GetValue<int>();
            if (count is < 1 or > 64 || payload is < 0 or > 15000) { throw new InvalidDataException("Invalid fixture command bounds."); }
            for (int i = 0; i < count; i++) { Write(input["name"]!.GetValue<string>() + "." + i, payload); }
            LogFlushResult flush = await runtime!.FlushAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            JsonObject value = Observe("written"); value["flush"] = JsonSerializer.SerializeToNode(flush, Json); Publish(value);
        }
        throw new InvalidDataException("Too many fixture commands.");
    }
    private static async Task<int> Main(string[] args)
    {
        mode = args[0]; reportPath = args[1];
        using var control = mode is "burst" or "deadline" ? new NamedPipeClientStream(".", Environment.GetEnvironmentVariable("CIS_CANDIDATE_CONTROL_PIPE")!, PipeDirection.InOut, PipeOptions.Asynchronous) : null;
        if (control is not null)
        {
            await control.ConnectAsync(10000);
            pipeInput = new StreamReader(control);
            pipeOutput = new StreamWriter(control) { AutoFlush = true };
        }
        string? path = Environment.GetEnvironmentVariable("CORE_LOG_FILE_PATH");
        JsonNode? before = path is not null && File.Exists(path + ".cis.inventory.json") ? JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json")) : null;
        try
        {
            var applicationWriter = mode == "processing" ? new ApplicationWriter() : null;
            runtime = applicationWriter is null ? CoreLogRuntime.Create() : CoreLogRuntime.Create(new LogProcessingOptions
            {
                Interceptors = [_ => new() { Message = "safe", ClearMessageTemplate = true, RemoveException = true }],
                Filter = value => value.EventName != "excluded",
                Route = (_, value) => value.EventName != "routed-away",
                DuplicateSuppression = new(value => value.EventName, TimeSpan.FromMinutes(1), 8)
            }, [new("application", applicationWriter, ownsWriter: true)]);
            logger = runtime.CreateLogger("Candidate.Consumer");
            if (mode is "burst" or "deadline") { await BlockedPipe(); return 0; }
            if (mode == "interactive") { await Interact(); return 0; }
            if (mode == "emit")
            {
                using (runtime.BeginScope(new() { OperationId = "candidate.operation" }))
                {
                    Write("event.info"); Write("event.error", level: CoreLogLevel.Error);
                    receipts.Add(logger.WriteLazy(CoreLogLevel.Debug, () => { factories++; return new() { EventName = "event.debug", Message = "deferred" }; }));
                    receipts.Add(logger.WriteLazy(CoreLogLevel.Trace, () => { factories++; throw new InvalidOperationException("Filtered factory ran."); }));
                }
            }
            else if (mode == "processing")
            {
                receipts.Add(logger.Info("private")); receipts.Add(logger.Info("private"));
                receipts.Add(logger.Info("excluded", "private")); receipts.Add(logger.Info("routed-away", "private"));
                receipts.Add(logger.Info(new LogEventData { EventName = "typed", Message = "private", Attributes = new Dictionary<string, LogValue> { ["customer"] = LogValue.Integer(42) }, Exception = new Exception("private") }));
                receipts.Add(logger.WarnLazy(() => { factories++; return new() { EventName = "lazy", Message = "private" }; }));
            }
            else if (mode == "provider")
            {
                using (var provider = new CoreLoggerProvider(runtime))
                {
                    using var activity = new Activity("candidate").SetIdFormat(ActivityIdFormat.W3C).Start();
                    ILogger adapted = provider.CreateLogger("Candidate.Provider");
                    using (adapted.BeginScope(new OperationContextData { OperationId = "provider.operation" }))
                    { adapted.LogError(new EventId(7, "provider.event"), new InvalidOperationException("provider failure"), "Failure {Code}", 42); }
                }
                Write("after.provider");
            }
            else if (mode == "produce")
            {
                int count = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                if (count is < 1 or > 24) { throw new ArgumentOutOfRangeException(nameof(args)); }
                for (int i = 0; i < count; i++) { Write("producer." + i, 14000); }
            }
            else if (mode == "cancel")
            {
                for (int i = 0; i < 16; i++) { Write("cancel." + i); }
                using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                try { await runtime.StopAsync(cancellation.Token); throw new InvalidOperationException("Cancelled waiter succeeded."); }
                catch (OperationCanceledException error) when (error.CancellationToken == cancellation.Token) { }
            }
            else if (mode != "disabled") { throw new ArgumentException("Unknown candidate mode."); }
            if (mode == "disabled") { receipts.Add(logger.WriteLazy(CoreLogLevel.Information, () => { factories++; throw new InvalidOperationException("Disabled factory ran."); })); }
            JsonObject result = await Stop(); result["before"] = before;
            if (applicationWriter is not null)
            {
                result["applicationLines"] = JsonSerializer.SerializeToNode(applicationWriter.Lines, Json);
                result["applicationDisposals"] = applicationWriter.Disposals;
            }
            // The application can still write its own command result after the borrowed console stops.
            if (mode == "emit" && Environment.GetEnvironmentVariable("CORE_LOG_CONSOLE_STREAM") == "stderr") { Console.WriteLine("{\"command\":\"ok\"}"); }
            Publish(result); return 0;
        }
        catch (CoreLogInitializationException error)
        {
            JsonObject result = Observe("initialization-failed"); result["output"] = error.OutputName; result["code"] = error.ErrorCode; Publish(result); return 0;
        }
        finally { if (runtime is not null) { await runtime.DisposeAsync(); } }
    }

    private sealed class ApplicationWriter : ILogWriter
    {
        internal List<string> Lines { get; } = [];
        internal int Disposals;
        public ValueTask WriteAsync(string jsonLine, CancellationToken cancellationToken) { Lines.Add(jsonLine); return ValueTask.CompletedTask; }
        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
