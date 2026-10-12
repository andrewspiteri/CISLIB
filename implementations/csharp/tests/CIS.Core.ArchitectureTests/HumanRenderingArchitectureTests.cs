using System.Reflection;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HumanRenderingArchitectureTests
{
    [TestMethod]
    public void HumanRenderingAndItsOwnedBytesRemainInternalWithoutCallerFields()
    {
        Assembly assembly = typeof(LogEventData).Assembly;
        Type renderer = assembly.GetType("CIS.Logging.Rendering.HumanRenderer")!;
        Assert.IsFalse(renderer.IsPublic); Assert.IsTrue(renderer.IsAbstract && renderer.IsSealed);
        Type result = assembly.GetType("CIS.Logging.Rendering.RenderedHumanEvent")!;
        Assert.IsFalse(result.IsPublic); Assert.IsTrue(result.IsSealed); Assert.AreEqual(0, result.GetConstructors().Length);
        foreach (FieldInfo field in result.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        { Assert.IsTrue(field.IsPrivate && field.IsInitOnly); Assert.IsTrue(field.FieldType == typeof(byte[]) || field.FieldType == typeof(bool)); }
        foreach (PropertyInfo property in result.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance))
        { Assert.IsNull(property.GetSetMethod(true)); Assert.IsTrue(property.PropertyType == typeof(ReadOnlyMemory<byte>) || property.PropertyType == typeof(bool)); }
    }
}
