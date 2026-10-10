using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using CIS.Configuration;
using CIS.Tests.UnitTests.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ApplicationIdentityDiscoveryTests
{
    [TestMethod]
    [DataRow(null, null, "Fixture.App", "3.4.5-rc.2+sha.AbC123")]
    [DataRow("", "", "Fixture.App", "3.4.5-rc.2+sha.AbC123")]
    [DataRow(" \t", "\u2003", "Fixture.App", "3.4.5-rc.2+sha.AbC123")]
    [DataRow(" Explicit.App ", null, "Explicit.App", "3.4.5-rc.2+sha.AbC123")]
    [DataRow(null, " 4.5.6-preview.3+Build.MixedCase ", "Fixture.App", "4.5.6-preview.3+Build.MixedCase")]
    [DataRow(" Explicit.App ", " 4.5.6-preview.3+Build.MixedCase ", "Explicit.App", "4.5.6-preview.3+Build.MixedCase")]
    public void SuppliedMetadataPrecedesTheSelectedApplicationAssembly(
        string? suppliedName, string? suppliedVersion, string expectedName, string expectedVersion)
    {
        Assembly application = typeof(ApplicationIdentityDiscoveryTests).Assembly;
        var metadata = new AssemblyMetadataStub { EntryAssembly = typeof(string).Assembly };
        var input = new ApplicationIdentityInput
        {
            ApplicationAssembly = application,
            ApplicationName = suppliedName,
            BuildVersion = suppliedVersion
        };

        ApplicationIdentity identity = ApplicationIdentityDiscovery.Discover(input, metadata);

        Assert.AreEqual(expectedName, identity.ApplicationName);
        Assert.AreEqual(expectedVersion, identity.BuildVersion);
        Assert.IsFalse(metadata.Reads.Contains("entry"));
        Assert.IsTrue(metadata.ObservedAssemblies.All(assembly => assembly == application));
    }

    [TestMethod]
    [DataRow("3.4.5-rc.2+sha.AbC123", "3.0.0.0", "3.4.5-rc.2+sha.AbC123")]
    [DataRow(" 3.4.5-rc.2+sha.AbC123 ", "3.0.0.0", "3.4.5-rc.2+sha.AbC123")]
    [DataRow(null, "3.0.0.0", "3.0.0.0")]
    [DataRow("", "3.0.0.0", "3.0.0.0")]
    [DataRow("\u2003", " 3.0.0.0 ", "3.0.0.0")]
    [DataRow(null, null, "unknown")]
    [DataRow(" ", "\t", "unknown")]
    public void VersionFallbackPreservesInformationBeforeAssemblyVersion(
        string? informationalVersion, string? assemblyVersion, string expected)
    {
        var metadata = new AssemblyMetadataStub
        {
            InformationalVersion = informationalVersion,
            Version = assemblyVersion
        };

        ApplicationIdentity identity = ApplicationIdentityDiscovery.Discover(new ApplicationIdentityInput(), metadata);

        Assert.AreEqual("Fixture.App", identity.ApplicationName);
        Assert.AreEqual(expected, identity.BuildVersion);
        Assert.AreEqual(1, metadata.Reads.Count(field => field == "entry"));
    }

    [TestMethod]
    public void UnavailableExplicitAssemblyMetadataDoesNotFallBackToTheEntryAssembly()
    {
        var metadata = new AssemblyMetadataStub { Name = null, InformationalVersion = null, Version = null };
        var input = new ApplicationIdentityInput { ApplicationAssembly = typeof(string).Assembly };

        ApplicationIdentity identity = ApplicationIdentityDiscovery.Discover(input, metadata);

        Assert.IsNull(identity.ApplicationName);
        Assert.AreEqual("unknown", identity.BuildVersion);
        Assert.IsFalse(metadata.Reads.Contains("entry"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CoreAssemblyCannotSupplyApplicationIdentity(bool explicitlySupplied)
    {
        Assembly core = typeof(ApplicationIdentityDiscovery).Assembly;
        var metadata = new AssemblyMetadataStub { EntryAssembly = core };
        var input = new ApplicationIdentityInput { ApplicationAssembly = explicitlySupplied ? core : null };

        ApplicationIdentity identity = ApplicationIdentityDiscovery.Discover(input, metadata);

        Assert.IsNull(identity.ApplicationName);
        Assert.AreEqual("unknown", identity.BuildVersion);
        Assert.AreEqual(0, metadata.ObservedAssemblies.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AnotherLoadContextCannotSupplyCoreIdentity(bool explicitlySupplied)
    {
        Assembly currentCore = typeof(ApplicationIdentityDiscovery).Assembly;
        var context = new AssemblyLoadContext("identity-core-copy", isCollectible: true);
        try
        {
            using Stream bytes = File.OpenRead(currentCore.Location);
            Assembly otherCore = context.LoadFromStream(bytes);
            Assert.AreNotSame(currentCore, otherCore);
            var metadata = new AssemblyMetadataStub { EntryAssembly = otherCore };
            var input = new ApplicationIdentityInput { ApplicationAssembly = explicitlySupplied ? otherCore : null };

            ApplicationIdentity identity = ApplicationIdentityDiscovery.Discover(input, metadata);

            Assert.IsNull(identity.ApplicationName);
            Assert.AreEqual("unknown", identity.BuildVersion);
            Assert.AreEqual(0, metadata.ObservedAssemblies.Count);
        }
        finally
        {
            context.Unload();
        }
    }

    [TestMethod]
    public void CompleteSuppliedIdentityWorksWithoutAssemblyDiscovery()
    {
        var metadata = new AssemblyMetadataStub { FailingField = "entry", Failure = new ArgumentException("Must not read") };
        var input = new ApplicationIdentityInput { ApplicationName = "Supplied.App", BuildVersion = "5.6.7+Exact.Case" };

        ApplicationIdentity identity = ApplicationIdentityDiscovery.Discover(input, metadata);

        Assert.AreEqual("Supplied.App", identity.ApplicationName);
        Assert.AreEqual("5.6.7+Exact.Case", identity.BuildVersion);
        Assert.AreEqual(0, metadata.Reads.Count);
    }

    [TestMethod]
    [DataRow(null, null)]
    [DataRow(" Explicit.App ", "Explicit.App")]
    public void MissingEntryAssemblyLeavesFactsAbsentAndBuildUnknown(string? suppliedName, string? expectedName)
    {
        var metadata = new AssemblyMetadataStub { EntryAssembly = null };

        var input = new ApplicationIdentityInput { ApplicationName = suppliedName };
        ApplicationIdentity identity = ApplicationIdentityDiscovery.Discover(input, metadata);

        Assert.AreEqual(expectedName, identity.ApplicationName);
        Assert.AreEqual("unknown", identity.BuildVersion);
        Assert.IsNull(identity.CommitId);
        Assert.IsNull(identity.BuildId);
    }

    [TestMethod]
    [DataRow(null, null, null, null)]
    [DataRow("", "", null, null)]
    [DataRow(" \t", "\u2003", null, null)]
    [DataRow(" AbC123 ", " Build-0042 ", "AbC123", "Build-0042")]
    [DataRow("sha.Exact.Case", "CI+17", "sha.Exact.Case", "CI+17")]
    public void CommitAndBuildIdsComeOnlyFromSuppliedInputs(
        string? commitId, string? buildId, string? expectedCommit, string? expectedBuild)
    {
        var input = new ApplicationIdentityInput
        {
            ApplicationName = "Supplied.App",
            BuildVersion = "1.2.3+sha.Inferred-file-42",
            CommitId = commitId,
            BuildId = buildId
        };

        ApplicationIdentity identity = ApplicationIdentityDiscovery.Discover(input);

        Assert.AreEqual("1.2.3+sha.Inferred-file-42", identity.BuildVersion);
        Assert.AreEqual(expectedCommit, identity.CommitId);
        Assert.AreEqual(expectedBuild, identity.BuildId);
    }

    [TestMethod]
    [DataRow("4.5.6-preview.3+Build.MixedCase", "4.5.6-preview.3+Build.MixedCase")]
    [DataRow(null, "4.0.0.0")]
    [DataRow(" ", "4.0.0.0")]
    public void ProductionReaderUsesApplicationAttributesAndAssemblyIdentity(string? information, string expected)
    {
        var name = new AssemblyName("Different.Application") { Version = new Version(4, 0, 0, 0) };
        AssemblyBuilder application = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
        if (information is not null)
        {
            ConstructorInfo constructor = typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!;
            application.SetCustomAttribute(new CustomAttributeBuilder(constructor, [information]));
        }

        ConstructorInfo fileVersion = typeof(AssemblyFileVersionAttribute).GetConstructor([typeof(string)])!;
        application.SetCustomAttribute(new CustomAttributeBuilder(fileVersion, ["9.9.9.9"]));

        var input = new ApplicationIdentityInput { ApplicationAssembly = application };
        ApplicationIdentity identity = ApplicationIdentityDiscovery.Discover(input);

        Assert.AreEqual("Different.Application", identity.ApplicationName);
        Assert.AreEqual(expected, identity.BuildVersion);
        Assert.IsNull(identity.CommitId);
        Assert.IsNull(identity.BuildId);
    }

    [TestMethod]
    public void NullInputIsAProgrammingError()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ApplicationIdentityDiscovery.Discover(null!));
    }
}
