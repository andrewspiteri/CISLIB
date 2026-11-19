using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using CIS.RestClient.Http;

namespace CIS.Examples.CountryData;

// Optional explicit operator custody for this native owner; no private-key data is written.
internal static class CountryNativeKeyCustody
{
    internal static RestHttpNativeKeyScope Create(string? directory)
    {
        if (directory is null)
        {
            return new(2);
        }
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException("The native key custody directory must be absolute.");
        }
        string parent = Path.GetFullPath(directory);
        DirectoryInfo? security = new(parent);
        while (security is not null && !(security.Name == "security" && security.Parent?.Name == ".artifacts"))
        {
            security = security.Parent;
        }
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (security is null || parent.Equals(Path.Combine(security.FullName, "local-development"), comparison)
            || parent.StartsWith(Path.Combine(security.FullName, "local-development") + Path.DirectorySeparatorChar, comparison))
        {
            throw new ArgumentException("Keep native custody under workspace .artifacts/security, outside setup-owned local-development files.");
        }
        string path = Path.GetFullPath(Path.Combine(parent, Guid.NewGuid().ToString("N")));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, comparison))
        {
            throw new ArgumentException("Native custody must remain within its explicit parent directory.");
        }
        var gate = new object();
        bool initialized = false;
        return new(2, (metadata, transition) =>
        {
            lock (gate)
            {
                if (!initialized)
                {
                    RejectLinks(path);
                    if (OperatingSystem.IsWindows())
                    {
                        DirectoryInfo created = Directory.CreateDirectory(path);
                        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                        SecurityIdentifier owner = identity.User ?? throw new InvalidOperationException("A native custody owner is required.");
                        DirectorySecurity access = created.GetAccessControl(AccessControlSections.Access);
                        access.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                        access.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
                            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
                            AccessControlType.Allow));
                        created.SetAccessControl(access);
                    }
                    else
                    {
                        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                    RejectLinks(path);
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
                using var stream = new FileStream(Path.Combine(path, "keys.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(entry);
                stream.WriteByte((byte)'\n');
                stream.Flush(flushToDisk: true);
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
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
