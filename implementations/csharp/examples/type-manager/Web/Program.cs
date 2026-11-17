using System.Text.Json;
using CIS.Configuration;
using CIS.Configuration.Hosting;
using CIS.Startup.Hosting;
using CIS.TypeManagement;
using CIS.TypeManagement.Examples;
using CIS.TypeManagement.Examples.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

bool verify = args is ["--verify", _];
bool unusedInitiallyLoaded = AppDomain.CurrentDomain.GetAssemblies()
    .Any(assembly => assembly.GetName().Name == "CIS.TypeManager.Examples.Unused");
if (unusedInitiallyLoaded) { throw new InvalidOperationException("Run this example in a fresh process."); }
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

await using var store = new LiteralConfigurationStore(typeof(Program).Assembly.GetName().Name!);
await using var configuration = await ConfigurationRuntime.PrepareAsync(manager,
    new ConfigurationOptions(typeof(Program).Assembly.GetName().Name!, store) { IsHealthCritical = false });
Console.WriteLine("CONFIGURATION_PREPARED");
// Keep an explicit bootstrap owner too: Build can fail before DI captures the runtime.
Console.WriteLine("BUILDER_CREATING");
var builder = WebApplication.CreateBuilder(verify ? [] : args);
builder.Logging.ClearProviders();
builder.Host.UseDefaultServiceProvider(options => { options.ValidateScopes = true; options.ValidateOnBuild = true; });
if (verify) { builder.WebHost.UseUrls("http://127.0.0.1:0"); }
builder.Services.AddCisConfiguration(configuration);
builder.Services.AddSingleton<ITypeManager>(manager);
builder.Services.AddScoped<CatalogConsumer>();
ConfigurationExample.AddConsumers(builder.Services, store);
builder.Services.AddCisStartup();
Console.WriteLine("CONTAINER_BUILDING");
var app = builder.Build();
Console.WriteLine("CONTAINER_BUILT");

object configurationReport;
try
{
    using (var first = app.Services.CreateScope())
    using (var second = app.Services.CreateScope())
    {
        var firstConsumer = first.ServiceProvider.GetRequiredService<CatalogConsumer>();
        var secondConsumer = second.ServiceProvider.GetRequiredService<CatalogConsumer>();
        if (ReferenceEquals(firstConsumer, secondConsumer) ||
            !ReferenceEquals(firstConsumer, first.ServiceProvider.GetRequiredService<CatalogConsumer>()) ||
            !ReferenceEquals(manager, app.Services.GetRequiredService<ITypeManager>()) ||
            !ReferenceEquals(manager, firstConsumer.Manager) || !ReferenceEquals(manager, secondConsumer.Manager) ||
            !ReferenceEquals(candidates, firstConsumer.Manager.GetImplementations<IDiscoveryExample>()) ||
            !ReferenceEquals(candidates, secondConsumer.Manager.GetImplementations<IDiscoveryExample>()))
        { throw new InvalidOperationException("DI must reuse the prepared manager and its cached query across scopes."); }
    }

    app.MapGet("/catalog", ([FromServices] CatalogConsumer consumer, HttpContext context) => new
    {
        consumerId = consumer.InstanceId,
        sameConsumerWithinRequest = ReferenceEquals(consumer, context.RequestServices.GetRequiredService<CatalogConsumer>()),
        samePreparedManager = ReferenceEquals(manager, consumer.Manager),
        sameCachedQuery = ReferenceEquals(candidates, consumer.Manager.GetImplementations<IDiscoveryExample>()),
        candidates = consumer.Manager.GetImplementations<IDiscoveryExample>()
            .Select(type => new { type = type.FullName, assembly = type.Assembly.FullName }),
        configuration = ConfigurationExample.Capture(context.RequestServices)
    });
    // Verify mode uses the actual awaited host lifecycle too, including Kestrel binding.
    await app.StartAsync();
    configurationReport = await ConfigurationExample.VerifyAndRefreshAsync(app.Services, manager, store);
    if (verify) { SampleVerification.CheckFixedMembership(manager, args[1]); }
    else { await app.WaitForShutdownAsync(); }
}
finally
{
    await configuration.StopAsync();
    await app.StopAsync(CancellationToken.None);
    await app.DisposeAsync();
}
if (!store.WatchDisposed || store.Disposals != 0)
{ throw new InvalidOperationException("Host stop must release Configuration's watch without disposing the host-owned store."); }
if (verify)
{
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        unusedInitiallyLoaded,
        candidates = candidates.Select(type => new { type = type.FullName, assembly = type.Assembly.FullName }),
        selected = manager.Assemblies.Select(assembly => assembly.FullName),
        report = manager.PreparationReport,
        rootAndScopesShareManager = true,
        queryIdentityStable = true,
        fixedMembershipChecked = true,
        configuration = configurationReport,
        configurationWatchDisposed = store.WatchDisposed,
        configurationStoreDisposedByRuntime = store.Disposals != 0
    }));
}
return 0;
