using System.Security.AccessControl;
using System.Security.Principal;
using CIS.TestSupport;

namespace CIS.Tests.IntegrationTests.Infrastructure.Containers;

internal sealed class PrivateContainerDirectory : IDisposable
{
    private readonly TemporaryDirectory directory = new();

    internal PrivateContainerDirectory()
    {
        try
        {
            Restrict(directory.Path);
        }
        catch
        {
            directory.Dispose();
            throw;
        }
    }

    internal string Path => directory.Path;

    internal static void Restrict(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            SecurityIdentifier user = WindowsIdentity.GetCurrent().User
                ?? throw new InvalidOperationException("The current fixture user has no security identifier.");
            var permissions = new DirectorySecurity();
            permissions.SetAccessRuleProtection(true, false);
            permissions.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            FileSystemAclExtensions.SetAccessControl(new DirectoryInfo(path), permissions);
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    internal static async Task WriteNewAsync(string path, byte[] content, CancellationToken cancellationToken = default)
    {
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await output.WriteAsync(content, cancellationToken);
        }
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public void Dispose() => directory.Dispose();
}
