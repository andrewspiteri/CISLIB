using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CIS.RestClient.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
public sealed class RestNativeCertificateCustodyTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void CreatedCustodyReentryOrFailureSettlesTheNewKeyWithoutPublishingMaterial(bool disposeDuringCreation)
    {
        var created = new List<RestHttpNativeKeyMetadata>();
        var removed = new List<RestHttpNativeKeyMetadata>();
        var original = new InvalidOperationException("Fixture custody sink failure after its private journal write.");
        RestHttpNativeCertificate? capturedOwner = null;
        using RestHttpNativeKeyScope scope = RestSecurityNativeKeyCustody.CreateScope(1, (metadata, transition) =>
        {
            if (transition == RestHttpNativeKeyTransition.Removed)
            {
                removed.Add(metadata);
                return;
            }
            created.Add(metadata);
            if (disposeDuringCreation)
            {
                capturedOwner!.Dispose();
            }
            else
            {
                throw original;
            }
        });
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Owned custody fixture " + Guid.NewGuid().ToString("N"),
            key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        using var owner = new RestHttpNativeCertificate(request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10)));
        capturedOwner = owner;

        if (!OperatingSystem.IsWindows())
        {
            owner.PrepareForTls(scope);
            Assert.IsNull(owner.WindowsKey);
            Assert.AreEqual(0, scope.LiveKeyCount);
            Assert.AreEqual(0, created.Count);
            Assert.AreEqual(0, removed.Count);
            using RestHttpCertificateMaterial memoryMaterial = RestHttpCertificateMaterial.FromNativeCertificate(owner);
            Assert.IsFalse(memoryMaterial.IsDisposed, "Native memory ownership remains usable without any Windows key import.");
            return;
        }

        if (disposeDuringCreation)
        {
            Assert.ThrowsExactly<ObjectDisposedException>(() => owner.PrepareForTls(scope));
            Assert.ThrowsExactly<ObjectDisposedException>(() => _ = owner.Certificate);
        }
        else
        {
            InvalidOperationException observed = Assert.ThrowsExactly<InvalidOperationException>(() => owner.PrepareForTls(scope));
            Assert.AreSame(original, observed);
        }
        Assert.IsNull(owner.WindowsKey, "The failed native owner must not publish usable material.");
        Assert.ThrowsExactly<InvalidOperationException>(() => RestHttpCertificateMaterial.FromNativeCertificate(owner));
        Assert.AreEqual(1, created.Count);
        CollectionAssert.AreEqual(created, removed);
        Assert.AreEqual(0, scope.LiveKeyCount);
        Assert.IsFalse(File.Exists(created[0].FilePath), "Only the exact newly owned backing is checked.");
    }
}
