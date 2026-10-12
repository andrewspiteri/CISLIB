using System.Reflection;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class EventEncodingArchitectureTests
{
    [TestMethod]
    public void QueuedJsonIsInternalAndExposesOnlyReadonlyBytesAndLossState()
    {
        Type type = typeof(LogEventData).Assembly.GetType("CIS.Logging.Events.EncodedEvent")!;
        Assert.IsFalse(type.IsPublic); Assert.IsTrue(type.IsSealed); Assert.AreEqual(0, type.GetConstructors().Length);
        foreach (FieldInfo field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        { Assert.IsTrue(field.IsPrivate && field.IsInitOnly); Assert.IsTrue(field.FieldType == typeof(byte[]) || field.FieldType == typeof(bool)); }
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance))
        { Assert.IsNull(property.GetSetMethod(true)); Assert.IsTrue(property.PropertyType == typeof(ReadOnlyMemory<byte>) || property.PropertyType == typeof(bool)); }
    }
}
