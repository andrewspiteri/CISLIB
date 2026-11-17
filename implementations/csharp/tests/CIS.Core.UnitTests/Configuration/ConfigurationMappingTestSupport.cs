using System.Text.Json;
using CIS.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Configuration;

internal static class ConfigurationMappingTestSupport
{
    internal static ConfigurationMappingFixture<T> Create<T>(Action<T>? validator = null,
        Func<T>? factory = null, Action<Type, string>? diagnostic = null)
        where T : class, IConfigurationObject, new()
    {
        var options = new ConfigurationOptions("MappingHost", new ConfigurationObjectTestSupport.UnusedStore())
        {
            Factories = factory is null ? new Dictionary<Type, Func<IConfigurationObject>>()
                : new Dictionary<Type, Func<IConfigurationObject>> { [typeof(T)] = () => factory() },
            Validators = validator is null ? new Dictionary<Type, Action<IConfigurationObject>>()
                : new Dictionary<Type, Action<IConfigurationObject>> { [typeof(T)] = value => validator((T)value) }
        };
        ConfigurationCatalog catalog = ConfigurationCatalog.Create(new[] { typeof(T) }, options);
        return new(catalog, catalog.Entries.Single(), new ConfigurationValuePipeline(diagnostic));
    }

    internal static JsonElement? Record(string? text)
    {
        if (text is null) { return null; }
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    internal static CoreConfigurationException Issue(string expectedCode, Action action)
    {
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(action);
        Assert.IsTrue(error.Issues.Any(issue => issue.Code == expectedCode));
        foreach (ConfigurationIssue issue in error.Issues.Where(issue => issue.Code == expectedCode))
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(issue.Key), "A mapping failure must identify its configuration source context.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(issue.Explanation), "A mapping failure must retain useful guidance without requiring exact wording.");
        }
        return error;
    }
}

internal sealed class ConfigurationMappingFixture<T>(ConfigurationCatalog catalog, ConfigurationEntry entry,
    ConfigurationValuePipeline pipeline) where T : class, IConfigurationObject
{
    internal ConfigurationCatalog Catalog { get; } = catalog;
    internal ConfigurationEntry Entry { get; } = entry;
    internal T Root => (T)Entry.Instance;
    internal ConfigurationValuePipeline Pipeline { get; } = pipeline;
    internal ConfigurationPreparedValues Prepare(string? common = null, string? application = null)
        => Pipeline.Prepare(Entry, ConfigurationMappingTestSupport.Record(common), ConfigurationMappingTestSupport.Record(application));
    internal void Baseline(string? common = null, string? application = null)
        => Assert.IsFalse(Pipeline.Apply(Entry, Prepare(common, application), CancellationToken.None, () => false, initial: true));
}

internal sealed class W2OverlaySettings : ConfigurationObject
{
    public W2OverlaySettings() { }
    public int IntervalSeconds { get; set; } = 30;
    public int TimeoutSeconds { get; set; } = 5;
    public bool Enabled { get; set; } = true;
    public string Label { get; set; } = "default";
    public string? Optional { get; set; } = "fallback";
    public W2Child? Child { get; set; } = new() { Left = 10, Right = 20 };
    public List<int> Items { get; set; } = [1, 2];
    public Dictionary<string, int> Scores { get; set; } = new(StringComparer.Ordinal) { ["default"] = 1 };
}

internal sealed class W2Child
{
    public W2Child() { }
    public int Left { get; set; } = 1;
    public int Right { get; set; } = 2;
}

internal sealed class W2NullableElements : ConfigurationObject
{
    public W2NullableElements() { }
    public List<string> Required { get; set; } = ["required"];
    public List<string?> Optional { get; set; } = ["optional"];
    public Dictionary<string, string> RequiredMap { get; set; } = new() { ["k"] = "required" };
    public Dictionary<string, string?> OptionalMap { get; set; } = new() { ["k"] = "optional" };
}

internal sealed class W2Numbers : ConfigurationObject
{
    public W2Numbers() { }
    public int Count { get; set; }
    public double Double { get; set; }
    public float Single { get; set; }
    public decimal Price { get; set; }
}

internal enum W2Mode { Ready = 1, Busy = 2 }
internal enum W2ByteMode : byte { Ready = 1 }
internal enum W2SignedMode : sbyte { Ready = 1 }
internal enum W2UnsignedMode : ulong { Ready = 1 }

internal sealed class W2Enums : ConfigurationObject
{
    public W2Enums() { }
    public W2Mode Mode { get; set; }
    public W2ByteMode ByteMode { get; set; }
    public W2SignedMode SignedMode { get; set; }
    public W2UnsignedMode UnsignedMode { get; set; }
}

internal sealed class W2Dates : ConfigurationObject
{
    public W2Dates() { }
    public DateTime When { get; set; } = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
    public DateTimeOffset Offset { get; set; } = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
}

internal sealed class W2ChecksumVector : ConfigurationObject
{
    public W2ChecksumVector() { }
    public int Count { get; set; }
    public W2Mode Mode { get; set; }
    public decimal Price { get; set; }
    public DateTime When { get; set; } = DateTime.UnixEpoch;
}

internal sealed class W2SeedSettings : ConfigurationObject
{
    public W2SeedSettings() { }
    public int Seed { get; set; }
}

internal sealed class W2ObservedSettings : ConfigurationObject
{
    private int alpha = 1;
    private int beta = 2;
    public W2ObservedSettings() { }
    public int Alpha
    {
        get => alpha;
        set { alpha = value; Writes++; AfterAlphaAssignment?.Invoke(); }
    }
    public int Beta
    {
        get => beta;
        set
        {
            if (RejectBetaAssignment) { throw new InvalidOperationException("live-setter-secret"); }
            beta = value;
            Writes++;
        }
    }
    internal int Writes { get; private set; }
    internal Action? AfterAlphaAssignment { get; set; }
    internal bool RejectBetaAssignment { get; set; }
    internal void ResetWrites() => Writes = 0;
}
