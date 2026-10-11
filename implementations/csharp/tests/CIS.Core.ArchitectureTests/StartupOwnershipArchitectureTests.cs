using System.Reflection;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class StartupOwnershipArchitectureTests
{
    private static readonly Type[] Owned = [typeof(EnvironmentSnapshot), typeof(LoggingOptions), typeof(EventMetadata)];

    [TestMethod]
    public void PublishedSnapshotGraphContainsOnlyReadonlyOwnedTypesAndImmutableValues()
    {
        foreach (Type type in Owned)
        {
            Assert.IsTrue(type.IsSealed);
            FieldInfo[] fields = Fields(type);
            Assert.IsTrue(fields.Length > 0);
            foreach (FieldInfo field in fields)
            {
                Assert.IsTrue(field.IsPrivate && field.IsInitOnly, type.Name + "." + field.Name);
                Assert.IsTrue(IsOwnedOrImmutable(field.FieldType), type.Name + "." + field.Name);
            }
        }
        CollectionAssert.AreEquivalent(new[] { typeof(LoggingOptions), typeof(EventMetadata) }, Fields(typeof(EnvironmentSnapshot)).Select(field => field.FieldType).ToArray());
    }

    [TestMethod]
    [DataRow("nested setup")]
    [DataRow("array")]
    public void ReadonlyFieldGuardRejectsAReadonlyReferenceToMutableCallerData(string kind)
    {
        Type probe = kind == "array" ? typeof(ArrayProbe) : typeof(SetupProbe);
        FieldInfo field = Fields(probe).Single();
        Assert.IsTrue(field.IsInitOnly);
        Assert.IsFalse(IsOwnedOrImmutable(field.FieldType));
    }

    private static FieldInfo[] Fields(Type type) => type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
    private static bool IsOwnedOrImmutable(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return Owned.Contains(type) || type == typeof(string) || type == typeof(bool) || type == typeof(int) || type == typeof(long) || type == typeof(TimeSpan) || type == typeof(DateTimeOffset) || type.IsEnum;
    }
    private sealed class SetupProbe { public LoggingSetup Mutable { get; } = new(); }
    private sealed class ArrayProbe { public string[] Mutable { get; } = []; }
}
