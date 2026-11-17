using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using CIS.Configuration;
using CIS.TypeManagement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Configuration;

internal static class ConfigurationObjectTestSupport
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    internal static ConfigurationOptions Options(string host = "CountryService",
        IReadOnlyDictionary<Type, Func<IConfigurationObject>>? factories = null)
        => new(host, new UnusedStore()) { Factories = factories ?? new Dictionary<Type, Func<IConfigurationObject>>() };

    internal static ConfigurationEntry Entry(ConfigurationCatalog catalog, Type type)
        => catalog.Entries.Single(entry => entry.Type == type);

    internal static void AssertIssue(string expectedCode, Action action)
    {
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(action);
        Assert.IsTrue(error.Issues.Any(issue => issue.Code == expectedCode),
            "Expected a bounded Configuration issue for " + expectedCode + ".");
    }

    internal sealed class SelectedTypes(params Type[] types) : ITypeManager
    {
        internal int Queries { get; private set; }
        public IReadOnlyList<Assembly> Assemblies => types.Select(type => type.Assembly).Distinct().ToArray();
        public IReadOnlyList<Type> Types => types;
        public TypeManagerPreparationReport PreparationReport => new(TimeSpan.Zero, types.Length, 0, [], [], []);
        public IReadOnlyList<Type> GetImplementations(Type contractType)
        {
            Assert.AreSame(typeof(IConfigurationObject), contractType);
            Queries++;
            return types;
        }
        public IReadOnlyList<Type> GetDefinedTypes(Assembly assembly) => throw new NotSupportedException("Query the prepared implementation catalog.");
        public bool TryGetType(string assemblyQualifiedTypeName, [NotNullWhen(true)] out Type? type)
        { type = null; throw new NotSupportedException("Configuration must retain the actual queried Type."); }
    }

    internal sealed class UnusedStore : IConfigurationStore
    {
        public ConfigurationNotificationMode NotificationMode => ConfigurationNotificationMode.Reliable;
        public ValueTask<ConfigurationReadResult> ReadAsync(ConfigurationRecordKey key, string? sourceToken, CancellationToken cancellationToken)
            => throw new AssertFailedException("Retained-object catalog construction must not perform storage I/O.");
    }
}

internal class W1Settings : ConfigurationObject
{
    public W1Settings() { }
    public int First { get; set; } = 10;
    public int Second { get; set; } = 20;
    public List<int> Values { get; set; } = [2, 4];
    public W1Child Child { get; set; } = new();
}

internal sealed class W1Child
{
    public W1Child() { }
    public string Label { get; set; } = "original";
}

internal sealed class W1OtherSettings : ConfigurationObject
{
    public W1OtherSettings() { }
    public int Value { get; set; } = 7;
}

internal sealed class W1DirectSettings : IConfigurationObject
{
    public W1DirectSettings() { }
    public int Value { get; set; } = 5;
    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged
    {
        add => ConfigurationEvents.Add(this, value);
        remove => ConfigurationEvents.Remove(this, value);
    }
}

internal sealed class W1FieldEventSettings : IConfigurationObject
{
    public W1FieldEventSettings() { }
    public int Value { get; set; }
    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;
    internal void Raise() => ConfigurationChanged?.Invoke(this, new(GetType(), 1));
}

internal sealed class W1BrokenRemoveSettings : IConfigurationObject
{
    public W1BrokenRemoveSettings() { }
    public int Value { get; set; }
    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged
    {
        add => ConfigurationEvents.Add(this, value);
        remove { }
    }
}

internal sealed class W1NeedsFactorySettings : ConfigurationObject
{
    public W1NeedsFactorySettings(int value) { Value = value; }
    public int Value { get; set; }
}

internal sealed class W1DerivedSettings : W1Settings { }
