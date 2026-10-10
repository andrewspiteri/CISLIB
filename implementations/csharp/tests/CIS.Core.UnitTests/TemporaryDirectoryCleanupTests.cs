using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class TemporaryDirectoryCleanupTests
{
    [TestMethod]
    public void SuccessfulDeletionDoesNotWaitOrChangeTheTarget()
    {
        string? observed = null;
        int waits = 0;

        OwnedDirectoryCleanup.Delete("owned-child", path => observed = path, () => waits++);

        Assert.AreEqual("owned-child", observed);
        Assert.AreEqual(0, waits);
    }

    [TestMethod]
    [DataRow(32)]
    [DataRow(33)]
    public void TransientSharingErrorsRetryOnlyOnWindows(int error)
    {
        int attempts = 0;
        int waits = 0;
        var failure = new IOException("Controlled sharing violation", unchecked((int)0x80070000) | error);
        void Delete(string path)
        {
            Assert.AreEqual("owned-child", path);
            if (++attempts < 3)
            {
                throw failure;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            OwnedDirectoryCleanup.Delete("owned-child", Delete, () => waits++);
            Assert.AreEqual(3, attempts);
            Assert.AreEqual(2, waits);
        }
        else
        {
            IOException actual = Assert.ThrowsExactly<IOException>(() => OwnedDirectoryCleanup.Delete("owned-child", Delete, () => waits++));
            Assert.AreSame(failure, actual);
            Assert.AreEqual(1, attempts);
            Assert.AreEqual(0, waits);
        }
    }

    [TestMethod]
    [DataRow(5)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(145)]
    public void OtherIOFailuresPropagateImmediately(int error)
    {
        int waits = 0;
        var failure = new IOException("Controlled other failure", unchecked((int)0x80070000) | error);

        IOException actual = Assert.ThrowsExactly<IOException>(() => OwnedDirectoryCleanup.Delete("owned-child", _ => throw failure, () => waits++));

        Assert.AreSame(failure, actual);
        Assert.AreEqual(0, waits);
    }

    [TestMethod]
    [DataRow(32)]
    [DataRow(33)]
    public void PersistentSharingErrorsStillFailAtTheFiniteLimit(int error)
    {
        int attempts = 0;
        int waits = 0;
        var failure = new IOException("Persistent controlled lock", unchecked((int)0x80070000) | error);
        void Delete(string path)
        {
            attempts++;
            throw failure;
        }

        IOException actual = Assert.ThrowsExactly<IOException>(() => OwnedDirectoryCleanup.Delete("owned-child", Delete, () => waits++));

        Assert.AreSame(failure, actual);
        int expectedAttempts = OperatingSystem.IsWindows() ? OwnedDirectoryCleanup.MaximumAttempts : 1;
        Assert.AreEqual(expectedAttempts, attempts);
        Assert.AreEqual(expectedAttempts - 1, waits);
    }

    [TestMethod]
    public void AccessFailuresAreNotHiddenByTheRetry()
    {
        int waits = 0;
        var failure = new UnauthorizedAccessException("Controlled access failure");

        UnauthorizedAccessException actual = Assert.ThrowsExactly<UnauthorizedAccessException>(() => OwnedDirectoryCleanup.Delete("owned-child", _ => throw failure, () => waits++));

        Assert.AreSame(failure, actual);
        Assert.AreEqual(0, waits);
    }
}
