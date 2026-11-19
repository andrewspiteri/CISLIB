using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using CIS.RestClient;
using CIS.CommonTesting;
using CIS.Security;
using CIS.Security.Entra;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Client;

namespace CIS.Tests.Security.Entra;

[TestClass]
public sealed class EntraWorkloadAcquisitionTests
{
    [TestMethod]
    public async Task ActualSdkReadsProjectedAssertionAndReusesItsCredentialForTheFinalRestHook()
    {
        using var handler = new WorkloadSdkHandler();
        using var http = new HttpClient(handler);
        using var sdkTransport = new HttpClientTransport(http);
        await using EntraTlsReceiver receiver = await EntraTlsReceiver.StartAsync();
        await using EntraWorkloadFixture fixture = await EntraWorkloadFixture.StartAsync(receiver.Origin,
            sdkTransport: sdkTransport, serverTrust: receiver.ServerTrust);

        RestResult<ValueBody> first = await fixture.Client.Get<ValueBody>("/value");
        RestResult<ValueBody> second = await fixture.Client.Get<ValueBody>("/value");

        Assert.IsTrue(first.Success);
        Assert.IsTrue(second.Success);
        Assert.AreEqual(200, first.Context.HttpStatusCode);
        Assert.AreEqual(7, first.Value!.Value);
        Assert.AreEqual(2, receiver.Calls);
        Assert.AreEqual("Bearer fixture-access-token", receiver.Authorization);
        Assert.AreEqual(1, handler.TokenRequests, "The supported SDK credential owns its cache; CIS adds no second cache or minting loop.");
        StringAssert.Contains(handler.LastBody!, "client_assertion=fixture.projected.assertion");
        StringAssert.Contains(handler.LastBody!, "client_credentials");
        SecurityProfileStatus status = fixture.Services.GetRequiredService<ISecurityProfileStatusSource>().CaptureStatus().Single();
        Assert.AreEqual(SecurityProfileAvailability.Available, status.Availability);
    }

    [TestMethod]
    public async Task ActualSdkProviderRejectionPreservesAuthenticationResultAndSafeKnownDetail()
    {
        using var handler = new WorkloadSdkHandler { Error = "invalid_client" };
        using var http = new HttpClient(handler);
        using var sdkTransport = new HttpClientTransport(http);
        await using EntraWorkloadFixture fixture = await EntraWorkloadFixture.StartAsync(sdkTransport: sdkTransport);

        RestResult<ValueBody> result = await fixture.Client.Get<ValueBody>("/value");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(RestFailureKind.Authentication, result.Context.FailureKind);
        Assert.AreEqual("client.authentication_failed", result.ErrorCode);
        Assert.AreEqual(0, result.Context.AttemptCount);
        Assert.AreEqual(SecurityFailureKind.CredentialInvalid, result.Context.SecurityFailure!.Kind);
        Assert.AreEqual("invalid_client", result.Context.SecurityFailure.ProviderCode);
        Assert.IsFalse(result.ErrorMessage!.Contains("raw-provider-secret", StringComparison.Ordinal));
        Assert.IsNull(result.Context.HttpStatusCode, "A token-endpoint rejection is not a fabricated response from the protected API.");
    }

