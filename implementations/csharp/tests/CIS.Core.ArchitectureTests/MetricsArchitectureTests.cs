using System.Reflection;
using CIS.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class MetricsArchitectureTests
{
    [TestMethod]
    public void OrdinaryMetricsSurfaceStaysPortableInTheExistingCoreAssembly()
    {
        Assembly core = typeof(IMetrics).Assembly;
        Assert.AreEqual("CIS.Core", core.GetName().Name);
        Type[] surface = core.GetExportedTypes().Where(type => type.Namespace == "CIS.Metrics").ToArray();
        CollectionAssert.AreEquivalent(new[] { "DisabledMetrics", "IMetrics", "ICounter", "IUpDownCounter", "IGauge", "IHistogram", "ITimer",
            "ITimingScope", "MetricUnit", "MetricDefinition", "MetricDimensions", "MetricObservation" }, surface.Select(type => type.Name).ToArray());
        foreach (Type type in surface)
        {
            AssertPortable(type);
        }
    }

    [TestMethod]
    public void CoreDoesNotAcquireCollectorSdkOrExporterAssemblyDependencies()
    {
        string[] references = typeof(IMetrics).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name!).ToArray();
        Assert.IsFalse(references.Any(name => name.StartsWith("OpenTelemetry", StringComparison.Ordinal)
            || name.StartsWith("Prometheus", StringComparison.Ordinal)), string.Join(", ", references));
    }

    [TestMethod]
    [DataRow(typeof(DirectInstrumentLeak))]
    [DataRow(typeof(NestedCallbackLeak))]
    [DataRow(typeof(PlatformClockLeak))]
    public void PublicBoundaryDetectsDirectGenericCallbackAndPlatformTimingLeaks(Type fixture)
        => Assert.ThrowsExactly<InvalidOperationException>(() => AssertPortable(fixture));

    // Same compiled public-metadata approach as existing provider boundaries;
    // hosting-specific APIs do not belong in this ordinary consumer namespace.
    private static void AssertPortable(Type source)
    {
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        IEnumerable<Type> signatures = source.GetInterfaces()
            .Concat(source.GetConstructors().SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)))
            .Concat(source.GetMethods(declared).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)))
            .Concat(source.GetProperties(declared).Select(property => property.PropertyType))
            .Concat(source.GetFields(declared).Select(field => field.FieldType));
        foreach (Type type in signatures.SelectMany(Contained))
        {
            string space = type.Namespace ?? "";
            if (space.StartsWith("System.Diagnostics.Metrics", StringComparison.Ordinal)
                || space.StartsWith("OpenTelemetry", StringComparison.Ordinal)
                || space.StartsWith("Microsoft.", StringComparison.Ordinal)
                || type == typeof(System.Diagnostics.Stopwatch) || type == typeof(TimeProvider))
            {
                throw new InvalidOperationException(source.FullName + " exposes a platform instrumentation dependency: " + type.FullName);
            }
        }
    }

    private static IEnumerable<Type> Contained(Type type)
    {
        yield return type;
        if (type.HasElementType)
        {
            foreach (Type inner in Contained(type.GetElementType()!))
            {
                yield return inner;
            }
        }
        if (type.IsGenericType)
        {
            foreach (Type inner in type.GetGenericArguments().SelectMany(Contained))
            {
                yield return inner;
            }
        }
    }

    public sealed class DirectInstrumentLeak
    {
        public System.Diagnostics.Metrics.Meter? Meter
        {
            get; set;
        }
    }
    public sealed class NestedCallbackLeak
    {
        public Func<IReadOnlyList<System.Diagnostics.Metrics.Instrument>>? Callback
        {
            get; set;
        }
    }
    public sealed class PlatformClockLeak
    {
        public TimeProvider? Clock
        {
            get; set;
        }
    }
}
