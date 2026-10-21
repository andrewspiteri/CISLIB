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
        DateTimeOffset CreatedUtc, bool CoverageAnalysisOff, bool AllowInitialUninstrumentedBaseline);
    internal sealed record Stage(string Directory, string OriginalDirectory, string AssemblyFile, string AssemblyName,
        string CoreSha256, Guid CoreMvid, string? ControllerType, FilePin[] OriginalFiles, FilePin[] DeployedFiles);
    internal sealed record Launch(string Schema, string Id, string SessionPath, string SessionSha256, DateTimeOffset CreatedUtc,
        int ParentPid, string StageOwnerId, string ParentAssemblyPath, string ParentAssemblySha256, string ParentCorePath, string ParentCoreSha256,
        Guid ParentCoreMvid, string? ControllerType, int ActiveMutant, string? OriginalSelectorName, string? OriginalSelectorValue,
        bool Instrumented, Stage Stage);
    internal sealed record Receipt(string Schema, string LaunchId, string LaunchSha256, DateTimeOffset ObservedUtc,
        int Pid, string EntryAssemblyPath, string EntryAssemblyName, string EntryAssemblySha256, string CorePath,
        string CoreSha256, Guid CoreMvid, string? ControllerType, int ActiveMutant, bool Instrumented, string Runtime,
        string DotnetPath, string DotnetSha256, string CoreLibrarySha256, string[] Arguments, string? Failure);
    internal sealed record ExitReceipt(string LaunchId, int Pid, int ExitCode, DateTimeOffset ExitedUtc);
    internal sealed record ParentExitReceipt(string Schema, string LaunchId, string LaunchSha256, int ParentPid, int Pid,
        string FileName, string WorkingDirectory, string[] Arguments, DateTimeOffset StartedUtc, DateTimeOffset ObservedStartedUtc,
        DateTimeOffset ExitedUtc, DateTimeOffset ObservedUtc, int ExitCode, bool ForcedStop, bool TimedOut);
    internal sealed record ManagedFailureReceipt(string Schema, string LaunchId, string LaunchSha256, string OriginReceiptSha256,
        int Pid, DateTimeOffset ObservedUtc, bool IsTerminating, string ExceptionType, string CorePath, string CoreSha256,
        Guid CoreMvid, int MetadataToken, string DeclaringType, string MethodName, int FrameIndex, int ActiveMutant);
}
