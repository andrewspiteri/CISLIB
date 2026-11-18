using CIS.RestClient;
using CIS.RestClient.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
public sealed class ResilienceExecutionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationSettlesSupplementalAndNativeWaitWithoutAnotherAttempt(bool supplemental)
    {
        using var clock = new ResilienceClock();
        using var cancellation = new CancellationTokenSource();
        var policy = new RestRetryPolicy("cancel", 1, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), [503], useJitter: false);
        var executor = new PollyRestRetryExecutor([policy], clock);
        int calls = 0;
        Task<RestPolicyResult> run = executor.ExecuteAsync(policy, ResilienceTestSupport.Context(clock, cancellation.Token), (_, token) =>
        {
            Assert.AreEqual(cancellation.Token, token); calls++;
            return ValueTask.FromResult(ResilienceTestSupport.Http(supplemental ? "10" : null));
        }).AsTask();
        try
        {
            ResilienceClock.Wait wait = await clock.NextAsync();
            Assert.AreEqual(TimeSpan.FromSeconds(supplemental ? 8 : 2), wait.Delay);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => run.WaitAsync(ResilienceTestSupport.Deadline));
            await wait.Disposed.Task.WaitAsync(ResilienceTestSupport.Deadline);
            Assert.AreEqual(1, calls);
        }
        finally { cancellation.Cancel(); await ResilienceTestSupport.SettleAsync(run); }
    }

    [TestMethod]
    public async Task CanceledAttemptCannotCompleteBeforeItsActualCleanupAndUnexpectedDefectStillEscapes()
    {
        using var clock = new ResilienceClock();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var policy = new RestRetryPolicy("cleanup", 1, TimeSpan.Zero, TimeSpan.Zero, [503], useJitter: false);
        var executor = new PollyRestRetryExecutor([policy], clock);
        int calls = 0;
        Task<RestPolicyResult> run = executor.ExecuteAsync(policy, ResilienceTestSupport.Context(clock, cancellation.Token), async (_, token) =>
        {
            calls++; entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); return ResilienceTestSupport.Success(); }
            finally { cleaning.TrySetResult(); await release.Task; }
        }).AsTask();
        try
        {
            await entered.Task.WaitAsync(ResilienceTestSupport.Deadline);
            cancellation.Cancel(); await cleaning.Task.WaitAsync(ResilienceTestSupport.Deadline);
            Assert.IsFalse(run.IsCompleted);
            release.TrySetResult();
            await Assert.ThrowsAsync<OperationCanceledException>(() => run.WaitAsync(ResilienceTestSupport.Deadline));
            Assert.AreEqual(1, calls);
        }
        finally { cancellation.Cancel(); release.TrySetResult(); await ResilienceTestSupport.SettleAsync(run); }

        using var failedCancellation = new CancellationTokenSource();
        var defect = new InvalidOperationException("Controlled programming defect.");
        InvalidOperationException thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => executor.ExecuteAsync(policy,
            ResilienceTestSupport.Context(clock, failedCancellation.Token), (_, _) =>
            { failedCancellation.Cancel(); throw defect; }).AsTask());
        Assert.AreSame(defect, thrown);
    }

    [TestMethod]
    public async Task ZeroNamedPolicyUsesOneNativeEmptyPipelineAndExactRegisteredClockAndInstance()
    {
        using var clock = new ResilienceClock();
        var policy = new RestRetryPolicy("zero", 0, TimeSpan.Zero, TimeSpan.Zero, [503]);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddCisRestClientResilience(policy);
        using ServiceProvider root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Assert.AreSame(policy, root.GetServices<RestRetryPolicy>().Single());
        IRestRetryExecutor executor = root.GetRequiredService<IRestRetryExecutor>();
        int calls = 0;
        RestPolicyResult result = await executor.ExecuteAsync(policy, ResilienceTestSupport.Context(clock), (iteration, _) =>
        { Assert.AreEqual(0, iteration); calls++; return ValueTask.FromResult(ResilienceTestSupport.Http()); });
        Assert.AreEqual(1, calls); Assert.AreEqual(0, clock.Created);
        Assert.AreEqual(RestRetryStopReason.AttemptLimit, result.StopReason);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => executor.ExecuteAsync(policy,
            ResilienceTestSupport.Context(TimeProvider.System), (_, _) => ValueTask.FromResult(ResilienceTestSupport.Success())).AsTask());
        Assert.ThrowsExactly<ArgumentException>(() => new ServiceCollection().AddCisRestClientResilience());
        Assert.ThrowsExactly<ArgumentException>(() => new ServiceCollection().AddCisRestClientResilience(policy, policy));
    }

    [TestMethod]
    [DataRow("POST", true, RestFailureKind.Http, null, RestRetryStopReason.NotEligible)]
    [DataRow(null, true, RestFailureKind.Http, null, RestRetryStopReason.NotEligible)]
    [DataRow("GET", false, RestFailureKind.Http, null, RestRetryStopReason.BodyNotReplayable)]
    [DataRow("GET", true, RestFailureKind.Transport, RestTransportFailureKind.Connection, RestRetryStopReason.NonRetryable)]
    [DataRow("GET", true, RestFailureKind.InvalidResponse, null, RestRetryStopReason.NonRetryable)]
    public async Task ActualAttemptMetadataPreventsUnsafeOrUnselectedRetry(string? method, bool replayable,
        RestFailureKind kind, RestTransportFailureKind? transport, RestRetryStopReason reason)
    {
        using var clock = new ResilienceClock();
        var policy = new RestRetryPolicy("eligibility", 1, TimeSpan.Zero, TimeSpan.Zero, [503]);
        var executor = new PollyRestRetryExecutor([policy], clock);
        int calls = 0;
        RestPolicyResult result = await executor.ExecuteAsync(policy, ResilienceTestSupport.Context(clock), (_, _) =>
        {
            calls++;
            return ValueTask.FromResult(new RestAttemptOutcome(kind == RestFailureKind.Http ? 503 : null, kind,
                BodyProcessingStarted: true, BodyReplayable: replayable, TransportFailure: transport, Method: method));
        });
        Assert.AreEqual(1, calls); Assert.AreEqual(reason, result.StopReason); Assert.AreEqual(0, clock.Created);
    }

    [TestMethod]
    [DataRow("transport", RestReplaySafety.Default, 2)]
    [DataRow("business-kind", RestReplaySafety.Default, 2)]
    [DataRow("business-code", RestReplaySafety.Default, 2)]
    [DataRow("post", RestReplaySafety.Assured, 2)]
    [DataRow("body", RestReplaySafety.Assured, 2)]
    [DataRow("body", RestReplaySafety.Default, 1)]
    public async Task ExplicitCustomPoliciesRetryOnlyWithTheirRequiredReplayAssurance(string scenario,
        RestReplaySafety replay, int expectedCalls)
    {
        using var clock = new ResilienceClock();
        using var cancellation = new CancellationTokenSource();
        RestRetryPolicy policy = scenario switch
        {
            "transport" => new("transport", 1, TimeSpan.Zero, TimeSpan.Zero, useJitter: false,
                transportFailures: [RestTransportFailureKind.NameResolution]),
            "business-kind" => new("business-kind", 1, TimeSpan.Zero, TimeSpan.Zero, useJitter: false,
                failureKinds: [RestFailureKind.Business]),
            "business-code" => new("business-code", 1, TimeSpan.Zero, TimeSpan.Zero, useJitter: false,
                errorCodes: ["provider.try_later"]),
            "post" => new("post", 1, TimeSpan.Zero, TimeSpan.Zero, [503], ["POST"], useJitter: false),
            "body" => new("body", 1, TimeSpan.Zero, TimeSpan.Zero, useJitter: false,
                failureKinds: [RestFailureKind.InvalidResponse], retryBodyFailures: true),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        RestAttemptOutcome failure = scenario switch
        {
            "transport" => new(null, RestFailureKind.Transport, "client.transport_error",
                TransportFailure: RestTransportFailureKind.NameResolution, Method: "GET"),
            "business-kind" => new(200, RestFailureKind.Business, "business.rejected", Method: "GET"),
            "business-code" => new(200, RestFailureKind.Business, "provider.try_later", Method: "GET"),
            "post" => ResilienceTestSupport.Http(method: "POST"),
            "body" => new(200, RestFailureKind.InvalidResponse, "client.invalid_response",
                BodyProcessingStarted: true, Method: "GET"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var executor = new PollyRestRetryExecutor([policy], clock);
        var iterations = new List<int>();
        Task<RestPolicyResult> run = executor.ExecuteAsync(policy,
            ResilienceTestSupport.Context(clock, cancellation.Token, replay: replay), (iteration, token) =>
            {
                Assert.AreEqual(cancellation.Token, token); iterations.Add(iteration);
                return ValueTask.FromResult(iteration == 0 ? failure : new RestAttemptOutcome(200, RestFailureKind.None, Method: failure.Method));
            }).AsTask();
        try
        {
            RestPolicyResult result = await run.WaitAsync(ResilienceTestSupport.Deadline);
            CollectionAssert.AreEqual(expectedCalls == 2 ? new[] { 0, 1 } : new[] { 0 }, iterations);
            Assert.AreEqual(0, clock.Created);
            if (expectedCalls == 2) { Assert.AreEqual(RestFailureKind.None, result.Terminal.FailureKind); Assert.IsNull(result.StopReason); }
            else { Assert.AreSame(failure, result.Terminal); Assert.AreEqual(RestRetryStopReason.NonRetryable, result.StopReason); }
        }
        finally { cancellation.Cancel(); await ResilienceTestSupport.SettleAsync(run); }
    }

    [TestMethod]
    public async Task StatusRetriesKeepFreshIterationAndTerminalCauseWhileNarrowTransportAndDecisionAreExplicit()
    {
        using var clock = new ResilienceClock();
        var policy = new RestRetryPolicy("status", 2, TimeSpan.Zero, TimeSpan.Zero, [503], useJitter: false);
        var executor = new PollyRestRetryExecutor([policy], clock);
        var iterations = new List<int>();
        RestPolicyResult result = await executor.ExecuteAsync(policy, ResilienceTestSupport.Context(clock), (iteration, _) =>
        { iterations.Add(iteration); return ValueTask.FromResult(ResilienceTestSupport.Http()); });
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, iterations);
        Assert.AreEqual("http.503", result.Terminal.ErrorCode);
        Assert.AreEqual(RestRetryStopReason.AttemptLimit, result.StopReason);

        int decisions = 0, calls = 0;
        var narrow = new RestRetryPolicy("narrow", 1, TimeSpan.Zero, TimeSpan.Zero,
            transportFailures: [RestTransportFailureKind.NameResolution],
            shouldRetry: (_, _) => { decisions++; return ValueTask.FromResult(false); });
        var narrowExecutor = new PollyRestRetryExecutor([narrow], clock);
        RestPolicyResult rejected = await narrowExecutor.ExecuteAsync(narrow, ResilienceTestSupport.Context(clock), (_, _) =>
        {
            calls++;
            return ValueTask.FromResult(new RestAttemptOutcome(null, RestFailureKind.Transport, "client.transport_error",
                TransportFailure: RestTransportFailureKind.NameResolution, Method: "GET"));
        });
        Assert.AreEqual(1, decisions); Assert.AreEqual(1, calls);
        Assert.AreEqual(RestRetryStopReason.NonRetryable, rejected.StopReason);
    }
}
