using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class VerificationCheckpointTests
{
    [TestMethod]
    public void SelectionBelongsToItsRepositoryAndDoesNotRedirectIsolatedFixtures()
    {
        using var repository = new TemporaryDirectory(); using var isolated = new TemporaryDirectory();
        string selected = Path.Combine(isolated.Path, "release-evidence");
        Assert.AreEqual(selected, VerificationCheckpoint.Resolve(repository.Path, selected, repository.Path + Path.DirectorySeparatorChar));
        Assert.ThrowsExactly<InvalidOperationException>(() => VerificationCheckpoint.Resolve(isolated.Path, selected, repository.Path));
        Assert.ThrowsExactly<InvalidOperationException>(() => VerificationCheckpoint.Resolve(isolated.Path, selected, null));
        Assert.ThrowsExactly<InvalidOperationException>(() => VerificationCheckpoint.Resolve(isolated.Path, null, isolated.Path));
    }
}