    [TestMethod]
    [DataRow("expired", SecurityFailureKind.CredentialExpired)]
    [DataRow("unavailable", SecurityFailureKind.CredentialUnavailable)]
    [DataRow("invalid", SecurityFailureKind.CredentialInvalid)]
    public async Task KnownAcquisitionFailuresDoNotDispatchOrReplay(string mode, SecurityFailureKind expected)
    {
        using var credential = new ControlledWorkloadCredential(_ => mode switch
        {
            "expired" => ValueTask.FromResult(new AccessToken("expired-fixture-token", DateTimeOffset.UtcNow.AddSeconds(-1))),
            "unavailable" => ValueTask.FromException<AccessToken>(new CredentialUnavailableException("private source detail")),
            _ => ValueTask.FromException<AccessToken>(new AuthenticationFailedException("private provider detail",
                new MsalServiceException("invalid_grant", "private provider response")))
        });
        await using EntraWorkloadFixture fixture = await EntraWorkloadFixture.StartAsync(borrowed: _ => credential);

        RestResult<ValueBody> result = await fixture.Client.Post<ValueBody>("/value");

        Assert.AreEqual(RestFailureKind.Authentication, result.Context.FailureKind);
        Assert.AreEqual(expected, result.Context.SecurityFailure!.Kind);
        Assert.AreEqual(0, result.Context.AttemptCount);
        Assert.AreEqual(1, credential.Calls);
        Assert.IsFalse(result.ErrorMessage!.Contains("private", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SourceRecheckDoesNotInvalidateTheSdkCacheOrMintAToken()
    {
        using var credential = new ControlledWorkloadCredential(_ => ValueTask.FromException<AccessToken>(new CredentialUnavailableException("fixture")));
        await using EntraWorkloadFixture fixture = await EntraWorkloadFixture.StartAsync(borrowed: _ => credential);
        ISecurityCredentialControl control = fixture.Services.GetRequiredKeyedService<ISecurityCredentialControl>(EntraNativeAuthenticationTests.Profile);

        await control.RequestSourceRecheckAsync();
        await control.RequestSourceRecheckAsync();

        Assert.AreEqual(0, credential.Calls);
        Assert.AreEqual(0, credential.Disposals);
    }

    [TestMethod]
    public async Task AnUnexpectedCredentialProgrammingFaultPreservesItsOriginalIdentity()
    {
        var fault = new InvalidOperationException("fixture programming fault");
        using var credential = new ControlledWorkloadCredential(_ => ValueTask.FromException<AccessToken>(fault));
        await using EntraWorkloadFixture fixture = await EntraWorkloadFixture.StartAsync(borrowed: _ => credential);

        InvalidOperationException observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Client.Get<ValueBody>("/value"));

        Assert.AreSame(fault, observed);
        Assert.AreEqual(1, credential.Calls);
    }

    [TestMethod]
    public async Task CallerCancellationWaitsForTheActualSdkOperationToSettle()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var credential = new ControlledWorkloadCredential(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
            return new AccessToken("fixture-held-token", DateTimeOffset.UtcNow.AddMinutes(5));
        });
        await using EntraWorkloadFixture fixture = await EntraWorkloadFixture.StartAsync(borrowed: _ => credential);
        using var cancellation = new CancellationTokenSource();
        Task<RestResult<ValueBody>> operation = fixture.Client.Get<ValueBody>("/value", cancellationToken: cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            Assert.IsFalse(operation.IsCompleted, "Cancellation cannot certify cleanup while the admitted SDK operation still owns work.");
        }
        finally
        {
            release.TrySetResult();
        }
        try
        {
            await operation;
            Assert.Fail("The caller cancellation must remain a cancellation, not an authentication denial.");
        }
        catch (OperationCanceledException)
        {
            Assert.IsTrue(cancellation.IsCancellationRequested);
        }
    }

