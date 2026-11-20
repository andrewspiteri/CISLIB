using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CIS.RestClient.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
public sealed class RestNativeKeyCleanupTests
{
    [TestMethod]
    public void RetainedExpiredRsaCertificateContextDoesNotLeaveItsNewlyOwnedUserKeyBacking()
    {
        var created = new List<RestHttpNativeKeyMetadata>();
        var removed = new List<RestHttpNativeKeyMetadata>();
        using RestHttpNativeKeyScope scope = RestSecurityNativeKeyCustody.CreateScope(1, (metadata, transition) =>
        {
            (transition == RestHttpNativeKeyTransition.Created ? created : removed).Add(metadata);
        });
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Owned expired RSA context " + Guid.NewGuid().ToString("N"),
            key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        using var owner = new RestHttpNativeCertificate(request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddMinutes(-1)));
        owner.PrepareForTls(scope);
        using var retainedContext = new X509Certificate2(owner.Certificate);
        string thumbprint = retainedContext.Thumbprint;
        if (OperatingSystem.IsWindows())
        {
            Assert.IsNotNull(owner.WindowsKey);
            Assert.AreEqual(1, scope.LiveKeyCount);
            Assert.IsTrue(File.Exists(owner.WindowsKey.FilePath));
        }
        else
        {
            Assert.IsNull(owner.WindowsKey);
            Assert.AreEqual(0, scope.LiveKeyCount);
        }

        // No TLS work remains. A native certificate context can outlive the disposed owning certificate,
        // as native credential caches do; it must not retain our owned user-key backing indefinitely.
        owner.Dispose();
        Assert.AreEqual(thumbprint, retainedContext.Thumbprint);
        Assert.AreEqual(0, scope.LiveKeyCount);
        Assert.AreEqual(OperatingSystem.IsWindows() ? 1 : 0, created.Count);
        CollectionAssert.AreEqual(created, removed);
        foreach (RestHttpNativeKeyMetadata metadata in created)
        {
            Assert.IsFalse(File.Exists(metadata.FilePath), "Only the exact newly owned backing is checked.");
        }
    }
}
