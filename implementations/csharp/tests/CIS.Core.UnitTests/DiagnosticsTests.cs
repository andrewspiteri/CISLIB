using System.Reflection;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class DiagnosticsTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task PublicOwnedSnapshotsQualifyTheActualAccountingFaultAndPartialWriteStory(string id)
    { JsonObject row = Row(id); DiagnosticsExpectations.Validate(row, await DiagnosticsFixture.ExecuteAsync(Folder(), row)); }
    [TestMethod]
    [DataRow("runtime-state")]
    [DataRow("closure")]
    [DataRow("observed-time")]
    [DataRow("shutdown-time")]
    [DataRow("global")]
    [DataRow("written")]
    [DataRow("ticket")]
    [DataRow("fault")]
    [DataRow("fault-time")]
    [DataRow("owned")]
    [DataRow("shutdown-retention")]
    [DataRow("fault-retention")]
    public async Task DiagnosticsMatcherRejectsInventedCountersLostFaultsAndRewrittenSnapshots(string fault)
    {
        JsonObject row = Row("DIAGNOSTICS-STATE-ISOLATE-CONSOLE"), actual = await DiagnosticsFixture.ExecuteAsync(Folder(), row); DiagnosticsExpectations.Damage(actual, fault);
        Assert.ThrowsExactly<InvalidDataException>(() => DiagnosticsExpectations.Validate(row, actual));
    }
    [TestMethod]
    [DataRow("runtime", "State,AdmissionClosed,ObservedAtUtc,ShutdownStartedAtUtc,InvalidEventCount,TruncatedEventCount,Outputs,LastStopResult,Incoming")]
    [DataRow("incoming", "Accepted,Processed,Failed,Abandoned,Pending,QueueFull,RejectedByWriters")]
    [DataRow("output", "OutputName,State,AcceptedSequence,Filtered,AdmissionAttempts,Accepted,QueueFull,Unavailable,Written,Failed,Abandoned,Pending,InFlight,Buffered,LastFaultCode,LastFaultAtUtc,RecoveredPartialSegment")]
    public void PublicDiagnosticContainersHaveOnlySealedGetterOnlyFacts(string typeName, string fields)
    {
        Type type = typeName switch { "runtime" => typeof(CoreLogDiagnostics), "incoming" => typeof(LogIncomingSnapshot), _ => typeof(LogOutputDiagnostics) };
        Assert.IsTrue(type.IsSealed); Assert.AreEqual(0, type.GetConstructors().Length); Assert.AreEqual(0, type.GetFields(BindingFlags.Instance | BindingFlags.Public).Length);
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        CollectionAssert.AreEquivalent(fields.Split(','), properties.Select(property => property.Name).ToArray());
        Assert.IsTrue(properties.All(property => property.GetMethod?.IsPublic == true && property.SetMethod is null));
        Assert.AreEqual(typeof(CoreLogDiagnostics), typeof(CoreLogRuntime).GetMethod(nameof(CoreLogRuntime.GetDiagnostics), Type.EmptyTypes)!.ReturnType);
    }
    [TestMethod]
    public async Task RepeatedAndWorkerReentrantReadsDoNotRouteResetOrMutateOwnedValues()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int callbacks = 0, factories = 0; CoreLogDiagnostics? callback = null; CoreLogRuntime? runtime = null;
        runtime = CoreLogRuntime.CreateQueued(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions(), async _ => { Interlocked.Increment(ref callbacks); callback = runtime!.GetDiagnostics(); entered.TrySetResult(); await release.Task; }, null);
        try
        {
            CoreLogger logger = runtime.CreateLogger("DiagnosticsReads"); CoreLogDiagnostics initial = runtime.GetDiagnostics();
            Assert.AreEqual(LogWriteOutcome.Accepted, logger.WriteLazy(CoreLogLevel.Information, () => { factories++; return Data(); }).Outcome); await entered.Task.WaitAsync(Limit);
            CoreLogDiagnostics held = runtime.GetDiagnostics(); Assert.AreEqual(1L, callback!.Outputs[0].Pending); Assert.AreEqual(0L, callback.Outputs[0].Written);
            for (int index = 0; index < 500; index++)
            {
                CoreLogDiagnostics value = runtime.GetDiagnostics(); Assert.AreEqual(CoreLogState.Running, value.State); Assert.AreEqual(1L, value.Outputs[0].Accepted); Assert.AreEqual(1L, value.Outputs[0].Pending); Assert.AreEqual(0L, value.Outputs[0].Filtered); Assert.AreEqual(1L, value.Outputs[0].AdmissionAttempts); Assert.AreEqual(0L, value.InvalidEventCount); Assert.AreEqual(0L, value.TruncatedEventCount);
            }
            var list = (IList<LogOutputDiagnostics>)held.Outputs;
            Assert.ThrowsExactly<NotSupportedException>(() => list[0] = held.Outputs[0]); Assert.ThrowsExactly<NotSupportedException>(() => list.Add(held.Outputs[0])); Assert.ThrowsExactly<NotSupportedException>(() => list.Clear());
            runtime.CloseAdmission(); CoreLogDiagnostics closing = runtime.GetDiagnostics(); Assert.AreEqual(CoreLogState.Stopping, closing.State); Assert.IsNotNull(closing.ShutdownStartedAtUtc);
            release.TrySetResult(); await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); runtime.CloseAdmission(); CoreLogDiagnostics final = runtime.GetDiagnostics();
            Assert.AreEqual(CoreLogState.Stopped, final.State); Assert.AreEqual(1L, final.Outputs[0].Written); Assert.AreEqual(0L, final.Outputs[0].Pending); Assert.AreEqual(closing.ShutdownStartedAtUtc, final.ShutdownStartedAtUtc);
            Assert.AreEqual(0L, initial.Outputs[0].Accepted); Assert.AreEqual(1L, held.Outputs[0].Pending); Assert.AreEqual(0L, held.Outputs[0].Written); Assert.AreEqual(CoreLogState.Running, held.State); Assert.IsNull(held.ShutdownStartedAtUtc); Assert.AreEqual(1, callbacks); Assert.AreEqual(1, factories);
        }
        finally { release.TrySetResult(); runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); }
    }
    private sealed class ExplosiveClock : TimeProvider
    {
        internal int Calls;
        public override DateTimeOffset GetUtcNow() { Interlocked.Increment(ref Calls); throw new InvalidOperationException("Diagnostics must not execute a caller clock."); }
    }
    [TestMethod]
    public async Task DiagnosticsDoNotQueryTheCallerEventClock()
    {
        var clock = new ExplosiveClock(); int callbacks = 0;
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions(), _ => { callbacks++; return ValueTask.CompletedTask; }, null, clock);
        try { for (int index = 0; index < 50; index++) { Assert.AreEqual(CoreLogState.Running, runtime.GetDiagnostics().State); } Assert.AreEqual(0, clock.Calls); Assert.AreEqual(0, callbacks); }
        finally { runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); }
        Assert.AreEqual(CoreLogState.Stopped, runtime.GetDiagnostics().State); Assert.AreEqual(0, clock.Calls); Assert.AreEqual(0, callbacks);
    }
    [TestMethod]
    [DataRow("filteredCount")]
    [DataRow("queueFullCount")]
    [DataRow("unavailableCount")]
    [DataRow("global")]
    public async Task PublicCountersKeepSaturatedExactInt64ValuesWithoutWrapping(string field)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions { QueueCapacity = 1, MaxEntryBytes = 16384 }, async _ => { entered.TrySetResult(); await release.Task; }, null);
        CoreLogger logger = runtime.CreateLogger("DiagnosticLimits"); OutputEventQueue queue = runtime.QueuedOutputs!.Console!;
        try
        {
            logger.Write(CoreLogLevel.Information, Data()); await entered.Task.WaitAsync(Limit); logger.Write(CoreLogLevel.Information, Data());
            if (field == "global")
            {
                foreach (string key in new[] { "invalidEventCount", "truncatedEventCount" }) { typeof(CoreLogRuntime).GetField(key, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtime, long.MaxValue - 1); }
                for (int index = 0; index < 2; index++) { Assert.AreEqual(LogWriteOutcome.InvalidEvent, logger.Write(CoreLogLevel.Information, new LogEventData { EventName = "" }).Outcome); Assert.IsTrue(logger.Write(CoreLogLevel.Information, new LogEventData { EventName = "event", Message = new string('x', 30000) }).IsTruncated); }
                CoreLogDiagnostics value = runtime.GetDiagnostics(); Assert.AreEqual(long.MaxValue, value.InvalidEventCount); Assert.AreEqual(long.MaxValue, value.TruncatedEventCount); Assert.AreEqual(2L, value.Outputs[0].AcceptedSequence);
            }
            else
            {
                typeof(OutputEventQueue).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(queue, long.MaxValue - 1);
                for (int index = 0; index < 2; index++) { if (field == "filteredCount") { queue.RecordFiltered(); } else if (field == "unavailableCount") { queue.RecordUnavailable(); } else { logger.Write(CoreLogLevel.Information, Data()); } }
                LogOutputDiagnostics value = runtime.GetDiagnostics().Outputs[0]; Assert.AreEqual(long.MaxValue, field == "filteredCount" ? value.Filtered : field == "queueFullCount" ? value.QueueFull : value.Unavailable); Assert.AreEqual(field == "filteredCount" ? 2L : long.MaxValue, value.AdmissionAttempts);
            }
            LogOutputDiagnostics held = runtime.GetDiagnostics().Outputs[0]; Assert.AreEqual(2L, held.Accepted); Assert.AreEqual(2L, held.Pending); Assert.AreEqual(0L, held.Written); Assert.AreEqual(0L, held.Failed); Assert.AreEqual(0L, held.Abandoned);
        }
        finally { release.TrySetResult(); runtime.CloseAdmission(); await runtime.QueuedOutputs.Completion.WaitAsync(Limit); }
    }
    [TestMethod]
    public async Task ConcurrentPublicReadsKeepAtomicFanoutAndGlobalCounts()
    {
        var entered = new[] { new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) }; var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions { FilePath = "control.jsonl", QueueCapacity = 128 }, async _ => { entered[0].TrySetResult(); await release.Task; }, async _ => { entered[1].TrySetResult(); await release.Task; });
        CoreLogger logger = runtime.CreateLogger("ConcurrentDiagnostics"); Task[] producers = []; Task? observer = null; var samples = new List<CoreLogDiagnostics>();
        try
        {
            logger.Write(CoreLogLevel.Information, Data()); await Task.WhenAll(entered.Select(item => item.Task)).WaitAsync(Limit); CoreLogDiagnostics seed = runtime.GetDiagnostics();
            producers = Enumerable.Range(0, 32).Select(_ => Task.Run(() => { for (int index = 0; index < 4; index++) { Assert.AreEqual(LogWriteOutcome.Accepted, logger.Write(CoreLogLevel.Information, Data()).Outcome); } Assert.AreEqual(LogWriteOutcome.InvalidEvent, logger.Write(CoreLogLevel.Information, new LogEventData { EventName = "" }).Outcome); })).ToArray();
            observer = Task.Run(async () => { for (int index = 0; index < 500; index++) { samples.Add(runtime.GetDiagnostics()); await Task.Yield(); } });
            await Task.WhenAll(producers.Append(observer)).WaitAsync(Limit); CoreLogDiagnostics held = runtime.GetDiagnostics(); Assert.AreEqual(32L, held.InvalidEventCount); Assert.AreEqual(129L, held.Outputs[0].Accepted); Assert.AreEqual(129L, held.Outputs[1].Accepted);
            foreach (CoreLogDiagnostics point in samples)
            {
                Assert.AreEqual(point.Outputs[0].AcceptedSequence, point.Outputs[1].AcceptedSequence); Assert.IsTrue(point.InvalidEventCount is >= 0 and <= 32);
                foreach (LogOutputDiagnostics output in point.Outputs) { Assert.AreEqual(output.Accepted, output.Written + output.Failed + output.Abandoned + output.Pending); Assert.AreEqual(output.AcceptedSequence, output.Accepted); Assert.AreEqual(0L, output.Written); Assert.AreEqual(CoreLogState.Running, point.State); }
            }
            runtime.CloseAdmission(); release.TrySetResult(); await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); CoreLogDiagnostics final = runtime.GetDiagnostics(); Assert.AreEqual(CoreLogState.Stopped, final.State); Assert.IsTrue(final.Outputs.All(output => output.Written == 129 && output.Pending == 0));
            Assert.AreEqual(0L, seed.InvalidEventCount); Assert.AreEqual(1L, seed.Outputs[0].Accepted); Assert.AreEqual(129L, held.Outputs[0].Pending);
        }
        finally { release.TrySetResult(); runtime.CloseAdmission(); await Task.WhenAll(producers).WaitAsync(Limit); if (observer is not null) { await observer.WaitAsync(Limit); } await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); }
    }
    [TestMethod]
    public void DiagnosticGuardDoesNotInventCountersForAdmissionDoubles()
    {
        var runtime = new CoreLogRuntime(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions { ConsoleLevel = CoreLogLevel.None }, null, null);
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.GetDiagnostics());
    }
    [TestMethod]
    public void RuntimeStateNamesAndNumbersMatchTheSelectedContract()
    { Assert.AreEqual(0, (int)CoreLogState.Running); Assert.AreEqual(1, (int)CoreLogState.Stopping); Assert.AreEqual(2, (int)CoreLogState.Stopped); Assert.AreEqual(3, Enum.GetValues<CoreLogState>().Length); }
    private static LogEventData Data() => new() { EventName = "event", Message = "record" };
    private static string Folder() => Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!;
    private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Folder(), "diagnostics-cases.json")))!.AsObject();
    private static JsonObject Row(string id) => Catalog()["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
}
