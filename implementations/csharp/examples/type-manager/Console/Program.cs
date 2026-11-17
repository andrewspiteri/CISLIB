using System.Text.Json;
using CIS.Configuration;
using CIS.Configuration.Hosting;
using CIS.Startup.Hosting;
using CIS.TypeManagement;
using CIS.TypeManagement.Examples;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

bool unusedInitiallyLoaded = AppDomain.CurrentDomain.GetAssemblies()
    .Any(assembly => assembly.GetName().Name == "CIS.TypeManager.Examples.Unused");
if (unusedInitiallyLoaded) { throw new InvalidOperationException("Run this example in a fresh process."); }
if (args.Length != 0 && args is not ["--verify", _]) { throw new ArgumentException("Expected no arguments, or --verify <late-test-assembly>."); }

Console.WriteLine("PREPARING");
ITypeManager manager;
try
{
    manager = await TypeManager.CreateAsync(new TypeManagerOptions(typeof(Program).Assembly)
    {
        PrecomputedContracts = [typeof(IDiscoveryExample)]
    });
}
catch (TypeManagerInitializationException error)
{
    Console.Error.WriteLine($"PREPARATION_FAILED: {error.FailureKind}\n{error}");
    return 1;
}

Console.WriteLine("PREPARATION_COMPLETE");
var candidates = manager.GetImplementations<IDiscoveryExample>();
foreach (var type in candidates) { Console.WriteLine($"{type.FullName} | {type.Assembly.FullName}"); }
Console.WriteLine($"Prepared {manager.PreparationReport.SelectedAssemblyCount} assemblies and {manager.PreparationReport.TypeCount} types in {manager.PreparationReport.Elapsed.TotalMilliseconds:F2} ms.");
if (args is ["--verify", var lateAssembly]) { SampleVerification.CheckFixedMembership(manager, lateAssembly); }

// Bootstrap completes before the one actual container and its consumers exist.
await using var store = new LiteralConfigurationStore(typeof(Program).Assembly.GetName().Name!);
await using var configuration = await ConfigurationRuntime.PrepareAsync(manager,
    new ConfigurationOptions(typeof(Program).Assembly.GetName().Name!, store) { IsHealthCritical = false });
Console.WriteLine("CONFIGURATION_PREPARED");
Console.WriteLine("BUILDER_CREATING");
IHostBuilder builder = Host.CreateDefaultBuilder()
    .UseDefaultServiceProvider(options => { options.ValidateScopes = true; options.ValidateOnBuild = true; })
    .ConfigureLogging(logging => logging.ClearProviders())
    .ConfigureServices(services =>
    {
        services.AddCisConfiguration(configuration);
        services.AddSingleton<ITypeManager>(manager);
        ConfigurationExample.AddConsumers(services, store);
        services.AddCisStartup();
    });
Console.WriteLine("CONTAINER_BUILDING");
IHost host = builder.Build();
Console.WriteLine("CONTAINER_BUILT");
object configurationReport;
try
{
    await host.StartAsync();
    configurationReport = await ConfigurationExample.VerifyAndRefreshAsync(host.Services, manager, store);
    Console.WriteLine("APPLICATION_READY");
}
finally
{
    await configuration.StopAsync();
    await host.StopAsync(CancellationToken.None);
    if (host is IAsyncDisposable asynchronous) { await asynchronous.DisposeAsync(); }
    else { host.Dispose(); }
}
if (!store.WatchDisposed || store.Disposals != 0)
{ throw new InvalidOperationException("Host stop must release Configuration's watch without disposing the host-owned store."); }
Console.WriteLine(JsonSerializer.Serialize(new
{
    unusedInitiallyLoaded,
    candidates = candidates.Select(type => new { type = type.FullName, assembly = type.Assembly.FullName }),
    selected = manager.Assemblies.Select(assembly => assembly.FullName),
    report = manager.PreparationReport,
    fixedMembershipChecked = args.Length != 0,
    configuration = configurationReport,
    configurationWatchDisposed = store.WatchDisposed,
    configurationStoreDisposedByRuntime = store.Disposals != 0
}));
return 0;
