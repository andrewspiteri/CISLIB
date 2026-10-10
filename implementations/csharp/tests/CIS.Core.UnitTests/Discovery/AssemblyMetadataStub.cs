using System.Reflection;
using CIS.Configuration;

namespace CIS.Tests.UnitTests.Discovery;

internal sealed class AssemblyMetadataStub : IApplicationAssemblyMetadata
{
    public Assembly? EntryAssembly { get; init; } = typeof(AssemblyMetadataStub).Assembly;
    public string? Name { get; init; } = "Fixture.App";
    public string? InformationalVersion { get; init; } = "3.4.5-rc.2+sha.AbC123";
    public string? Version { get; init; } = "3.0.0.0";
    public string? FailingField { get; init; }
    public Exception? Failure { get; init; }
    public List<string> Reads { get; } = [];
    public List<Assembly> ObservedAssemblies { get; } = [];

    public Assembly? GetEntryAssembly() => Read("entry", EntryAssembly);

    public string? GetName(Assembly assembly)
    {
        ObservedAssemblies.Add(assembly);
        return Read("name", Name);
    }

    public string? GetInformationalVersion(Assembly assembly)
    {
        ObservedAssemblies.Add(assembly);
        return Read("information", InformationalVersion);
    }

    public string? GetVersion(Assembly assembly)
    {
        ObservedAssemblies.Add(assembly);
        return Read("version", Version);
    }

    private T? Read<T>(string field, T? value) where T : class
    {
        Reads.Add(field);
        if (field == FailingField)
        {
            throw Failure ?? new IOException("Metadata unavailable");
        }

        return value;
    }
}
