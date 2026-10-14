using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using CIS.TypeManagement;
using PerformanceFixtures;

if (args.Length == 1 && args[0] == "--identities")
{
    Console.WriteLine(JsonSerializer.Serialize(Directory.GetFiles(AppContext.BaseDirectory, "*.dll")
        .ToDictionary(path => Path.GetFileNameWithoutExtension(path), path => AssemblyName.GetAssemblyName(path).FullName)));
    return;
}

string mode = args[0];
int libraryCount = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
int classesPerLibrary = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
int iterations = int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
if (mode is not ("eager" or "deferred") || iterations < 1) { throw new ArgumentException("Invalid measurement configuration."); }
var process = Process.GetCurrentProcess();
var host = Assembly.GetExecutingAssembly();
var options = new TypeManagerOptions(host)
{
    PrecomputedContracts = [typeof(IPrepared)]
};
// Touch the measurement APIs before taking a baseline, not the manager factory.
_ = Stopwatch.GetTimestamp();
_ = GC.GetTotalAllocatedBytes(precise: true);
_ = GC.GetAllocatedBytesForCurrentThread();
_ = Cost.Since(Stopwatch.GetTimestamp(), GC.GetTotalAllocatedBytes(precise: true));
_ = Query(() => Array.Empty<Type>(), 1);
_ = Jit();
var baseline = Memory();
bool librariesInitiallyUnloaded = !AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name!.StartsWith("Perf.", StringComparison.Ordinal));
ITypeManager? manager = null;
Cost preparation = default;

long startupAllocated = GC.GetTotalAllocatedBytes(precise: true);
long startupStarted = Stopwatch.GetTimestamp();
if (mode == "eager") { manager = await Prepare(); }
var bootstrapBoundary = Cost.Since(startupStarted, startupAllocated);
bool preparedAtBoundary = manager is not null;

long firstUseAllocated = GC.GetTotalAllocatedBytes(precise: true);
long firstUseStarted = Stopwatch.GetTimestamp();
manager ??= await Prepare();
long preparedAllocated = GC.GetAllocatedBytesForCurrentThread();
long preparedStarted = Stopwatch.GetTimestamp();
var prepared = manager.GetImplementations(typeof(IPrepared));
var firstPrepared = new QueryCost(Stopwatch.GetElapsedTime(preparedStarted).TotalMilliseconds,
    GC.GetAllocatedBytesForCurrentThread() - preparedAllocated, 1, prepared.Count);
var firstUse = Cost.Since(firstUseStarted, firstUseAllocated);
var afterPreparedLookup = Memory();

Func<IReadOnlyList<Type>> preparedQuery = () => manager.GetImplementations(typeof(IPrepared));
Func<IReadOnlyList<Type>> lazyQuery = () => manager.GetImplementations(typeof(ILazy));
Func<IReadOnlyList<Type>> referenceQuery = () => prepared;
var firstUncached = Query(lazyQuery, 1);
var lazy = manager.GetImplementations(typeof(ILazy));
string exactName = prepared[0].AssemblyQualifiedName!;
Func<bool> exactQuery = () => manager.TryGetType(exactName, out var found) && found == prepared[0];
// Warm the same delegates and loop bodies that the recorded batches use.
for (int pass = 0; pass < 2; pass++)
{
    _ = Query(preparedQuery, iterations);
    _ = Query(lazyQuery, iterations);
    _ = Query(referenceQuery, iterations);
    _ = Lookup(exactQuery, iterations);
    Thread.Sleep(250);
}
const int batchCount = 20;
var preparedBatches = new List<QueryCost>(batchCount);
var lazyBatches = new List<QueryCost>(batchCount);
var controlBatches = new List<QueryCost>(batchCount);
var lookupBatches = new List<QueryCost>(batchCount);
var jitBeforeBatches = Jit();
for (int batch = 0; batch < batchCount; batch++)
{
    for (int offset = 0; offset < 4; offset++)
    {
        switch ((batch + offset) % 4)
        {
            case 0: preparedBatches.Add(Query(preparedQuery, iterations)); break;
            case 1: lazyBatches.Add(Query(lazyQuery, iterations)); break;
            case 2: controlBatches.Add(Query(referenceQuery, iterations)); break;
            case 3: lookupBatches.Add(Lookup(exactQuery, iterations)); break;
        }
    }
}
var jitAfterBatches = Jit();
var preparedCached = SumCosts(preparedBatches);
var lazyCached = SumCosts(lazyBatches);
var loopControl = SumCosts(controlBatches);
var exactLookup = SumCosts(lookupBatches, sumResults: true);
var afterQueries = Memory();

int expectedPrepared = libraryCount * ((classesPerLibrary + 1) / 2);
int expectedLazy = libraryCount * ((classesPerLibrary + 2) / 3);
bool valid = librariesInitiallyUnloaded && preparedAtBoundary == (mode == "eager") &&
    manager.Assemblies.Count == libraryCount + 5 && manager.PreparationReport.LoadedAssemblyIdentities.Count == libraryCount + 2 &&
    manager.PreparationReport.ReusedAssemblyIdentities.Count == 3 && manager.PreparationReport.PreparedContractCount == 1 &&
    prepared.Count == expectedPrepared && lazy.Count == expectedLazy && firstUncached.ResultCount == expectedLazy &&
    preparedBatches.All(batch => batch.ResultCount == expectedPrepared) && lazyBatches.All(batch => batch.ResultCount == expectedLazy) &&
    lookupBatches.All(batch => batch.ResultCount == iterations) && Counters.Activated == 0 &&
    ReferenceEquals(prepared, manager.GetImplementations<IPrepared>()) && ReferenceEquals(lazy, manager.GetImplementations<ILazy>()) &&
    manager.Assemblies.Where(assembly => assembly.GetName().Name!.StartsWith("Perf.", StringComparison.Ordinal))
        .All(assembly => manager.GetDefinedTypes(assembly).Count == classesPerLibrary) &&
    manager.Types.Count == manager.Assemblies.Sum(assembly => manager.GetDefinedTypes(assembly).Count);
