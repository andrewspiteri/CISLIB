using CIS.RestClient;
using CIS.RestClient.Http;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
public sealed class RestNativeTlsKeyProbeTests
{
    [TestMethod]
    public async Task TwoOwnedLeavesCompleteNativeMutualTlsAndRemoveTheirExactTemporaryKeyBacking()
    {
        var created = new List<RestHttpNativeKeyMetadata>();
        var removed = new List<RestHttpNativeKeyMetadata>();
        using RestHttpNativeKeyScope scope = RestSecurityNativeKeyCustody.CreateScope(2, (metadata, transition) =>
        {
            (transition == RestHttpNativeKeyTransition.Created ? created : removed).Add(metadata);
        });
        RestSecurityTlsFixture server = await RestSecurityTlsFixture.StartAsync(
            context => context.Response.WriteAsync("native TLS proved", context.RequestAborted), keyScope: scope);
        RestHttpTransport transport = RestHttpTransport.CreateSecurityBound(server.Binding(),
            serverTrust: new([server.ServerAuthority], X509RevocationMode.NoCheck));
        try
        {
            await transport.TryAdoptCertificateAsync(_ =>
                ValueTask.FromResult(server.PrepareMaterial(server.ClientLeaf("two-leaf-probe"))));
            Assert.AreEqual(OperatingSystem.IsWindows() ? 2 : 0, scope.LiveKeyCount);
            using var request = new HttpRequestMessage(HttpMethod.Get, server.Address);
            using var cancellation = new CancellationTokenSource(ResilienceTestSupport.Deadline);
            await using IRestResponse response = await transport.SendAsync(request, static () => { }, cancellation.Token);
            Assert.AreEqual(200, response.HttpStatusCode);
            using var reader = new StreamReader(response.Body);
            Assert.AreEqual("native TLS proved", await reader.ReadToEndAsync(cancellation.Token));
        }
        finally
        {
            try
            {
                await transport.DisposeAsync();
            }
            finally { await server.DisposeAsync(); }
        }
        Assert.AreEqual(0, scope.LiveKeyCount);
        Assert.AreEqual(OperatingSystem.IsWindows() ? 2 : 0, created.Count);
        CollectionAssert.AreEquivalent(created, removed);
        foreach (RestHttpNativeKeyMetadata metadata in created)
        {
            Assert.IsFalse(File.Exists(metadata.FilePath), "Only the exact newly owned backing is checked.");
        }
    }
}
