using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class LoggerConvenienceTests
{
    [TestMethod]
    [DataRow("Trace", "trace")]
    [DataRow("Debug", "debug")]
    [DataRow("Info", "info")]
    [DataRow("Warn", "warn")]
    [DataRow("Error", "error")]
    [DataRow("Critical", "critical")]
    public async Task ConvenienceOverloadsPreserveSeverityNamesTypedValuesAndScopes(string method, string status)
    {
        var writer = new Writer();
        await using var runtime = QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None, QueueCapacity = 16 },
            writers: [new("capture", writer, CoreLogLevel.Trace)], incomingPipeline: true);
        CoreLogger logger = runtime.CreateLogger("Convenience");
        using (runtime.BeginScope(new OperationContextData { OperationId = "operation" }))
        {
            object Invoke(Type[] types, params object[] args) => typeof(CoreLogger).GetMethod(method, types)!.Invoke(logger, args)!;
            Assert.AreEqual(LogWriteOutcome.Accepted, ((LogWriteResult)Invoke([typeof(string)], "message-only")).Outcome);
            Invoke([typeof(string), typeof(string)], "explicit.name", "named-message");
            var values = new Dictionary<string, LogValue> { ["customer"] = LogValue.Integer(42) };
            Invoke([typeof(LogEventData)], new LogEventData { EventName = "typed", Message = "typed-message", Attributes = values });
            values["customer"] = LogValue.Integer(99);
            int factories = 0;
            Func<LogEventData> factory = () => { factories++; return new() { EventName = "lazy", Message = "lazy-message" }; };
            typeof(CoreLogger).GetMethod(method + "Lazy")!.Invoke(logger, [factory]);
            Assert.AreEqual(1, factories);
        }
        Assert.IsTrue((await runtime.StopAsync(CancellationToken.None)).Succeeded);
        JsonNode[] records = writer.Lines.Select(line => JsonNode.Parse(line)!).ToArray();
        CollectionAssert.AreEqual(new[] { "log.message", "explicit.name", "typed", "lazy" }, records.Select(row => row["event_name"]!.GetValue<string>()).ToArray());
        Assert.IsTrue(records.All(row => row["status"]!.GetValue<string>() == status));
        Assert.IsTrue(records.All(row => row["operation_id"]!.GetValue<string>() == "operation"));
        Assert.AreEqual(42, records[2]["attributes"]!["customer"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task DisabledAndStoppedConvenienceFactoriesDoNotRun()
    {
        await using var runtime = QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None }, incomingPipeline: true);
        CoreLogger logger = runtime.CreateLogger("Disabled");
        foreach (string name in new[] { "Trace", "Debug", "Info", "Warn", "Error", "Critical" })
        {
            Func<LogEventData> fail = () => throw new AssertFailedException("Disabled factory executed.");
            Assert.AreEqual(LogWriteOutcome.Filtered, ((LogWriteResult)typeof(CoreLogger).GetMethod(name + "Lazy")!.Invoke(logger, [fail])!).Outcome);
        }
        await runtime.StopAsync(CancellationToken.None);
        Assert.AreEqual(LogWriteOutcome.Stopped, logger.InfoLazy(() => throw new AssertFailedException("Stopped factory executed.")).Outcome);
    }

    private sealed class Writer : ILogWriter
    {
        internal ConcurrentQueue<string> Lines { get; } = new();
        public ValueTask WriteAsync(string jsonLine, CancellationToken cancellationToken) { Lines.Enqueue(jsonLine); return ValueTask.CompletedTask; }
        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
