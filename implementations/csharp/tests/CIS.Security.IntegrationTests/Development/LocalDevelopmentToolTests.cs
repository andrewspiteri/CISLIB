using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Security.Certificates;
using CIS.Security.LocalDevelopment;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.Security.Development;

[TestClass]
public sealed class LocalDevelopmentToolTests
{
    [TestMethod]
    public void WrongResetConfirmationIsRejectedBeforeReadingOwnedPrivateMaterial()
    {
        using var workspace = new ToolWorkspace();
        Assert.AreEqual(0, workspace.Run("setup"));
        string key = Path.Combine(workspace.Credentials, "ca.key.pem");
        File.WriteAllText(key, "owned changed private-material sentinel");
        Dictionary<string, string> before = workspace.Hashes();
        Assert.AreEqual(1, workspace.Run("reset", "--confirm", Guid.NewGuid().ToString("N")));
        StringAssert.Contains(workspace.Output, "Reset requires the exact workspace-id");
        CollectionAssert.AreEquivalent(before.ToArray(), workspace.Hashes().ToArray());
        Assert.IsFalse(workspace.Output.Contains("sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ARealHeldDestinationRollsBackEarlierReplacementAndCleansAllCreatedStagingFiles()
    {
        using var workspace = new ToolWorkspace();
        Assert.AreEqual(0, workspace.Run("setup"));
        Dictionary<string, string> before = workspace.Hashes();
        // Read sharing permits ownership validation, but Windows denies replacing this
        // later destination after the new server certificate/key and service certificate
        // have been published. Unix permits renaming open files and uses its lock boundary.
        if (OperatingSystem.IsWindows())
        {
            using var held = new FileStream(Path.Combine(workspace.Credentials, "service.key.pem"),
                FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.AreEqual(1, workspace.Run("renew"));
        }
        else
        {
            // Keep the test meaningful on Unix without relying on Windows sharing semantics:
            // an exclusively held operation lock rejects admission before any private publication.
            using var held = new FileStream(Path.Combine(workspace.Credentials, "operation.lock"),
                FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.AreEqual(1, workspace.Run("renew"));
        }
        CollectionAssert.AreEquivalent(before.ToArray(), workspace.Hashes().ToArray());
        Assert.IsFalse(Directory.GetFiles(workspace.Credentials).Any(path => Path.GetFileName(path).StartsWith("staged-", StringComparison.Ordinal)));
        Assert.IsFalse(workspace.Output.Contains("PRIVATE KEY", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SetupCreatesIsolatedNativeValidatedPerApplicationMaterialWithBoundedActualLifetimes()
    {
        using var workspace = new ToolWorkspace();
        Assert.AreEqual(0, workspace.Run("setup"));
        JsonObject config = workspace.Configuration();
        Assert.AreEqual("CountryData", config["serviceApplication"]!.GetValue<string>());
        Assert.AreEqual("CountryData.Caller", config["callerApplication"]!.GetValue<string>());
        Assert.AreEqual("Development", config["environment"]!.GetValue<string>());
        Assert.AreEqual("https://localhost:7443", config["allowedOrigins"]![0]!.GetValue<string>());
        using X509Certificate2 authority = workspace.Certificate("ca");
        using X509Certificate2 service = workspace.Certificate("service");
        using X509Certificate2 caller = workspace.Certificate("caller");
        using X509Certificate2 server = workspace.Certificate("server");
        Assert.AreEqual(TimeSpan.FromDays(30), authority.NotAfter.ToUniversalTime() - authority.NotBefore.ToUniversalTime());
        foreach (X509Certificate2 leaf in new[] { service, caller, server })
        {
            Assert.AreEqual(TimeSpan.FromDays(7), leaf.NotAfter.ToUniversalTime() - leaf.NotBefore.ToUniversalTime());
            Assert.IsTrue(leaf.NotAfter.ToUniversalTime() <= authority.NotAfter.ToUniversalTime());
            Assert.IsTrue(leaf.HasPrivateKey);
        }
        CollectionAssert.AreNotEqual(service.GetPublicKey(), caller.GetPublicKey());
        CollectionAssert.AreNotEqual(server.GetPublicKey(), caller.GetPublicKey());
        var trust = new CertificateTrustPolicy(config["trustDomain"]!.GetValue<string>(), [authority], [authority],
            revocation: CertificateRevocationPolicy.Development);
        CertificateValidationResult serviceIdentity = CertificateTrustValidator.Validate(service, trust, DateTimeOffset.UtcNow);
        CertificateValidationResult callerIdentity = CertificateTrustValidator.Validate(caller, trust, DateTimeOffset.UtcNow);
        Assert.IsTrue(serviceIdentity.Succeeded);
        Assert.IsTrue(callerIdentity.Succeeded);
        Assert.AreEqual("https://identity.cislib.local/applications/CountryData/environments/Development", serviceIdentity.Subject!.Identifier);
        Assert.AreEqual("https://identity.cislib.local/applications/CountryData.Caller/environments/Development", callerIdentity.Subject!.Identifier);
        Assert.IsFalse(CertificateTrustValidator.Validate(server, trust, DateTimeOffset.UtcNow).Succeeded,
            "A server credential must not acquire application-client identity.");
        Assert.AreEqual(11, Directory.GetFiles(workspace.Credentials).Length);
        Assert.IsFalse(Directory.GetFiles(workspace.Credentials).Any(path => path.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(workspace.Output.Contains("PRIVATE KEY", StringComparison.Ordinal));
        Assert.IsFalse(workspace.Output.Contains("CERTIFICATE-----", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RenewalRetainsTheExactAuthorityAndStableIdentityWhileRotatingEveryLeafKey()
    {
        using var workspace = new ToolWorkspace();
        Assert.AreEqual(0, workspace.Run("setup", "--caller", "CIS.Security.IntegrationTests"));
        Dictionary<string, string> before = workspace.Hashes();
        string identity = workspace.Configuration()["applications"]![1]!["identity"]!.GetValue<string>();
        Assert.AreEqual(0, workspace.Run("renew"));
        Dictionary<string, string> after = workspace.Hashes();
        Assert.AreEqual(before["ca.cert.pem"], after["ca.cert.pem"]);
        Assert.AreEqual(before["ca.key.pem"], after["ca.key.pem"]);
        foreach (string prefix in new[] { "service", "caller", "server" })
        {
            Assert.AreNotEqual(before[prefix + ".cert.pem"], after[prefix + ".cert.pem"]);
            Assert.AreNotEqual(before[prefix + ".key.pem"], after[prefix + ".key.pem"]);
        }
        Assert.AreEqual(identity, workspace.Configuration()["applications"]![1]!["identity"]!.GetValue<string>());
        Assert.AreEqual("CIS.Security.IntegrationTests", workspace.Configuration()["callerApplication"]!.GetValue<string>());
    }

    [TestMethod]
    public void ResetRequiresExactConfirmationAndReplacesTrustWithoutBroadeningIt()
    {
        using var workspace = new ToolWorkspace();
        Assert.AreEqual(0, workspace.Run("setup"));
        JsonObject before = workspace.Configuration();
        string workspaceId = before["workspaceId"]!.GetValue<string>();
        using X509Certificate2 oldAuthority = workspace.Certificate("ca");
        using X509Certificate2 oldCaller = workspace.Certificate("caller");
        Dictionary<string, string> hashes = workspace.Hashes();
        Assert.AreEqual(1, workspace.Run("reset", "--confirm", Guid.NewGuid().ToString("N")));
        CollectionAssert.AreEquivalent(hashes.ToArray(), workspace.Hashes().ToArray());
        Assert.AreEqual(0, workspace.Run("reset", "--confirm", workspaceId));
        using X509Certificate2 newAuthority = workspace.Certificate("ca");
        using X509Certificate2 newCaller = workspace.Certificate("caller");
        var oldTrust = new CertificateTrustPolicy(before["trustDomain"]!.GetValue<string>(), [oldAuthority], [oldAuthority],
            revocation: CertificateRevocationPolicy.Development);
        var newTrust = new CertificateTrustPolicy(before["trustDomain"]!.GetValue<string>(), [newAuthority], [newAuthority],
            revocation: CertificateRevocationPolicy.Development);
        Assert.IsFalse(CertificateTrustValidator.Validate(newCaller, oldTrust, DateTimeOffset.UtcNow).Succeeded);
        Assert.IsFalse(CertificateTrustValidator.Validate(oldCaller, newTrust, DateTimeOffset.UtcNow).Succeeded);
        Assert.IsTrue(CertificateTrustValidator.Validate(newCaller, newTrust, DateTimeOffset.UtcNow).Succeeded);
        Assert.AreEqual(workspaceId, workspace.Configuration()["workspaceId"]!.GetValue<string>());
        StringAssert.Contains(workspace.Output, "Stop and restart");
    }

    [DataRow("unknown-file")]
    [DataRow("changed-config")]
    [DataRow("wrong-workspace")]
    [DataRow("wrong-owner")]
    [DataRow("wrong-identity")]
    [DataRow("wrong-ca-hash")]
    [DataRow("oversized-marker")]
    [TestMethod]
    public void ChangedOwnershipOrMaterialFailsClosedWithoutRewritingAnyFile(string change)
    {
        using var workspace = new ToolWorkspace();
        Assert.AreEqual(0, workspace.Run("setup"));
        string markerPath = Path.Combine(workspace.Credentials, "workspace.json");
        if (change == "unknown-file")
        {
            workspace.WriteOwnedExtra("unexpected.txt", "owned test sentinel");
        }
        else if (change == "changed-config")
        {
            File.AppendAllText(Path.Combine(workspace.Credentials, "configuration.json"), " ");
        }
        else if (change == "oversized-marker")
        {
            File.WriteAllText(markerPath, new string('x', 16 * 1024 + 1));
        }
        else
        {
            JsonObject marker = JsonNode.Parse(File.ReadAllText(markerPath))!.AsObject();
            string field = change switch
            {
                "wrong-workspace" => "workspace",
                "wrong-owner" => "owner",
                "wrong-identity" => "callerApplication",
                _ => "caSha256"
            };
            marker[field] = change == "wrong-ca-hash" ? new string('0', 64) : "different";
            File.WriteAllText(markerPath, marker.ToJsonString());
        }
        Dictionary<string, string> before = workspace.Hashes();
        Assert.AreEqual(1, workspace.Run("renew"));
        CollectionAssert.AreEquivalent(before.ToArray(), workspace.Hashes().ToArray());
    }

    [TestMethod]
    public void UnmarkedNonemptyDirectoryIsPreservedAndApplicationNamesStayWithinTheToolsDocumentedSubset()
    {
        using var workspace = new ToolWorkspace();
        Directory.CreateDirectory(workspace.Credentials);
        workspace.WriteOwnedExtra("unexpected.txt", "preserve this exact file");
        Assert.AreEqual(1, workspace.Run("setup"));
        Assert.AreEqual("preserve this exact file", File.ReadAllText(Path.Combine(workspace.Credentials, "unexpected.txt")));
        foreach (string invalid in new[] { "../caller", "caller with spaces", new string('a', 129), "CountryData" })
        {
            Assert.AreEqual(1, workspace.Run("setup", "--caller", invalid));
        }
        Assert.AreEqual(1, workspace.Run("renew", "--caller", "Other"));
        Assert.IsFalse(File.Exists(Path.Combine(workspace.Credentials, "workspace.json")));
    }

    [TestMethod]
    public void PermissionsAndLinkBoundariesUseTheActualPlatformFilesystem()
    {
        using var workspace = new ToolWorkspace();
        Assert.AreEqual(0, workspace.Run("setup"));
        if (OperatingSystem.IsWindows())
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier owner = identity.User!;
            DirectorySecurity access = new DirectoryInfo(workspace.Credentials).GetAccessControl(AccessControlSections.Access);
            Assert.IsTrue(access.AreAccessRulesProtected);
            foreach (FileSystemAccessRule rule in access.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                Assert.AreEqual(owner, rule.IdentityReference);
                Assert.AreEqual(AccessControlType.Allow, rule.AccessControlType);
                Assert.AreEqual(0, (int)(rule.FileSystemRights & FileSystemRights.TakeOwnership));
            }
            foreach (string path in Directory.GetFiles(workspace.Credentials))
            {
                FileSecurity fileAccess = new FileInfo(path).GetAccessControl(AccessControlSections.Access);
                foreach (FileSystemAccessRule rule in fileAccess.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                {
                    Assert.AreEqual(owner, rule.IdentityReference);
                }
            }
        }
        else
        {
            Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(workspace.Credentials));
            foreach (string path in Directory.GetFiles(workspace.Credentials))
            {
                Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            }
            using var linked = new ToolWorkspace();
            Directory.CreateDirectory(Path.GetDirectoryName(linked.Credentials)!);
            Directory.CreateSymbolicLink(linked.Credentials, workspace.Credentials);
            linked.HasDirectoryLink = true;
            Dictionary<string, string> before = workspace.Hashes();
            Assert.AreEqual(1, linked.Run("setup"));
            CollectionAssert.AreEquivalent(before.ToArray(), workspace.Hashes().ToArray());
        }
    }
}

internal sealed class ToolWorkspace : IDisposable
{
    private static readonly string[] Names = ["ca.cert.pem", "ca.key.pem", "server.cert.pem", "server.key.pem",
        "service.cert.pem", "service.key.pem", "caller.cert.pem", "caller.key.pem", "configuration.json", "workspace.json", "operation.lock"];
    private readonly HashSet<string> extraFiles = new(StringComparer.Ordinal);

    internal ToolWorkspace()
    {
        Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "local-tool-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Root);
        Credentials = Path.Combine(Root, ".artifacts", "security", "local-development");
    }

    internal string Root
    {
        get;
    }
    internal string Credentials
    {
        get;
    }
    internal string Output { get; private set; } = "";
    internal bool HasDirectoryLink
    {
        get; set;
    }

    internal int Run(string operation, params string[] options)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        string[] args = [operation, "--workspace", Root, .. options];
        int result = LocalDevelopmentCommand.Run(args, output, error);
        Output = output.ToString() + error.ToString();
        return result;
    }

    internal JsonObject Configuration() => JsonNode.Parse(File.ReadAllText(Path.Combine(Credentials, "configuration.json")))!.AsObject();

    internal X509Certificate2 Certificate(string prefix) => X509Certificate2.CreateFromPemFile(
        Path.Combine(Credentials, prefix + ".cert.pem"), Path.Combine(Credentials, prefix + ".key.pem"));

    internal Dictionary<string, string> Hashes() => Directory.GetFiles(Credentials).ToDictionary(path => Path.GetFileName(path)!,
        path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    internal void WriteOwnedExtra(string name, string content)
    {
        extraFiles.Add(name);
        File.WriteAllText(Path.Combine(Credentials, name), content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path.Combine(Credentials, name), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public void Dispose()
    {
        string expected = Path.GetFullPath(Path.Combine(Root, ".artifacts", "security", "local-development"));
        Assert.AreEqual(expected, Credentials);
        if (HasDirectoryLink)
        {
            Directory.Delete(Credentials);
        }
        else if (Directory.Exists(Credentials))
        {
            foreach (string name in Names.Concat(extraFiles))
            {
                string path = Path.GetFullPath(Path.Combine(Credentials, name));
                Assert.AreEqual(Credentials, Path.GetDirectoryName(path));
                File.Delete(path);
            }
            Directory.Delete(Credentials);
        }
        string security = Path.Combine(Root, ".artifacts", "security");
        string artifacts = Path.Combine(Root, ".artifacts");
        if (Directory.Exists(security))
        {
            Directory.Delete(security);
        }
        if (Directory.Exists(artifacts))
        {
            Directory.Delete(artifacts);
        }
        Directory.Delete(Root);
    }
}
