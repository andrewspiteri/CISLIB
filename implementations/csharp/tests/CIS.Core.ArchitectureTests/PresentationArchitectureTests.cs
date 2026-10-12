using System.Reflection;
using System.Reflection.Emit;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class PresentationArchitectureTests
{
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);
    [TestMethod]
    public void RendererTakesOnlyOwnedCaptureAndExplicitPresentationChoices()
    {
        Assembly assembly = typeof(LogEventData).Assembly;
        Type renderer = assembly.GetType("CIS.Logging.Rendering.EventRenderer")!;
        Assert.IsFalse(renderer.IsPublic); Assert.IsTrue(renderer.IsAbstract && renderer.IsSealed);
        MethodInfo render = renderer.GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Static)!;
        CollectionAssert.AreEqual(new[] { "CIS.Logging.Events.EncodedEvent", typeof(LogOutputFormat).FullName!, typeof(HumanOutputDetail).FullName!, typeof(int).FullName! }, render.GetParameters().Select(item => item.ParameterType.FullName!).ToArray());
        Type result = assembly.GetType("CIS.Logging.Rendering.RenderedEvent")!;
        Assert.IsFalse(result.IsPublic); Assert.IsTrue(result.IsValueType); Assert.AreEqual(0, result.GetConstructors().Length);
        foreach (FieldInfo field in result.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        { Assert.IsTrue(field.IsPrivate && field.IsInitOnly); Assert.IsTrue(field.FieldType == typeof(ReadOnlyMemory<byte>) || field.FieldType == typeof(bool)); }
        foreach (PropertyInfo property in result.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance)) { Assert.IsNull(property.GetSetMethod(true)); }
    }
    [TestMethod]
    public void RenderingIlHasNoAmbientRoutingFilteringOrTerminalCalls()
    {
        foreach (Type type in typeof(LogEventData).Assembly.GetTypes().Where(type => type.Namespace == "CIS.Logging.Rendering"))
        {
            foreach (MethodBase method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Cast<MethodBase>().Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)))
            { AssertNoAmbientCalls(method); }
        }
    }
    [TestMethod]
    [DataRow(nameof(TerminalProbe))]
    [DataRow(nameof(DebuggerProbe))]
    [DataRow(nameof(EnvironmentProbe))]
    [DataRow(nameof(ClockProbe))]
    public void IlBoundaryInspectorRejectsRealForbiddenCallSites(string name)
    {
        MethodInfo method = typeof(PresentationArchitectureTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.ThrowsExactly<InvalidDataException>(() => AssertNoAmbientCalls(method));
    }
    private static DateTime ClockProbe() => DateTime.UtcNow;
    private static bool TerminalProbe() => Console.IsOutputRedirected;
    private static bool DebuggerProbe() => System.Diagnostics.Debugger.IsAttached;
    private static string? EnvironmentProbe() => Environment.GetEnvironmentVariable("CIS_TEST_NOT_READ");
    private static void AssertNoAmbientCalls(MethodBase method)
    {
        byte[]? body = method.GetMethodBody()?.GetILAsByteArray();
        if (body is null) { return; }
        for (int offset = 0; offset < body.Length;)
        {
            short key = body[offset++] == 0xFE ? unchecked((short)(0xFE00 | body[offset++])) : body[offset - 1];
            OpCode code = Codes[key];
            if (code.OperandType == OperandType.InlineMethod)
            {
                MethodBase target = method.Module.ResolveMethod(BitConverter.ToInt32(body, offset), method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null)!;
                string name = target.DeclaringType?.FullName ?? "";
                if (name is "System.Console" or "System.Environment" or "System.Diagnostics.Debugger" or "CIS.EnvironmentOptions" or "System.TimeProvider" or "System.Diagnostics.Activity" or "CIS.Logging.LoggingOptions" or "CIS.Logging.CoreLogger" or "CIS.Logging.CoreLogRuntime" || name.StartsWith("System.IO.", StringComparison.Ordinal) || name.StartsWith("CIS.Logging.Runtime.", StringComparison.Ordinal) || name.StartsWith("CIS.Logging.Scopes.", StringComparison.Ordinal)
                    || name is "System.DateTime" or "System.DateTimeOffset" && target.Name is "get_Now" or "get_UtcNow")
                { throw new InvalidDataException("Rendering calls an ambient/filter/destination dependency: " + name + "." + target.Name); }
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
