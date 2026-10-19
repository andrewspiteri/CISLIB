using System.Reflection;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class EventProjectionArchitectureTests
{
    [TestMethod]
    public void CapturedProjectionIsInternalImmutableAndHasNoCallerOrConfigurationFields()
    {
        Type type = typeof(LogEventData).Assembly.GetType("CIS.Logging.Events.CapturedEventFacts")!;
        Assert.IsFalse(type.IsPublic); Assert.IsTrue(type.IsSealed);
        Assert.AreEqual(0, type.GetConstructors().Length);
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic)) { Assert.IsNull(property.GetSetMethod(nonPublic: true)); }
        Type interpolation = typeof(LogEventData).Assembly.GetType("CIS.Logging.Events.CapturedInterpolation")!;
        Assert.IsFalse(interpolation.IsPublic); Assert.IsTrue(interpolation.IsSealed);
        Assert.AreEqual(0, interpolation.GetConstructors().Length);
        foreach (PropertyInfo property in interpolation.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic)) { Assert.IsNull(property.GetSetMethod(nonPublic: true)); }
        foreach (FieldInfo field in interpolation.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)) { Assert.IsTrue(field.IsInitOnly); }
        FieldInfo parts = interpolation.GetField("parts", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.IsTrue(parts.IsPrivate); Assert.IsTrue(parts.FieldType.IsArray);
        Type part = parts.FieldType.GetElementType()!;
        Assert.AreEqual("CIS.Logging.Events.InterpolationPart", part.FullName);
        Assert.IsTrue(part.IsValueType);
        Assert.IsTrue(part.IsDefined(typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute)));
        var allowed = new[] { typeof(string), typeof(bool), typeof(int), typeof(DateTimeOffset), typeof(LogValue), typeof(LogEventData).Assembly.GetType("CIS.Logging.Exceptions.CapturedException")!, interpolation };
        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsTrue(fields.Length > 0);
        foreach (FieldInfo field in fields) { Assert.IsTrue(field.IsInitOnly); Assert.IsTrue(allowed.Contains(field.FieldType), field.Name); }
    }
}
