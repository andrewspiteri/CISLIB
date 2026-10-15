using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CIS;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.Extensions.Logging;

internal static class Program
{
    private static void Require(bool condition, string contract)
    { if (!condition) { throw new InvalidDataException(contract); } }

    private static object Loaded(Assembly assembly) => new
    {
        path = assembly.Location,
        identity = assembly.FullName,
        sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location)))
    };

    private static object Signature(MethodBase method) => new
    {
        parameters = method.GetParameters().Select(p => new { name = p.Name, type = p.ParameterType.FullName, optional = p.IsOptional }).ToArray()
    };

    public static async Task Main(string[] args)
    {
        string root = Path.GetFullPath(args.Single());
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "provider.jsonl");
        LoggingOptions options = EnvironmentOptions.Logging;
        options.ConsoleLevel = CoreLogLevel.None;
        options.FileLevel = CoreLogLevel.Trace;
        options.FilePath = path;
        options.FileFormat = LogOutputFormat.JsonLines;
        options.QueueCapacity = 16;
        options.MaxEntryBytes = 16384;
        options.FileMaxBytes = 65536;
        options.FileRetainedFiles = 1;
        options.FlushIntervalMilliseconds = 1000;
        options.ShutdownTimeoutMilliseconds = 5000;
        await using CoreLogRuntime runtime = CoreLogRuntime.Create();
        using var provider = new CoreLoggerProvider(runtime: runtime);
        ILoggerProvider providerInterface = provider;
        ILogger logger = providerInterface.CreateLogger(categoryName: "Compatibility.Provider");
        var hostScopes = new CustomScopes();
        ISupportExternalScope scopeInterface = provider;
        scopeInterface.SetScopeProvider(scopeProvider: hostScopes);
        Require(logger.IsEnabled(logLevel: LogLevel.Information), "enabled provider");
        using (hostScopes.Push(new Dictionary<string, object?> { ["tenant"] = "host" }))
        {
            logger.LogInformation(eventId: new EventId(7, "provider.fixture"), message: "Item {ItemId}", args: new object?[] { 42 });
            using (logger.BeginScope(state: new OperationContextData { OperationId = "operation.fixture" }))
            {
                logger.Log(logLevel: LogLevel.Warning, eventId: new EventId(-2, "typed"), state: "typed message", exception: null,
                    formatter: static (state, _) => state);
            }
        }
        provider.Dispose();
        provider.Dispose();
        Require(!logger.IsEnabled(logLevel: LogLevel.Information), "disposed provider disabled");
        logger.Log(logLevel: LogLevel.Information, eventId: default, state: "never", exception: null,
            formatter: static (_, _) => throw new InvalidDataException("disposed formatter invoked"));
        Require(!runtime.GetDiagnostics().AdmissionClosed, "provider must not own runtime");
        LogWriteResult direct = runtime.CreateLogger(component: "Compatibility.Direct").Write(level: CoreLogLevel.Information,
            data: new LogEventData { EventName = "direct", Message = "core remains live" });
        Require(direct.Outcome == LogWriteOutcome.Accepted, "direct write after provider disposal");
        LogFlushResult flush = await runtime.FlushAsync(timeout: TimeSpan.FromSeconds(5), cancellationToken: CancellationToken.None);
        Require(flush.Succeeded && !flush.LossObserved, "successful lossless flush");
        LogStopResult stop = await runtime.StopAsync(cancellationToken: CancellationToken.None);
        Require(stop.Succeeded && !stop.LossObserved && stop.Outputs.Single().Written == 3, "three confirmed records");
        byte[] bytes = File.ReadAllBytes(path);
        Require(bytes.Length > 0 && bytes[^1] == (byte)'\n' && !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }), "UTF8 LF without BOM");
        using var first = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]);
        string[] lines = new UTF8Encoding(false, true).GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Require(lines.Length == 3, "exact record count");
        using var second = JsonDocument.Parse(lines[1]);
        using var third = JsonDocument.Parse(lines[2]);
        JsonElement one = first.RootElement, two = second.RootElement, three = third.RootElement;
        Require(one.GetProperty("event_name").GetString() == "provider.fixture" && one.GetProperty("message").GetString() == "Item 42", "extension event/message");
        Require(one.GetProperty("message_template").GetString() == "Item {ItemId}", "retained template");
        Require(one.GetProperty("attributes").GetProperty("ItemId").GetInt32() == 42 && one.GetProperty("attributes").GetProperty("event_id").GetInt32() == 7, "structured state/event id");
        Require(one.GetProperty("attributes").GetProperty("tenant").GetString() == "host", "borrowed custom scope");
        Require(two.GetProperty("event_name").GetString() == "typed" && two.GetProperty("operation_id").GetString() == "operation.fixture", "typed owned scope");
        Require(two.GetProperty("attributes").GetProperty("event_id").GetInt32() == -2 && two.GetProperty("message").GetString() == "typed message", "generic formatter/event id");
        Require(three.GetProperty("event_name").GetString() == "direct" && three.GetProperty("message").GetString() == "core remains live", "runtime remains caller owned");
        Type type = typeof(CoreLoggerProvider);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            contract = "passed", records = 3, core = Loaded(type.Assembly), consumer = Loaded(Assembly.GetExecutingAssembly()),
            logging = Loaded(typeof(ILogger).Assembly), dependencyInjection = Loaded(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly),
            runtime = System.Environment.Version.ToString(), os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            api = new
            {
                type = type.FullName, isSealed = type.IsSealed,
                interfaces = type.GetInterfaces().Select(t => t.FullName).ToArray(),
                constructor = Signature(type.GetConstructor(new[] { typeof(CoreLogRuntime) })!),
                methods = new[]
                {
                    type.GetMethod("CreateLogger", new[] { typeof(string) })!,
                    type.GetMethod("SetScopeProvider", new[] { typeof(IExternalScopeProvider) })!,
                    type.GetMethod("Dispose", Type.EmptyTypes)!
                }.Select(method => new { name = method.Name, returns = method.ReturnType.FullName, signature = Signature(method) }).ToArray()
            }
        }));
    }

    private sealed class CustomScopes : IExternalScopeProvider
    {
        private readonly LoggerExternalScopeProvider inner = new();
        public void ForEachScope<TState>(Action<object?, TState> callback, TState state) => inner.ForEachScope(callback, state);
        public IDisposable Push(object? state) => inner.Push(state);
    }
}
