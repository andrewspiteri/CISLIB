using System.Text.Json;
using CIS.TypeManagement;
using CIS.TypeManagement.Examples;

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

// Application code may now construct its services. This console intentionally
// needs no DI container or ASP.NET shared framework; see the separate Web sample.
Console.WriteLine("APPLICATION_READY");
Console.WriteLine(JsonSerializer.Serialize(new
{
    unusedInitiallyLoaded,
    candidates = candidates.Select(type => new { type = type.FullName, assembly = type.Assembly.FullName }),
    selected = manager.Assemblies.Select(assembly => assembly.FullName),
    report = manager.PreparationReport,
    fixedMembershipChecked = args.Length != 0
}));
return 0;
