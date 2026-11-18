using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.TestSupport;
using CIS.Tests.UnitTests.RestClient;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
[DoNotParallelize]
public sealed class RestClientLoggingTests
{
    [TestMethod]
    [DataRow(200, LogLevel.Information, "200 Ok")]
    [DataRow(500, LogLevel.Error, "500 Technical Error")]
    public async Task FinalSummaryUsesActualDispatchMethodSafeUriInvariantDurationAndNoSensitiveState(int status, LogLevel level, string result)
    {
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        { await context.Request.Body.CopyToAsync(Stream.Null); context.Response.StatusCode = status; await context.Response.WriteAsync("response-secret"); });
        var capture = new CaptureProvider();
        using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddProvider(capture));
        var clock = Clock();
        await using var client = new HttpRestClient(new("orders") { BaseUrl = server.Address + "/" }, null, clock, factory);
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            RestResult<RestNoValue> actual = await client.Get<RestNoValue>("entry?entry-secret=yes", new()
            {
                Body = RestBody.Text("payload-secret"),
                ConfigureRequest = (context, _) =>
                {
                    context.Method = "POST"; context.Url = server.Address + "/final?token=query-secret#fragment-secret";
                    context.Headers.Set("Authorization", "Bearer header-secret");
                    clock.Advance(TimeSpan.FromTicks(102_500)); return Task.CompletedTask;
                }
            }).WaitAsync(RestHttpTestSupport.Deadline);
            Assert.AreEqual(status == 200, actual.Success);
            Entry entry = One(capture, level);
            Assert.AreEqual("[REST] POST " + server.Address + "/final " + result + " 10.25ms", entry.Message);
            Assert.IsFalse(entry.Message.Contains("Attempt", StringComparison.Ordinal));
            Safe(entry);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestMethod]
    public async Task RetrySummaryIsOncePerLogicalCallAndUsesActualTwoDispatchesOfTheConfiguredThree()
    {
        int received = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(context =>
        { context.Response.StatusCode = Interlocked.Increment(ref received) == 1 ? 503 : 500; return Task.CompletedTask; });
        var capture = new CaptureProvider(); var clock = Clock();
        var policy = new RestRetryPolicy("logging", 2, TimeSpan.Zero, TimeSpan.Zero, [503], useJitter: false);
        var options = new RestClientOptions("orders")
        {
            BaseUrl = server.Address + "/", RetryPolicy = RestRetrySelection.Named("logging"),
            ConfigureRequest = (_, _) => { clock.Advance(TimeSpan.FromMilliseconds(5)); return Task.CompletedTask; }
        };
        await using var host = RuntimeTestSupport.Root(options, clock, policy);
        host.Services.GetRequiredService<ILoggerFactory>().AddProvider(capture);
        RestResult<RestNoValue> result = await host.Services.GetRequiredKeyedService<IRestClient>("orders").Get<RestNoValue>("orders").WaitAsync(RestHttpTestSupport.Deadline);
        Assert.IsFalse(result.Success); Assert.AreEqual(2, received); Assert.AreEqual(2, result.Context.AttemptCount);
        Entry entry = One(capture, LogLevel.Error);
        Assert.AreEqual("[REST] GET " + server.Address + "/orders 500 Technical Error 10ms (Attempt 2/3)", entry.Message);
        Safe(entry);
    }

    [TestMethod]
    public async Task BusinessFailureWithHttp200LogsErrorWithoutItsProviderExplanation()
    {
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(context => context.Response.WriteAsync("response-secret"));
        var capture = new CaptureProvider();
        using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddProvider(capture));
        await using var client = new HttpRestClient(new("orders") { BaseUrl = server.Address + "/", ResponseMapper = new DeclinedMapper() }, null, Clock(), factory);
        RestResult<RestNoValue> result = await client.Get<RestNoValue>("orders").WaitAsync(RestHttpTestSupport.Deadline);
        Assert.IsFalse(result.Success); Assert.AreEqual(RestFailureKind.Business, result.Context.FailureKind);
        Entry entry = One(capture, LogLevel.Error);
        Assert.AreEqual("[REST] GET " + server.Address + "/orders 200 Business Error 0ms", entry.Message);
        Safe(entry);
    }

    [TestMethod]
    public async Task LongDispatchedUriHasBoundedRetainedStateAndStillOmitsItsQuery()
    {
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(context => { context.Response.StatusCode = 204; return Task.CompletedTask; });
        var capture = new CaptureProvider();
        using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddProvider(capture));
        await using var client = new HttpRestClient(new("orders") { BaseUrl = server.Address + "/" }, null, Clock(), factory);
        Assert.IsTrue((await client.Get<RestNoValue>("long/" + new string('a', 700) + "?token=query-secret").WaitAsync(RestHttpTestSupport.Deadline)).Success);
        Entry entry = One(capture, LogLevel.Information); Safe(entry);
        string uri = (string)entry.State.Single(pair => pair.Key == "Uri").Value!;
        Assert.IsTrue(uri.StartsWith(server.Address + "/long/", StringComparison.Ordinal));
        Assert.IsTrue(uri.Length <= 512);
    }

    [TestMethod]
    [DataRow("success")]
    [DataRow("cancel")]
    [DataRow("fault")]
    public async Task FinalEntryWaitsForActualCleanupAndPreservesCallerCancellationOrTheOriginalFault(string outcome)
    {
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        { await context.Request.Body.CopyToAsync(Stream.Null); context.Response.StatusCode = 200; });
        var capture = new CaptureProvider(); var clock = Clock(); var input = new RuntimeHeldInput();
        using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddProvider(capture));
        using var caller = new CancellationTokenSource();
        var entered = RestHttpTestSupport.Signal(); var defect = new InvalidOperationException("exception-secret");
        var options = new RestClientOptions("orders") { BaseUrl = server.Address + "/" };
        if (outcome == "cancel") { options.Authenticate = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }; }
        if (outcome == "fault") { options.Authenticate = (_, _) => throw defect; }
        await using var client = new HttpRestClient(options, null, clock, factory);
        Task<RestResult<RestNoValue>> run = client.Post<RestNoValue>("orders?token=query-secret", new()
        { Body = RestHttpBody.Reopen(_ => ValueTask.FromResult<Stream>(input)) }, caller.Token);
        try
        {
            if (outcome == "cancel") { await entered.Task.WaitAsync(RestHttpTestSupport.Deadline); caller.Cancel(); }
            await input.Cleaning.Task.WaitAsync(RestHttpTestSupport.Deadline);
            Assert.IsFalse(run.IsCompleted); Assert.AreEqual(0, capture.Entries.Count);
            clock.Advance(TimeSpan.FromMilliseconds(25)); input.Release.TrySetResult();
            if (outcome == "cancel")
            {
                OperationCanceledException actual = await Assert.ThrowsAsync<OperationCanceledException>(() => run.WaitAsync(RestHttpTestSupport.Deadline));
                Assert.AreEqual(caller.Token, actual.CancellationToken); Assert.IsTrue(run.IsCanceled);
            }
            else if (outcome == "fault")
            {
                InvalidOperationException actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => run.WaitAsync(RestHttpTestSupport.Deadline));
                Assert.AreSame(defect, actual); Assert.IsTrue(run.IsFaulted);
            }
            else { Assert.IsTrue((await run.WaitAsync(RestHttpTestSupport.Deadline)).Success); }
            Entry entry = One(capture, outcome == "success" ? LogLevel.Information : LogLevel.Error);
            StringAssert.Contains(entry.Message, outcome switch { "success" => " 200 Ok 25ms", "cancel" => " Cancelled 25ms", _ => " Unexpected Error 25ms" });
            Safe(entry); Assert.AreEqual(1, input.Disposals);
        }
        finally { caller.Cancel(); input.Release.TrySetResult(); await RuntimeTestSupport.SettleAsync(run, defect); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DisabledOrThrowingLoggingCannotChangeTheResultOrDisposeTheBorrowedFactory(bool throwing)
    {
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(context => { context.Response.StatusCode = 204; return Task.CompletedTask; });
        var capture = new CaptureProvider { ThrowOnWrite = throwing };
        using ILoggerFactory factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(throwing ? LogLevel.Information : LogLevel.None).AddProvider(capture));
        await using var client = new HttpRestClient(new("orders") { BaseUrl = server.Address + "/" }, null, Clock(), factory);
        RestResult<RestNoValue> result = await client.Get<RestNoValue>("orders").WaitAsync(RestHttpTestSupport.Deadline);
        Assert.IsTrue(result.Success); Assert.AreEqual(1, result.Context.AttemptCount);
        await client.DisposeAsync(); Assert.IsFalse(capture.Disposed);
        Assert.AreEqual(throwing ? 1 : 0, capture.Writes); Assert.AreEqual(0, capture.Entries.Count);
    }

    [TestMethod]
    public async Task StandardCoreLoggerProviderReceivesTheSameFinalSummaryThroughItsActualQueuedOutput()
    {
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(context => { context.Response.StatusCode = 204; return Task.CompletedTask; });
        var lines = new ConcurrentQueue<string>();
        var metadata = new EventMetadata("Rest.Logging", "build-1", "1.0.0", "test", EnvironmentKind.Console,
            "11111111-1111-1111-1111-111111111111", null, new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero), "unknown", null, null, null, null, null, null, null, null);
        await using CoreLogRuntime core = CoreLogRuntime.CreateQueued(metadata,
            new LoggingOptions { ConsoleLevel = CoreLogLevel.Information, FileLevel = CoreLogLevel.None },
            admitted => { lines.Enqueue(System.Text.Encoding.UTF8.GetString(admitted.Event.JsonLine.Span)); return ValueTask.CompletedTask; }, null);
        using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddProvider(new CoreLoggerProvider(core)));
        await using var client = new HttpRestClient(new("orders") { BaseUrl = server.Address + "/" }, null, Clock(), factory);
        Assert.IsTrue((await client.Get<RestNoValue>("orders?token=query-secret").WaitAsync(RestHttpTestSupport.Deadline)).Success);
        await core.StopAsync(CancellationToken.None).WaitAsync(RestHttpTestSupport.Deadline);
        Assert.AreEqual(1, lines.Count);
        JsonNode record = JsonNode.Parse(lines.Single())!;
        Assert.AreEqual("[REST] GET " + server.Address + "/orders 204 Ok 0ms", record["message"]!.GetValue<string>());
        Assert.IsFalse(lines.Single().Contains("query-secret", StringComparison.Ordinal));
    }

    private static ManualTimeProvider Clock() => new(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
    private static Entry One(CaptureProvider capture, LogLevel level)
    {
        Assert.AreEqual(1, capture.Entries.Count);
        Entry entry = capture.Entries.Single(); Assert.AreEqual(level, entry.Level); Assert.AreEqual("restclient.completed", entry.Event.Name);
        Assert.IsNull(entry.Exception); return entry;
    }
    private static void Safe(Entry entry)
    {
        CollectionAssert.AreEquivalent(new[] { "Method", "Uri", "Result", "Duration", "RetrySuffix", "{OriginalFormat}" }, entry.State.Select(pair => pair.Key).ToArray());
        Assert.IsTrue(entry.State.All(pair => pair.Value is string));
        string retained = entry.Message + " " + string.Join(" ", entry.State.Select(pair => pair.Value));
        foreach (string secret in new[] { "entry-secret", "query-secret", "fragment-secret", "payload-secret", "header-secret", "response-secret", "exception-secret" })
        { Assert.IsFalse(retained.Contains(secret, StringComparison.Ordinal), "Log message/state cannot retain " + secret); }
        string uri = (string)entry.State.Single(pair => pair.Key == "Uri").Value!;
        Assert.IsTrue(uri.Length <= 512); Assert.IsFalse(uri.Contains('?') || uri.Contains('#') || uri.Contains('@'));
    }
    private sealed record Entry(LogLevel Level, EventId Event, string Message, KeyValuePair<string, object?>[] State, Exception? Exception);
    private sealed class CaptureProvider : ILoggerProvider
    {
        internal ConcurrentQueue<Entry> Entries { get; } = new();
        internal bool ThrowOnWrite { get; init; }
        internal bool Disposed { get; private set; }
        internal int Writes;
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
        public void Dispose() => Disposed = true;
    }
    private sealed class CaptureLogger(CaptureProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => !owner.Disposed;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (category != "CIS.RestClient.RestClientRuntime") { return; }
            Interlocked.Increment(ref owner.Writes);
            if (owner.ThrowOnWrite) { throw new InvalidOperationException("Controlled logger failure."); }
            owner.Entries.Enqueue(new(level, eventId, formatter(state, exception),
                state is IEnumerable<KeyValuePair<string, object?>> fields ? fields.ToArray() : [new("state", state)], exception));
        }
    }
    private sealed class DeclinedMapper : IRestResponseMapper
    {
        public ValueTask<RestResponseDecision> MapAsync<T>(RestResponseContext<T> response, CancellationToken token)
            => ValueTask.FromResult(RestResponseDecision.RejectBusiness("response-secret", "orders.declined"));
    }
}
