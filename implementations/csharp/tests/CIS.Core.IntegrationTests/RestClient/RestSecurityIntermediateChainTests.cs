using System.Security.Cryptography.X509Certificates;
using CIS.RestClient;
using CIS.RestClient.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
[DoNotParallelize]
public sealed class RestSecurityIntermediateChainTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task IntermediateIssuedNativeClientRequiresAnExplicitIssuerAtTheWindowsPeer(bool peerHasIssuer)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("This native transmission contract qualifies Windows and Linux.");
        }

        using var cancellation = new CancellationTokenSource(ResilienceTestSupport.Deadline);
        await using RestIntermediateChainFixture peer = await RestIntermediateChainFixture.StartAsync(peerHasIssuer);
        await using RestHttpTransport transport = RestHttpTransport.CreateSecurityBound(peer.Binding(),
            serverTrust: new([peer.ServerAuthority], X509RevocationMode.NoCheck));
        Assert.AreEqual(RestHttpCertificateAdoption.Adopted,
            await transport.TryAdoptCertificateAsync(_ => ValueTask.FromResult(peer.PrepareMaterial())));

        using var request = new HttpRequestMessage(HttpMethod.Get, peer.Address);
        bool expectedSuccess = peerHasIssuer || OperatingSystem.IsLinux();
        if (expectedSuccess)
        {
            await using IRestResponse response = await transport.SendAsync(request, static () => { }, cancellation.Token);
            Assert.AreEqual(200, response.HttpStatusCode);
            using var reader = new StreamReader(response.Body);
            Assert.AreEqual("intermediate-issued client accepted", await reader.ReadToEndAsync(cancellation.Token));
        }
        else
        {
            RestTransportException failure = await Assert.ThrowsExactlyAsync<RestTransportException>(async () =>
            {
                await using IRestResponse response = await transport.SendAsync(request, static () => { }, cancellation.Token);
            });
            Assert.IsInstanceOfType<HttpRequestException>(failure.InnerException);
        }

        RestIntermediateChainObservation observation = await peer.Observation.WaitAsync(ResilienceTestSupport.Deadline);
        Assert.AreEqual(expectedSuccess, observation.Accepted);
        Assert.AreEqual(expectedSuccess, observation.ExpectedIssuerInChain);
        Assert.AreEqual(expectedSuccess ? 1 : 0, peer.Requests);
        if (!peerHasIssuer)
        {
            Assert.AreEqual(OperatingSystem.IsLinux(), observation.IssuerInNativeExtraStore,
                "The root-only peer retains the actual native TLS chain; it is not rebuilt from the leaf alone.");
        }
    }
}