    [TestMethod]
    public async Task HostStopJoinsHeldAcquisitionAndDoesNotDisposeABorrowedSdkCredential()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var credential = new ControlledWorkloadCredential(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
            return new AccessToken("fixture-held-token", DateTimeOffset.UtcNow.AddMinutes(5));
        });
        EntraWorkloadFixture fixture = await EntraWorkloadFixture.StartAsync(borrowed: _ => credential);
        Task<RestResult<ValueBody>> operation = fixture.Client.Get<ValueBody>("/value");
        Task? stop = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stop = fixture.StopAsync();
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(operation.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            try
            {
                RestResult<ValueBody> result = await operation;
                Assert.AreEqual(RestFailureKind.Stopping, result.Context.FailureKind);
                if (stop is not null)
                {
                    await stop;
                }
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        }
        Assert.AreEqual(0, credential.Disposals);
    }

    [TestMethod]
    public async Task RequiredWarmupFailureAffectsOnlyItsOwnedReadinessCondition()
    {
        using var credential = new ControlledWorkloadCredential(_ => ValueTask.FromException<AccessToken>(new CredentialUnavailableException("fixture")));
        await using EntraWorkloadFixture fixture = await EntraWorkloadFixture.StartAsync(borrowed: _ => credential, required: true);

        SecurityProfileStatus status = fixture.Services.GetRequiredService<ISecurityProfileStatusSource>().CaptureStatus().Single();

        Assert.AreEqual(1, credential.Calls);
        Assert.AreEqual(SecurityProfileAvailability.Unavailable, status.Availability);
        var owned = fixture.Health.CaptureSnapshot().Monitors.Single(monitor =>
            monitor.Name == "Security: " + EntraNativeAuthenticationTests.Profile);
        Assert.IsTrue(owned.IsCritical);
        Assert.IsFalse(fixture.Health.CaptureSnapshot().Ready);
    }

    [TestMethod]
    public async Task RequiredCredentialExpiryIsObservedByMetadataHeartbeatWithoutAcquisition()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var credential = new ControlledWorkloadCredential(_ => ValueTask.FromResult(
            new AccessToken("fixture-token", clock.GetUtcNow().AddSeconds(5))));
        await using EntraWorkloadFixture fixture = await EntraWorkloadFixture.StartAsync(borrowed: _ => credential,
            required: true, clock: clock);
        Assert.IsTrue(fixture.Health.CaptureSnapshot().Ready);
        Assert.HasCount(1, fixture.Heartbeat.Registrations);
        Assert.AreEqual(TimeSpan.FromSeconds(1), fixture.Heartbeat.Registrations.Single().Options.Interval);
        clock.Advance(TimeSpan.FromSeconds(10));

        await fixture.Heartbeat.TriggerAsync(fixture.Heartbeat.Registrations.Single().Subscriber);

        Assert.AreEqual(1, credential.Calls, "The shared Heartbeat reads expiry metadata, without a token minting/cache loop.");
        Assert.IsFalse(fixture.Health.CaptureSnapshot().Ready);
        Assert.AreEqual(SecurityProfileAvailability.Expired,
            fixture.Services.GetRequiredService<ISecurityProfileStatusSource>().CaptureStatus().Single().Availability);
    }

    [TestMethod]
    public async Task AThrowingCancellationCallbackDoesNotCertifyHeldCredentialCleanup()
    {
        var fault = new InvalidOperationException("owned cancellation callback fault");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var credential = new ControlledWorkloadCredential(async token =>
        {
            using CancellationTokenRegistration registration = token.Register(() => throw fault);
            entered.TrySetResult();
            await release.Task;
            return new AccessToken("held-fixture-token", DateTimeOffset.UtcNow.AddMinutes(5));
        });
        EntraWorkloadFixture fixture = await EntraWorkloadFixture.StartAsync(borrowed: _ => credential);
        Task<RestResult<ValueBody>> operation = fixture.Client.Get<ValueBody>("/value");
        Task? stop = null;
        var observed = new List<Exception>();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stop = fixture.StopAsync();
            Assert.IsFalse(operation.IsCompleted);
            Assert.IsFalse(stop.IsCompleted,
                "A cancellation callback fault must not bypass actual SDK/REST settlement.");
        }
        finally
        {
            release.TrySetResult();
            try
            {
                await operation;
            }
            catch (Exception error)
            {
                observed.Add(error);
            }
            try
            {
                if (stop is not null)
                {
                    await stop;
                }
            }
            catch (Exception error)
            {
                observed.Add(error);
            }
            try
            {
                await fixture.DisposeAsync();
            }
            catch (Exception error)
            {
                observed.Add(error);
            }
        }
        Assert.IsTrue(operation.IsCompleted);
        Assert.IsTrue(observed.Any(error => Contains(error, fault)),
            "The original callback fault remains observable after all owners settle.");
        Assert.AreEqual(0, credential.Disposals);
    }

    private static bool Contains(Exception candidate, Exception expected)
    {
        if (ReferenceEquals(candidate, expected))
        {
            return true;
        }
        if (candidate is AggregateException aggregate)
        {
            return aggregate.InnerExceptions.Any(error => Contains(error, expected));
        }
        return candidate.InnerException is { } inner && Contains(inner, expected);
    }

    private sealed record ValueBody(int Value);
}