if (!valid) { throw new InvalidOperationException("The measurement fixture failed its correctness checks."); }

Console.WriteLine(JsonSerializer.Serialize(new
{
    schemaVersion = 1,
    valid,
    processId = Environment.ProcessId,
    mode,
    iterations,
    librariesInitiallyUnloaded,
    preparedAtBoundary,
    libraryCount,
    classesPerLibrary,
    selectedAssemblyCount = manager.Assemblies.Count,
    catalogTypeCount = manager.Types.Count,
    preparedMatchCount = prepared.Count,
    lazyMatchCount = lazy.Count,
    bootstrapBoundary,
    firstUse,
    firstPrepared,
    preparation,
    preparationReportMilliseconds = manager.PreparationReport.Elapsed.TotalMilliseconds,
    firstUncached,
    preparedCached,
    lazyCached,
    loopControl,
    exactLookup,
    batches = new { preparedBatches, lazyBatches, controlBatches, lookupBatches },
    batchCount,
    jitBeforeBatches,
    jitAfterBatches,
    baseline,
    afterPreparedLookup,
    afterQueries,
    retainedManagedBytes = afterQueries.ManagedBytes - baseline.ManagedBytes,
    retainedPrivateBytes = afterQueries.PrivateBytes - baseline.PrivateBytes,
    retainedWorkingSetBytes = afterQueries.WorkingSetBytes - baseline.WorkingSetBytes,
    memoryScope = "Whole-process changes after the baseline, including runtime, loaded images, reflection, query and measurement state; not manager-only retained size",
    privateBytesSource = OperatingSystem.IsWindows() ? "Process.PrivateMemorySize64: Windows private bytes" : "Process.PrivateMemorySize64: Linux VmData virtual data mappings",
    environment = new
    {
        processorCount = Environment.ProcessorCount,
        os = RuntimeInformation.OSDescription,
        osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
        processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
        framework = RuntimeInformation.FrameworkDescription,
        runtime = Environment.Version.ToString(),
        serverGc = GCSettings.IsServerGC,
        gcLatencyMode = GCSettings.LatencyMode.ToString(),
        stopwatchFrequency = Stopwatch.Frequency,
        tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
        tieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO")
    }
}));
GC.KeepAlive(manager);

async Task<ITypeManager> Prepare()
{
    long allocated = GC.GetTotalAllocatedBytes(precise: true);
    long started = Stopwatch.GetTimestamp();
    var result = await TypeManager.CreateAsync(options);
    preparation = Cost.Since(started, allocated);
    return result;
}

MemorySample Memory()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    long managed = GC.GetTotalMemory(forceFullCollection: true);
    process.Refresh();
    return new MemorySample(managed, process.PrivateMemorySize64, process.WorkingSet64);
}

static QueryCost Query(Func<IReadOnlyList<Type>> query, int count)
{
    IReadOnlyList<Type>? result = null;
    long allocated = GC.GetAllocatedBytesForCurrentThread();
    long started = Stopwatch.GetTimestamp();
    for (int index = 0; index < count; index++) { result = query(); }
    double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
    GC.KeepAlive(result);
    return new QueryCost(elapsed, bytes, count, result!.Count);
}

static QueryCost Lookup(Func<bool> lookup, int count)
{
    int found = 0;
    long allocated = GC.GetAllocatedBytesForCurrentThread();
    long started = Stopwatch.GetTimestamp();
    for (int index = 0; index < count; index++)
    {
        if (lookup()) { found++; }
    }
    double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    return new QueryCost(elapsed, GC.GetAllocatedBytesForCurrentThread() - allocated, count, found);
}

static QueryCost SumCosts(IReadOnlyList<QueryCost> batches, bool sumResults = false) => new(
    batches.Sum(batch => batch.Milliseconds), batches.Sum(batch => batch.AllocatedBytes),
    batches.Sum(batch => batch.Operations), sumResults ? batches.Sum(batch => batch.ResultCount) : batches[0].ResultCount);

static JitSample Jit() => new(JitInfo.GetCompiledMethodCount(currentThread: false),
    JitInfo.GetCompilationTime(currentThread: false).TotalMilliseconds);

internal readonly record struct JitSample(long CompiledMethods, double CompilationMilliseconds);

internal readonly record struct Cost(double Milliseconds, long AllocatedBytes)
{
    public string AllocationScope => "Process-wide managed allocation, including background/runtime work";
    internal static Cost Since(long started, long allocated) => new(Stopwatch.GetElapsedTime(started).TotalMilliseconds, GC.GetTotalAllocatedBytes(precise: true) - allocated);
}

internal readonly record struct QueryCost(double Milliseconds, long AllocatedBytes, int Operations, int ResultCount)
{
    public string AllocationScope => "Calling-thread managed allocation during synchronous query loop";
}
internal readonly record struct MemorySample(long ManagedBytes, long PrivateBytes, long WorkingSetBytes);
