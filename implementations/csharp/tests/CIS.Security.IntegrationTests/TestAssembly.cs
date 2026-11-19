using Microsoft.VisualStudio.TestTools.UnitTesting;
using CIS.RestClient.Http;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]

namespace CIS.Tests.Security;

[TestClass]
public sealed class SecurityTestAssemblySetup
{
    private static readonly Dictionary<string, string?> Previous = new(StringComparer.Ordinal);
    private static readonly object JournalGate = new();

    internal static RestHttpNativeKeyScope NativeKeys { get; private set; } = null!;

    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        // This independent native-provider test host has one explicit canonical
        // application identity. Production defaults and other test hosts retain theirs.
        Set("CORE_SERVICE_NAME", "CIS.Security.IntegrationTests");
        Set("CORE_ENVIRONMENT", "Development");
        Set("UseSharedCompilation", "false");
        Set("MSBUILDDISABLENODEREUSE", "1");
        string temporary = CreatePrivateTemporaryDirectory();
        Set("TEMP", temporary);
        Set("TMP", temporary);
        Set("TMPDIR", temporary);
        NativeKeys = new(16, (metadata, transition) =>
        {
            lock (JournalGate)
            {
                File.AppendAllText(Path.Combine(temporary, "native-key-custody.jsonl"), JsonSerializer.Serialize(new
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
            }
        });
        Assert.AreEqual("CIS.Security.IntegrationTests", EnvironmentOptions.ApplicationName);
        Assert.AreEqual("Development", EnvironmentOptions.EnvironmentName);
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        try
        {
            NativeKeys?.Dispose();
        }
        finally
        {
            foreach ((string key, string? value) in Previous)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static void Set(string key, string value)
    {
        Previous.Add(key, Environment.GetEnvironmentVariable(key));
        Environment.SetEnvironmentVariable(key, value);
    }

    private static string CreatePrivateTemporaryDirectory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "implementations", "csharp", "CisLib.slnx")))
        {
            current = current.Parent;
        }
        if (current is null)
        {
            throw new InvalidOperationException("Run security integration tests from their repository checkout.");
        }
        string path = Path.Combine(current.FullName, ".artifacts", "security", "CIS-0016", "tests", Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
        {
            // Establish a private directory before any test assertion/key file is written.
            DirectoryInfo directory = Directory.CreateDirectory(path);
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier owner = identity.User ?? throw new InvalidOperationException("A native test owner is required.");
            DirectorySecurity security = directory.GetAccessControl(AccessControlSections.Access);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(security);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }
}
