using System.Text;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthDiagnosticRuntimeAcceptanceBoundaryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ValidCallerIdentifiersAndRuntimeExceptionProjectionProduceADetachedDiagnostic(bool explicitMessage)
    {
        var exception = new Exception("runtime \ud800 \U0001f642");
        const string Code = "Projection.Valid";
        const string CallerMessage = "caller \u0000 \U0001f642";
        HealthDiagnostic? diagnostic = null;
        ArgumentException? unexpectedRejection = null;

        try
        {
            diagnostic = HealthDiagnostic.FromException(exception, Code, explicitMessage ? CallerMessage : null);
        }
        catch (ArgumentException error)
        {
            // These actual inputs satisfy the documented identifier/direct-text
            // rules. An argument-family rejection of this call is an acceptance
            // failure, independent of any collector dispatcher or teardown.
            unexpectedRejection = error;
        }

        Assert.IsNull(unexpectedRejection, "Valid caller input and a real exception must produce a bounded detached diagnostic.");
        Assert.IsNotNull(diagnostic);
        Assert.AreEqual(Code, diagnostic.Code);
        Assert.AreEqual(explicitMessage ? CallerMessage : "runtime \uFFFD \U0001f642", diagnostic.Message);
        Assert.AreEqual(typeof(Exception).FullName, diagnostic.ExceptionType);
        Assert.AreEqual(!explicitMessage, diagnostic.IsTruncated);
        Assert.IsNotNull(diagnostic.Message);
        Assert.IsNotNull(diagnostic.ExceptionType);
        Assert.IsTrue(ScalarCount(diagnostic.Code!) <= 64);
        Assert.IsTrue(ScalarCount(diagnostic.Message) <= 1024);
        Assert.IsTrue(ScalarCount(diagnostic.ExceptionType) <= 256);
    }

    private static int ScalarCount(string text)
    {
        int count = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            count++;
        }
        return count;
    }
}
