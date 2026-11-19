using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace CIS.Security.LocalDevelopment;

internal static class PrivateWorkspaceFiles
{
    internal static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    internal static string CurrentOwner()
    {
        if (OperatingSystem.IsWindows())
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? throw new DevelopmentToolException("A current Windows user SID is required.");
        }
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return "uid:" + GetEffectiveUserId().ToString(CultureInfo.InvariantCulture);
        }
        throw new DevelopmentToolException("This tool supports Windows, Linux and macOS filesystem permissions.");
    }

    internal static string DirectoryFor(string workspace)
    {
        RejectLinks(workspace);
        if (!Directory.Exists(workspace))
        {
            throw new DevelopmentToolException("Create the workspace directory before running setup.");
        }
        string directory = Path.GetFullPath(Path.Combine(workspace, ".artifacts", "security", "local-development"));
        if (!directory.StartsWith(workspace + Path.DirectorySeparatorChar, PathComparison))
        {
            throw new DevelopmentToolException("The credential directory must remain inside its exact workspace.");
        }
        RejectLinks(directory);
        return directory;
    }

    internal static void CreateDirectory(string directory)
    {
        RejectLinks(directory);
        if (OperatingSystem.IsWindows())
        {
            DirectoryInfo created = Directory.CreateDirectory(directory);
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier owner = identity.User ?? throw new DevelopmentToolException("A current Windows user SID is required.");
            var access = new DirectorySecurity();
            access.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            access.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl & ~FileSystemRights.TakeOwnership,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            // Only the DACL is modified: no owner/group assignment or WRITE_OWNER operation.
            created.SetAccessControl(access);
        }
        else
        {
            Directory.CreateDirectory(directory, PrivateDirectory);
            File.SetUnixFileMode(directory, PrivateDirectory);
        }
        VerifyPrivate(directory, directory: true);
    }

    internal static void VerifyPrivate(string path, bool directory)
    {
        RejectLinks(path);
        if (OperatingSystem.IsWindows())
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier owner = identity.User ?? throw new DevelopmentToolException("A current Windows user SID is required.");
            FileSystemSecurity security = directory
                ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access)
                : new FileInfo(path).GetAccessControl(AccessControlSections.Access);
            AuthorizationRuleCollection rules = security.GetAccessRules(includeExplicit: true, includeInherited: true,
                targetType: typeof(SecurityIdentifier));
            if (directory && !security.AreAccessRulesProtected || rules.Count == 0)
            {
                throw new DevelopmentToolException("Credential files must retain their private current-user DACL.");
            }
            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.AccessControlType != AccessControlType.Allow || !rule.IdentityReference.Equals(owner))
                {
                    throw new DevelopmentToolException("Credential files must retain their private current-user DACL.");
                }
            }
        }
        else if ((File.GetUnixFileMode(path) & ~(directory ? PrivateDirectory : PrivateFile)) != 0)
        {
            throw new DevelopmentToolException("Credential directories require mode 0700 and files mode 0600.");
        }
    }

    internal static string Child(string directory, string name)
    {
        if (name.Length is < 1 or > 128 || name != Path.GetFileName(name) || name is "." or "..")
        {
            throw new DevelopmentToolException("A managed file must have one exact bounded filename.");
        }
        string path = Path.GetFullPath(Path.Combine(directory, name));
        if (!string.Equals(Path.GetDirectoryName(path), directory, PathComparison))
        {
            throw new DevelopmentToolException("A managed file must remain in its exact credential directory.");
        }
        RejectLinks(path);
        return path;
    }

    internal static byte[] Read(string path, int limit)
    {
        VerifyPrivate(path, directory: false);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > limit)
        {
            throw new DevelopmentToolException("A managed file exceeds its supported bound.");
        }
        byte[] buffer = new byte[limit + 1];
        try
        {
            int count = 0;
            while (count < buffer.Length)
            {
                int read = stream.Read(buffer, count, buffer.Length - count);
                if (read == 0)
                {
                    break;
                }
                count += read;
            }
            if (count > limit)
            {
                throw new DevelopmentToolException("A managed file exceeds its supported bound.");
            }
            return buffer.AsSpan(0, count).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    internal static void WriteNew(string path, ReadOnlySpan<byte> bytes, Action<string>? onCreated = null)
    {
        RejectLinks(path);
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.WriteThrough
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = PrivateFile;
        }
        bool created = false;
        FileStream? stream = null;
        var failures = new List<Exception>(3);
        try
        {
            stream = new FileStream(path, options);
            created = true;
            // Register only a path this CreateNew actually created, before writing any secret bytes.
            onCreated?.Invoke(path);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception original)
        {
            failures.Add(original);
        }
        finally
        {
            try
            {
                stream?.Dispose();
            }
            catch (Exception closeFailure)
            {
                failures.Add(closeFailure);
            }
        }
        if (failures.Count == 0)
        {
            try
            {
                VerifyPrivate(path, directory: false);
            }
            catch (Exception verificationFailure)
            {
                failures.Add(verificationFailure);
            }
        }
        if (failures.Count != 0 && created)
        {
            try
            {
                string directory = Path.GetDirectoryName(path)
                    ?? throw new DevelopmentToolException("An owned file requires its exact parent directory.");
                DeleteOwnedFile(directory, path);
            }
            catch (Exception cleanupFailure)
            {
                failures.Add(cleanupFailure);
            }
        }
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures.Count > 1)
        {
            throw new AggregateException("The owned file write and cleanup did not complete.", failures);
        }
    }

    internal static void Replace(string directory, string source, string destination)
    {
        string checkedSource = Child(directory, Path.GetFileName(source));
        string checkedDestination = Child(directory, Path.GetFileName(destination));
        if (!string.Equals(source, checkedSource, PathComparison) || !string.Equals(destination, checkedDestination, PathComparison))
        {
            throw new DevelopmentToolException("Replacement requires two exact managed files.");
        }
        File.Move(checkedSource, checkedDestination, overwrite: true);
    }

    internal static void DeleteOwnedFile(string directory, string path)
    {
        string verified = Child(directory, Path.GetFileName(path));
        if (!string.Equals(path, verified, PathComparison))
        {
            throw new DevelopmentToolException("Cleanup requires one exact owned file.");
        }
        File.Delete(verified);
    }

    internal static void RejectLinks(string path)
    {
        string absolute = Path.GetFullPath(path);
        string root = Path.GetPathRoot(absolute) ?? throw new DevelopmentToolException("An absolute filesystem root is required.");
        string current = root;
        foreach (string component in absolute[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new DevelopmentToolException("Symbolic links and reparse points are not managed credential paths.");
                }
            }
            catch (FileNotFoundException)
            {
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
        }
    }

    [DllImport("libc", EntryPoint = "geteuid", ExactSpelling = true)]
    private static extern uint GetEffectiveUserId();
}
