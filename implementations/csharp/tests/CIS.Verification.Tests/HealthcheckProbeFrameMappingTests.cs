using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using CIS.Healthcheck;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using P = CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildProtocol;

namespace CIS.Tests.Verification;

[TestClass]
public sealed class HealthcheckProbeFrameMappingTests
{
    [TestMethod]
    public void ActualShippingProbeMapsTheSixArgumentWorkerAndRejectsTheAsyncFacadeFrame()
    {
        Assembly core = typeof(HealthcheckService).Assembly;
        Type probe = core.GetType(P.ProbeType, throwOnError: true)!;
        MethodInfo[] methods = probe.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Where(method => method.Name == "RunAsync").ToArray();
        Assert.HasCount(2, methods);
        MethodInfo facade = methods.Single(method => method.GetParameters().Length == 4);
        MethodInfo worker = methods.Single(method => method.GetParameters().Length == 6);
        Assert.AreEqual(worker.MetadataToken, P.ProbeRunAsyncToken(core.Location));
        MethodInfo body = MoveNext(worker);
        P.VerifyProbeNullFrame(core.Location, core.ManifestModule.ModuleVersionId, body.MetadataToken, body.DeclaringType!.FullName!, body.Name);
        MethodInfo forwardingBody = MoveNext(facade);
        Assert.ThrowsExactly<InvalidOperationException>(() => P.VerifyProbeNullFrame(core.Location, core.ManifestModule.ModuleVersionId,
            forwardingBody.MetadataToken, forwardingBody.DeclaringType!.FullName!, forwardingBody.Name));
    }

    [TestMethod]
    [DataRow("legacy")]
    [DataRow("pair")]
    [DataRow("worker-first")]
    public void ExactLegacyOrKnownPairMetadataPreservesTheActualBodyMapping(string mode)
    {
        using var fixture = new MetadataFixture(mode);
        Assert.AreEqual(fixture.SelectedRunToken, P.ProbeRunAsyncToken(fixture.Path));
        P.VerifyProbeNullFrame(fixture.Path, fixture.Mvid, fixture.SelectedMoveToken, fixture.SelectedState, "MoveNext");
    }

    [TestMethod]
    [DataRow("worker-only")]
    [DataRow("extra")]
    [DataRow("duplicate-facade")]
    [DataRow("bad-clock")]
    [DataRow("bad-parser")]
    [DataRow("bad-core-type")]
    [DataRow("public-worker")]
    [DataRow("instance-worker")]
    [DataRow("generic-worker")]
    [DataRow("bad-return")]
    public void UnexpectedOverloadsOrSignatureChangesCannotSelectAnArbitraryFrame(string mode)
    {
        using var fixture = new MetadataFixture(mode);
        Assert.ThrowsExactly<InvalidOperationException>(() => P.ProbeRunAsyncToken(fixture.Path));
        Assert.ThrowsExactly<InvalidOperationException>(() => P.VerifyProbeNullFrame(fixture.Path, fixture.Mvid,
            fixture.SelectedMoveToken, fixture.SelectedState, "MoveNext"));
    }

