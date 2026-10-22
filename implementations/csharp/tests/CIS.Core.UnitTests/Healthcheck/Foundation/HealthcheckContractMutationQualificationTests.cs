using CIS.Configuration;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthcheckContractMutationQualificationTests
{
    [TestMethod]
    [DataRow("A")]
    [DataRow("a.b")]
    [DataRow("a_b")]
    [DataRow("a-b")]
    [DataRow("a0")]
    public void PortablePipeNamesAcceptTheirInclusiveMinimumAndEachAllowedSeparator(string name)
    {
        HealthcheckOptions options = new HealthcheckOptions { PipeEnabled = true, PipeName = name }.ValidatedCopy();
        Assert.AreEqual(name, options.PipeName);
        Assert.IsTrue(options.PipeEnabled);
    }

    [TestMethod]
    public void RuntimeDiagnosticReplacementAndTruncationBothRemainObservable()
    {
        var diagnostic = HealthDiagnostic.FromException(new Exception("\uD800" + new string('x', 1024)));
        Assert.AreEqual("\uFFFD" + new string('x', 1023), diagnostic.Message);
        Assert.IsTrue(diagnostic.IsTruncated);
        Assert.AreEqual(typeof(Exception).FullName, diagnostic.ExceptionType);
    }

    [TestMethod]
    public void ConfigurationResolutionRequiresItsEnvironmentReader()
    {
        ArgumentNullException error = Assert.ThrowsExactly<ArgumentNullException>(() => HealthcheckOptions.Resolve(null, null!));
        Assert.AreEqual("readEnvironment", error.ParamName);
    }

    [TestMethod]
    [DataRow("anonymous")]
    [DataRow("invalid/name")]
    [DataRow("_invalid")]
    public void InvalidPortablePipeNamesRetainTheStructuredIssueIdentity(string name)
    {
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => new HealthcheckOptions { PipeName = name }.ValidatedCopy());
        Assert.AreEqual("CIS_HEALTHCHECK_PIPE_NAME", error.Issues.Single().Key);
        Assert.AreEqual("Healthcheck.InvalidValue", error.Issues.Single().Code);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DiagnosticIdentifiersAcceptValidControlScalarsWithoutInventingTruncation(bool exceptionType)
    {
        const string text = "owned\0\t\r\nreason";
        var diagnostic = exceptionType ? new HealthDiagnostic(exceptionType: text) : new HealthDiagnostic(code: text);
        Assert.AreEqual(text, exceptionType ? diagnostic.ExceptionType : diagnostic.Code);
        Assert.IsFalse(diagnostic.IsTruncated);
    }

    [TestMethod]
    public void ExplicitExceptionProjectionAcceptsValidCallerControlScalars()
    {
        const string message = "owned\0\t\r\nreason";
        var diagnostic = HealthDiagnostic.FromException(new Exception("discarded runtime message"), message: message);
        Assert.AreEqual(message, diagnostic.Message);
        Assert.IsFalse(diagnostic.IsTruncated);
    }
}
