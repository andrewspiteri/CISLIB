using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class IncomingRuntimeTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static LogEventData Event(string name) => new() { EventName = name, Message = "owned" };
    private static CoreLogRuntime Create(ILogWriter[] writers, Task? start = null, int timeout = 5000, int capacity = 2, int informationScanPercentage = 40)
    {
        int launches = 0;
        Task Launch(Func<Task> worker)
        {
            bool input = ++launches == writers.Length + 1;
            return Task.Run(async () => { if (input && start is not null) { await start; } await worker(); });
        }
        return QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(), new LoggingOptions
        {
            ConsoleLevel = CoreLogLevel.None,
            FileLevel = CoreLogLevel.None,
            QueueCapacity = capacity,
            ShutdownTimeoutMilliseconds = timeout,
            IncomingInformationScanPercentage = informationScanPercentage
        },
            writers: writers.Select((writer, index) => new LogWriterRegistration("writer" + index, writer)),
            launchWorker: Launch, incomingPipeline: true);
    }

    [TestMethod]
    public async Task CallerOwnsCaptureAndReceivesIncomingTicketsBeforeBackgroundProcessing()
    {
        var start = Signal(); var writer = new Writer(); var runtime = Create([writer], start.Task, informationScanPercentage: 100);
        try
        {
            var attributes = new Dictionary<string, LogValue> { ["number"] = LogValue.Integer(7) };
            var data = Event("before"); data.Attributes = attributes;
            var logger = runtime.CreateLogger("Incoming");
            LogWriteResult first = logger.Write(CoreLogLevel.Error, data);
            data.EventName = "after"; attributes["number"] = LogValue.Integer(9);
            LogWriteResult second = logger.Write(CoreLogLevel.Information, Event("second"));
            LogWriteResult full = logger.Write(CoreLogLevel.Information, Event("full"));
            Assert.AreEqual(1L, first.IncomingSequence); Assert.AreEqual(2L, second.IncomingSequence);
            Assert.HasCount(0, first.Outputs); Assert.AreEqual(LogWriteOutcome.Accepted, full.Outcome);
            Assert.IsNull(full.ErrorCode); Assert.AreEqual(3L, full.IncomingSequence);
            Assert.AreEqual(2L, runtime.GetDiagnostics().Incoming!.Pending);
            Task<LogFlushResult> flush = runtime.FlushAsync(Limit, CancellationToken.None);
            Assert.IsFalse(flush.IsCompleted); Assert.AreEqual(0, writer.Lines.Count);
            start.TrySetResult(); LogFlushResult result = await flush.WaitAsync(Limit);
            Assert.IsTrue(result.Succeeded); Assert.IsTrue(result.LossObserved);
            Assert.AreEqual(2L, result.Incoming!.Processed); Assert.AreEqual(1L, result.Incoming.Evicted);
            Assert.AreEqual(0L, result.Incoming.QueueFull);
            Assert.AreEqual(2L, result.Outputs.Single().Written);
            JsonNode record = JsonNode.Parse(writer.Lines.First())!;
            Assert.AreEqual("before", record["event_name"]!.GetValue<string>());
            Assert.AreEqual(7, record["attributes"]!["number"]!.GetValue<int>());
        }
        finally { start.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task FlushIncludesArrivalsBeforeCaptureAndWaitsForTheirWriterBoundary()
    {
        var start = Signal(); var later = Signal(); var release = Signal(); int calls = 0;
        var writer = new Writer(async (_, _) =>
        { if (Interlocked.Increment(ref calls) == 2) { later.TrySetResult(); await release.Task; } });
        var runtime = Create([writer], start.Task);
        try
        {
            var logger = runtime.CreateLogger("Incoming");
            logger.Write(CoreLogLevel.Information, Event("covered"));
            Task<LogFlushResult> flush = runtime.FlushAsync(Limit, CancellationToken.None);
            logger.Write(CoreLogLevel.Information, Event("later"));
            start.TrySetResult(); await later.Task.WaitAsync(Limit);
            Assert.IsFalse(flush.IsCompleted, "The captured batch includes both pre-capture events and their writer flush.");
            release.TrySetResult(); LogFlushResult result = await flush.WaitAsync(Limit);
            Assert.IsTrue(result.Succeeded); Assert.AreEqual(2L, result.FrontendBatch!.TargetCount);
            Assert.AreEqual(2L, result.Outputs.Single().AcceptedSequenceBoundary);
            Assert.AreEqual(2L, result.Outputs.Single().Written);
            Assert.AreEqual(2L, runtime.GetDiagnostics().Incoming!.Accepted);
        }
        finally { start.TrySetResult(); release.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task OneStopDeadlineClosesAdmissionAndAbandonsUndispatchedInput()
    {
        var start = Signal(); var writer = new Writer(); var runtime = Create([writer], start.Task, timeout: 100);
        try
        {
            var logger = runtime.CreateLogger("Incoming");
            logger.Write(CoreLogLevel.Information, Event("one")); logger.Write(CoreLogLevel.Information, Event("two"));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Assert.AreEqual(LogCompletion.Canceled, (await runtime.StopAsync(cancelled.Token)).Completion);
            LogStopResult result = await runtime.StopAsync(CancellationToken.None).WaitAsync(Limit);
            Assert.AreEqual(LogCompletion.TimedOut, result.Completion); Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(0L, result.Incoming!.Abandoned); Assert.AreEqual(2L, result.Incoming.Pending);
            Assert.AreEqual(LogWriteOutcome.Stopped, logger.Write(CoreLogLevel.Information, Event("closed")).Outcome);
            Assert.IsFalse(runtime.Completion.IsCompleted);
            start.TrySetResult(); await runtime.Completion.WaitAsync(Limit);
            Assert.AreEqual(2L, runtime.GetDiagnostics().Incoming!.Abandoned);
            Assert.AreEqual(0, writer.Lines.Count);
            Assert.AreSame(result, await runtime.StopAsync(CancellationToken.None));
            Assert.AreSame(result, runtime.GetDiagnostics().LastStopResult);
        }
        finally { start.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task FlushTimeoutKeepsIncomingWorkAndCancellationDoesNotCancelDrain()
    {
        var start = Signal(); var writer = new Writer(); var runtime = Create([writer], start.Task);
        try
        {
            runtime.CreateLogger("Incoming").Write(CoreLogLevel.Information, Event("retained"));
            LogFlushResult timed = await runtime.FlushAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);
            Assert.AreEqual(LogCompletion.TimedOut, timed.Completion); Assert.AreEqual(1L, timed.Incoming!.Pending);
            Assert.HasCount(0, timed.Outputs, "Downstream boundaries are not invented before batch dispatch.");
            using var cancellation = new CancellationTokenSource();
            Task<LogFlushResult> waiter = runtime.FlushAsync(Limit, cancellation.Token); cancellation.Cancel();
            Assert.AreEqual(LogCompletion.Canceled, (await waiter).Completion);
            start.TrySetResult(); LogStopResult stopped = await runtime.StopAsync(CancellationToken.None);
            Assert.IsTrue(stopped.Succeeded); Assert.AreEqual(1L, stopped.Incoming!.Processed);
            Assert.AreEqual(1L, stopped.Outputs.Single().Written);
            Assert.AreEqual(1L, timed.Incoming.Pending, "The earlier timeout result remains immutable.");
        }
        finally { start.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task IncomingAcceptanceIsNotAClaimOfWriterDelivery()
    {
        var failed = new Writer((_, _) => throw new IOException("private")); var healthy = new Writer();
        var runtime = Create([failed, healthy]);
        LogWriteResult accepted = runtime.CreateLogger("Incoming").Write(CoreLogLevel.Information, Event("event"));
        Assert.AreEqual(LogWriteOutcome.Accepted, accepted.Outcome); Assert.AreEqual(1L, accepted.IncomingSequence);
        LogStopResult stopped = await runtime.StopAsync(CancellationToken.None);
        Assert.AreEqual(1L, stopped.Incoming!.Processed); Assert.AreEqual(0L, stopped.Incoming.Failed);
        Assert.AreEqual(1L, stopped.Outputs[0].Failed); Assert.AreEqual(1L, stopped.Outputs[1].Written);
        Assert.IsFalse(stopped.Succeeded);
        await Assert.ThrowsExactlyAsync<IOException>(() => runtime.Completion);
        await runtime.DisposeAsync();
    }

    [TestMethod]
    public async Task WriterQueueRejectionOfAcceptedInputPreventsSuccessfulFlush()
    {
        var entered = Signal(); var release = Signal();
        var writer = new Writer(async (_, _) => { entered.TrySetResult(); await release.Task; });
        var runtime = Create([writer], capacity: 1);
        async Task Processed(long count)
        {
            async Task Wait() { while (runtime.GetDiagnostics().Incoming!.Processed < count) { await Task.Yield(); } }
            await Wait().WaitAsync(Limit);
        }
        try
        {
            var logger = runtime.CreateLogger("Incoming");
            logger.Write(CoreLogLevel.Information, Event("active")); await entered.Task.WaitAsync(Limit);
            Assert.AreEqual(2L, logger.Write(CoreLogLevel.Information, Event("buffered")).IncomingSequence);
            await Processed(2);
            Assert.AreEqual(3L, logger.Write(CoreLogLevel.Information, Event("rejected-by-writer")).IncomingSequence);
            await Processed(3);
            release.TrySetResult();
            LogStopResult result = await runtime.StopAsync(CancellationToken.None);
            Assert.AreEqual(LogCompletion.Completed, result.Completion);
            Assert.IsFalse(result.Succeeded); Assert.IsTrue(result.LossObserved);
            Assert.AreEqual(3L, result.Incoming!.Processed); Assert.AreEqual(1L, result.Incoming.RejectedByWriters);
            Assert.AreEqual(2L, result.Outputs.Single().Written); Assert.AreEqual(1L, result.Outputs.Single().Rejections.QueueFull);
        }
        finally { release.TrySetResult(); await runtime.DisposeAsync(); }
    }


    [TestMethod]
    public async Task FlushKeepsCallerLossSnapshotsWhileCoveredWorkWaits()
    {
        var start = Signal(); var runtime = Create([new Writer()], start.Task);
        try
        {
            var logger = runtime.CreateLogger("Snapshots");
            logger.Info("covered");
            Task<LogFlushResult> first = runtime.FlushAsync(Limit, CancellationToken.None);
            Assert.AreEqual(LogWriteOutcome.InvalidEvent, logger.Write(CoreLogLevel.Information,
                new LogEventData { EventName = "", Message = "invalid" }).Outcome);
            Assert.IsTrue(logger.Info(new string('x', 9000)).IsTruncated);
            Task<LogFlushResult> second = runtime.FlushAsync(Limit, CancellationToken.None);
            start.TrySetResult();
            LogFlushResult before = await first.WaitAsync(Limit), after = await second.WaitAsync(Limit);
            Assert.IsTrue(before.Succeeded); Assert.IsFalse(before.LossObserved);
            Assert.AreEqual(0L, before.InvalidEventCount); Assert.AreEqual(0L, before.TruncatedEventCount);
            Assert.AreEqual(1L, after.InvalidEventCount); Assert.AreEqual(1L, after.TruncatedEventCount);
            Assert.AreEqual(2L, before.FrontendBatch!.TargetCount); Assert.AreEqual(2L, after.FrontendBatch!.TargetCount);
        }
        finally { start.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task CoalescedFlushesObserveLifetimeEvictionsWithoutInventingCapacityRejections()
    {
        var start = Signal(); var runtime = Create([new Writer()], start.Task, capacity: 1);
        try
        {
            var logger = runtime.CreateLogger("Coalesced"); logger.Info("covered");
            Task<LogFlushResult> first = runtime.FlushAsync(Limit, CancellationToken.None);
            Assert.AreEqual(LogWriteOutcome.Accepted, logger.Info("replacement-one").Outcome);
            Assert.AreEqual(LogWriteOutcome.Accepted, logger.Info("replacement-two").Outcome);
            Task<LogFlushResult> second = runtime.FlushAsync(Limit, CancellationToken.None);
            start.TrySetResult();
            LogFlushResult before = await first.WaitAsync(Limit), after = await second.WaitAsync(Limit);
            Assert.AreEqual(3L, before.Incoming!.Admitted); Assert.AreEqual(3L, after.Incoming!.Admitted);
            Assert.AreEqual(0L, before.Incoming.QueueFull); Assert.AreEqual(0L, after.Incoming.QueueFull);
            Assert.AreEqual(2L, before.Incoming.Evicted); Assert.AreEqual(2L, after.Incoming.Evicted);
            Assert.AreEqual(1L, before.FrontendBatch!.TargetCount); Assert.AreEqual(0L, before.FrontendBatch.Evicted);
            Assert.IsTrue(before.LossObserved); Assert.IsTrue(after.LossObserved);
            Assert.IsTrue(before.Succeeded); Assert.IsTrue(after.Succeeded);
        }
        finally { start.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task StopExcludesTruncationFromCaptureThatLosesAdmissionRace()
    {
        var start = Signal(); var entered = Signal(); var release = Signal();
        var runtime = Create([new Writer()], start.Task);
        try
        {
            var logger = runtime.CreateLogger("StopRace"); logger.Info("covered");
            Task<LogWriteResult> write = Task.Run(() => logger.InfoLazy(() =>
            {
                entered.TrySetResult(); release.Task.WaitAsync(Limit).GetAwaiter().GetResult();
                return new LogEventData { EventName = "late", Message = new string('x', 9000) };
            }));
            await entered.Task.WaitAsync(Limit);
            Task<LogStopResult> stop = runtime.StopAsync(CancellationToken.None);
            release.TrySetResult(); LogWriteResult late = await write.WaitAsync(Limit);
            Assert.AreEqual(LogWriteOutcome.Stopped, late.Outcome); Assert.IsTrue(late.IsTruncated);
            start.TrySetResult(); LogStopResult result = await stop.WaitAsync(Limit);
            Assert.IsTrue(result.Succeeded); Assert.IsFalse(result.LossObserved);
            Assert.AreEqual(0L, result.TruncatedEventCount); Assert.AreEqual(1L, runtime.GetDiagnostics().TruncatedEventCount);
        }
        finally { release.TrySetResult(); start.TrySetResult(); await runtime.DisposeAsync(); }
    }


    [TestMethod]
    public async Task FlushExcludesLaterEarlyOutputDecisionsAndRetainsCoveredWorkerRejections()
    {
        var entered = Signal(); var release = Signal(); int calls = 0;
        var runtime = QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None, QueueCapacity = 4 },
            writers: [new("failed", new Writer((_, _) => throw new IOException()), CoreLogLevel.Information),
                new("healthy", new Writer(), CoreLogLevel.Error)],
            processing: new LogProcessingOptions
            {
                Interceptors = [_ =>
            {
                if (Interlocked.Increment(ref calls) == 2)
                { entered.TrySetResult(); release.Task.WaitAsync(Limit).GetAwaiter().GetResult(); }
                return new();
            }]
            });
        try
        {
            var logger = runtime.CreateLogger("Rejections"); logger.Info("fault-writer");
            await Assert.ThrowsExactlyAsync<IOException>(() => runtime.QueuedOutputs!.Get("failed").Completion.WaitAsync(Limit));
            logger.Error("covered"); await entered.Task.WaitAsync(Limit);
            Task<LogFlushResult> first = runtime.FlushAsync(Limit, CancellationToken.None);
            Assert.AreEqual(LogWriteOutcome.Rejected, logger.Info("later-unavailable").Outcome);
            Task<LogFlushResult> second = runtime.FlushAsync(Limit, CancellationToken.None);
            release.TrySetResult();
            LogFlushResult before = await first.WaitAsync(Limit), after = await second.WaitAsync(Limit);
            Assert.AreEqual(1L, before.Outputs.Single(output => output.OutputName == "failed").Rejections.Unavailable);
            Assert.AreEqual(2L, after.Outputs.Single(output => output.OutputName == "failed").Rejections.Unavailable);
            Assert.AreEqual(1L, before.Outputs.Single(output => output.OutputName == "healthy").Rejections.Filtered);
            Assert.AreEqual(2L, after.Outputs.Single(output => output.OutputName == "healthy").Rejections.Filtered);
        }
        finally { release.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task IncomingFlushTimeoutKeepsOnlyItsRegisteredOutputLoss(bool rejectionAfterRegistration)
    {
        var entered = Signal(); var release = Signal();
        var runtime = QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None, QueueCapacity = 4 },
            writers: [new("failed", new Writer((_, _) => throw new IOException()), CoreLogLevel.Information),
                new("healthy", new Writer(), CoreLogLevel.Error)],
            processing: new LogProcessingOptions
            {
                Interceptors = [value =>
            {
                if (value.Message == "covered") { entered.TrySetResult(); release.Task.WaitAsync(Limit).GetAwaiter().GetResult(); }
                return new();
            }]
            });
        try
        {
            var logger = runtime.CreateLogger("TimeoutLoss");
            logger.Info("fault-writer");
            await Assert.ThrowsExactlyAsync<IOException>(() => runtime.QueuedOutputs!.Get("failed").Completion.WaitAsync(Limit));
            logger.Error("covered"); await entered.Task.WaitAsync(Limit);
            if (!rejectionAfterRegistration) { Assert.AreEqual(LogWriteOutcome.Rejected, logger.Info("unavailable").Outcome); }
            Task<LogFlushResult> flush = runtime.FlushAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);
            if (rejectionAfterRegistration) { Assert.AreEqual(LogWriteOutcome.Rejected, logger.Info("unavailable").Outcome); }
            LogFlushResult result = await flush.WaitAsync(Limit);
            Assert.AreEqual(LogCompletion.TimedOut, result.Completion);
            Assert.HasCount(0, result.Outputs, "Unresolved input must not invent downstream boundaries.");
            Assert.AreEqual(1L, result.Incoming!.Pending);
            Assert.AreEqual(!rejectionAfterRegistration, result.LossObserved, "Registration loss must survive without admitting later caller loss.");
            release.TrySetResult(); await runtime.StopAsync(CancellationToken.None).WaitAsync(Limit);
            Assert.AreEqual(!rejectionAfterRegistration, result.LossObserved, "The timeout result stays immutable.");
        }
        finally { release.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DetachedCallbackRejectionUsesItsOwnCallerRegistration(bool rejectionBeforeRegistration)
    {
        var enterSecond = Signal(); var releaseSecond = Signal(); var releaseChild = Signal();
        CoreLogRuntime? runtime = null; Task<LogWriteResult>? detached = null;
        runtime = QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None },
            writers: [new("writer", new Writer())],
            processing: new LogProcessingOptions
            {
                Interceptors = [value =>
            {
                if (value.Message == "first") { detached = Task.Run(async () => { await releaseChild.Task; return runtime!.CreateLogger("Detached").Info("late"); }); }
                if (value.Message == "covered") { enterSecond.TrySetResult(); releaseSecond.Task.WaitAsync(Limit).GetAwaiter().GetResult(); }
                return new();
            }]
            });
        try
        {
            var logger = runtime.CreateLogger("Callbacks"); logger.Info("first");
            Assert.IsTrue((await runtime.FlushAsync(Limit, CancellationToken.None)).Succeeded);
            Assert.IsNotNull(detached);
            logger.Info("covered"); await enterSecond.Task.WaitAsync(Limit);
            if (rejectionBeforeRegistration) { releaseChild.TrySetResult(); await detached.WaitAsync(Limit); }
            Task<LogFlushResult> flush = runtime.FlushAsync(Limit, CancellationToken.None);
            releaseChild.TrySetResult();
            LogWriteResult rejected = await detached.WaitAsync(Limit);
            Assert.AreEqual("RecursiveProcessing", rejected.ErrorCode);
            releaseSecond.TrySetResult(); LogFlushResult result = await flush.WaitAsync(Limit);
            Assert.AreEqual(rejectionBeforeRegistration ? 1L : 0L, result.InvalidEventCount, "An ended callback flow cannot add worker loss to a later prefix.");
            Assert.AreEqual(rejectionBeforeRegistration, result.LossObserved);
            Assert.AreEqual(1L, runtime.GetDiagnostics().InvalidEventCount);
            Assert.AreEqual(2L, result.Incoming!.Processed);
        }
        finally { releaseChild.TrySetResult(); releaseSecond.TrySetResult(); if (detached is not null) { await detached.WaitAsync(Limit); } await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task JoinedCallbackChildRejectionStillBelongsToItsActiveWorkerPrefix()
    {
        CoreLogRuntime? runtime = null; LogWriteResult? rejected = null;
        runtime = QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None },
            writers: [new("writer", new Writer())],
            processing: new LogProcessingOptions
            {
                Interceptors = [_ =>
            {
                rejected = Task.Run(() => runtime!.CreateLogger("Child").Info("recursive")).WaitAsync(Limit).GetAwaiter().GetResult();
                return new();
            }]
            });
        await using (runtime)
        {
            runtime.CreateLogger("Parent").Info("covered");
            LogFlushResult result = await runtime.FlushAsync(Limit, CancellationToken.None);
            Assert.AreEqual("RecursiveProcessing", rejected!.ErrorCode);
            Assert.AreEqual(1L, result.InvalidEventCount);
            Assert.IsTrue(result.LossObserved);
            Assert.AreEqual(1L, result.Incoming!.Processed);
            Assert.AreEqual(1L, result.Outputs.Single().Written);
        }
    }
    private sealed class Writer(Func<string, CancellationToken, ValueTask>? write = null) : ILogWriter
    {
        internal ConcurrentQueue<string> Lines { get; } = new();
        public async ValueTask WriteAsync(string jsonLine, CancellationToken cancellationToken)
        { if (write is not null) { await write(jsonLine, cancellationToken); } Lines.Enqueue(jsonLine); }
        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
