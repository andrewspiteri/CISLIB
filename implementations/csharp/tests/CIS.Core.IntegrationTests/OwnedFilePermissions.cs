using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace CIS.Tests.IntegrationTests;

// Reversible permission changes for physical tests, confined to their owned temporary tree.
internal sealed class OwnedFilePermissions : IDisposable
{
    private readonly string path;
    private readonly bool directory;
    private readonly byte[]? originalAcl;
    private readonly UnixFileMode originalMode;
    private bool disposed;

    internal OwnedFilePermissions(string root, string path, FileSystemRights deniedRights, UnixFileMode unixMode)
    {
        string boundary = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        this.path = Path.GetFullPath(path);
        if (!this.path.StartsWith(boundary, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        { throw new InvalidOperationException("Permission target is outside the owned temporary tree."); }
        for (string? item = this.path; item is not null && item.Length >= boundary.Length; item = Path.GetDirectoryName(item))
        { if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) { throw new InvalidOperationException("Permission target traverses a link."); } }
        directory = Directory.Exists(this.path);
        if (OperatingSystem.IsWindows())
        {
            FileSystemSecurity security = ReadAcl(); originalAcl = security.GetSecurityDescriptorBinaryForm();
            SecurityIdentifier sid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("No Windows user SID.");
            security.AddAccessRule(new FileSystemAccessRule(sid, deniedRights, AccessControlType.Deny));
            try { WriteAcl(security); }
            catch { RestoreWindows(); throw; }
        }
        else
        {
            originalMode = File.GetUnixFileMode(this.path);
            File.SetUnixFileMode(this.path, unixMode);
        }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        if (OperatingSystem.IsWindows())
        {
            RestoreWindows();
            if (!ReadAcl().GetSecurityDescriptorBinaryForm().AsSpan().SequenceEqual(originalAcl))
            { throw new InvalidOperationException("Original permissions were not restored."); }
        }
        else
        {
            File.SetUnixFileMode(path, originalMode);
            if (File.GetUnixFileMode(path) != originalMode) { throw new InvalidOperationException("Original permissions were not restored."); }
        }
        disposed = true;
    }

    [SupportedOSPlatform("windows")]
    private FileSystemSecurity ReadAcl() => directory
        ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access)
        : new FileInfo(path).GetAccessControl(AccessControlSections.Access);

    [SupportedOSPlatform("windows")]
    private void WriteAcl(FileSystemSecurity security)
    {
        if (directory) { new DirectoryInfo(path).SetAccessControl((DirectorySecurity)security); }
        else { new FileInfo(path).SetAccessControl((FileSecurity)security); }
    }

    [SupportedOSPlatform("windows")]
    private void RestoreWindows()
    {
        FileSystemSecurity security = directory ? new DirectorySecurity() : new FileSecurity();
        security.SetSecurityDescriptorBinaryForm(originalAcl!, AccessControlSections.Access); WriteAcl(security);
    }
}
