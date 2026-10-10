using System.Reflection;
using System.Security;
using CIS.Configuration;
using CIS.Tests.UnitTests.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ApplicationIdentityFailureTests
{
    [TestMethod]
    [DataRow("entry", null, "unknown")]
    [DataRow("name", null, "3.4.5-rc.2+sha.AbC123")]
    [DataRow("information", "Fixture.App", "3.0.0.0")]
    [DataRow("version", "Fixture.App", "unknown")]
    public void UnavailableFactsDoNotDiscardOtherMetadata(string field, string? expectedName, string expectedVersion)
    {
        var metadata = new AssemblyMetadataStub
        {
            FailingField = field,
            InformationalVersion = field == "version" ? null : "3.4.5-rc.2+sha.AbC123"
        };

        ApplicationIdentity identity = ApplicationIdentityDiscovery.Discover(new ApplicationIdentityInput(), metadata);

        Assert.AreEqual(expectedName, identity.ApplicationName);
        Assert.AreEqual(expectedVersion, identity.BuildVersion);
    }

    [TestMethod]
    [DataRow("NotSupported")]
    [DataRow("InvalidOperation")]
    [DataRow("IO")]
    [DataRow("Access")]
    [DataRow("Security")]
    [DataRow("Attribute")]
    [DataRow("Ambiguous")]
    [DataRow("TypeLoad")]
    [DataRow("ReflectionLoad")]
    public void ExpectedDiscoveryFailuresUseTheAssemblyVersionFallback(string kind)
    {
        Exception failure = kind switch
        {
            "NotSupported" => new NotSupportedException(),
            "InvalidOperation" => new InvalidOperationException(),
            "IO" => new IOException(),
            "Access" => new UnauthorizedAccessException(),
            "Security" => new SecurityException(),
            "Attribute" => new CustomAttributeFormatException(),
            "Ambiguous" => new AmbiguousMatchException(),
            "TypeLoad" => new TypeLoadException(),
            _ => new ReflectionTypeLoadException([], [])
        };
        var metadata = new AssemblyMetadataStub { FailingField = "information", Failure = failure };
        var input = new ApplicationIdentityInput { ApplicationAssembly = typeof(ApplicationIdentityFailureTests).Assembly };

        ApplicationIdentity identity = ApplicationIdentityDiscovery.Discover(input, metadata);

        Assert.AreEqual("Fixture.App", identity.ApplicationName);
        Assert.AreEqual("3.0.0.0", identity.BuildVersion);
        Assert.IsFalse(metadata.Reads.Contains("entry"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnexpectedFaultsAndCancellationPropagate(bool cancellation)
    {
        Exception failure = cancellation ? new OperationCanceledException() : new ArgumentException();
        var metadata = new AssemblyMetadataStub { FailingField = "information", Failure = failure };

        Exception actual = Assert.Throws<Exception>(() => ApplicationIdentityDiscovery.Discover(new ApplicationIdentityInput(), metadata));

        Assert.AreSame(failure, actual);
    }
}
