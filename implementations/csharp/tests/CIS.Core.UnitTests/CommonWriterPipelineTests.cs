using System.Collections.Concurrent;
using System.Text;
using CIS.Logging;
using CIS.Logging.Runtime;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class CommonWriterPipelineTests
{
    [TestMethod]
    [DataRow(LogOutputFormat.JsonLines, false)]
    [DataRow(LogOutputFormat.Human, false)]
    [DataRow(LogOutputFormat.JsonLines, true)]
    [DataRow(LogOutputFormat.Human, true)]
    public async Task BuiltInAndApplicationWritersShareDrainAndIsolateFailure(LogOutputFormat format, bool fail)
    {
        using var directory = new TemporaryDirectory();
        using var console = new MemoryStream();
        var writer = new Writer(fail);
        string path = Path.Combine(directory.Path, "combined.log");
        await using var runtime = QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(), new LoggingOptions
        {
            ConsoleLevel = CoreLogLevel.Information,
            FileLevel = CoreLogLevel.Information,
            ConsoleFormat = format,
            FileFormat = format,
            FilePath = path,
            QueueCapacity = 8
        }, acquireConsole: _ => console, writers: [new("application", writer, ownsWriter: true)], incomingPipeline: true);
        var accepted = runtime.CreateLogger("Mixed").Write(CoreLogLevel.Warning,
            new LogEventData { EventName = "mixed.writer", Message = "shared-message" });
        Assert.AreEqual(1L, accepted.IncomingSequence);
        var stopped = await runtime.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(!fail, stopped.Succeeded);
        Assert.AreEqual(1L, stopped.Incoming!.Processed);
        foreach (string name in new[] { "console", "file" })
        { Assert.AreEqual(1L, stopped.Outputs.Single(output => output.OutputName == name).Written); }
        CollectionAssert.AreEqual(console.ToArray(), File.ReadAllBytes(path));
        StringAssert.Contains(Encoding.UTF8.GetString(console.ToArray()), "shared-message");
        Assert.IsTrue(console.CanWrite, "The console stream remains borrowed after shutdown.");
        Assert.AreEqual(1, writer.Disposals);
        if (fail)
        {
            Assert.AreEqual(1L, stopped.Outputs.Single(output => output.OutputName == "application").Failed);
            await Assert.ThrowsExactlyAsync<IOException>(() => runtime.Completion);
        }
        else
        {
            await runtime.Completion;
            if (format == LogOutputFormat.JsonLines)
            { Assert.AreEqual(writer.Lines.Single(), Encoding.UTF8.GetString(console.ToArray())); }
            else { StringAssert.StartsWith(writer.Lines.Single(), "{"); }
        }
    }

    private sealed class Writer(bool fail) : ILogWriter
    {
        internal ConcurrentQueue<string> Lines { get; } = new();
        internal int Disposals;
        public ValueTask WriteAsync(string jsonLine, CancellationToken cancellationToken)
        {
            if (fail) { throw new IOException("controlled application writer fault"); }
            Lines.Enqueue(jsonLine); return ValueTask.CompletedTask;
        }
        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
