using System.Reflection;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class EventProjectionArchitectureTests
{
    [TestMethod]
    public void CapturedProjectionIsInternalImmutableAndHasNoCallerOrConfigurationFields()
    {
        Type type = typeof(CapturedEventFacts);
        Assert.IsFalse(type.IsPublic); Assert.IsTrue(type.IsSealed);
        Assert.AreEqual(0, type.GetConstructors().Length);
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic)) { Assert.IsNull(property.GetSetMethod(nonPublic: true)); }
        var allowed = new[] { typeof(string), typeof(bool), typeof(int), typeof(LogValue), typeof(CapturedException) };
        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsTrue(fields.Length > 0);
        foreach (FieldInfo field in fields) { Assert.IsTrue(field.IsInitOnly); Assert.IsTrue(allowed.Contains(field.FieldType), field.Name); }
    }
}
