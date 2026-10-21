using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace CIS.Healthcheck.Pipes;

/// <summary>Creates the protected local endpoint without changing process-wide permissions.</summary>
internal static class HealthPipeEndpoint
{
    internal static NamedPipeServerStream Create(string name, int maximumClients, bool first)
    {
        if (OperatingSystem.IsWindows()) { return CreateWindows(name, maximumClients, first); }
        return new NamedPipeServerStream(name, PipeDirection.InOut, maximumClients, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None));
    }

    [SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreateWindows(string name, int maximumClients, bool first)
    {
        // The pinned .NET 10 CurrentUserOnly constructor installs an owner-only
        // ACL but does not set PIPE_REJECT_REMOTE_CLIENTS. Native creation is
        // needed to enforce both boundaries; all connected I/O stays in the BCL.
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.Owner ?? throw new InvalidOperationException("The local pipe owner is unavailable.");
        var security = new PipeSecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(owner, PipeAccessRights.FullControl, AccessControlType.Allow));
        byte[] descriptor = security.GetSecurityDescriptorBinaryForm();
        var pinned = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        SafePipeHandle handle;
        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = pinned.AddrOfPinnedObject(),
                InheritHandle = 0
            };
            const uint Duplex = 0x00000003;
            const uint Asynchronous = 0x40000000;
            const uint FirstInstance = 0x00080000;
            const uint RejectRemoteClients = 0x00000008;
            handle = CreateNamedPipeW(@"\\.\pipe\" + name,
                Duplex | Asynchronous | (first ? FirstInstance : 0), RejectRemoteClients,
                (uint)maximumClients, 4096, 4096, 0, in attributes);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                throw new Win32Exception(error, "The protected local Healthcheck pipe could not be opened.");
            }
        }
        finally { pinned.Free(); }
        try
        {
            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal int Length;
        internal nint SecurityDescriptor;
        internal int InheritHandle;
    }

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafePipeHandle CreateNamedPipeW(string name, uint openMode, uint pipeMode,
        uint maximumInstances, uint outputBufferSize, uint inputBufferSize, uint defaultTimeout,
        in SecurityAttributes securityAttributes);
}
