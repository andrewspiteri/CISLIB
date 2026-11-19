using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CIS.Security.LocalDevelopment;

internal static class WorkspaceCredentials
{
    private const string MarkerName = "workspace.json";
    private const string LockName = "operation.lock";
    private static readonly string[] ManagedNames = ["ca.cert.pem", "ca.key.pem", "server.cert.pem", "server.key.pem",
        "service.cert.pem", "service.key.pem", "caller.cert.pem", "caller.key.pem", "configuration.json"];
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        MaxDepth = 8
    };

    internal static void Execute(Command command, TextWriter output)
    {
        string directory = PrivateWorkspaceFiles.DirectoryFor(command.Workspace);
        bool setup = command.Operation == "setup";
        if (setup)
        {
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            {
                throw new DevelopmentToolException("Setup requires a missing or empty isolated directory. Use renew or an explicitly confirmed reset for an existing owned workspace.");
            }
            PrivateWorkspaceFiles.CreateDirectory(directory);
        }
        else if (!Directory.Exists(directory))
        {
            throw new DevelopmentToolException("Run setup for this workspace first.");
        }
        PrivateWorkspaceFiles.VerifyPrivate(directory, directory: true);
        string lockPath = PrivateWorkspaceFiles.Child(directory, LockName);
        if (setup)
        {
            PrivateWorkspaceFiles.WriteNew(lockPath, []);
        }
        else
        {
            PrivateWorkspaceFiles.VerifyPrivate(lockPath, directory: false);
            if (new FileInfo(lockPath).Length != 0)
            {
                throw new DevelopmentToolException("The managed operation lock must remain empty.");
            }
        }
        using var operation = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        WorkspaceMarker marker;
        Dictionary<string, byte[]> previous = new(StringComparer.Ordinal);
        if (setup)
        {
            marker = new WorkspaceMarker
            {
                Workspace = command.Workspace,
                Owner = PrivateWorkspaceFiles.CurrentOwner(),
                WorkspaceId = Guid.NewGuid().ToString("N"),
                ServiceApplication = command.Service,
                CallerApplication = command.Caller
            };
        }
        else
        {
            marker = ReadMarker(command, directory);
            if (command.Operation == "reset" && !string.Equals(command.Confirmation, marker.WorkspaceId, StringComparison.Ordinal))
            {
                throw new DevelopmentToolException("Reset requires the exact workspace-id from this owned workspace's non-secret configuration.");
            }
        }
        DateTimeOffset now = DateTimeOffset.UtcNow;
        try
        {
            if (!setup)
            {
                previous = ReadOwned(directory, marker);
            }
            using X509Certificate2 authority = command.Operation == "renew"
                ? DevelopmentCertificates.LoadAuthority(previous) : DevelopmentCertificates.CreateAuthority(now);
            if (authority.NotBefore.ToUniversalTime() > now.UtcDateTime || authority.NotAfter.ToUniversalTime() <= now.UtcDateTime)
            {
                throw new DevelopmentToolException("The local CA is not currently valid. Deliberately reset trust and restart the applications.");
            }
            Dictionary<string, byte[]> generated = DevelopmentCertificates.CreateFiles(marker, directory, authority, now);
            try
            {
                marker.CaSha256 = Hash(generated["ca.cert.pem"]);
                marker.GenerationId = Guid.NewGuid().ToString("N");
                marker.Files = generated.ToDictionary(pair => pair.Key, pair => Hash(pair.Value), StringComparer.Ordinal);
                Publish(directory, generated, JsonSerializer.SerializeToUtf8Bytes(marker, JsonOptions), previous);
            }
            finally
            {
                Clear(generated);
            }
            output.WriteLine("Local development credentials " + command.Operation + " completed.");
            output.WriteLine("Workspace ID: " + marker.WorkspaceId);
            output.WriteLine("Non-secret configuration: " + PrivateWorkspaceFiles.Child(directory, "configuration.json"));
            if (command.Operation == "reset")
            {
                output.WriteLine("Trust changed. Stop and restart every caller/service with the new explicit roots; leaf reload alone does not replace immutable trust policy.");
            }
        }
        finally
        {
            Clear(previous);
        }
    }

    private static WorkspaceMarker ReadMarker(Command command, string directory)
    {
        string[] entries = Directory.EnumerateFileSystemEntries(directory).Take(ManagedNames.Length + 3).ToArray();
        if (entries.Length != ManagedNames.Length + 2 || entries.Any(path =>
            !ManagedNames.Contains(Path.GetFileName(path), StringComparer.Ordinal)
            && Path.GetFileName(path) is not (MarkerName or LockName)))
        {
            throw new DevelopmentToolException("The isolated directory contains missing or unrecognized files. No unknown entry will be deleted or adopted.");
        }
        foreach (string entry in entries)
        {
            PrivateWorkspaceFiles.VerifyPrivate(entry, directory: false);
            if (Directory.Exists(entry))
            {
                throw new DevelopmentToolException("Managed entries must be files, never subdirectories.");
            }
        }
        WorkspaceMarker marker = JsonSerializer.Deserialize<WorkspaceMarker>(
            PrivateWorkspaceFiles.Read(PrivateWorkspaceFiles.Child(directory, MarkerName), 16 * 1024), JsonOptions)
            ?? throw new DevelopmentToolException("The workspace marker is missing.");
        if (marker.Version != 1 || marker.Workspace is null || marker.Owner != PrivateWorkspaceFiles.CurrentOwner()
            || !string.Equals(marker.Workspace, command.Workspace, PrivateWorkspaceFiles.PathComparison)
            || !IsId(marker.WorkspaceId) || !IsId(marker.GenerationId) || !IsHash(marker.CaSha256)
            || marker.Files is null || marker.Files.Count != ManagedNames.Length
            || ManagedNames.Any(name => !marker.Files.TryGetValue(name, out string? hash) || !IsHash(hash)))
        {
            throw new DevelopmentToolException("The marker must match this canonical workspace, current user and exact bounded managed file set.");
        }
        LocalDevelopmentCommand.ValidateApplication(marker.ServiceApplication);
        LocalDevelopmentCommand.ValidateApplication(marker.CallerApplication);
        if (marker.ServiceApplication == marker.CallerApplication)
        {
            throw new DevelopmentToolException("The persisted application identities must remain distinct.");
        }
        return marker;
    }

    private static Dictionary<string, byte[]> ReadOwned(string directory, WorkspaceMarker marker)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            foreach (string name in ManagedNames)
            {
                int limit = name.EndsWith(".key.pem", StringComparison.Ordinal) ? 64 * 1024 : 256 * 1024;
                byte[] bytes = PrivateWorkspaceFiles.Read(PrivateWorkspaceFiles.Child(directory, name), limit);
                files.Add(name, bytes);
                if (Hash(bytes) != marker.Files[name])
                {
                    throw new DevelopmentToolException("An owned file no longer matches its marker. Refusing to overwrite changed material or configuration.");
                }
            }
            if (Hash(files["ca.cert.pem"]) != marker.CaSha256)
            {
                throw new DevelopmentToolException("The isolated authority no longer matches its marker.");
            }
            // Historical validation permits renewing expired leaves, but not changed identities, keys or issuers.
            DevelopmentCertificates.ValidateFiles(files, marker, DateTimeOffset.UtcNow, historical: true);
            return files;
        }
        catch
        {
            Clear(files);
            throw;
        }
    }

    private static void Publish(string directory, IReadOnlyDictionary<string, byte[]> files, byte[] marker,
        IReadOnlyDictionary<string, byte[]> previous)
    {
        string prefix = "staged-" + Guid.NewGuid().ToString("N") + "-";
        var staged = new Dictionary<string, string>(ManagedNames.Length + 1, StringComparer.Ordinal);
        var ownedTemporaryPaths = new HashSet<string>((ManagedNames.Length + 1) * 2, StringComparer.Ordinal);
        var replaced = new List<string>(ManagedNames.Length + 1);
        var failures = new List<Exception>((ManagedNames.Length + 1) * 4 + 1);
        byte[]? oldMarker = previous.Count == 0 ? null
            : PrivateWorkspaceFiles.Read(PrivateWorkspaceFiles.Child(directory, MarkerName), 16 * 1024);
        try
        {
            foreach ((string name, byte[] bytes) in files.Append(new KeyValuePair<string, byte[]>(MarkerName, marker)))
            {
                string temporary = PrivateWorkspaceFiles.Child(directory, prefix + name);
                PrivateWorkspaceFiles.WriteNew(temporary, bytes, path => ownedTemporaryPaths.Add(path));
                staged.Add(name, temporary);
            }
            foreach ((string name, string temporary) in staged)
            {
                PrivateWorkspaceFiles.Replace(directory, temporary, PrivateWorkspaceFiles.Child(directory, name));
                replaced.Add(name);
            }
        }
        catch (Exception originalFailure)
        {
            failures.Add(originalFailure);
            // Restore only exact files replaced by this invocation. No directory-wide cleanup is allowed.
            foreach (string name in replaced)
            {
                try
                {
                    string destination = PrivateWorkspaceFiles.Child(directory, name);
                    byte[]? original = name == MarkerName ? oldMarker : previous.GetValueOrDefault(name);
                    if (original is null)
                    {
                        PrivateWorkspaceFiles.DeleteOwnedFile(directory, destination);
                    }
                    else
                    {
                        string restoration = PrivateWorkspaceFiles.Child(directory, prefix + "restore-" + name);
                        PrivateWorkspaceFiles.WriteNew(restoration, original, path => ownedTemporaryPaths.Add(path));
                        PrivateWorkspaceFiles.Replace(directory, restoration, destination);
                    }
                }
                catch (Exception restorationFailure)
                {
                    failures.Add(restorationFailure);
                }
            }
        }
        finally
        {
            try
            {
                foreach (string temporary in ownedTemporaryPaths)
                {
                    try
                    {
                        PrivateWorkspaceFiles.DeleteOwnedFile(directory, temporary);
                    }
                    catch (Exception cleanupFailure)
                    {
                        failures.Add(cleanupFailure);
                    }
                }
            }
            finally
            {
                if (oldMarker is not null)
                {
                    CryptographicOperations.ZeroMemory(oldMarker);
                }
            }
        }
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures.Count > 1)
        {
            throw new AggregateException("The owned credential publication and restoration did not complete.", failures);
        }
    }

    private static bool IsId(string? value) => value is { Length: 32 } && value.All(char.IsAsciiHexDigitLower);

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void Clear(IEnumerable<KeyValuePair<string, byte[]>> files)
    {
        foreach ((_, byte[] value) in files)
        {
            CryptographicOperations.ZeroMemory(value);
        }
    }
}

internal sealed class WorkspaceMarker
{
    public WorkspaceMarker()
    {
    }

    [JsonRequired]
    public int Version { get; set; } = 1;
    [JsonRequired]
    public string Workspace { get; set; } = "";
    [JsonRequired]
    public string Owner { get; set; } = "";
    [JsonRequired]
    public string WorkspaceId { get; set; } = "";
    [JsonRequired]
    public string GenerationId { get; set; } = "";
    [JsonRequired]
    public string ServiceApplication { get; set; } = "";
    [JsonRequired]
    public string CallerApplication { get; set; } = "";
    [JsonRequired]
    public string CaSha256 { get; set; } = "";
    [JsonRequired]
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.Ordinal);
}
