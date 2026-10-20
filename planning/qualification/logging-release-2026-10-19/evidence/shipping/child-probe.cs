using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using CIS;
using CIS.Logging;

try
{
    EnvironmentOptions.Logging.ConsoleLevel = CoreLogLevel.None;
    EnvironmentOptions.Logging.FileLevel = CoreLogLevel.None;
    var writer = new Writer();
    await using var runtime = CoreLogRuntime.Create([new LogWriterRegistration("child", writer, CoreLogLevel.Trace)]);
    LogWriteResult write = runtime.CreateLogger("Mutation.Child").Info($"value {42,5}");
    LogStopResult stop = await runtime.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));
    string[] messages = writer.Messages.ToArray();
    if (write.Outcome != LogWriteOutcome.Accepted || !stop.Succeeded || messages.Length != 1 || messages[0] != "value    42")
    {
        Console.Error.WriteLine("CORE-CHILD-ASSERT: alignment output differs: " + JsonSerializer.Serialize(messages));
        return 1;
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        processId = Environment.ProcessId,
        coreSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(CoreLogger).Assembly.Location))),
        message = messages[0],
        activeMutant = Environment.GetEnvironmentVariable("CIS_RELEASE_MUTANT")
    }));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("CORE-CHILD-INFRASTRUCTURE: " + error);
    return 2;
}

internal sealed class Writer : ILogWriter
{
    internal ConcurrentQueue<string> Messages { get; } = new();
    public ValueTask WriteAsync(string jsonLine, CancellationToken cancellationToken)
    {
        using JsonDocument document = JsonDocument.Parse(jsonLine);
        Messages.Enqueue(document.RootElement.GetProperty("message").GetString()!);
        return ValueTask.CompletedTask;
    }
    public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
