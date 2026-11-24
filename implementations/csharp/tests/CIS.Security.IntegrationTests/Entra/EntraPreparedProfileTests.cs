using System.Diagnostics.Metrics;
using System.Security.Authentication;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using CIS;
using CIS.CommonTesting;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.Security;
using CIS.Security.Entra;
using CIS.Security.Hosting;
using CIS.Startup.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CIS.Tests.Security.Entra;

[TestClass]
public sealed class EntraPreparedProfileTests
{
    [TestMethod]
    public async Task ActualSdkPreparationAndFinalNativeRestReuseTheSameCredentialPolicyAndBinding()
    {
        string assertion = Path.Combine(Path.GetTempPath(), "cis-entra-prepared-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(assertion, "fixture.projected.assertion");
        try
        {
            using var handler = new WorkloadSdkHandler();
            using var http = new HttpClient(handler);
            using var sdkTransport = new HttpClientTransport(http);
            await using EntraTlsReceiver receiver = await EntraTlsReceiver.StartAsync();
            SecurityPolicySnapshot policy = Policy(receiver.Origin, required: true);
            EntraSecurityOptions options = Options(assertion, sdkTransport);
            await using EntraPreparedProfile prepared = await EntraPreparedProfile.PrepareAsync(policy,
                EntraNativeAuthenticationTests.Profile, options);
            Assert.AreEqual(1, handler.TokenRequests, "Preparation performs actual SDK acquisition before a provider exists.");
            Assert.AreSame(policy, prepared.Policy);
            Assert.AreSame(policy, prepared.Binding.PolicyOwner);
            // Later mutable setup cannot change the captured native credential or scope.
            options.TenantId = "changed-after-capture";
            options.Outbound!.Scope = "invalid-after-capture";

            await using (RestHttpTransport bootstrapTransport = RestHttpTransport.CreateSecurityBound(prepared.Binding,
                serverTrust: receiver.ServerTrust))
            await using (var bootstrap = new HttpRestClient(new RestClientOptions("management-bootstrap")
            {
                BaseUrl = receiver.Origin,
                SecurityBinding = prepared.Binding,
                Authenticate = prepared.AuthenticateAsync,
                LogCalls = false
            }, bootstrapTransport))
            {
                RestResult<ValueBody> beforeBuild = await bootstrap.Get<ValueBody>("/value");
                Assert.IsTrue(beforeBuild.Success);
                Assert.AreEqual(7, beforeBuild.Value!.Value);
            }
            await prepared.RetireBootstrapAsync();

            HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
            await using var health = new HealthcheckService(new());
            await using var heartbeat = new ManualHeartbeat(capacity: 8);
            builder.Services.AddSingleton<IHealthMonitorFactory>(health);
            builder.Services.AddSingleton<IHeartbeatOwnerFactory>(heartbeat);
            builder.Services.AddCisSecurity(policy);
            builder.Services.AddCisEntraSecurity(prepared);
            RestHttpTransport? finalTransport = null;
            builder.Services.AddCisEntraRestClient(new RestClientOptions("management-final")
            {
                BaseUrl = receiver.Origin,
                LogCalls = false
            }, EntraNativeAuthenticationTests.Profile, (provider, binding) =>
            {
                Assert.AreSame(prepared.Binding, binding);
                return finalTransport = RestHttpTransport.CreateSecurityBound(binding,
                    provider.GetRequiredService<IMeterFactory>(), serverTrust: receiver.ServerTrust);
            });
            builder.Services.AddCisStartup();
            IHost host = builder.Build();
            try
            {
                await prepared.ActivateFinalAsync(host.Services);
                await host.StartAsync();
                IRestClient final = host.Services.GetRequiredKeyedService<IRestClient>("management-final");
                RestResult<ValueBody> afterBuild = await final.Get<ValueBody>("/value");
                Assert.IsTrue(afterBuild.Success);
                Assert.AreEqual(2, receiver.Calls);
                Assert.AreEqual("Bearer fixture-access-token", receiver.Authorization);
                Assert.AreEqual(1, handler.TokenRequests,
                    "The same native credential's SDK cache serves preparation, bootstrap, final warmup and final REST.");
                Assert.AreSame(policy, host.Services.GetRequiredService<SecurityPolicySnapshot>());
                Assert.AreEqual(SecurityProfileAvailability.Available,
                    host.Services.GetRequiredService<ISecurityProfileStatusSource>().CaptureStatus().Single().Availability);
                Assert.HasCount(1, heartbeat.Registrations, "Only final activation owns required host observations.");
            }
            finally
            {
                try
                {
                    await host.StopAsync(CancellationToken.None);
                }
                finally
                {
                    try
                    {
                        if (host is IAsyncDisposable asynchronous)
                        {
                            await asynchronous.DisposeAsync();
                        }
                        else
                        {
                            host.Dispose();
                        }
                    }
                    finally
                    {
                        if (finalTransport is not null)
                        {
                            await finalTransport.DisposeAsync();
                        }
                    }
                }
            }
        }
        finally
        {
            File.Delete(assertion);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CanceledPreparationJoinsActualSdkSettlementAndPreservesAnOriginalProgrammingFault(bool sdkFaults)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new InvalidOperationException("Prepared SDK programming fault");
        using var cancellation = new CancellationTokenSource();
        using var credential = new ControlledWorkloadCredential(async _ =>
        {
            entered.TrySetResult();
            try
            {
                await release.Task;
                if (sdkFaults)
                {
                    throw original;
                }
                return new AccessToken("prepared-token", DateTimeOffset.UtcNow.AddMinutes(5));
            }
            finally
            {
                settled.TrySetResult();
            }
        });
        Task<EntraPreparedProfile> preparation = EntraPreparedProfile.PrepareAsync(Policy("https://provider.invalid"),
            EntraNativeAuthenticationTests.Profile, Options(UnusedAssertion()), borrowedCredential: credential,
            cancellationToken: cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            Assert.IsFalse(preparation.IsCompleted);
            Assert.IsFalse(settled.Task.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
        }
        if (sdkFaults)
        {
            InvalidOperationException observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => preparation);
            Assert.AreSame(original, observed);
        }
        else
        {
            OperationCanceledException observed = await Assert.ThrowsAsync<OperationCanceledException>(() => preparation);
            Assert.AreEqual(cancellation.Token, observed.CancellationToken);
        }
        Assert.IsTrue(settled.Task.IsCompletedSuccessfully);
        Assert.AreEqual(0, credential.Disposals, "Preparation never takes disposal ownership of an external native credential.");
    }

    [TestMethod]
    public async Task ExpectedPreparationRefusalFailsClosedWithoutProviderTextOrBorrowedCredentialDisposal()
    {
        using var credential = new ControlledWorkloadCredential(_ => ValueTask.FromException<AccessToken>(
            new CredentialUnavailableException("private-provider-text")));
        AuthenticationException observed = await Assert.ThrowsExactlyAsync<AuthenticationException>(() =>
            EntraPreparedProfile.PrepareAsync(Policy("https://provider.invalid"), EntraNativeAuthenticationTests.Profile,
                Options(UnusedAssertion()), borrowedCredential: credential));
        Assert.IsFalse(observed.Message.Contains("private-provider-text", StringComparison.Ordinal));
        Assert.AreEqual(1, credential.Calls);
        Assert.AreEqual(0, credential.Disposals);
    }

    [TestMethod]
    public async Task PassiveFinalConstructionIsAllowedButActivationBeforeRetirementFailsWithoutHostObservers()
    {
        using var credential = new ControlledWorkloadCredential(_ => ValueTask.FromResult(
            new AccessToken("prepared-token", DateTimeOffset.UtcNow.AddMinutes(5))));
        SecurityPolicySnapshot policy = Policy("https://provider.invalid", required: true);
        await using EntraPreparedProfile prepared = await EntraPreparedProfile.PrepareAsync(policy,
            EntraNativeAuthenticationTests.Profile, Options(UnusedAssertion()), borrowedCredential: credential);
        await using var health = new HealthcheckService(new());
        await using var heartbeat = new ManualHeartbeat(capacity: 8);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHealthMonitorFactory>(health);
        services.AddSingleton<IHeartbeatOwnerFactory>(heartbeat);
        services.AddCisSecurity(policy);
        services.AddCisEntraSecurity(prepared);
        await using ServiceProvider provider = services.BuildServiceProvider();
        Assert.IsNotNull(provider.GetRequiredKeyedService<ISecurityCredentialControl>(EntraNativeAuthenticationTests.Profile));
        Assert.AreEqual(1, credential.Calls, "Ordinary final construction performs no SDK acquisition.");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => EntraNativeAuthenticationTests.StartAsync(provider));
        Assert.HasCount(0, heartbeat.Registrations);
        Assert.HasCount(0, health.CaptureSnapshot().Monitors);
        Assert.AreEqual(1, credential.Calls);
    }

    [TestMethod]
    public async Task WrongPolicyAndRejectedSecondProviderCannotRetireTheLegitimatePreparedOwner()
    {
        using var credential = new ControlledWorkloadCredential(_ => ValueTask.FromResult(
            new AccessToken("prepared-token", DateTimeOffset.UtcNow.AddMinutes(5))));
        SecurityPolicySnapshot policy = Policy("https://provider.invalid");
        await using EntraPreparedProfile prepared = await EntraPreparedProfile.PrepareAsync(policy,
            EntraNativeAuthenticationTests.Profile, Options(UnusedAssertion()), borrowedCredential: credential);
        await prepared.RetireBootstrapAsync();
        await using (ServiceProvider wrongPolicy = Services(Policy("https://provider.invalid"), prepared).BuildServiceProvider())
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => wrongPolicy.GetRequiredKeyedService<ISecurityCredentialControl>(EntraNativeAuthenticationTests.Profile));
        }
        await using ServiceProvider legitimate = Services(policy, prepared).BuildServiceProvider();
        Assert.IsNotNull(legitimate.GetRequiredKeyedService<ISecurityCredentialControl>(EntraNativeAuthenticationTests.Profile));
        await using (ServiceProvider second = Services(policy, prepared).BuildServiceProvider())
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => second.GetRequiredKeyedService<ISecurityCredentialControl>(EntraNativeAuthenticationTests.Profile));
        }
        await EntraNativeAuthenticationTests.StartAsync(legitimate);
        await legitimate.GetRequiredKeyedService<ISecurityCredentialControl>(EntraNativeAuthenticationTests.Profile).RequestSourceRecheckAsync();
        Assert.AreEqual(2, credential.Calls,
            "Failed foreign/duplicate claims must not track and dispose the actual prepared owner.");
        Assert.AreEqual(0, credential.Disposals);
    }

    [TestMethod]
    public async Task RetirementInvokesNativeCancellationWithoutHoldingThePreparationGate()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackAccess = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EntraPreparedProfile? captured = null;
        int acquisitions = 0;
        using var credential = new ControlledWorkloadCredential(async token =>
        {
            if (Interlocked.Increment(ref acquisitions) > 1)
            {
                using CancellationTokenRegistration callback = token.Register(() =>
                {
                    Task access = Task.Run(() =>
                    {
                        // Obtain the retained retirement task on another thread, without
                        // awaiting the actual owner that this callback is helping settle.
                        _ = captured!.RetireBootstrapAsync();
                        callbackAccess.TrySetResult();
                    });
                    Assert.IsTrue(access.Wait(TimeSpan.FromSeconds(5)),
                        "Native cancellation must not execute under the preparation gate.");
                });
                entered.TrySetResult();
                await release.Task;
            }
            return new AccessToken("prepared-token", DateTimeOffset.UtcNow.AddMinutes(5));
        });
        await using EntraPreparedProfile prepared = await EntraPreparedProfile.PrepareAsync(Policy("https://provider.invalid"),
            EntraNativeAuthenticationTests.Profile, Options(UnusedAssertion()), borrowedCredential: credential);
        captured = prepared;
        await using var bootstrap = new HttpRestClient(new RestClientOptions("cancellation-gate")
        {
            BaseUrl = "https://provider.invalid",
            SecurityBinding = prepared.Binding,
            Authenticate = prepared.AuthenticateAsync,
            LogCalls = false
        });
        Task<RestResult<ValueBody>> operation = bootstrap.Get<ValueBody>("/value");
        Task? retirement = null;
        Task? clientDrain = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            retirement = prepared.RetireBootstrapAsync();
            await callbackAccess.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(retirement.IsCompleted, "Gate access does not replace actual SDK settlement.");
            clientDrain = bootstrap.DisposeAsync().AsTask();
        }
        finally
        {
            release.TrySetResult();
            await operation;
            if (clientDrain is not null)
            {
                await clientDrain;
            }
            if (retirement is not null)
            {
                await retirement;
            }
        }
        Assert.AreEqual(2, credential.Calls);
        Assert.AreEqual(0, credential.Disposals);
    }

    [TestMethod]
    public async Task BootstrapRetirementAndFailedBuildOwnerCleanupWaitForActualHeldCredentialWork()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int acquisitions = 0;
        using var credential = new ControlledWorkloadCredential(async _ =>
        {
            if (Interlocked.Increment(ref acquisitions) > 1)
            {
                entered.TrySetResult();
                await release.Task;
            }
            return new AccessToken("prepared-token", DateTimeOffset.UtcNow.AddMinutes(5));
        });
        await using EntraPreparedProfile prepared = await EntraPreparedProfile.PrepareAsync(Policy("https://provider.invalid"),
            EntraNativeAuthenticationTests.Profile, Options(UnusedAssertion()), borrowedCredential: credential);
        await using var bootstrap = new HttpRestClient(new RestClientOptions("held-bootstrap")
        {
            BaseUrl = "https://provider.invalid",
            SecurityBinding = prepared.Binding,
            Authenticate = prepared.AuthenticateAsync,
            LogCalls = false
        });
        Task<RestResult<ValueBody>> operation = bootstrap.Get<ValueBody>("/value");
        Task? restDrain = null;
        Task? retirement = null;
        Task? ownerDrain = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            restDrain = bootstrap.DisposeAsync().AsTask();
            retirement = prepared.RetireBootstrapAsync();
            ownerDrain = prepared.DisposeAsync().AsTask();
            Assert.IsFalse(operation.IsCompleted);
            Assert.IsFalse(restDrain.IsCompleted);
            Assert.IsFalse(retirement.IsCompleted);
            Assert.IsFalse(ownerDrain.IsCompleted,
                "Failed-Build cleanup retains the actual SDK owner even when no final provider exists.");
            Assert.AreEqual(0, credential.Disposals);
        }
        finally
        {
            release.TrySetResult();
            RestResult<ValueBody> result = await operation;
            Assert.AreEqual(RestFailureKind.Stopping, result.Context.FailureKind);
            foreach (Task? draining in new[] { restDrain, retirement, ownerDrain })
            {
                if (draining is not null)
                {
                    await draining;
                }
            }
        }
        Assert.AreEqual(2, credential.Calls);
        Assert.AreEqual(0, credential.Disposals);
    }

    private static ServiceCollection Services(SecurityPolicySnapshot policy, EntraPreparedProfile prepared)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCisSecurity(policy);
        services.AddCisEntraSecurity(prepared);
        return services;
    }

    private static SecurityPolicySnapshot Policy(string origin, bool required = false)
    {
        LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
        var options = new SecurityOptions { PolicyVersion = "entra-prepared-fixture-v1" };
        options.Profiles.Add(new(EntraNativeAuthenticationTests.Profile, SecurityProfileKind.EntraWorkloadIdentity,
            SecurityProfileRoles.Outbound, outboundIdentity: new(local.Application, local.Environment,
                new(EntraNativeAuthenticationTests.Issuer, EntraNativeAuthenticationTests.ObjectId)),
            allowedOrigins: [origin], requiredForReadiness: required));
        return SecurityPolicySnapshot.Create(options);
    }

    private static EntraSecurityOptions Options(string assertion, HttpPipelineTransport? transport = null) => new()
    {
        TenantId = EntraNativeAuthenticationTests.Tenant,
        Outbound = new()
        {
            ClientId = EntraNativeAuthenticationTests.ClientId,
            ServicePrincipalObjectId = EntraNativeAuthenticationTests.ObjectId,
            FederatedSubject = "system:serviceaccount:country-demo:country-worker",
            FederatedTokenFile = assertion,
            Scope = "api://country-api/.default",
            Transport = transport
        }
    };

    private static string UnusedAssertion() => Path.Combine(Path.GetTempPath(), "unused-borrowed-assertion");

    private sealed record ValueBody(int Value);
}
