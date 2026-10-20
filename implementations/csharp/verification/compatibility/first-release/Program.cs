using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using CIS;
using CIS.Logging;

EnvironmentOptions.Logging.ConsoleLevel = CoreLogLevel.None;
EnvironmentOptions.Logging.FileLevel = CoreLogLevel.None;
var writer = new Recorder();
await using var runtime = CoreLogRuntime.Create([new LogWriterRegistration("compatibility", writer)]);
var logger = runtime.CreateLogger("FirstRelease");
int id = 42;
var values = new Dictionary<string, LogValue> { ["id"] = LogValue.Integer(id) };
LogWriteResult admitted = logger.Info($"Order {id:D4} accepted", values);
values["id"] = LogValue.Integer(-1);
logger.Info("legacy", "ordinary message");
if (admitted.Outcome != LogWriteOutcome.Accepted || admitted.IncomingSequence is null)
{ throw new InvalidOperationException("Frontend admission changed."); }
LogFlushResult flushed = await runtime.FlushAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
LogStopResult stopped = await runtime.StopAsync(CancellationToken.None);
if (!flushed.Succeeded || !stopped.Succeeded || writer.Records.Count != 2)
{ throw new InvalidOperationException("Covered delivery changed."); }
JsonElement[] rows = writer.Records.ToArray();
JsonElement order = rows.Single(row => row.GetProperty("event_name").GetString() == "log.message");
if (order.GetProperty("message").GetString() != "Order 0042 accepted" ||
    order.GetProperty("message_template").GetString() != "Order {0:D4} accepted" ||
    order.GetProperty("attributes").GetProperty("id").GetInt32() != 42 ||
    rows.Single(row => row.GetProperty("event_name").GetString() == "legacy").GetProperty("message").GetString() != "ordinary message")
{ throw new InvalidOperationException("Frozen logging behavior changed."); }
Console.WriteLine(JsonSerializer.Serialize(new
{
    succeeded = true,
    runtime = Environment.Version.ToString(),
    coreSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(CoreLogRuntime).Assembly.Location))),
    records = rows.Length
}));

sealed class Recorder : ILogWriter
{
    internal ConcurrentQueue<JsonElement> Records { get; } = new();
    public ValueTask WriteAsync(string jsonLine, CancellationToken cancellationToken)
    {
        using var parsed = JsonDocument.Parse(jsonLine);
        Records.Enqueue(parsed.RootElement.Clone());
        return ValueTask.CompletedTask;
    }
    public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
