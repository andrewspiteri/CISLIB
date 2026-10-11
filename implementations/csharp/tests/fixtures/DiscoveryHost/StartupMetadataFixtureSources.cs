using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using CIS.Configuration;

namespace CIS.Tests.DiscoveryFixture;

// Controlled discovery facts for portable vectors; never substitutes the public publisher.
internal sealed class StartupMetadataFixtureSources : IApplicationAssemblyMetadata, IRuntimeMetadataSource, IEnvironmentVariableSource
{
    private readonly JsonObject input;
    private bool blocked;

    public StartupMetadataFixtureSources(JsonObject input)
    {
        this.input = input;
    }

    public int VariableReads { get; private set; }
    public int RuntimeReads { get; private set; }

    public void BlockFurtherReads()
    {
        blocked = true;
    }

    public string? GetValue(string name)
    {
        RequireReadable();
        VariableReads++;
        return Text(input, name);
    }

    public Assembly? GetEntryAssembly()
    {
        RequireReadable();
        return input.ContainsKey("entryAssembly") && input["entryAssembly"] is null ? null : typeof(JsonNode).Assembly;
    }

    public string? GetName(Assembly assembly)
    {
        RequireReadable();
        return AssemblyFacts(assembly) is { } facts ? Text(facts, "name") : "Fixture.App";
    }

    public string? GetInformationalVersion(Assembly assembly)
    {
        RequireReadable();
        JsonObject? facts = AssemblyFacts(assembly);
        if (facts is not null)
        {
            return Text(facts, "informationalVersion");
        }
        if (input.ContainsKey("informationalVersion"))
        {
            return Text(input, "informationalVersion");
        }
        return "8.0.0+discovery-decoy";
    }

    public string? GetVersion(Assembly assembly)
    {
        RequireReadable();
        return AssemblyFacts(assembly) is { } facts ? Text(facts, "assemblyVersion") : Text(input, "assemblyVersion");
    }

    public DateTimeOffset? GetProcessStartedAt()
    {
        ReadRuntime();
        if (Text(input, "processDiscovery") == "unavailable")
        {
            return null;
        }
        return Text(input, "processStartedAt") is { } value ? DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture) :
            new DateTimeOffset(2026, 10, 10, 7, 0, 0, TimeSpan.Zero);
    }

    public string? GetHostName()
    {
        ReadRuntime();
        return Text(input, "hostDiscovery") == "unavailable" ? null : Text(input, "observedHostName") ?? "fixture-visible-host";
    }

    public OSPlatform? GetOperatingSystem()
    {
        ReadRuntime();
        return Text(input, "osFamily") == "unavailable" ? null : OSPlatform.Linux;
    }

    public string? GetOperatingSystemDescription()
    {
        ReadRuntime();
        return Text(input, "osFamily") == "unavailable" ? null : "Fixture OS";
    }

    public Architecture? GetOSArchitecture()
    {
        ReadRuntime();
        return Text(input, "osFamily") == "unavailable" ? null : Architecture.Arm64;
    }

    public Architecture? GetProcessArchitecture()
    {
        ReadRuntime();
        return Text(input, "processDiscovery") == "unavailable" ? null : Architecture.X64;
    }

    public string? GetRuntimeDescription()
    {
        ReadRuntime();
        return Text(input, "runtimeDescription") == "unavailable" ? null : ".NET Controlled Fixture";
    }

    public int? GetProcessId()
    {
        ReadRuntime();
        return Text(input, "processDiscovery") == "unavailable" ? null : 431;
    }

    private JsonObject? AssemblyFacts(Assembly assembly) =>
        input[assembly == typeof(JsonNode).Assembly ? "entryAssembly" : "explicitAssembly"]?.AsObject();

    private void ReadRuntime()
    {
        RequireReadable();
        RuntimeReads++;
    }

    private void RequireReadable()
    {
        if (blocked)
        {
            throw new InvalidOperationException("Stored snapshot queried a blocked discovery source.");
        }
    }

    private static string? Text(JsonObject obj, string name) => obj[name]?.GetValue<string>();
}
