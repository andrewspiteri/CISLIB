using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using CIS.CommonTesting;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
public sealed class RestSecurityGenerationTests
{
    [TestMethod]
    public async Task RealCertificateRotationRetainsTheOldResponseAndDefersBeforeConstructingAThirdKey()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource(ResilienceTestSupport.Deadline);
        await using RestSecurityTlsFixture server = await RestSecurityTlsFixture.StartAsync(async context =>
        {
            string thumbprint = context.Connection.ClientCertificate!.Thumbprint;
            await context.Response.WriteAsync(thumbprint, context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            if (context.Request.Path == "/hold")
            {
                try
                {
                    await release.Task.WaitAsync(context.RequestAborted);
                }
                finally { finished.TrySetResult(); }
            }
        });
        RestSecurityBinding binding = server.Binding();
        await using RestHttpTransport transport = RestHttpTransport.CreateSecurityBound(binding,
            serverTrust: new([server.ServerAuthority], X509RevocationMode.NoCheck));
        RestHttpCertificateMaterial? firstMaterial = null;
        RestHttpCertificateMaterial? secondMaterial = null;
        string? firstThumbprint = null;
        string? secondThumbprint = null;
        IRestResponse? held = null;
        try
        {
            Assert.AreEqual(RestHttpCertificateAdoption.Adopted, await transport.TryAdoptCertificateAsync(_ =>
            {
                X509Certificate2 certificate = server.ClientLeaf("first");
                firstThumbprint = certificate.Thumbprint;
                firstMaterial = server.PrepareMaterial(certificate);
                return ValueTask.FromResult(firstMaterial);
            }));
            using var request = new HttpRequestMessage(HttpMethod.Get, server.Address + "/hold");
            held = await transport.SendAsync(request, static () => { }, cancellation.Token).AsTask()
                .WaitAsync(ResilienceTestSupport.Deadline);

            Assert.AreEqual(RestHttpCertificateAdoption.Adopted, await transport.TryAdoptCertificateAsync(_ =>
            {
                X509Certificate2 certificate = server.ClientLeaf("second");
                secondThumbprint = certificate.Thumbprint;
                secondMaterial = server.PrepareMaterial(certificate);
                return ValueTask.FromResult(secondMaterial);
            }));
            Assert.AreEqual(2, transport.LiveGenerationCount);
            Assert.IsFalse(firstMaterial!.IsDisposed, "An actual response still owns the retired key.");
            int thirdFactories = 0;
            RestHttpCertificateAdoption deferred = await transport.TryAdoptCertificateAsync(_ =>
            {
                thirdFactories++;
                return ValueTask.FromResult(server.PrepareMaterial(server.ClientLeaf("third")));
            });
            Assert.AreEqual(RestHttpCertificateAdoption.Deferred, deferred);
            Assert.AreEqual(0, thirdFactories, "Capacity must be reserved before another key is created.");

            using (var secondRequest = new HttpRequestMessage(HttpMethod.Get, server.Address + "/current"))
            {
                await using IRestResponse second = await transport.SendAsync(secondRequest, static () => { }, cancellation.Token);
                using var reader = new StreamReader(second.Body);
                Assert.AreEqual(secondThumbprint, await reader.ReadToEndAsync());
            }
            release.TrySetResult();
            using (var reader = new StreamReader(held.Body, leaveOpen: true))
            {
                Assert.AreEqual(firstThumbprint, await reader.ReadToEndAsync());
            }
            await held.DisposeAsync();
            Assert.IsTrue(firstMaterial.IsDisposed);
            Assert.IsFalse(secondMaterial!.IsDisposed);
            Assert.AreEqual(1, transport.LiveGenerationCount);
            Assert.AreEqual(RestHttpCertificateAdoption.Adopted, await transport.TryAdoptCertificateAsync(_ =>
                ValueTask.FromResult(server.PrepareMaterial(server.ClientLeaf("third")))));
            Assert.IsTrue(secondMaterial.IsDisposed);
            Assert.AreEqual(1, transport.LiveGenerationCount);
        }
        finally
        {
            release.TrySetResult();
            if (held is not null)
            {
                await held.DisposeAsync();
                await finished.Task.WaitAsync(ResilienceTestSupport.Deadline);
            }
        }
    }

