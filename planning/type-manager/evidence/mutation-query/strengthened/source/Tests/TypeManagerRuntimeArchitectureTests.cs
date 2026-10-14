using System.Reflection;
using System.Reflection.Emit;
using CIS.TypeManagement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class TypeManagerRuntimeArchitectureTests
{
    private static readonly Assembly Core = typeof(TypeManager).Assembly;
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);

    [TestMethod]
    public void ModuleCannotActivateApplicationsSubscribeResolversOrDependOnOtherModules()
    {
        foreach (var type in Core.GetTypes().Where(type => type.Namespace == "CIS.TypeManagement"))
        {
            foreach (var method in Methods(type))
            {
                foreach (var target in Calls(method)) { RejectForbiddenCall(target, query: false); }
            }
        }
    }

    [TestMethod]
    public void PublicQueryCallGraphsCannotReadFilesLoadOrEnumerateAssemblies()
    {
        foreach (var name in new[] { nameof(TypeManager.GetDefinedTypes), nameof(TypeManager.GetImplementations), nameof(TypeManager.TryGetType) })
        {
            InspectQuery(typeof(TypeManager).GetMethod(name)!);
        }
        InspectQuery(typeof(TypeManagerExtensions).GetMethod(nameof(TypeManagerExtensions.GetImplementations))!.MakeGenericMethod(typeof(object)));
    }

    [TestMethod]
    public void OnlyThePreparationStateMachineContainsADefinitionEnumerationCall()
    {
        var callers = Core.GetTypes().Where(type => type.Namespace == "CIS.TypeManagement").SelectMany(Methods)
            .SelectMany(method => Calls(method).Where(target => target.DeclaringType == typeof(Assembly) && target.Name == nameof(Assembly.GetTypes)).Select(_ => method)).ToArray();
        Assert.HasCount(1, callers);
        Assert.AreEqual("MoveNext", callers[0].Name);
        StringAssert.Contains(callers[0].DeclaringType!.Name, "<CreateCoreAsync>");
        // This is a structural guard, not a replacement for executed enumeration counters.
    }

    [TestMethod]
    [DataRow(nameof(ActivationProbe))]
    [DataRow(nameof(EnumerationProbe))]
    [DataRow(nameof(LoadProbe))]
    [DataRow(nameof(ResolverProbe))]
    [DataRow(nameof(FileProbe))]
    [DataRow(nameof(IndirectFileProbe))]
    public void QueryInspectorRejectsRealForbiddenAndTransitiveCalls(string method)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => InspectQuery(typeof(TypeManagerRuntimeArchitectureTests).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!));
    }

    private static object? ActivationProbe(Type type) => Activator.CreateInstance(type);
    private static Type[] EnumerationProbe(Assembly assembly) => assembly.GetTypes();
    private static Assembly LoadProbe(string name) => Assembly.Load(name);
    private static void ResolverProbe() => AppDomain.CurrentDomain.AssemblyResolve += (_, _) => null;
    private static string FileProbe(string path) => File.ReadAllText(path);
    private static string IndirectFileProbe(string path) => FileProbe(path);

    private static IEnumerable<MethodBase> Methods(Type type) => type
        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Cast<MethodBase>().Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance));

    private static void InspectQuery(MethodBase root)
    {
        var pending = new Stack<MethodBase>();
        var visited = new HashSet<MethodBase>();
        pending.Push(root);
        while (pending.TryPop(out var method))
        {
            if (!visited.Add(method)) { continue; }
            foreach (var target in Calls(method))
            {
                RejectForbiddenCall(target, query: true);
                if (target.Module.Assembly == Core || target.DeclaringType == typeof(TypeManagerRuntimeArchitectureTests)) { pending.Push(target); }
            }
        }
    }

    private static void RejectForbiddenCall(MethodBase target, bool query)
    {
        string type = target.DeclaringType?.FullName ?? "";
        bool forbidden = type == "System.Activator" || type.StartsWith("CIS.Logging", StringComparison.Ordinal) ||
            type.StartsWith("CIS.Configuration", StringComparison.Ordinal) || type == "CIS.EnvironmentOptions" ||
            type.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal) || type.StartsWith("Microsoft.Build", StringComparison.Ordinal) ||
            type is "System.Threading.Thread" or "System.Threading.Timer" or "System.Threading.ThreadPool" or "System.IO.Directory" ||
            type == "System.Runtime.CompilerServices.RuntimeHelpers" && target.Name == "RunClassConstructor" ||
            type.StartsWith("System.Reflection.", StringComparison.Ordinal) && !type.StartsWith("System.Reflection.Metadata.", StringComparison.Ordinal) && target.Name is "Invoke" or "GetCustomAttributes" ||
            type == "System.Attribute" && target.Name.StartsWith("GetCustomAttribute", StringComparison.Ordinal) ||
            type == "System.Threading.Tasks.Task" && target.Name == "Run" ||
            target.Name is "add_AssemblyLoad" or "add_AssemblyResolve" or "add_Resolving" or "add_ResolvingUnmanagedDll";
        if (query)
        {
            forbidden |= type.StartsWith("System.IO.", StringComparison.Ordinal) ||
                type == "System.Reflection.Assembly" && target.Name is "GetTypes" or "GetExportedTypes" or "get_DefinedTypes" or "GetType" or "Load" or "LoadFrom" or "LoadFile" ||
                type == "System.Runtime.Loader.AssemblyLoadContext" && target.Name.StartsWith("Load", StringComparison.Ordinal) ||
                type == "System.Type" && target.Name == "GetType" && target.IsStatic;
        }
        if (forbidden) { throw new InvalidDataException("Forbidden Type Manager call: " + type + "." + target.Name); }
    }

    private static IEnumerable<MethodBase> Calls(MethodBase method)
    {
        byte[]? body = method.GetMethodBody()?.GetILAsByteArray();
        if (body is null) { yield break; }
        for (int offset = 0; offset < body.Length;)
        {
            short key = body[offset++] == 0xfe ? unchecked((short)(0xfe00 | body[offset++])) : body[offset - 1];
            var code = Codes[key];
            if (code.OperandType == OperandType.InlineMethod)
            {
                yield return method.Module.ResolveMethod(BitConverter.ToInt32(body, offset), method.DeclaringType?.GetGenericArguments(),
                    method.IsGenericMethod ? method.GetGenericArguments() : null)!;
            }
            offset += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(body, offset),
                _ => 4
            };
        }
    }
}
