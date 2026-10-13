using System.Text;
using CIS.Logging;
using CIS.Logging.Runtime;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class FileAcquisitionTests
{
    [TestMethod]
    [DataRow("logs/app.jsonl")]
    [DataRow("logs/\u03a9-\U0001f642-e\u0301.jsonl")]
    public void PreparationDoesNoIoAndLaterCwdCannotRedirectAcquisition(string relativePath)
    {
        using var root = new TemporaryDirectory();
        string captured = Path.Combine(root.Path, "captured"), later = Path.Combine(root.Path, "later");
        var options = new LoggingOptions { FilePath = relativePath };
        options.Prepare(captured);
        string path = options.FilePath!;
        Assert.IsFalse(Directory.Exists(captured));
        Directory.CreateDirectory(later);
        string previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = later;
            using var owner = FileOutputOwner.Initialize(path);
            Assert.AreEqual(Path.Combine(captured, relativePath.Replace('/', Path.DirectorySeparatorChar)), owner.AbsolutePath);
            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual(0L, owner.ActiveFile.Length);
            Assert.AreEqual(0, Directory.GetFileSystemEntries(later).Length);
        }
        finally { Environment.CurrentDirectory = previous; }
    }

    [TestMethod]
    public void ExistingBytesReadersAndPersistentLeaseSurviveIdempotentRelease()
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "existing.jsonl");
        byte[] bytes = Encoding.UTF8.GetBytes("existing-unvalidated-content\n");
        File.WriteAllBytes(path, bytes);
        var owner = FileOutputOwner.Initialize(path);
        try
        {
            using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] observed = new byte[bytes.Length]; reader.ReadExactly(observed);
            CollectionAssert.AreEqual(bytes, observed);
            CoreLogInitializationException error = Assert.ThrowsExactly<CoreLogInitializationException>(() => FileOutputOwner.Initialize(path));
            AssertSafe(error, "FileOwnershipUnavailable");
        }
        finally { owner.Dispose(); }
        owner.Dispose();
        Assert.IsTrue(File.Exists(path + ".cis.lock"));
        Assert.AreEqual(0L, new FileInfo(path + ".cis.lock").Length);
        using var next = FileOutputOwner.Initialize(path);
        byte[] final = new byte[bytes.Length]; next.ActiveFile.ReadExactly(final);
        CollectionAssert.AreEqual(bytes, final);
    }

    [TestMethod]
    public void FailedActiveOpenReleasesLeaseAndPreservesExistingDirectory()
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "directory.jsonl"); Directory.CreateDirectory(path);
        string retained = Path.Combine(path, "retained.txt"); File.WriteAllText(retained, "keep");
        AssertSafe(Assert.ThrowsExactly<CoreLogInitializationException>(() => FileOutputOwner.Initialize(path)), "FileInitializationFailed");
        using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.AreEqual("keep", File.ReadAllText(retained));
    }

    [TestMethod]
    public void NonemptyLeaseCollisionIsPreservedAndActiveFileIsNotCreated()
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "collision.jsonl");
        File.WriteAllText(path + ".cis.lock", "unrelated");
        AssertSafe(Assert.ThrowsExactly<CoreLogInitializationException>(() => FileOutputOwner.Initialize(path)), "FileOwnershipUnavailable");
        Assert.IsFalse(File.Exists(path));
        Assert.AreEqual("unrelated", File.ReadAllText(path + ".cis.lock"));
        using var released = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("relative.jsonl")]
    public void UncapturedPathsFailBeforeCreatingAnyArtifact(string path)
    {
        Assert.Throws<ArgumentException>(() => FileOutputOwner.Initialize(path));
    }

    [TestMethod]
    public void ParentFileFailurePreservesBytesAndDoesNotChooseFallback()
    {
        using var root = new TemporaryDirectory(); string parent = Path.Combine(root.Path, "parent");
        File.WriteAllText(parent, "keep-parent");
        AssertSafe(Assert.ThrowsExactly<CoreLogInitializationException>(() => FileOutputOwner.Initialize(Path.Combine(parent, "active.jsonl"))), "FileInitializationFailed");
        Assert.AreEqual("keep-parent", File.ReadAllText(parent));
        Assert.AreEqual(1, Directory.GetFileSystemEntries(root.Path).Length);
    }

    private static void AssertSafe(CoreLogInitializationException error, string code)
    {
        Assert.AreEqual("file", error.OutputName); Assert.AreEqual(code, error.ErrorCode);
        Assert.AreEqual($"Logging output 'file' could not initialize ({code}).", error.Message);
        Assert.IsNull(error.InnerException); Assert.AreEqual(0, error.Data.Count);
    }
}
