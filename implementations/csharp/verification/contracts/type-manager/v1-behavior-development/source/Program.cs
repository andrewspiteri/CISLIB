using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CIS.TypeManagement;
using CompatibilityFixtures;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v10.0", FrameworkDisplayName = ".NET 10.0")]

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
var host = typeof(Program).Assembly;
const string extraName = "CIS.TypeManager.BehaviorExtra";
const string extraIdentity = extraName + ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
bool extraInitiallyUnloaded = !AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == extraName);
string mode = args[0];
string manifest = args[1];
var defaults = new TypeManagerOptions(host);
var values = new[] { "InvalidInventory", "AssemblyLoadFailure", "AssemblyIdentityConflict", "TypeDiscoveryFailure", "UnsupportedDeployment" }
    .ToDictionary(name => name, name => Convert.ToInt32(Enum.Parse<TypeManagerFailureKind>(name), CultureInfo.InvariantCulture));
var snapshot = new Dictionary<string, object?>
{
    ["hostIdentity"] = host.FullName,
    ["defaults"] = new { hostPreserved = ReferenceEquals(host, defaults.HostAssembly), manifestNull = defaults.AssemblyManifestPath is null, preparedCount = defaults.PrecomputedContracts.Count },
    ["failureValues"] = values
};

if (mode == "arguments")
{
    var nullHostOptions = new TypeManagerOptions(null!);
    snapshot["outcome"] = new
    {
        status = "arguments",
        nullOptions = Capture(() => TypeManager.CreateAsync(null!)),
        nullHost = Capture(() => TypeManager.CreateAsync(nullHostOptions)),
        blankPath = Capture(() => TypeManager.CreateAsync(new TypeManagerOptions(host) { AssemblyManifestPath = " " })),
        nullContracts = Capture(() => TypeManager.CreateAsync(new TypeManagerOptions(host) { PrecomputedContracts = null! })),
        nullContract = Capture(() => TypeManager.CreateAsync(new TypeManagerOptions(host) { PrecomputedContracts = [null!] })),
        valueContract = Capture(() => TypeManager.CreateAsync(new TypeManagerOptions(host) { PrecomputedContracts = [typeof(int)] })),
        openContract = Capture(() => TypeManager.CreateAsync(new TypeManagerOptions(host) { PrecomputedContracts = [typeof(List<>)] })),
        nullManager = Capture(() => TypeManagerExtensions.GetImplementations<IMarker>(null!))
    };
}
else
{
    var options = mode == "default" ? defaults : new TypeManagerOptions(host)
    {
        AssemblyManifestPath = mode == "relative" ? Path.GetFileName(manifest) : manifest,
        PrecomputedContracts = mode == "prepared" ? [typeof(IMarker), typeof(IMarker)] : []
    };
    bool returned = false;
    Task<ITypeManager>? pending = null;
    try
    {
        pending = TypeManager.CreateAsync(options, mode == "cancel" ? new CancellationToken(canceled: true) : CancellationToken.None);
        returned = true;
        var manager = await pending;
        var matches = manager.GetImplementations<IMarker>();
        var empty = manager.GetImplementations<IUnimplemented>();
        var report = manager.PreparationReport;
        int before = AppDomain.CurrentDomain.GetAssemblies().Length;
        bool extraLookupFound = manager.TryGetType("CompatibilityExtra.AvailableType, " + extraIdentity, out _);
        bool miss = !manager.TryGetType("Missing.Type, Missing.Assembly", out var missing) && missing is null;
        bool noLoads = before == AppDomain.CurrentDomain.GetAssemblies().Length;
        string identity = typeof(ZCandidate).AssemblyQualifiedName!;
        string changedAssemblyCase = identity.Replace(", " + host.GetName().Name + ",", ", " + host.GetName().Name!.ToLowerInvariant() + ",", StringComparison.Ordinal);
        bool extraBindable = Assembly.Load(extraIdentity).GetType("CompatibilityExtra.AvailableType") is not null;
        int selectedCount = mode == "selected" ? 2 : 1;
        snapshot["outcome"] = new
        {
            status = "success",
            factoryReturned = returned,
            selectedCount = manager.Assemblies.Count,
            preparedCount = report.PreparedContractCount,
            markers = Names(matches),
            baseMatches = Names(manager.GetImplementations<MarkerBase>()),
            selfMatches = Names(manager.GetImplementations<ZCandidate>()),
            varianceMatches = Names(manager.GetImplementations<IProducer<object>>()),
            emptyCount = empty.Count,
            cached = ReferenceEquals(matches, manager.GetImplementations(typeof(IMarker))) && ReferenceEquals(empty, manager.GetImplementations<IUnimplemented>()),
            exact = manager.TryGetType(identity, out var found) && found == typeof(ZCandidate),
            ordinalMisses = !manager.TryGetType(changedAssemblyCase, out _) &&
                !manager.TryGetType(typeof(ZCandidate).FullName + ", " + host.GetName().Name, out _) && !manager.TryGetType(" " + identity, out _),
            unknownMiss = miss,
            lookupDoesNotLoad = noLoads,
            extraInitiallyUnloaded,
            extraLookupFound,
            extraBindable,
            fixedMembershipAfterLoad = manager.Assemblies.Count == selectedCount,
            catalogKinds = manager.Types.Contains(typeof(GenericCandidate<>)) && manager.Types.Contains(typeof(MarkerBase)) && manager.Types.Contains(typeof(IMarker)),
            ordered = Ordered(manager.Types) && Ordered(manager.GetDefinedTypes(host)),
            collectionsProtected = Protected(manager.Assemblies) && Protected(manager.Types) && Protected(manager.GetDefinedTypes(host)) && Protected(matches) && Protected(empty),
            reportProtected = Protected(report.SelectedAssemblyIdentities) && Protected(report.LoadedAssemblyIdentities) && Protected(report.ReusedAssemblyIdentities),
            reportConsistent = report.SelectedAssemblyCount == selectedCount && report.TypeCount == manager.Types.Count &&
                report.SelectedAssemblyIdentities.Contains(host.FullName!) && report.ReusedAssemblyIdentities.Single() == host.FullName &&
                (mode == "selected" ? report.LoadedAssemblyIdentities.Single() == extraIdentity : report.LoadedAssemblyIdentities.Count == 0) && report.Elapsed >= TimeSpan.Zero,
            activationCount = Counters.Activated,
            queryErrors = new
            {
                nullType = Capture(() => manager.GetImplementations(null!)),
                valueType = Capture(() => manager.GetImplementations(typeof(int))),
                nullAssembly = Capture(() => manager.GetDefinedTypes(null!)),
                outsideAssembly = Capture(() => manager.GetDefinedTypes(typeof(TypeManager).Assembly)),
                nullName = Capture(() => manager.TryGetType(null!, out _)),
                blankName = Capture(() => manager.TryGetType(" ", out _))
            }
        };
    }
    catch (TypeManagerInitializationException error)
    {
        snapshot["outcome"] = new
        {
            status = "failure",
            factoryReturned = returned,
            faultedTask = pending?.IsFaulted == true,
            kind = error.FailureKind.ToString(),
            value = (int)error.FailureKind,
            causePresent = error.InnerException is not null,
            manifestMatches = error.ManifestPath == Path.GetFullPath(manifest),
            diagnosticContainsManifest = error.Message.Contains(manifest, StringComparison.Ordinal),
            assetPresent = error.AssetPath is not null,
            selectedIdentityPresent = error.AssemblyIdentity is not null,
            actualIdentityPresent = error.ActualAssemblyIdentity is not null,
            loaderCount = error.LoaderExceptions.Count,
            loadersProtected = Protected(error.LoaderExceptions)
        };
    }
    catch (OperationCanceledException)
    {
        snapshot["outcome"] = new { status = "cancelled", factoryReturned = returned, cancelledTask = pending?.IsCanceled == true };
    }
}
Console.WriteLine(JsonSerializer.Serialize(snapshot));

