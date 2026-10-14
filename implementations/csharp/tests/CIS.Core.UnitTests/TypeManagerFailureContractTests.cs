using CIS.TypeManagement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class TypeManagerFailureContractTests
{
    [TestMethod]
    public void FailureCapturesContextAndOwnsItsReadOnlyLoaderExceptions()
    {
        var inner = new InvalidOperationException("Original cause.");
        var first = new TypeLoadException("First loader cause.");
        var second = new TypeLoadException("Second loader cause.");
        Exception[] supplied = [first, second];
        const string manifest = "/deployment/application.inventory.json";
        const string asset = "/deployment/Plugin.dll";
        const string selected = "Plugin, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
        const string observed = "Plugin, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null";
        var failure = new TypeManagerInitializationException(TypeManagerFailureKind.TypeDiscoveryFailure,
            "Discovery did not complete.", inner, manifest, asset, selected, observed, supplied);

        Assert.AreEqual(TypeManagerFailureKind.TypeDiscoveryFailure, failure.FailureKind);
        Assert.AreSame(inner, failure.InnerException);
        Assert.AreEqual(manifest, failure.ManifestPath);
        Assert.AreEqual(asset, failure.AssetPath);
        Assert.AreEqual(selected, failure.AssemblyIdentity);
        Assert.AreEqual(observed, failure.ActualAssemblyIdentity);
        foreach (string context in new[] { manifest, asset, selected, observed })
        {
            Assert.Contains(context, failure.Message, StringComparison.Ordinal);
        }
        supplied[0] = new Exception("Caller changed the original array.");
        Assert.HasCount(2, failure.LoaderExceptions);
        Assert.AreSame(first, failure.LoaderExceptions[0]);
        Assert.AreSame(second, failure.LoaderExceptions[1]);
        if (failure.LoaderExceptions is IList<Exception> mutableView)
        {
            Assert.IsTrue(mutableView.IsReadOnly);
            Assert.ThrowsExactly<NotSupportedException>(() => mutableView[0] = inner);
            Assert.ThrowsExactly<NotSupportedException>(() => mutableView.Add(inner));
        }
        Assert.AreSame(first, failure.LoaderExceptions[0]);
        Assert.HasCount(2, failure.LoaderExceptions);
    }

    [TestMethod]
    public void FailureWithoutOptionalContextHasNoSyntheticLoaderException()
    {
        var inner = new FormatException("Original cause.");
        var failure = new TypeManagerInitializationException(TypeManagerFailureKind.InvalidInventory,
            "The supplied inventory is invalid.", inner);

        Assert.AreSame(inner, failure.InnerException);
        Assert.AreEqual(TypeManagerFailureKind.InvalidInventory, failure.FailureKind);
        Assert.Contains("The supplied inventory is invalid.", failure.Message, StringComparison.Ordinal);
        Assert.IsNull(failure.ManifestPath);
        Assert.IsNull(failure.AssetPath);
        Assert.IsNull(failure.AssemblyIdentity);
        Assert.IsNull(failure.ActualAssemblyIdentity);
        Assert.IsEmpty(failure.LoaderExceptions);
    }
}