    [TestMethod]
    public async Task ExistingNativeResponseCanSettleAfterExpiryButANewExchangeReceivesTypedLocalExpiryWithoutDispatch()
    {
        using var cancellation = new CancellationTokenSource(ResilienceTestSupport.Deadline);
        int requests = 0;
        await using RestSecurityTlsFixture server = await RestSecurityTlsFixture.StartAsync(async context =>
        {
            Interlocked.Increment(ref requests);
            await context.Response.WriteAsync("settled", context.RequestAborted);
        });
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        RestSecurityBinding binding = server.Binding();
        await using RestHttpTransport transport = RestHttpTransport.CreateSecurityBound(binding, clock: clock,
            serverTrust: new([server.ServerAuthority], X509RevocationMode.NoCheck));
        await transport.TryAdoptCertificateAsync(_ => ValueTask.FromResult(
            server.PrepareMaterial(server.ClientLeaf("expiring"))));
        using var firstRequest = new HttpRequestMessage(HttpMethod.Get, server.Address);
        await using IRestResponse response = await transport.SendAsync(firstRequest, static () => { }, cancellation.Token);
        clock.SetUtcNow(transport.CertificateExpiresAtUtc!.Value.AddSeconds(1));
        var options = new RestClientOptions("certificate")
        {
            BaseUrl = server.Address,
            SecurityBinding = binding,
            LogCalls = false
        };
        await using var client = new HttpRestClient(options, transport, clock);
        RestResult<RestNoValue> late = await client.Get<RestNoValue>("");
        Assert.AreEqual(RestFailureKind.Authentication, late.Context.FailureKind);
        Assert.AreEqual(SecurityFailureKind.CredentialExpired, late.Context.SecurityFailure?.Kind);
        Assert.AreEqual(0, late.Context.AttemptCount);
        Assert.IsNull(late.Context.HttpStatusCode);
        Assert.AreEqual(1, requests);
        using var reader = new StreamReader(response.Body);
        Assert.AreEqual("settled", await reader.ReadToEndAsync());
    }

