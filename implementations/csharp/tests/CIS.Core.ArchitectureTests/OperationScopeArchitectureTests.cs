using System.Reflection;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class OperationScopeArchitectureTests
{
    [TestMethod]
    public void ActualContextInputHasTheSpecifiedClosedPublicSurface()
    {
        Type type = typeof(OperationContextData);
        Assert.AreEqual("CIS.Logging.OperationContextData", type.FullName);
        Assert.IsTrue(type.IsSealed);
        Assert.AreEqual(1, type.GetConstructors().Length);
        Assert.AreEqual(0, type.GetConstructors().Single().GetParameters().Length);
        Assert.AreEqual(2, type.GetProperties().Length);
        Assert.AreEqual(typeof(string), type.GetProperty("OperationId")!.PropertyType);
        Assert.AreEqual(typeof(IReadOnlyDictionary<string, LogValue>), type.GetProperty("Attributes")!.PropertyType);
        Assert.IsTrue(type.GetProperties().All(property => property.CanRead && property.CanWrite));
        var annotations = new NullabilityInfoContext();
        foreach (PropertyInfo property in type.GetProperties())
        {
            NullabilityInfo info = annotations.Create(property);
            Assert.AreEqual(NullabilityState.Nullable, info.ReadState);
            Assert.AreEqual(NullabilityState.Nullable, info.WriteState);
        }
    }

    [TestMethod]
    public void CapturedPayloadAndParentFramesHaveOnlyReadonlyOwnedFields()
    {
        Type payload = typeof(LogValue).Assembly.GetType("CIS.Logging.Scopes.CapturedOperationContext", throwOnError: true)!;
        Type frame = typeof(LogValue).Assembly.GetType("CIS.Logging.Scopes.OperationScopeFrame", throwOnError: true)!;
        foreach (Type type in new[] { payload, frame })
        {
            Assert.IsFalse(type.IsPublic);
            Assert.IsTrue(type.IsSealed);
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsTrue(fields.Length > 0);
            Assert.IsTrue(fields.All(field => field.IsInitOnly));
            Assert.IsTrue(fields.All(field => field.FieldType == typeof(string) || field.FieldType == typeof(int)
                || field.FieldType == typeof(LogValue) || field.FieldType == payload
                || field.FieldType == frame));
        }
    }
}
