using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class FileRuntimeStartupTests
{
    public static IEnumerable<object[]> Modes => FileRuntimeStartupExpectations.Modes.Where(mode => mode != "physical-console").Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ConfiguredOutputsWriteOrRollbackWithoutLeakingResources(string mode)
    {
        using var root = new TemporaryDirectory(); FileRuntimeStartupExpectations.Validate(mode, await FileRuntimeStartupFixture.ExecuteAsync(mode, root.Path));
    }
    [TestMethod]
    [DataRow("metadata")]
    [DataRow("format")]
    [DataRow("relative")]
    [DataRow("capacity")]
    [DataRow("file-size")]
    [DataRow("retained")]
    public void InvalidInputsFailBeforeAnyStreamWorkerOrDirectory(string kind)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "absent", "file.log"); var options = new LoggingOptions { FilePath = path }; int selections = 0, launches = 0;
        if (kind == "format") { options.FileFormat = (LogOutputFormat)999; }
        if (kind == "relative") { options.FilePath = "relative.log"; }
        if (kind == "capacity") { options.QueueCapacity = 0; }
        if (kind == "file-size") { options.FileMaxBytes = 65536; options.MaxEntryBytes = 131072; }
        if (kind == "retained") { options.FileRetainedFiles = 0; }
        Assert.Throws<ArgumentException>(() => CoreLogRuntime.CreateConfiguredQueued(kind == "metadata" ? null! : ConsoleCompositionFixture.Metadata(), options,
            acquireConsole: _ => { selections++; return Stream.Null; }, launchWorker: _ => { launches++; return Task.CompletedTask; }));
        Assert.AreEqual(0, selections); Assert.AreEqual(0, launches); Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(root.Path).Count());
    }
    [TestMethod]
    [DataRow("worker")]
    [DataRow("disposal")]
    [DataRow("safe-error")]
    [DataRow("file-bytes")]
    [DataRow("ownership")]
    public async Task StartupComparerRejectsChangedRollbackFacts(string fault)
    {
        using var root = new TemporaryDirectory(); JsonObject actual = await FileRuntimeStartupFixture.ExecuteAsync("rollback-missing", root.Path);
        FileRuntimeStartupExpectations.Validate("rollback-missing", actual);
        switch (fault)
        {
            case "worker": actual["joinedAtReturn"]![0] = false; break;
            case "disposal": actual["borrowedDisposes"] = 1; break;
            case "safe-error": actual["error"]!["message"] = root.Path; break;
            case "file-bytes": actual["fileBytes"] = Convert.ToBase64String([10]); break;
            case "ownership": actual["ownershipReleased"] = false; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => FileRuntimeStartupExpectations.Validate("rollback-missing", actual));
    }
    private sealed class HeldCleanup : IDisposable
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Dispose() { Started.TrySetResult(); Release.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); }
    }
    [TestMethod]
    public async Task OwnedCleanupCompletesBeforeWorkerCompletionAndRunsOutsideAdmissionGate()
    {
        var gate = new object(); var resource = new HeldCleanup(); var queue = new OutputEventQueue(1, _ => ValueTask.CompletedTask, gate, ownedResource: resource);
        try
        {
            queue.Complete(); await resource.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(queue.Completion.IsCompleted); Assert.AreEqual(LogOutputState.Stopping, queue.CaptureAccounting().State);
            Task observation = Task.Run(() => { lock (gate) { Assert.AreEqual(0L, queue.CaptureAccounting().Pending); } }); await observation.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { resource.Release.TrySetResult(); await queue.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.AreEqual(LogOutputState.Stopped, queue.CaptureAccounting().State);
    }
}
