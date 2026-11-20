using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using CIS.RestClient.Http;

namespace CIS.Tests.IntegrationTests.RestClient;

// One shared finite scope for this native qualification process. No existing keys or stores are inventoried.
internal static class RestSecurityNativeKeyCustody
{
    private static readonly Lazy<RestHttpNativeKeyScope> SharedOwner = new(() => CreateScope(16));

    internal static RestHttpNativeKeyScope Shared => SharedOwner.Value;

    internal static RestHttpNativeKeyScope CreateScope(int maximumLiveKeys,
        Action<RestHttpNativeKeyMetadata, RestHttpNativeKeyTransition>? observe = null)
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null
            && !File.Exists(Path.Combine(repository.FullName, "implementations", "csharp", "CisLib.slnx")))
        {
            repository = repository.Parent;
        }
        if (repository is null)
        {
            throw new InvalidOperationException("Run native security fixtures from their repository checkout.");
        }
        string path = Path.Combine(repository.FullName, ".artifacts", "security", "CIS-0016", "native-key-custody",
            Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
        {
            DirectoryInfo directory = Directory.CreateDirectory(path);
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier owner = identity.User ?? throw new InvalidOperationException("A native fixture owner is required.");
            DirectorySecurity security = directory.GetAccessControl(AccessControlSections.Access);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
                AccessControlType.Allow));
            directory.SetAccessControl(security);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var gate = new object();
        return new(maximumLiveKeys, (metadata, transition) =>
        {
            lock (gate)
            {
                File.AppendAllText(Path.Combine(path, "keys.jsonl"), JsonSerializer.Serialize(new
                {
                    transition,
                    metadata.ProviderName,
                    metadata.ContainerName,
                    metadata.UniqueName,
                    metadata.FilePath,
                    metadata.FileLength,
                    metadata.ProviderType,
                    metadata.KeyNumber
                }) + Environment.NewLine);
                observe?.Invoke(metadata, transition);
            }
        });
    }
}
