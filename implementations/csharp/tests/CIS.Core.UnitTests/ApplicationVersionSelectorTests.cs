using CIS.Configuration;
using CIS.Tests.UnitTests.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ApplicationVersionSelectorTests
{
    private const string ActualBuild = "1.2.3-rc.2+sha.AbC123";

    [TestMethod]
    [DataRow(" Core.Release ", "Alias", "Setup", "Core.Release", "CORE_SERVICE_VERSION")]
    [DataRow(null, " Alias.Release ", "Setup", "Alias.Release", "DD_VERSION")]
    [DataRow("", " Alias.Release ", "Setup", "Alias.Release", "DD_VERSION")]
    [DataRow(" \t", " Alias.Release ", "Setup", "Alias.Release", "DD_VERSION")]
    [DataRow("\u2003", "\t", " Setup.Release ", "Setup.Release", "EnvironmentSetup.ServiceVersion")]
    [DataRow(null, null, " Setup.Release ", "Setup.Release", "EnvironmentSetup.ServiceVersion")]
    [DataRow(null, null, null, ActualBuild, "BuildVersion")]
    [DataRow(" ", "", "\u2003", ActualBuild, "BuildVersion")]
    [DataRow("Core.Valid", "bad\nAlias", "bad\nSetup", "Core.Valid", "CORE_SERVICE_VERSION")]
    [DataRow("bad\nCore", "Alias.Valid", "Setup.Valid", "bad\nCore", "CORE_SERVICE_VERSION")]
    [DataRow(null, "bad\nAlias", "Setup.Valid", "bad\nAlias", "DD_VERSION")]
    [DataRow(null, null, " 4.5.6-preview.3+Build.MixedCase ", "4.5.6-preview.3+Build.MixedCase", "EnvironmentSetup.ServiceVersion")]
    public void TelemetryVersionSelectsTheFirstNonblankSourceWithoutChangingTheBuild(
        string? core, string? alias, string? setup, string expected, string expectedSource)
    {
        var identity = new ApplicationIdentity("Fixture.App", ActualBuild, "AbC123", "Build-42");
        var variables = new EnvironmentVariableStub();
        variables.Values["CORE_SERVICE_VERSION"] = core;
        variables.Values["DD_VERSION"] = alias;

        ApplicationVersionSelection selection = ApplicationVersionSelector.Select(identity, setup, variables);

        Assert.AreEqual(ActualBuild, selection.BuildVersion);
        Assert.AreEqual(expected, selection.ServiceVersion);
        Assert.AreEqual(expectedSource, selection.ServiceVersionSource);
        Assert.AreEqual(ActualBuild, identity.BuildVersion);
        Assert.AreEqual("AbC123", identity.CommitId);
        Assert.AreEqual("Build-42", identity.BuildId);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SelectionReadsOnlyRecognizedUnshadowedVersionKeys(bool corePresent)
    {
        var variables = new EnvironmentVariableStub();
        variables.Values["CORE_SERVICE_VERSION"] = corePresent ? "Core" : null;
        variables.Values["DD_VERSION"] = "Alias";
        variables.Values["CORE_BUILD_VERSION"] = "MustNotReplaceBuild";
        variables.Values["DD_AGENT_HOST"] = "MustNotBecomeIdentity";
        var identity = new ApplicationIdentity("Fixture.App", ActualBuild, null, null);

        ApplicationVersionSelection selection = ApplicationVersionSelector.Select(identity, variables: variables);

        Assert.AreEqual(ActualBuild, selection.BuildVersion);
        string[] expected = corePresent ? ["CORE_SERVICE_VERSION"] : ["CORE_SERVICE_VERSION", "DD_VERSION"];
        CollectionAssert.AreEqual(expected, variables.Reads);
    }

    [TestMethod]
    [DataRow("3.4.5-rc.2+sha.AbC123")]
    [DataRow("unknown")]
    public void ChangingTelemetryLabelsKeepsActualBuildFactsStable(string expectedBuild)
    {
        ApplicationIdentity identity = ApplicationIdentityDiscovery.Discover(new ApplicationIdentityInput
        {
            ApplicationName = "Fixture.App",
            BuildVersion = expectedBuild,
            CommitId = "Explicit.Commit",
            BuildId = "Explicit.Build"
        });
        var variables = new EnvironmentVariableStub();
        variables.Values["CORE_SERVICE_VERSION"] = "Deployment.First";
        ApplicationVersionSelection first = ApplicationVersionSelector.Select(identity, variables: variables);
        variables.Values["CORE_SERVICE_VERSION"] = "Deployment.Second";
        ApplicationVersionSelection second = ApplicationVersionSelector.Select(identity, variables: variables);

        Assert.AreEqual(expectedBuild, first.BuildVersion);
        Assert.AreEqual(expectedBuild, second.BuildVersion);
        Assert.AreEqual("Deployment.First", first.ServiceVersion);
        Assert.AreEqual("Deployment.Second", second.ServiceVersion);
        Assert.AreEqual("Explicit.Commit", identity.CommitId);
        Assert.AreEqual("Explicit.Build", identity.BuildId);
    }

    [TestMethod]
    [DataRow("CORE_SERVICE_VERSION")]
    [DataRow("DD_VERSION")]
    public void VariableReadFailuresPropagateWithoutTryingAUserOrBuildFallback(string name)
    {
        var failure = new IOException("Variable unavailable");
        var variables = new EnvironmentVariableStub { FailingName = name, Failure = failure };
        var identity = new ApplicationIdentity("Fixture.App", ActualBuild, null, null);

        Exception actual = Assert.Throws<Exception>(() => ApplicationVersionSelector.Select(identity, "Lower.Setup", variables));

        Assert.AreSame(failure, actual);
    }

    [TestMethod]
    public void NullBuildIdentityIsAProgrammingError()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ApplicationVersionSelector.Select(null!));
    }

    [TestMethod]
    public void ProcessVariableSourceReadsExactKeysWithoutChangingTheEnvironment()
    {
        foreach (string name in new[] { "CORE_SERVICE_VERSION", "DD_VERSION" })
        {
            string? before = System.Environment.GetEnvironmentVariable(name);
            Assert.AreEqual(before, ProcessEnvironmentVariableSource.Instance.GetValue(name));
            Assert.AreEqual(before, System.Environment.GetEnvironmentVariable(name));
        }
    }
}
