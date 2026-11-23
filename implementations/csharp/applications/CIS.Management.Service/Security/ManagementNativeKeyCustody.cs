using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using CIS.RestClient.Http;

namespace CIS.Management.Service.Security;

// The existing native certificate owner performs imports and exact owned-key removal.
// This adapter only journals non-secret custody transitions when explicitly configured.
internal static class ManagementNativeKeyCustody
{
    internal static RestHttpNativeKeyScope Create(string? directory)
    {
        if (directory is null)
        {
            return new(2);
        }

        string parent = Path.GetFullPath(directory);
        string owned = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!owned.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, comparison))
        {
            throw new ArgumentException("Native custody must remain below its configured parent.");
        }

        var gate = new object();
        bool initialized = false;
        return new(2, (metadata, transition) =>
        {
            lock (gate)
            {
                if (!initialized)
                {
                    RejectLinks(owned);
                    if (OperatingSystem.IsWindows())
                    {
                        DirectoryInfo created = Directory.CreateDirectory(owned);
                        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                        SecurityIdentifier user = identity.User ?? throw new InvalidOperationException("A current-user native custody identity is required.");
                        DirectorySecurity permissions = created.GetAccessControl(AccessControlSections.Access);
                        permissions.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                        permissions.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
                            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
                            AccessControlType.Allow));
                        created.SetAccessControl(permissions);
                    }
                    else
                    {
                        Directory.CreateDirectory(owned, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                    RejectLinks(owned);
                    initialized = true;
                }

                byte[] entry = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    transition,
                    metadata.ProviderName,
                    metadata.ContainerName,
                    metadata.UniqueName,
                    metadata.FilePath,
                    metadata.FileLength,
                    metadata.ProviderType,
                    metadata.KeyNumber
                });
                using var journal = new FileStream(Path.Combine(owned, "keys.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read);
                journal.Write(entry);
                journal.WriteByte((byte)'\n');
                journal.Flush(flushToDisk: true);
            }
        });
    }

    private static void RejectLinks(string path)
    {
        for (DirectoryInfo? current = new(path); current is not null; current = current.Parent)
        {
            try
            {
                if ((File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Native custody cannot traverse a filesystem link.");
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
