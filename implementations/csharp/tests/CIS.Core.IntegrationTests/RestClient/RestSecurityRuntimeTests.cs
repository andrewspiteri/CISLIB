using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Security.Cryptography.X509Certificates;
using CIS.RestClient;
using CIS.RestClient.Hosting;
using CIS.RestClient.Http;
using CIS.RestClient.Resilience;
using CIS.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
public sealed class RestSecurityRuntimeTests
{
    [TestMethod]
    public async Task ActualPollyRetrySelectsTheNewNativeGenerationAfterItsDelay()
    {
        var received = new ConcurrentQueue<string>();
        var firstResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        await using RestSecurityTlsFixture server = await RestSecurityTlsFixture.StartAsync(async context =>
        {
            string credential = context.Connection.ClientCertificate!.Thumbprint;
            received.Enqueue(credential);
            context.Response.StatusCode = Interlocked.Increment(ref calls) == 1 ? 503 : 200;
            await context.Response.WriteAsync(credential, context.RequestAborted);
            firstResponse.TrySetResult();
        });
        using var clock = new ResilienceClock();
        clock.SetUtcNow(DateTimeOffset.UtcNow);
        RestSecurityBinding binding = server.Binding();
        RestHttpTransport? transport = null;
        RestHttpCertificateMaterial? firstMaterial = null;
        string? firstThumbprint = null;
        string? secondThumbprint = null;
        var policy = new RestRetryPolicy("rotation", 1, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), [503],
            backoff: RestRetryBackoff.Constant, useJitter: false);
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddCisRestClientResilience(policy);
        builder.Services.AddCisSecurityBoundRestClient(new("certificate")
        {
            BaseUrl = server.Address,
            ResponseFormat = RestResponseFormat.Text,
            RetryPolicy = RestRetrySelection.Named("rotation"),
            LogCalls = false
        }, binding, borrowedTransport: (root, selected) => transport = RestHttpTransport.CreateSecurityBound(
            selected, root.GetRequiredService<IMeterFactory>(), clock: clock,
            serverTrust: new([server.ServerAuthority], X509RevocationMode.NoCheck)));
        Task<RestResult<string>>? operation = null;
        try
        {
            await using WebApplication host = builder.Build();
            IRestClient client = host.Services.GetRequiredKeyedService<IRestClient>("certificate");
            Assert.IsNotNull(transport);
            await transport.TryAdoptCertificateAsync(_ =>
            {
                X509Certificate2 certificate = server.ClientLeaf("first");
                firstThumbprint = certificate.Thumbprint;
                firstMaterial = server.PrepareMaterial(certificate);
                return ValueTask.FromResult(firstMaterial);
            });
            operation = client.Get<string>("");
            Task first = await Task.WhenAny(firstResponse.Task, operation).WaitAsync(ResilienceTestSupport.Deadline);
            if (ReferenceEquals(first, operation))
            {
                RestResult<string> early = await operation;
                Assert.Fail("The operation settled before the first server response: " + early.Context.FailureKind
                    + "/" + early.ErrorCode + ".");
            }
            ResilienceClock.Wait delay = await RuntimeTestSupport.RetryWaitAsync(clock, TimeSpan.FromSeconds(2));
            Assert.IsFalse(operation.IsCompleted);
            await transport.TryAdoptCertificateAsync(_ =>
            {
                X509Certificate2 certificate = server.ClientLeaf("second");
                secondThumbprint = certificate.Thumbprint;
                return ValueTask.FromResult(server.PrepareMaterial(certificate));
            });
            Assert.IsTrue(firstMaterial!.IsDisposed, "No old generation lease may survive the REST retry delay.");
            clock.Advance(TimeSpan.FromSeconds(2));
            RestResult<string> result = await operation.WaitAsync(ResilienceTestSupport.Deadline);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(secondThumbprint, result.Value);
            Assert.AreEqual(2, result.Context.AttemptCount);
            CollectionAssert.AreEqual(new[] { firstThumbprint, secondThumbprint }, received.ToArray());
            await delay.Disposed.Task.WaitAsync(ResilienceTestSupport.Deadline);
        }
        finally
        {
            clock.Advance(TimeSpan.FromSeconds(30));
            if (operation is not null)
            {
                await RuntimeTestSupport.SettleAsync(operation);
            }
            if (transport is not null)
            {
                await transport.DisposeAsync();
            }
        }
    }

    [TestMethod]
    public async Task BindingAndAuthenticationFactoriesAreDeferredOnceAndUseCapturedRegistrationInputs()
    {
        int bindingFactories = 0;
        int authenticationFactories = 0;
        IServiceProvider? observedRoot = null;
        RootWitness? observedWitness = null;
        var failure = new SecurityFailure(SecurityFailureKind.CredentialUnavailable, "Fixture provider is unavailable.");
        var services = new ServiceCollection();
        services.AddSingleton(_ => new RootWitness());
        var options = new RestClientOptions("deferred") { BaseUrl = "https://service.invalid/", LogCalls = false };
        options.Headers.Set("X-Captured", "initial");
        services.AddCisSecurityBoundRestClient(options, root =>
        {
            bindingFactories++;
            observedRoot = root;
            observedWitness = root.GetRequiredService<RootWitness>();
            return Binding("https://service.invalid");
        }, root =>
        {
            authenticationFactories++;
            Assert.AreSame(observedRoot, root);
            Assert.AreSame(observedWitness, root.GetRequiredService<RootWitness>());
            return (context, _) =>
            {
                Assert.AreEqual("initial", context.Headers.Snapshot()["X-Captured"].Single());
                context.FailWithSecurity(failure);
                return Task.CompletedTask;
            };
        });
        options.BaseUrl = "https://changed.invalid/";
        options.Headers.Set("X-Captured", "changed");
        Assert.AreEqual(0, bindingFactories);
        Assert.AreEqual(0, authenticationFactories);
        await using ServiceProvider root = services.BuildServiceProvider();
        IRestClient client = root.GetRequiredKeyedService<IRestClient>("deferred");
        RestResult<RestNoValue> result = await client.Get<RestNoValue>("");
        Assert.AreSame(root.GetRequiredService<RootWitness>(), observedWitness);
        Assert.AreSame(client, root.GetRequiredKeyedService<IRestClient>("deferred"));
        Assert.AreSame(failure, result.Context.SecurityFailure);
        Assert.AreEqual(0, result.Context.AttemptCount);
        Assert.AreEqual(1, bindingFactories);
        Assert.AreEqual(1, authenticationFactories);
    }

    [TestMethod]
    [DataRow("correct")]
    [DataRow("wrong-root")]
    [DataRow("wrong-name")]
    public async Task CapturedPrivateServerTrustPreservesNativeChainAndHostnameValidation(string selection)
    {
        await using RestSecurityTlsFixture server = await RestSecurityTlsFixture.StartAsync(
            context => context.Response.WriteAsync("value", context.RequestAborted),
            requireClientCertificate: false, wrongServerName: selection == "wrong-name");
        var inputRoots = new List<X509Certificate2>
        {
            selection == "wrong-root" ? server.ClientAuthority : server.ServerAuthority
        };
        var serverTrust = new RestHttpServerTrust(inputRoots, X509RevocationMode.NoCheck);
        inputRoots[0] = server.ClientAuthority;
        RestSecurityBinding binding = Binding(server.Address);
        await using RestHttpTransport transport = RestHttpTransport.CreateSecurityBound(binding, serverTrust: serverTrust);
        await using var client = new HttpRestClient(new("private-server")
        {
            BaseUrl = server.Address,
            SecurityBinding = binding,
            Authenticate = (_, _) => Task.CompletedTask,
            LogCalls = false
        }, transport);
        RestResult<RestNoValue> result = await client.Get<RestNoValue>("").WaitAsync(ResilienceTestSupport.Deadline);
        if (selection == "correct")
        {
            Assert.IsTrue(result.Success, "Mutating the input list cannot change captured server trust.");
        }
        else
        {
            Assert.IsFalse(result.Success);
            Assert.AreEqual(RestFailureKind.Transport, result.Context.FailureKind);
            Assert.IsNull(result.Context.HttpStatusCode);
            Assert.AreEqual(SecurityFailureKind.CauseUnknown, result.Context.SecurityFailure?.Kind);
            Assert.AreEqual(SecurityFailureSource.Unknown, result.Context.SecurityFailure?.Source);
        }
    }

    [TestMethod]
    [DataRow(401)]
    [DataRow(403)]
    public async Task ArbitraryRemoteAuthenticationStatusesKeepActualHttpFactsWithoutInventingCapabilityDenial(int status)
    {
        await using RestSecurityTlsFixture server = await RestSecurityTlsFixture.StartAsync(context =>
        {
            context.Response.StatusCode = status;
            return Task.CompletedTask;
        }, requireClientCertificate: false);
        RestSecurityBinding binding = Binding(server.Address);
        await using RestHttpTransport transport = RestHttpTransport.CreateSecurityBound(binding,
            serverTrust: new([server.ServerAuthority], X509RevocationMode.NoCheck));
        await using var client = new HttpRestClient(new("remote-status")
        {
            BaseUrl = server.Address,
            SecurityBinding = binding,
            Authenticate = (_, _) => Task.CompletedTask,
            LogCalls = false
        }, transport);
        RestResult<RestNoValue> result = await client.Get<RestNoValue>("");
        Assert.AreEqual(status, result.Context.HttpStatusCode);
        Assert.AreEqual("http." + status, result.ErrorCode);
        Assert.AreEqual(RestFailureKind.Http, result.Context.FailureKind);
        Assert.AreEqual(SecurityFailureKind.CauseUnknown, result.Context.SecurityFailure?.Kind);
        Assert.AreEqual(SecurityFailureSource.Unknown, result.Context.SecurityFailure?.Source);
        Assert.AreEqual(status, result.Context.SecurityFailure?.HttpStatusCode);
        Assert.AreEqual(1, result.Context.AttemptCount);
    }

    private static RestSecurityBinding Binding(string origin) => new("fixture",
        new SecuritySubject("fixture-issuer", "fixture-client"), "fixture-policy",
        RestSecurityCredentialKind.RequestAuthentication, [origin]);

    private sealed class RootWitness
    {
    }
}
