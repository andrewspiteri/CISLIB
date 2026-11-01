using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace CIS.Verification.HealthcheckMutationChildren;

// Verification-only source linked into the integration test assembly and real fixtures.
internal static class HealthcheckMutationChildProtocol
{
    internal const string Schema = "cis.healthcheck.stryker-children/v4-development";
    internal const string RegistrationType = "CIS.Healthcheck.AspNetCore.HealthcheckApplicationBuilderExtensions";
    internal const string RegistrationMethod = "UseCisHealthcheck";
    internal const string ProbeType = "CIS.Healthcheck.Pipes.HealthProbeClient";
    internal const string ProbeFrameError = "The probe failure frame is not the exact mapped Core RunAsync async MoveNext.";
    internal const uint NoWindowsErrorReporting = 0x0002;
    internal const string WindowsErrorModeError = "Missing or invalid mutation-child Windows error mode witness.";
    internal const string SessionVariable = "CIS_HEALTHCHECK_MUTATION_SESSION";
    internal const string LaunchVariable = "CIS_HEALTHCHECK_MUTATION_CHILD_LAUNCH";
    internal const string SelectorVariable = "CIS_HEALTHCHECK_MUTATION_CHILD_ID";
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }

    internal static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    internal static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    internal static T Read<T>(string path)
    {
        Require(new FileInfo(path).Length <= 16 * 1024 * 1024, "Child evidence input exceeds its bounded size.");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidOperationException("Missing child evidence object.");
    }

    internal static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions) + "\n");
        File.Move(temporary, path, overwrite: false);
    }

    internal static bool Under(string root, string path) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, PathComparison);

    internal static string Input(string root, string relative)
    {
        Require(!Path.IsPathRooted(relative) && relative.Length > 0, "Expected a relative child input path.");
        string path = Path.GetFullPath(Path.Combine(root, relative));
        Require(Under(root, path), "Child input escaped its root.");
        Require(File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "Missing or linked child input.");
        return path;
    }

    internal static FilePin[] Files(string root)
    {
        FilePin[] files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => new FilePin(Path.GetRelativePath(root, path).Replace('\\', '/'), Hash(Input(root, Path.GetRelativePath(root, path)))))
            .OrderBy(pin => pin.Path, StringComparer.Ordinal).ToArray();
        Require(files.Length is > 0 and <= 1024, "Unexpected child output file count.");
        return files;
    }

    internal static void VerifyFiles(string root, FilePin[] pins)
    {
        Require(pins.Length > 0 && pins.Select(pin => pin.Path).Distinct(StringComparer.Ordinal).Count() == pins.Length, "Duplicate child file pins.");
        foreach (FilePin pin in pins) { Require(Hash(Input(root, pin.Path)) == pin.Sha256, "Child input changed: " + pin.Path); }
        Require(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count() == pins.Length, "Unexpected files in the isolated child output.");
    }

    internal static Guid Mvid(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        return metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
    }

    internal static void VerifyRegistrationFrame(string path, Guid mvid, int token, string type, string method)
    {
        Require(type == RegistrationType && method == RegistrationMethod && (token & unchecked((int)0xff000000)) == 0x06000000,
            "The managed failure is not the exact allowed Core registration method.");
        using FileStream stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        Require(metadata.GetGuid(metadata.GetModuleDefinition().Mvid) == mvid, "The failure frame Core MVID changed.");
        MethodDefinition definition = metadata.GetMethodDefinition(System.Reflection.Metadata.Ecma335.MetadataTokens.MethodDefinitionHandle(token & 0x00ffffff));
        TypeDefinition declaring = metadata.GetTypeDefinition(definition.GetDeclaringType());
        Require(metadata.GetString(declaring.Namespace) + "." + metadata.GetString(declaring.Name) == type
            && metadata.GetString(definition.Name) == method && definition.Attributes.HasFlag(MethodAttributes.Public)
            && definition.Attributes.HasFlag(MethodAttributes.Static)
            && definition.GetParameters().Count(handle => metadata.GetParameter(handle).SequenceNumber > 0) == 4,
            "The recorded failure frame token does not identify the actual public Core registration method.");
    }

    internal static void VerifyProbeNullFrame(string path, Guid mvid, int token, string type, string method)
    {
        Require(method == "MoveNext" && (token & unchecked((int)0xff000000)) == 0x06000000, ProbeFrameError);
        using FileStream stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        Require(metadata.GetGuid(metadata.GetModuleDefinition().Mvid) == mvid, ProbeFrameError);
        MethodDefinition moveNext = metadata.GetMethodDefinition(System.Reflection.Metadata.Ecma335.MetadataTokens.MethodDefinitionHandle(token & 0x00ffffff));
        TypeDefinitionHandle stateHandle = moveNext.GetDeclaringType();
        TypeDefinition state = metadata.GetTypeDefinition(stateHandle);
        var provider = new ProbeSignatureProvider();
        MethodSignature<string> moveSignature = moveNext.DecodeSignature(provider, null);
        Require(TypeName(metadata, stateHandle) == type && metadata.GetString(moveNext.Name) == "MoveNext"
            && (moveNext.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private
            && !moveNext.Attributes.HasFlag(MethodAttributes.Static) && moveSignature.Header.IsInstance
            && moveSignature.Header.CallingConvention == SignatureCallingConvention.Default
            && moveSignature.GenericParameterCount == 0 && moveSignature.ParameterTypes.Length == 0 && moveSignature.ReturnType == "Void"
            && !state.GetDeclaringType().IsNil && TypeName(metadata, state.GetDeclaringType()) == ProbeType, ProbeFrameError);
        MethodDefinition run = ProbeRunAsync(metadata);
        Require(run.GetDeclaringType() == state.GetDeclaringType()
            && state.GetInterfaceImplementations().Any(handle => FrameworkTypeName(metadata, metadata.GetInterfaceImplementation(handle).Interface)
                == "System.Runtime.CompilerServices.IAsyncStateMachine"), ProbeFrameError);
        CustomAttribute[] mappings = run.GetCustomAttributes().Select(metadata.GetCustomAttribute).Where(attribute =>
        {
            if (attribute.Constructor.Kind != HandleKind.MemberReference) { return false; }
            MemberReference constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (metadata.GetString(constructor.Name) != ".ctor" || constructor.Parent.Kind != HandleKind.TypeReference
                || FrameworkTypeName(metadata, constructor.Parent) != "System.Runtime.CompilerServices.AsyncStateMachineAttribute") { return false; }
            MethodSignature<string> signature = constructor.DecodeMethodSignature(provider, null);
            return signature.Header.IsInstance && signature.GenericParameterCount == 0 && signature.ReturnType == "Void"
                && signature.ParameterTypes.SequenceEqual(["System.Type"], StringComparer.Ordinal);
        }).ToArray();
        Require(mappings.Length == 1, ProbeFrameError);
        BlobReader value = metadata.GetBlobReader(mappings[0].Value);
        Require(value.ReadUInt16() == 1, ProbeFrameError);
        string mapped = value.ReadSerializedString() ?? throw new InvalidOperationException(ProbeFrameError);
        string[] identity = mapped.Split(',', 2);
        Require(identity[0] == type && (identity.Length == 1 || new AssemblyName(identity[1].Trim()).FullName == AssemblyName.GetAssemblyName(path).FullName)
            && value.ReadUInt16() == 0 && value.RemainingBytes == 0, ProbeFrameError);
    }

    internal static int ProbeRunAsyncToken(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        _ = ProbeRunAsync(metadata);
        return System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(ProbeRunAsyncHandle(metadata));
    }

    private static MethodDefinition ProbeRunAsync(MetadataReader metadata)
    {
        return metadata.GetMethodDefinition(ProbeRunAsyncHandle(metadata));
    }

    private static MethodDefinitionHandle ProbeRunAsyncHandle(MetadataReader metadata)
    {
        MethodDefinitionHandle[] matches = metadata.MethodDefinitions.Where(handle =>
        {
            MethodDefinition method = metadata.GetMethodDefinition(handle);
            return metadata.GetString(method.Name) == "RunAsync" && TypeName(metadata, method.GetDeclaringType()) == ProbeType;
        }).ToArray();
        Require(matches.Length is 1 or 2, ProbeFrameError);
        string[] facade = ["String[]", "System.IO.Stream", "System.IO.TextWriter", "System.Threading.CancellationToken"];
        string[] worker = ["String[]", "System.IO.Stream", "System.IO.TextWriter", "System.TimeProvider",
            "System.Func`4<Byte[],CIS.Healthcheck.HealthReportView,Int32,CIS.Healthcheck.Reporting.HealthParsedResponse>", "System.Threading.CancellationToken"];
        var signatures = matches.Select(handle =>
        {
            MethodDefinition method = metadata.GetMethodDefinition(handle);
            MethodSignature<string> signature = method.DecodeSignature(new ProbeSignatureProvider(), null);
            Require((method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Assembly && method.Attributes.HasFlag(MethodAttributes.Static)
                && !signature.Header.IsInstance && signature.Header.CallingConvention == SignatureCallingConvention.Default && signature.GenericParameterCount == 0
                && signature.ReturnType == "System.Threading.Tasks.Task`1<Int32>", ProbeFrameError);
            return (Handle: handle, Parameters: signature.ParameterTypes);
        }).ToArray();
        Require(signatures.Count(item => item.Parameters.SequenceEqual(facade, StringComparer.Ordinal)) == 1, ProbeFrameError);
        if (matches.Length == 1) { return matches[0]; } // Historical four-argument Core body.
        var workers = signatures.Where(item => item.Parameters.SequenceEqual(worker, StringComparer.Ordinal)).ToArray();
        Require(workers.Length == 1, ProbeFrameError);
        return workers[0].Handle; // Current actual async body, rather than its forwarding facade.
    }

    private static string TypeName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        TypeDefinition type = metadata.GetTypeDefinition(handle);
        return type.GetDeclaringType().IsNil ? metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name)
            : TypeName(metadata, type.GetDeclaringType()) + "+" + metadata.GetString(type.Name);
    }

    private static string FrameworkTypeName(MetadataReader metadata, EntityHandle handle)
    {
        Require(handle.Kind == HandleKind.TypeReference, ProbeFrameError);
        TypeReference type = metadata.GetTypeReference((TypeReferenceHandle)handle);
        Require(type.ResolutionScope.Kind == HandleKind.AssemblyReference
            && (metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name) is "System.Runtime" or "System.Private.CoreLib"), ProbeFrameError);
        return metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
    }

    private sealed class ProbeSignatureProvider : ISignatureTypeProvider<string, object?>
    {
        public string GetPrimitiveType(PrimitiveTypeCode code) => code.ToString();
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
        public string GetTypeFromReference(MetadataReader metadata, TypeReferenceHandle handle, byte rawTypeKind) => FrameworkTypeName(metadata, handle);
        public string GetTypeFromSpecification(MetadataReader metadata, object? context, TypeSpecificationHandle handle, byte rawTypeKind) => metadata.GetTypeSpecification(handle).DecodeSignature(this, context);
        public string GetTypeFromDefinition(MetadataReader metadata, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            string name = TypeName(metadata, handle);
            Require(name is "CIS.Healthcheck.HealthReportView" or "CIS.Healthcheck.Reporting.HealthParsedResponse", ProbeFrameError);
            return name;
        }
        public string GetArrayType(string elementType, ArrayShape shape) => throw new InvalidOperationException(ProbeFrameError);
        public string GetByReferenceType(string elementType) => throw new InvalidOperationException(ProbeFrameError);
        public string GetFunctionPointerType(MethodSignature<string> signature) => throw new InvalidOperationException(ProbeFrameError);
        public string GetGenericMethodParameter(object? context, int index) => throw new InvalidOperationException(ProbeFrameError);
        public string GetGenericTypeParameter(object? context, int index) => throw new InvalidOperationException(ProbeFrameError);
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => throw new InvalidOperationException(ProbeFrameError);
        public string GetPinnedType(string elementType) => throw new InvalidOperationException(ProbeFrameError);
        public string GetPointerType(string elementType) => throw new InvalidOperationException(ProbeFrameError);
    }

    internal static Type? Controller(Assembly core)
    {
        Type[] matches = core.GetTypes().Where(type => type.Name == "MutantControl" && type.Namespace?.StartsWith("Stryker", StringComparison.Ordinal) == true).ToArray();
        Require(matches.Length <= 1, "Ambiguous Stryker controller in loaded Core.");
        return matches.SingleOrDefault();
    }

    internal static int Active(Type controller)
    {
        Require(controller.GetField("CaptureCoverage", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is false,
            "Child mutation qualification requires coverage-analysis=off; child coverage is not collected by VSTest.");
        MethodInfo active = controller.GetMethod("IsActive", BindingFlags.Public | BindingFlags.Static, [typeof(int)])
            ?? throw new InvalidOperationException("Missing pinned Stryker IsActive selector.");
        _ = active.Invoke(null, [int.MinValue]); // Resolve the tool's lazy environment selector; this ID is never a real mutant.
        int id = (int)(controller.GetField("ActiveMutant", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? throw new InvalidOperationException("Missing pinned Stryker ActiveMutant field."));
        Require(id >= -1, "Stryker selector was not initialized.");
        Require((bool)active.Invoke(null, [id >= 0 ? id : int.MinValue])! == (id >= 0), "Selected Stryker mutant is not active.");
        Require(!(bool)active.Invoke(null, [id == int.MaxValue ? int.MinValue : id + 1])!, "Stryker activated more than the frozen selector.");
        return id;
    }

    internal sealed record FilePin(string Path, string Sha256);
    internal sealed record Session(string Schema, string LanguageRoot, string OutputRoot, string ConfigurationPath,
        string ConfigurationSha256, string StrykerCorePath, string StrykerCoreSha256, string SelectorSourceSha256,
        string DotnetPath, string DotnetSha256, string CoreLibraryPath, string CoreLibrarySha256, FilePin[] SourceInputs,
        DateTimeOffset CreatedUtc, bool CoverageAnalysisOff, bool AllowInitialUninstrumentedBaseline,
        SourceReplayControl? SourceReplay = null, bool RequireWindowsErrorMode = false, int BoundIntentVersion = 0);
    internal sealed record SourceReplayControl(string Kind, string ReportPath, string ReportSha256, string OriginalId,
        string SourcePath, string OriginalSourceSha256, string CanonicalSourceSha256, string CurrentSourceSha256,
        int Offset, string OriginalText, string Replacement, string CorePath, string CoreSha256, Guid CoreMvid);
    internal sealed record Stage(string Directory, string OriginalDirectory, string AssemblyFile, string AssemblyName,
        string CoreSha256, Guid CoreMvid, string? ControllerType, FilePin[] OriginalFiles, FilePin[] DeployedFiles);
    internal sealed record Launch(string Schema, string Id, string SessionPath, string SessionSha256, DateTimeOffset CreatedUtc,
        int ParentPid, string StageOwnerId, string ParentAssemblyPath, string ParentAssemblySha256, string ParentCorePath, string ParentCoreSha256,
        Guid ParentCoreMvid, string? ControllerType, int ActiveMutant, string? OriginalSelectorName, string? OriginalSelectorValue,
        bool Instrumented, Stage Stage);
    internal sealed record Receipt(string Schema, string LaunchId, string LaunchSha256, DateTimeOffset ObservedUtc,
        int Pid, string EntryAssemblyPath, string EntryAssemblyName, string EntryAssemblySha256, string CorePath,
        string CoreSha256, Guid CoreMvid, string? ControllerType, int ActiveMutant, bool Instrumented, string Runtime,
        string DotnetPath, string DotnetSha256, string CoreLibrarySha256, string[] Arguments, string? Failure,
        WindowsErrorModeReceipt? WindowsErrorMode = null);
    internal sealed record WindowsErrorModeReceipt(int Pid, uint Before, uint SetReturnedPrevious, uint Requested,
        uint After, DateTimeOffset ObservedUtc);
    internal sealed record ExitReceipt(string LaunchId, int Pid, int ExitCode, DateTimeOffset ExitedUtc);
    internal sealed record ParentExitReceipt(string Schema, string LaunchId, string LaunchSha256, int ParentPid, int Pid,
        string FileName, string WorkingDirectory, string[] Arguments, DateTimeOffset StartedUtc, DateTimeOffset ObservedStartedUtc,
        DateTimeOffset ExitedUtc, DateTimeOffset ObservedUtc, int ExitCode, bool ForcedStop, bool TimedOut);
    internal sealed record ManagedFailureReceipt(string Schema, string LaunchId, string LaunchSha256, string OriginReceiptSha256,
        int Pid, DateTimeOffset ObservedUtc, bool IsTerminating, string ExceptionType, string CorePath, string CoreSha256,
        Guid CoreMvid, int MetadataToken, string DeclaringType, string MethodName, int FrameIndex, int ActiveMutant);
}