    private static MethodInfo MoveNext(MethodInfo method) => method.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType
        .GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)!;

    // Minimal PE signature/state-machine parser controls only. No synthetic subject executes.
    private sealed class MetadataFixture : IDisposable
    {
        private readonly TemporaryDirectory directory = new();
        internal string Path => System.IO.Path.Combine(directory.Path, "probe-metadata.dll");
        internal Guid Mvid { get; } = Guid.NewGuid();
        internal int SelectedRunToken { get; }
        internal int SelectedMoveToken { get; }
        internal string SelectedState { get; }

        internal MetadataFixture(string mode)
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString("probe-metadata.dll"), metadata.GetOrAddGuid(Mvid), default, default);
            metadata.AddAssembly(metadata.GetOrAddString("Probe.MetadataControl"), new(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.None);
            AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(metadata.GetOrAddString("System.Runtime"), new(10, 0, 0, 0), default, default, 0, default);
            TypeReferenceHandle Reference(string ns, string name) => metadata.AddTypeReference(runtime, metadata.GetOrAddString(ns), metadata.GetOrAddString(name));
            TypeReferenceHandle task = Reference("System.Threading.Tasks", "Task`1"), stream = Reference("System.IO", "Stream"), writer = Reference("System.IO", "TextWriter");
            TypeReferenceHandle cancellation = Reference("System.Threading", "CancellationToken"), clock = Reference("System", "TimeProvider"), function = Reference("System", "Func`4");
            TypeReferenceHandle stateInterface = Reference("System.Runtime.CompilerServices", "IAsyncStateMachine");
            TypeReferenceHandle attribute = Reference("System.Runtime.CompilerServices", "AsyncStateMachineAttribute"), systemType = Reference("System", "Type");
            bool[] workers = mode switch { "legacy" => [false], "worker-only" => [true], "worker-first" => [true, false], "extra" => [false, true, false], "duplicate-facade" => [false, false], _ => [false, true] };
            int selected = Array.IndexOf(workers, true); if (selected < 0) { selected = 0; }
            SelectedRunToken = MetadataTokens.GetToken(MetadataTokens.MethodDefinitionHandle(selected + 1));
            SelectedMoveToken = MetadataTokens.GetToken(MetadataTokens.MethodDefinitionHandle(workers.Length + selected + 1));
            SelectedState = P.ProbeType + "+State" + selected;
            metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            TypeDefinitionHandle probe = metadata.AddTypeDefinition(TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed,
                metadata.GetOrAddString("CIS.Healthcheck.Pipes"), metadata.GetOrAddString("HealthProbeClient"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            TypeDefinitionHandle view = metadata.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed, metadata.GetOrAddString("CIS.Healthcheck"), metadata.GetOrAddString("HealthReportView"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(workers.Length + 1));
            TypeDefinitionHandle parsed = metadata.AddTypeDefinition(TypeAttributes.NotPublic | TypeAttributes.Sealed, metadata.GetOrAddString("CIS.Healthcheck.Reporting"),
                metadata.GetOrAddString(mode == "bad-core-type" ? "UnexpectedResponse" : "HealthParsedResponse"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(workers.Length + 1));
            for (int index = 0; index < workers.Length; index++)
            {
                TypeDefinitionHandle state = metadata.AddTypeDefinition(TypeAttributes.NestedPrivate | TypeAttributes.Sealed, default, metadata.GetOrAddString("State" + index), default,
                    MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(workers.Length + index + 1));
                metadata.AddNestedType(state, probe); metadata.AddInterfaceImplementation(state, stateInterface);
            }
            var ctor = new BlobBuilder(); ctor.WriteByte(0x20); ctor.WriteByte(1); ctor.WriteByte(1); Type(ctor, 0x12, systemType);
            MemberReferenceHandle constructor = metadata.AddMemberReference(attribute, metadata.GetOrAddString(".ctor"), metadata.GetOrAddBlob(ctor));
            for (int index = 0; index < workers.Length; index++)
            {
                bool worker = workers[index]; var signature = new BlobBuilder();
                signature.WriteByte(worker && mode == "generic-worker" ? (byte)0x10 : worker && mode == "instance-worker" ? (byte)0x20 : (byte)0);
                if (worker && mode == "generic-worker") { signature.WriteByte(1); }
                signature.WriteByte(worker ? (byte)6 : (byte)4);
                signature.WriteByte(0x15); Type(signature, 0x12, task); signature.WriteByte(1); signature.WriteByte(worker && mode == "bad-return" ? (byte)0x0e : (byte)0x08);
                signature.WriteByte(0x1d); signature.WriteByte(0x0e); Type(signature, 0x12, stream); Type(signature, 0x12, writer);
                if (worker)
                {
                    if (mode == "bad-clock") { signature.WriteByte(0x0e); } else { Type(signature, 0x12, clock); }
                    signature.WriteByte(0x15); Type(signature, 0x12, function); signature.WriteByte(4);
                    signature.WriteByte(0x1d); signature.WriteByte(0x05); Type(signature, 0x11, view); signature.WriteByte(0x08);
                    if (mode == "bad-parser") { signature.WriteByte(0x0e); } else { Type(signature, 0x12, parsed); }
                }
                Type(signature, 0x11, cancellation);
                MethodAttributes access = worker && mode == "public-worker" ? MethodAttributes.Public : MethodAttributes.Assembly;
                MethodAttributes attributes = access | MethodAttributes.HideBySig;
                if (!(worker && mode == "instance-worker")) { attributes |= MethodAttributes.Static; }
                MethodDefinitionHandle method = metadata.AddMethodDefinition(attributes, MethodImplAttributes.IL, metadata.GetOrAddString("RunAsync"), metadata.GetOrAddBlob(signature), 0, MetadataTokens.ParameterHandle(1));
                var mapping = new BlobBuilder(); mapping.WriteUInt16(1); mapping.WriteSerializedString(P.ProbeType + "+State" + index); mapping.WriteUInt16(0);
                metadata.AddCustomAttribute(method, constructor, metadata.GetOrAddBlob(mapping));
            }
            for (int index = 0; index < workers.Length; index++)
            {
                metadata.AddMethodDefinition(MethodAttributes.Private, MethodImplAttributes.IL, metadata.GetOrAddString("MoveNext"), metadata.GetOrAddBlob(new byte[] { 0x20, 0, 1 }), 0, MetadataTokens.ParameterHandle(1));
            }
            var pe = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(metadata), new BlobBuilder(), flags: CorFlags.ILOnly);
            var image = new BlobBuilder(); pe.Serialize(image);
            using FileStream output = new(Path, FileMode.CreateNew); image.WriteContentTo(output);
        }

        private static void Type(BlobBuilder signature, byte kind, EntityHandle type)
        {
            signature.WriteByte(kind); signature.WriteCompressedInteger((MetadataTokens.GetRowNumber(type) << 2) | (type.Kind == HandleKind.TypeReference ? 1 : 0));
        }

        public void Dispose() => directory.Dispose();
    }
}
