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
        var diagnostic = ProjectedDiagnostic(new Exception("\uD800" + new string('x', 1024)));
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
        var diagnostic = exceptionType ? AcceptedDiagnostic(exceptionType: text) : AcceptedDiagnostic(code: text);
        Assert.AreEqual(text, exceptionType ? diagnostic.ExceptionType : diagnostic.Code);
        Assert.IsFalse(diagnostic.IsTruncated);
    }

    [TestMethod]
    public void ExplicitExceptionProjectionAcceptsValidCallerControlScalars()
    {
        const string message = "owned\0\t\r\nreason";
        var diagnostic = ProjectedDiagnostic(new Exception("discarded runtime message"), message: message);
        Assert.AreEqual(message, diagnostic.Message);
        Assert.IsFalse(diagnostic.IsTruncated);
    }
    private static HealthDiagnostic AcceptedDiagnostic(string? code = null, string? message = null, string? exceptionType = null)
    {
        HealthDiagnostic? diagnostic = null;
        ArgumentException? rejection = null;
        try { diagnostic = new HealthDiagnostic(code, message, exceptionType); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "Valid diagnostic constructor arguments must produce a detached diagnostic.");
        Assert.IsNotNull(diagnostic);
        return diagnostic;
    }

    private static HealthDiagnostic ProjectedDiagnostic(Exception exception, string? code = null, string? message = null)
    {
        HealthDiagnostic? diagnostic = null;
        ArgumentException? rejection = null;
        try { diagnostic = HealthDiagnostic.FromException(exception, code, message); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "Valid exception projection arguments must produce a detached diagnostic.");
        Assert.IsNotNull(diagnostic);
        return diagnostic;
    }

}
