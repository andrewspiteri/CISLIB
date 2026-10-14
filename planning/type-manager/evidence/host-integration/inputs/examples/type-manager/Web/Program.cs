using System.Text.Json;
using CIS.TypeManagement;
using CIS.TypeManagement.Examples;
using CIS.TypeManagement.Examples.Web;
using Microsoft.AspNetCore.Mvc;

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

// Even the Web builder is created after preparation. Register this exact instance,
// not a factory that starts discovery during dependency resolution.
Console.WriteLine("BUILDER_CREATING");
var builder = WebApplication.CreateBuilder(verify ? [] : args);
builder.Services.AddSingleton<ITypeManager>(manager);
builder.Services.AddScoped<CatalogConsumer>();
Console.WriteLine("CONTAINER_BUILDING");
await using var app = builder.Build();
Console.WriteLine("CONTAINER_BUILT");

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
    {
        throw new InvalidOperationException("DI must reuse the prepared manager and its cached query across scopes.");
    }
}

if (verify)
{
    SampleVerification.CheckFixedMembership(manager, args[1]);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        unusedInitiallyLoaded,
        candidates = candidates.Select(type => new { type = type.FullName, assembly = type.Assembly.FullName }),
        selected = manager.Assemblies.Select(assembly => assembly.FullName),
        report = manager.PreparationReport,
        rootAndScopesShareManager = true,
        queryIdentityStable = true,
        fixedMembershipChecked = true
    }));
    return 0;
}

app.MapGet("/catalog", ([FromServices] CatalogConsumer consumer, HttpContext context) => new
{
    consumerId = consumer.InstanceId,
    sameConsumerWithinRequest = ReferenceEquals(consumer, context.RequestServices.GetRequiredService<CatalogConsumer>()),
    samePreparedManager = ReferenceEquals(manager, consumer.Manager),
    sameCachedQuery = ReferenceEquals(candidates, consumer.Manager.GetImplementations<IDiscoveryExample>()),
    candidates = consumer.Manager.GetImplementations<IDiscoveryExample>()
        .Select(type => new { type = type.FullName, assembly = type.Assembly.FullName })
});
await app.RunAsync();
return 0;