static string Capture(Action action)
{
    try { action(); return "no synchronous error"; }
    catch (ArgumentException error) { return error.GetType().Name + ":" + error.ParamName; }
}

static string[] Names(IEnumerable<Type> types) => types.Select(type => type.FullName!).ToArray();
static bool Ordered(IEnumerable<Type> types) => types.Select(type => type.AssemblyQualifiedName).SequenceEqual(types.Select(type => type.AssemblyQualifiedName).OrderBy(name => name, StringComparer.Ordinal));
static bool Protected<T>(IReadOnlyList<T> values)
{
    if (values is not IList<T> list) { return false; }
    try { list.Clear(); return false; }
    catch (NotSupportedException) { }
    if (list.Count > 0)
    {
        try { list[0] = values[0]; return false; }
        catch (NotSupportedException) { }
    }
    return true;
}

namespace CompatibilityFixtures
{
    internal static class Counters { internal static int Activated; }
    internal interface IMarker;
    internal interface IUnimplemented;
    internal abstract class MarkerBase : IMarker;
    internal sealed class ZCandidate : MarkerBase
    {
        private ZCandidate() { Counters.Activated++; throw new InvalidOperationException("Unexpected activation."); }
    }
    internal sealed class ÉCandidate : IMarker
    {
        private ÉCandidate() { Counters.Activated++; throw new InvalidOperationException("Unexpected activation."); }
    }
    internal sealed class GenericCandidate<T> : IMarker;
    internal interface IProducer<out T>;
    internal sealed class StringProducer : IProducer<string>;
}
