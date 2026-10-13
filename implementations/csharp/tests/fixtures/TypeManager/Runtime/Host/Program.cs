using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Text.Json;
using CIS.TypeManagement;

if (args.Length != 3) { throw new ArgumentException("Expected mode, fixture root, output directory."); }
string mode = args[0];
string fixtures = Path.GetFullPath(args[1]);
string output = Path.GetFullPath(args[2]);
Directory.CreateDirectory(output);
var host = typeof(Program).Assembly;
var selected = new List<Selection>();
CopySelection(host.Location);
CopySelection(typeof(TypeManager).Assembly.Location);
TypeManagerFailureKind? expected = null;
AssemblyLoadContext? foreignContext = null;
Func<AssemblyLoadContext, AssemblyName, Assembly?>? foreignResolver = null;
switch (mode)
{
    case "partial-types":
        CopySelection(Path.Combine(fixtures, "Broken/bin/Release/net10.0/CIS.TypeManager.BrokenFixture.dll"));
        expected = TypeManagerFailureKind.TypeDiscoveryFailure;
        break;
    case "validation-before-load":
        if (AssemblyLoadContext.Default.Assemblies.Any(assembly => assembly.GetName().Name == "CIS.TypeManager.BrokenFixture")) { throw new InvalidOperationException("Earlier fixture was preloaded."); }
        CopySelection(Path.Combine(fixtures, "Broken/bin/Release/net10.0/CIS.TypeManager.BrokenFixture.dll"));
        CopySelection(Path.Combine(fixtures, "versions/ref/CIS.TypeManager.ConflictFixture.dll"));
        expected = TypeManagerFailureKind.AssemblyLoadFailure;
        break;
    case "dynamic-name-collision":
        var dynamicName = AssemblyName.GetAssemblyName(Path.Combine(fixtures, "versions/v1/CIS.TypeManager.ConflictFixture.dll"));
        AssemblyBuilder.DefineDynamicAssembly(dynamicName, AssemblyBuilderAccess.Run).DefineDynamicModule("dynamic-impostor");
        CopySelection(Path.Combine(fixtures, "versions/v1/CIS.TypeManager.ConflictFixture.dll"));
        break;
    case "foreign-resolver-result":
        string foreignPath = Path.Combine(fixtures, "Forwarded/bin/Release/net10.0/CIS.TypeManager.ForwardedFixture.dll");
        foreignContext = new AssemblyLoadContext("unsupported-foreign-fixture", isCollectible: false);
        var foreignAssembly = foreignContext.LoadFromAssemblyPath(foreignPath);
        foreignResolver = (_, requested) => requested.Name == foreignAssembly.GetName().Name ? foreignAssembly : null;
        AssemblyLoadContext.Default.Resolving += foreignResolver;
        CopySelection(foreignPath);
        expected = TypeManagerFailureKind.AssemblyLoadFailure;
        break;
    case "file-identity-conflict":
        var wrong = CopySelection(Path.Combine(fixtures, "versions/v2/CIS.TypeManager.ConflictFixture.dll"));
        selected.Remove(wrong);
        selected.Add(wrong with { assemblyName = wrong.assemblyName.Replace("Version=2.0.0.0", "Version=1.0.0.0", StringComparison.Ordinal) });
        expected = TypeManagerFailureKind.AssemblyIdentityConflict;
        break;
    case "loaded-identity-conflict":
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(fixtures, "versions/v1/CIS.TypeManager.ConflictFixture.dll"));
        CopySelection(Path.Combine(fixtures, "versions/v2/CIS.TypeManager.ConflictFixture.dll"));
        expected = TypeManagerFailureKind.AssemblyIdentityConflict;
        break;
    case "tpa-selected-newer-version":
        CopySelection(Path.Combine(fixtures, "versions/v2/CIS.TypeManager.ConflictFixture.dll"));
        expected = TypeManagerFailureKind.AssemblyIdentityConflict;
        break;
    case "reference-reuse":
        CopySelection(Path.Combine(fixtures, "versions/v1/CIS.TypeManager.ConflictFixture.dll"));
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(fixtures, "versions/v1/CIS.TypeManager.ConflictFixture.dll"));
        // Matching identity in Default must not disguise a selected reference-only file.
        File.Copy(Path.Combine(fixtures, "versions/ref/CIS.TypeManager.ConflictFixture.dll"), Path.Combine(output, "CIS.TypeManager.ConflictFixture.dll"), true);
        expected = TypeManagerFailureKind.AssemblyLoadFailure;
        break;
    case "self-defined-reference-reuse":
        CopySelection(Path.Combine(fixtures, "versions/v1/CIS.TypeManager.ConflictFixture.dll"));
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(fixtures, "versions/v1/CIS.TypeManager.ConflictFixture.dll"));
        File.Copy(Path.Combine(fixtures, "versions/self-reference/CIS.TypeManager.ConflictFixture.dll"), Path.Combine(output, "CIS.TypeManager.ConflictFixture.dll"), true);
        expected = TypeManagerFailureKind.AssemblyLoadFailure;
        break;
    case "invalid-executable":
        var invalid = CopySelection(Path.Combine(fixtures, "versions/v1/CIS.TypeManager.ConflictFixture.dll"));
        File.WriteAllText(Path.Combine(output, invalid.relativePath), "not executable metadata");
        expected = TypeManagerFailureKind.AssemblyLoadFailure;
        break;
    case "locationless-host":
        using (var stream = File.OpenRead(Path.Combine(fixtures, "Forwarded/bin/Release/net10.0/CIS.TypeManager.ForwardedFixture.dll")))
        {
            host = AssemblyLoadContext.Default.LoadFromStream(stream);
        }
        if (!string.IsNullOrEmpty(host.Location)) { throw new InvalidOperationException("The locationless host fixture unexpectedly has a location."); }
        expected = TypeManagerFailureKind.UnsupportedDeployment;
        break;
    case "forwarding-and-collisions":
        CopySelection(Path.Combine(fixtures, "versions/v1/CIS.TypeManager.ConflictFixture.dll"));
        CopySelection(Path.Combine(fixtures, "Forwarder/bin/Release/net10.0/CIS.TypeManager.ForwarderFixture.dll"));
        CopySelection(Path.Combine(fixtures, "Forwarded/bin/Release/net10.0/CIS.TypeManager.ForwardedFixture.dll"));
        break;
    default: throw new ArgumentException(mode);
}
string manifest = Path.Combine(output, "fixture.types.json");
File.WriteAllText(manifest, JsonSerializer.Serialize(new { schemaVersion = 1, hostAssembly = host.FullName, assemblies = selected }));
bool containerConstructed = false;
try
{
    var manager = await TypeManager.CreateAsync(new TypeManagerOptions(host) { AssemblyManifestPath = manifest });
    if (expected is not null) { throw new InvalidOperationException("Preparation unexpectedly succeeded."); }
    if (mode == "dynamic-name-collision")
    {
        var actual = manager.Assemblies.Single(assembly => assembly.GetName().Name == "CIS.TypeManager.ConflictFixture");
        if (actual.IsDynamic || manager.Types.All(type => type.FullName != "RuntimeFixtures.Collision")) { throw new InvalidOperationException("Dynamic impostor replaced the selected executable."); }
        if (!manager.PreparationReport.LoadedAssemblyIdentities.Contains(actual.FullName!) || manager.PreparationReport.ReusedAssemblyIdentities.Contains(actual.FullName!))
        {
            throw new InvalidOperationException("Dynamic impostor caused a newly loaded executable to be reported as reused.");
        }
        containerConstructed = true;
        Console.WriteLine(JsonSerializer.Serialize(new { mode, success = true, containerConstructed }));
        return;
    }
    var forwarder = manager.Assemblies.Single(assembly => assembly.GetName().Name == "CIS.TypeManager.ForwarderFixture");
    if (manager.GetDefinedTypes(forwarder).Count != 0) { throw new InvalidOperationException("Forwarded types were counted as definitions."); }
    if (manager.Types.Count(type => type.FullName == "RuntimeFixtures.ForwardedType") != 1) { throw new InvalidOperationException("Forwarded definition was lost or duplicated."); }
    var collisions = manager.Types.Where(type => type.FullName == "RuntimeFixtures.Collision").ToArray();
    if (collisions.Length != 2) { throw new InvalidOperationException("Cross-assembly name collision was lost."); }
    foreach (var type in collisions)
    {
        if (!manager.TryGetType(type.AssemblyQualifiedName!, out var observed) || observed != type) { throw new InvalidOperationException("Exact identity was collapsed."); }
    }
    containerConstructed = true;
    Console.WriteLine(JsonSerializer.Serialize(new { mode, success = true, containerConstructed, typeCount = manager.Types.Count, selected = manager.Assemblies.Count }));
}
catch (TypeManagerInitializationException error) when (expected is not null)
{
    if (mode == "validation-before-load" && AssemblyLoadContext.Default.Assemblies.Any(assembly => assembly.GetName().Name == "CIS.TypeManager.BrokenFixture"))
    {
        throw new InvalidOperationException("Preparation loaded an earlier assembly before validating every selection.", error);
    }
    if (error.FailureKind != expected || error.InnerException is null || error.ManifestPath != manifest ||
        (expected != TypeManagerFailureKind.UnsupportedDeployment && string.IsNullOrWhiteSpace(error.AssetPath)) || string.IsNullOrWhiteSpace(error.AssemblyIdentity))
    {
        throw new InvalidOperationException("Failure diagnostics do not match the expected contract.", error);
    }
    if (expected == TypeManagerFailureKind.TypeDiscoveryFailure && (error.InnerException is not ReflectionTypeLoadException || error.LoaderExceptions.Count == 0))
    {
        throw new InvalidOperationException("Partial reflection failure lost loader details.", error);
    }
    if (expected == TypeManagerFailureKind.AssemblyIdentityConflict && string.IsNullOrWhiteSpace(error.ActualAssemblyIdentity))
    {
        throw new InvalidOperationException("Identity conflict lost the actual identity.", error);
    }
    if (mode == "foreign-resolver-result" && !error.InnerException.Message.Contains("must bind to a nondynamic assembly in the default loading context", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("The fixture did not reach Type Manager's custom-context rejection.", error);
    }
    Console.WriteLine(JsonSerializer.Serialize(new { mode, success = true, containerConstructed, failureKind = error.FailureKind.ToString(), loaderExceptions = error.LoaderExceptions.Count }));
}
finally
{
    if (foreignResolver is not null) { AssemblyLoadContext.Default.Resolving -= foreignResolver; }
    if (foreignContext?.IsCollectible == true) { foreignContext.Unload(); }
}

Selection CopySelection(string path)
{
    string name = Path.GetFileName(path);
    var selection = new Selection(AssemblyName.GetAssemblyName(path).FullName, name);
    File.Copy(path, Path.Combine(output, name), true);
    selected.Add(selection);
    return selection;
}
internal sealed record Selection(string assemblyName, string relativePath);
