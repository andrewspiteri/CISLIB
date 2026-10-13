using System.Text.Json;

namespace CIS.TypeManagement.Fixtures.Host;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // String identities deliberately avoid typeof references that could preload the test assemblies.
        var loadedNames = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name).ToArray();
        var unusedInitiallyLoaded = loadedNames.Contains("CIS.TypeManager.Fixture.Unused", StringComparer.Ordinal);
        var host = typeof(Program).Assembly;
        var manifestPath = Path.Combine(AppContext.BaseDirectory, host.GetName().Name + ".types.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var identities = manifest.RootElement.GetProperty("assemblies").EnumerateArray()
            .Select(entry => entry.GetProperty("assemblyName").GetString()).ToArray();
        if (unusedInitiallyLoaded) { throw new InvalidOperationException("The discovery fixture was preloaded."); }
        using var cancellation = new CancellationTokenSource();
        bool cancelDuringLoad = args is ["cancel-during-load"];
        if (args.Length != 0 && !cancelDuringLoad) { throw new ArgumentException("Unknown fixture mode."); }
        bool containerConstructed = false;
        AssemblyLoadEventHandler cancelOnFixture = (_, eventArgs) =>
        {
            if (eventArgs.LoadedAssembly.GetName().Name == "CIS.TypeManager.Fixture.Unused") { cancellation.Cancel(); }
        };
        if (cancelDuringLoad) { AppDomain.CurrentDomain.AssemblyLoad += cancelOnFixture; }
        try
        {
            var manager = await TypeManager.CreateAsync(new TypeManagerOptions(host), cancellation.Token);
            if (cancelDuringLoad) { throw new InvalidOperationException("Cancellation did not prevent publication."); }
            var marker = manager.Types.Single(type => type.FullName == "CIS.TypeManagement.Fixtures.Contracts.IDiscoveryMarker");
            var implementation = manager.GetImplementations(marker).Single();
            if (implementation.FullName != "CIS.TypeManagement.Fixtures.Unused.DiscoveredSettings" ||
                implementation.IsPublic || implementation.GetConstructor(Type.EmptyTypes) is not null)
            {
                throw new InvalidOperationException("The unused inherited private-constructor implementation was not discovered.");
            }
            if (!manager.PreparationReport.LoadedAssemblyIdentities.Any(identity => identity.StartsWith("CIS.TypeManager.Fixture.Unused,", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("The preparation report did not record the newly loaded library.");
            }
            containerConstructed = true;
            Console.WriteLine(JsonSerializer.Serialize(new { unusedInitiallyLoaded, identities, containerConstructed, typeCount = manager.Types.Count }));
            return 0;
        }
        catch (OperationCanceledException) when (cancelDuringLoad && cancellation.IsCancellationRequested)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { unusedInitiallyLoaded, canceledDuringLoad = true, containerConstructed }));
            return 0;
        }
        finally
        {
            if (cancelDuringLoad) { AppDomain.CurrentDomain.AssemblyLoad -= cancelOnFixture; }
        }
    }
}
