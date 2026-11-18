using System.Text.Json;
using CIS.Configuration;
using CIS.Hosting;
using CIS.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.TypeManagement.Fixtures.Host;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // No strong reference to Unused is allowed before the facade prepares its actual inventory.
        bool unusedInitiallyLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
            assembly.GetName().Name == "CIS.TypeManager.Fixture.Unused");
        if (unusedInitiallyLoaded) { throw new InvalidOperationException("The discovery fixture was preloaded."); }
        bool cancelDuringLoad = args is ["cancel-during-load"];
        if (args.Length != 0 && !cancelDuringLoad) { throw new ArgumentException("Unknown fixture mode."); }
        using var cancellation = new CancellationTokenSource();
        AssemblyLoadEventHandler cancelOnFixture = (_, eventArgs) =>
        {
            if (eventArgs.LoadedAssembly.GetName().Name == "CIS.TypeManager.Fixture.Unused") { cancellation.Cancel(); }
        };
        if (cancelDuringLoad) { AppDomain.CurrentDomain.AssemblyLoad += cancelOnFixture; }
        try
        {
            int typeCount, startupCalls;
            bool sameConfigurationAcrossScopes;
            await using (var application = new CISApplication(setup =>
            {
                setup.TypeManager = new TypeManagerOptions(typeof(Program).Assembly);
                setup.LoggingEnabled = false;
                setup.HealthcheckEnabled = false;
                setup.HeartbeatEnabled = false;
                setup.EnableOptionalIntegrations = false;
                setup.Services.AddSingleton<FixtureConsumer>();
                setup.Services.AddSingleton<IStartable>(root => root.GetRequiredService<FixtureConsumer>());
            }))
            {
                await application.StartAsync(cancellation.Token);
                if (cancelDuringLoad) { throw new InvalidOperationException("Cancellation did not prevent publication."); }
                IServiceProvider root = application.Services;
                var consumer = root.GetRequiredService<FixtureConsumer>();
                if (FixtureConsumer.Constructions != 1 || !consumer.Defaults || consumer.StartupCalls != 1)
                { throw new InvalidOperationException("Defaults must reach the one consumer before its ordered start."); }
                using IServiceScope first = root.CreateScope();
                using IServiceScope second = root.CreateScope();
                sameConfigurationAcrossScopes = consumer.Roots.All(pair =>
                    ReferenceEquals(pair.Value, first.ServiceProvider.GetRequiredService(pair.Key)) &&
                    ReferenceEquals(pair.Value, second.ServiceProvider.GetRequiredService(pair.Key)));
                if (!sameConfigurationAcrossScopes ||
                    !ReferenceEquals(consumer.Manager, first.ServiceProvider.GetRequiredService<ITypeManager>()))
                { throw new InvalidOperationException("The root and scopes must reuse the exact prepared instances."); }
                ConfigurationRefreshStatus status = root.GetRequiredService<ConfigurationRuntime>().CaptureStatus();
                if (!status.ActivationCompleted || status.WatchState != ConfigurationWatchState.NotRequired ||
                    status.Entries.Count != 2 || status.Entries.Any(entry => !entry.IsValid || entry.Revision != 0 || entry.Disabled))
                { throw new InvalidOperationException("Defaults-only configuration must activate without a watch or a changed revision."); }
                typeCount = consumer.Manager.Types.Count;
                startupCalls = consumer.StartupCalls;
                await application.StopAsync();
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                unusedInitiallyLoaded, containerConstructed = FixtureConsumer.Constructions == 1,
                typeCount, configurationDefaults = true, sameConfigurationAcrossScopes, startupCalls, stopped = true
            }));
            return 0;
        }
        catch (OperationCanceledException) when (cancelDuringLoad && cancellation.IsCancellationRequested)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            { unusedInitiallyLoaded, canceledDuringLoad = true, containerConstructed = FixtureConsumer.Constructions != 0, stopped = true }));
            return 0;
        }
        catch (TypeManagerInitializationException error)
        {
            Console.Error.WriteLine("PREPARATION_FAILED: " + error.FailureKind);
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { unusedInitiallyLoaded, containerConstructed = FixtureConsumer.Constructions != 0 }));
            return 1;
        }
        finally
        {
            if (cancelDuringLoad) { AppDomain.CurrentDomain.AssemblyLoad -= cancelOnFixture; }
        }
    }
}

// Only Core contracts and string identities are referenced here, keeping Unused initially unloaded.
internal sealed class FixtureConsumer : IStartable
{
    private static int constructions;
    private readonly IConfigurationReader reader;
    internal static int Constructions => Volatile.Read(ref constructions);
    internal ITypeManager Manager { get; }
    internal IReadOnlyDictionary<Type, IConfigurationObject> Roots { get; }
    internal int StartupCalls { get; private set; }
    internal bool Defaults => ReadDefaults();

    public FixtureConsumer(ITypeManager manager, IConfigurationReader reader, IServiceProvider root)
    {
        Interlocked.Increment(ref constructions);
        Console.WriteLine("CONSUMER_CONSTRUCTED");
        Manager = manager;
        this.reader = reader;
        Type marker = manager.Types.Single(type => type.FullName == "CIS.TypeManagement.Fixtures.Contracts.IDiscoveryMarker");
        Type implementation = manager.GetImplementations(marker).Single();
        if (implementation.FullName != "CIS.TypeManagement.Fixtures.Unused.DiscoveredSettings" ||
            implementation.IsPublic || implementation.GetConstructor(Type.EmptyTypes) is not null)
        { throw new InvalidOperationException("Discovery lost the inherited, private-constructor marker implementation."); }
        if (!manager.PreparationReport.LoadedAssemblyIdentities.Any(identity =>
            identity.StartsWith("CIS.TypeManager.Fixture.Unused,", StringComparison.Ordinal)))
        { throw new InvalidOperationException("Preparation did not report loading the initially unused library."); }
        Type[] types = manager.GetImplementations<IConfigurationObject>().OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
        if (!types.Select(type => type.FullName).SequenceEqual(new[]
            { "CIS.TypeManagement.Fixtures.Unused.DirectConfiguration", "CIS.TypeManagement.Fixtures.Unused.InheritedConfiguration" }))
        { throw new InvalidOperationException("The full prepared query must discover exactly the two passive configuration roots."); }
        Roots = types.ToDictionary(type => type, type => (IConfigurationObject)root.GetRequiredService(type));
        if (!ReadDefaults()) { throw new InvalidOperationException("Configuration defaults were unavailable during consumer construction."); }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartupCalls++;
        if (!ReadDefaults()) { throw new InvalidOperationException("Startup did not receive the retained defaults."); }
        return Task.CompletedTask;
    }

    private bool ReadDefaults()
    {
        IConfigurationObject direct = Roots.Single(pair => pair.Key.Name == "DirectConfiguration").Value;
        IConfigurationObject inherited = Roots.Single(pair => pair.Key.Name == "InheritedConfiguration").Value;
        return reader.Read(direct, value => (string)value.GetType().GetProperty("Greeting")!.GetValue(value)!) == "fixture-default" &&
            reader.Read(inherited, value => (int)value.GetType().GetProperty("BatchSize")!.GetValue(value)!) == 42;
    }
}
