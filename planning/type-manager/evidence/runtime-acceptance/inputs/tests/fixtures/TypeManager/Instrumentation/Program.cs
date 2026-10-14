using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using CIS.TypeManagement;

if (args.Length != 2) { throw new ArgumentException("Expected mode and output directory."); }
string mode = args[0];
if (mode is not "enumeration" and not "publication-cancellation") { throw new ArgumentException(mode); }
string output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var host = typeof(Program).Assembly;
string libraryPath = Path.Combine(AppContext.BaseDirectory, "CIS.TypeManager.ProbeLibrary.dll");
string libraryIdentity = AssemblyName.GetAssemblyName(libraryPath).FullName;
if (AssemblyLoadContext.Default.Assemblies.Any(assembly => assembly.FullName == libraryIdentity))
{
    throw new InvalidOperationException("Probe library was loaded before preparation.");
}
string[] paths = [host.Location, typeof(TypeManager).Assembly.Location, libraryPath];
var entries = paths.Select(path => new
{
    assemblyName = AssemblyName.GetAssemblyName(path).FullName,
    relativePath = Path.GetFileName(path)
}).ToArray();
foreach (string path in paths) { File.Copy(path, Path.Combine(output, Path.GetFileName(path))); }
string manifest = Path.Combine(output, "probe.types.json");
File.WriteAllText(manifest, JsonSerializer.Serialize(new { schemaVersion = 1, hostAssembly = host.FullName, assemblies = entries }));
using var cancellation = new CancellationTokenSource();
int publicationVisits = 0;
TypeManagerTestProbe.BeforePublication = () =>
{
    publicationVisits++;
    if (mode == "publication-cancellation") { cancellation.Cancel(); }
};
ITypeManager? manager = null;
OperationCanceledException? canceled = null;
Dictionary<Assembly, int>? repeatedCounts = null;
var preparation = TypeManager.CreateAsync(new TypeManagerOptions(host)
{
    AssemblyManifestPath = manifest,
    PrecomputedContracts = [typeof(IDisposable)]
}, cancellation.Token);
try
{
    try { manager = await preparation; }
    catch (OperationCanceledException error) { canceled = error; }
    if (publicationVisits != 1) { throw new InvalidOperationException("Publication probe was not reached exactly once."); }
    var counts = TypeManagerTestProbe.Snapshot();
    if (counts.Count != entries.Length || counts.Any(pair => pair.Value != 1) ||
        !counts.Keys.Select(assembly => assembly.FullName).ToHashSet().SetEquals(entries.Select(entry => entry.assemblyName)))
    {
        throw new InvalidOperationException("ENUMERATION_COUNT: Expected one actual GetTypes call for every selection.");
    }
    if (mode == "publication-cancellation")
    {
        if (manager is not null || !preparation.IsCanceled || canceled?.CancellationToken != cancellation.Token)
        {
            throw new InvalidOperationException("PUBLICATION_CANCELLATION: Cancellation at the final boundary published a manager.");
        }
        if (!AssemblyLoadContext.Default.Assemblies.Any(assembly => assembly.FullName == libraryIdentity))
        {
            throw new InvalidOperationException("Cancellation falsely rolled back an already loaded assembly.");
        }
    }
    else
    {
        if (manager is null || canceled is not null) { throw new InvalidOperationException("Instrumented preparation did not succeed."); }
        var ready = manager;
        await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            _ = ready.GetImplementations<IDisposable>();
            _ = ready.GetImplementations<object>();
            _ = ready.GetImplementations<IEquatable<DateTime>>();
            foreach (var assembly in ready.Assemblies) { _ = ready.GetDefinedTypes(assembly); }
            if (!ready.TryGetType(typeof(Program).AssemblyQualifiedName!, out var found) || found != typeof(Program))
            {
                throw new InvalidOperationException("Prepared exact lookup failed.");
            }
        })));
        var afterQueries = TypeManagerTestProbe.Snapshot();
        if (afterQueries.Count != counts.Count || counts.Any(pair => afterQueries.GetValueOrDefault(pair.Key) != pair.Value))
        {
            throw new InvalidOperationException("QUERY_ENUMERATION: A prepared query enumerated an assembly.");
        }
        var second = await TypeManager.CreateAsync(new TypeManagerOptions(host) { AssemblyManifestPath = manifest });
        repeatedCounts = TypeManagerTestProbe.Snapshot();
        if (ReferenceEquals(ready, second) || !ready.Types.SequenceEqual(second.Types) || publicationVisits != 2 ||
            repeatedCounts.Count != counts.Count || counts.Any(pair => repeatedCounts.GetValueOrDefault(pair.Key) != 2) ||
            second.PreparationReport.LoadedAssemblyIdentities.Count != 0 || second.PreparationReport.ReusedAssemblyIdentities.Count != entries.Length)
        {
            throw new InvalidOperationException("REPEATED_PREPARATION_ENUMERATION: A separate preparation must enumerate each reused selection once again.");
        }
    }
    using var exclusive = new FileStream(manifest, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        mode,
        success = true,
        publicationVisits,
        canceled = preparation.IsCanceled,
        published = manager is not null,
        manifestReopenedExclusively = exclusive.CanWrite,
        repeatedEnumerations = repeatedCounts?.ToDictionary(pair => pair.Key.FullName!, pair => pair.Value),
        enumerations = counts.OrderBy(pair => pair.Key.FullName, StringComparer.Ordinal)
            .Select(pair => new { assembly = pair.Key.FullName, calls = pair.Value }).ToArray()
    }));
}
finally { TypeManagerTestProbe.BeforePublication = null; }
