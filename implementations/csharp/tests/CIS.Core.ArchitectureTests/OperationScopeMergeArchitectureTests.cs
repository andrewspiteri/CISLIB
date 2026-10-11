using System.Reflection;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class OperationScopeMergeArchitectureTests
{
    [TestMethod]
    public void MergedViewContainsOnlyReadonlyOwnedValuesAndNoFlowOrFrameReferences()
    {
        Type view = typeof(LogValue).Assembly.GetType("CIS.Logging.Scopes.CapturedScopeContext", throwOnError: true)!;
        Assert.IsFalse(view.IsPublic);
        Assert.IsTrue(view.IsSealed);
        FieldInfo[] fields = view.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.AreEqual(3, fields.Length);
        Assert.IsTrue(fields.All(field => field.IsInitOnly));
        Assert.IsTrue(fields.All(field => field.FieldType == typeof(string) || field.FieldType == typeof(LogValue) || field.FieldType == typeof(int)));
    }
}