    [TestMethod]
    public async Task ConstructionReservationCoalescesConcurrentRequestsAndDisposeJoinsTheRealFactory()
    {
        RestSecurityBinding binding = PrimitiveBinding(RestSecurityCredentialKind.ClientCertificate);
        RestHttpTransport transport = RestHttpTransport.CreateSecurityBound(binding);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new InvalidOperationException("Fixture material factory fault.");
        Task<RestHttpCertificateAdoption> adoption = transport.TryAdoptCertificateAsync(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
            throw original;
        }).AsTask();
        Task? disposal = null;
        try
        {
            await entered.Task.WaitAsync(ResilienceTestSupport.Deadline);
            int extraFactories = 0;
            Assert.AreEqual(RestHttpCertificateAdoption.Deferred, await transport.TryAdoptCertificateAsync(_ =>
            {
                extraFactories++;
                throw new AssertFailedException("The occupied construction slot must prevent another factory.");
            }));
            Assert.AreEqual(0, extraFactories);
            disposal = transport.DisposeAsync().AsTask();
            Assert.IsFalse(disposal.IsCompleted, "Closing admission is not settlement of the held material factory.");
            release.TrySetResult();
            InvalidOperationException observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => adoption);
            Assert.AreSame(original, observed);
            await disposal.WaitAsync(ResilienceTestSupport.Deadline);
        }
        finally
        {
            release.TrySetResult();
            try
            {
                await adoption;
            }
            catch (InvalidOperationException error) when (ReferenceEquals(error, original)) { }
            await (disposal ?? transport.DisposeAsync().AsTask());
        }
    }

    [TestMethod]
    public async Task ConcurrentNativeSelectionAndRetirementNeverDisposesASelectedGeneration()
    {
        using var cancellation = new CancellationTokenSource(ResilienceTestSupport.Deadline);
        var seen = new ConcurrentQueue<string>();
        await using RestSecurityTlsFixture server = await RestSecurityTlsFixture.StartAsync(async context =>
        {
            string credential = context.Connection.ClientCertificate!.Thumbprint;
            seen.Enqueue(credential);
            await context.Response.WriteAsync(credential, context.RequestAborted);
        });
        RestSecurityBinding binding = server.Binding();
        await using RestHttpTransport transport = RestHttpTransport.CreateSecurityBound(binding,
            serverTrust: new([server.ServerAuthority], X509RevocationMode.NoCheck));
        await transport.TryAdoptCertificateAsync(_ => ValueTask.FromResult(server.PrepareMaterial(server.ClientLeaf("one"))));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string>[] sends = Enumerable.Range(0, 8).Select(async _ =>
        {
            await start.Task;
            using var request = new HttpRequestMessage(HttpMethod.Get, server.Address);
            await using IRestResponse response = await transport.SendAsync(request, static () => { }, cancellation.Token);
            using var reader = new StreamReader(response.Body);
            return await reader.ReadToEndAsync();
        }).ToArray();
        start.TrySetResult();
        try
        {
            await transport.TryAdoptCertificateAsync(_ => ValueTask.FromResult(
                server.PrepareMaterial(server.ClientLeaf("two"))));
            string[] results = await Task.WhenAll(sends).WaitAsync(ResilienceTestSupport.Deadline);
            Assert.AreEqual(8, seen.Count);
            Assert.IsTrue(results.All(value => seen.Contains(value)));
            Assert.IsTrue(transport.LiveGenerationCount <= 2);
        }
        finally
        {
            cancellation.Cancel();
            await RuntimeTestSupport.SettleAsync(Task.WhenAll(sends));
        }
    }

    [TestMethod]
    public async Task RequestAuthenticationCannotAdoptATlsIdentityAndNativeBoundaryRejectsAnUntrustedOrigin()
    {
        using var deadline = new CancellationTokenSource(ResilienceTestSupport.Deadline);
        await using RestHttpTransport transport = RestHttpTransport.CreateSecurityBound(
            PrimitiveBinding(RestSecurityCredentialKind.RequestAuthentication));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => transport.TryAdoptCertificateAsync(
            _ => throw new AssertFailedException("A request-authenticated transport must not acquire a client key.")).AsTask());
        int dispatches = 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://127.0.0.1:1/");
        await Assert.ThrowsExactlyAsync<RestSecurityPolicyException>(() => transport.SendAsync(request,
            () => dispatches++, deadline.Token).AsTask());
        Assert.AreEqual(0, dispatches);
    }

    [TestMethod]
    public async Task CanceledAndExpiredCandidatesAreDisposedWithoutReplacingTheLastValidNativeIdentity()
    {
        using var deadline = new CancellationTokenSource(ResilienceTestSupport.Deadline);
        await using RestSecurityTlsFixture server = await RestSecurityTlsFixture.StartAsync(context =>
            context.Response.WriteAsync(context.Connection.ClientCertificate!.Thumbprint, context.RequestAborted));
        RestSecurityBinding binding = server.Binding();
        await using RestHttpTransport transport = RestHttpTransport.CreateSecurityBound(binding,
            serverTrust: new([server.ServerAuthority], X509RevocationMode.NoCheck));
        RestHttpCertificateMaterial? current = null;
        string? currentThumbprint = null;
        await transport.TryAdoptCertificateAsync(_ =>
        {
            X509Certificate2 certificate = server.ClientLeaf("current");
            currentThumbprint = certificate.Thumbprint;
            current = server.PrepareMaterial(certificate);
            return ValueTask.FromResult(current);
        });
        using var canceled = new CancellationTokenSource();
        RestHttpCertificateMaterial? canceledMaterial = null;
        await Assert.ThrowsAsync<OperationCanceledException>(() => transport.TryAdoptCertificateAsync(_ =>
        {
            canceledMaterial = server.PrepareMaterial(server.ClientLeaf("canceled"));
            canceled.Cancel();
            return ValueTask.FromResult(canceledMaterial);
        }, canceled.Token).AsTask());
        Assert.IsTrue(canceledMaterial!.IsDisposed);

        RestHttpCertificateMaterial? expired = null;
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => transport.TryAdoptCertificateAsync(_ =>
        {
            expired = server.PrepareMaterial(server.ClientLeaf("expired", DateTimeOffset.UtcNow.AddSeconds(-10)));
            return ValueTask.FromResult(expired);
        }).AsTask());
        Assert.IsTrue(expired!.IsDisposed);
        Assert.IsFalse(current!.IsDisposed);
        Assert.AreEqual(1, transport.LiveGenerationCount);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.Address);
        await using IRestResponse response = await transport.SendAsync(request, static () => { }, deadline.Token);
        using var reader = new StreamReader(response.Body);
        Assert.AreEqual(currentThumbprint, await reader.ReadToEndAsync());
    }

    [TestMethod]
    public async Task DisposingTransferredMaterialIsAnOwnershipFaultRatherThanAnOrdinaryCredentialRejection()
    {
        await using RestSecurityTlsFixture server = await RestSecurityTlsFixture.StartAsync(_ =>
            throw new AssertFailedException("The ownership violation must fail before native dispatch."));
        RestSecurityBinding binding = server.Binding();
        await using RestHttpTransport transport = RestHttpTransport.CreateSecurityBound(binding,
            serverTrust: new([server.ServerAuthority], X509RevocationMode.NoCheck));
        RestHttpCertificateMaterial? transferred = null;
        await transport.TryAdoptCertificateAsync(_ =>
        {
            transferred = server.PrepareMaterial(server.ClientLeaf("transferred"));
            return ValueTask.FromResult(transferred);
        });
        transferred!.Dispose();
        await using var client = new HttpRestClient(new("ownership")
        {
            BaseUrl = server.Address,
            SecurityBinding = binding,
            LogCalls = false
        }, transport);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.Get<RestNoValue>(""));
    }

    private static RestSecurityBinding PrimitiveBinding(RestSecurityCredentialKind kind) => new("fixture",
        new SecuritySubject("fixture-issuer", "fixture-client"), "fixture-policy", kind, ["https://service.invalid"]);
}
