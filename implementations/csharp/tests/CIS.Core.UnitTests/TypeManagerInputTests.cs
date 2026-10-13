using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using CIS.TypeManagement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class TypeManagerInputTests
{
    private static Assembly Host => typeof(TypeManagerInputTests).Assembly;

    [TestMethod]
    public void NullArgumentsFailSynchronouslyBeforeReadingAnyInventory()
    {
        Assert.Throws<ArgumentNullException>(() => TypeManager.CreateAsync(null!));
        Assert.Throws<ArgumentNullException>(() => TypeManager.CreateAsync(new TypeManagerOptions(null!)));
        Assert.Throws<ArgumentNullException>(() => TypeManager.CreateAsync(new TypeManagerOptions(Host) { PrecomputedContracts = null! }));
        Assert.Throws<ArgumentNullException>(() => TypeManager.CreateAsync(new TypeManagerOptions(Host) { PrecomputedContracts = [null!] }));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("\t\r\n")]
    public void BlankAlternatePathIsAnArgumentError(string path)
    {
        Assert.Throws<ArgumentException>(() => TypeManager.CreateAsync(new TypeManagerOptions(Host) { AssemblyManifestPath = path }));
    }

    [TestMethod]
    public void InvalidPathTextNamesThePublicOptionsParameter()
    {
        var error = Assert.Throws<ArgumentException>(() => TypeManager.CreateAsync(new TypeManagerOptions(Host) { AssemblyManifestPath = "bad\0path" }));
        Assert.AreEqual("options", error.ParamName);
        Assert.IsInstanceOfType<ArgumentException>(error.InnerException);
    }

    [TestMethod]
    public void InputsAreOwnedAndDefaultPathUsesTheSuppliedHostName()
    {
        var list = new List<Type> { typeof(IDisposable), typeof(IDisposable) };
        var input = PreparationInputs.Capture(new TypeManagerOptions(Host) { PrecomputedContracts = list });
        list.Clear();
        list.Add(typeof(int));
        CollectionAssert.AreEqual(new[] { typeof(IDisposable) }, input.Contracts);
        Assert.AreEqual(Path.Combine(AppContext.BaseDirectory, Host.GetName().Name + ".types.json"), input.ManifestPath);
    }

    [TestMethod]
    [DataRow("value")]
    [DataRow("nullable")]
    [DataRow("open-generic")]
    [DataRow("type-parameter")]
    [DataRow("array")]
    [DataRow("pointer")]
    [DataRow("byref")]
    [DataRow("function-pointer")]
    [DataRow("delegated")]
    public void UnsupportedTypeShapesAreRejectedBeforePreparation(string shape)
    {
        Type type = shape switch
        {
            "value" => typeof(int),
            "nullable" => typeof(int?),
            "open-generic" => typeof(List<>),
            "type-parameter" => typeof(List<>).GetGenericArguments()[0],
            "array" => typeof(int[]),
            "pointer" => typeof(int).MakePointerType(),
            "byref" => typeof(int).MakeByRefType(),
            "function-pointer" => typeof(FunctionPointerHolder).GetField(nameof(FunctionPointerHolder.Pointer))!.FieldType,
            "delegated" => new TypeDelegator(typeof(IDisposable)),
            _ => throw new ArgumentException(shape, nameof(shape))
        };
        Assert.Throws<ArgumentException>(() => PreparationInputs.ValidateContract(type));
        Assert.Throws<ArgumentException>(() => TypeManager.CreateAsync(new TypeManagerOptions(Host) { PrecomputedContracts = [type] }));
    }

    [TestMethod]
    public void ForeignContextIsRejectedInsideClosedGenericContractsAndForTheHost()
    {
        var context = new AssemblyLoadContext("type-manager-input-test", isCollectible: true);
        try
        {
            var foreignAssembly = context.LoadFromAssemblyPath(Host.Location);
            var foreignType = foreignAssembly.GetType(typeof(TypeManagerInputTests).FullName!)!;
            Assert.Throws<ArgumentException>(() => PreparationInputs.ValidateContract(foreignType));
            Assert.Throws<ArgumentException>(() => PreparationInputs.ValidateContract(typeof(IEnumerable<>).MakeGenericType(foreignType)));
            Assert.Throws<ArgumentException>(() => TypeManager.CreateAsync(new TypeManagerOptions(foreignAssembly)));
        }
        finally { context.Unload(); }
    }

    [TestMethod]
    public void DynamicTypesAndTheirHostAssemblyAreRejected()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("DynamicInput_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var dynamicType = assembly.DefineDynamicModule("Main").DefineType("DynamicType", TypeAttributes.Public).CreateType()!;
        Assert.Throws<ArgumentException>(() => PreparationInputs.ValidateContract(dynamicType));
        Assert.Throws<ArgumentException>(() => TypeManager.CreateAsync(new TypeManagerOptions(assembly)));
    }

    [TestMethod]
    public void ClosedContractsWithValueOrArrayArgumentsRemainSupported()
    {
        PreparationInputs.ValidateContract(typeof(IEnumerable<int>));
        PreparationInputs.ValidateContract(typeof(IEnumerable<string[]>));
        PreparationInputs.ValidateContract(typeof(Dictionary<string, List<int>>));
    }

    [TestMethod]
    public async Task AlreadyCanceledInputReturnsACanceledTask()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var preparation = TypeManager.CreateAsync(new TypeManagerOptions(Host), cancellation.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(() => preparation);
        Assert.IsTrue(preparation.IsCanceled);
    }

    [TestMethod]
    public void RelativeManifestPathsUseTheApplicationDirectoryAndOptionErrorsNameOptions()
    {
        var input = PreparationInputs.Capture(new TypeManagerOptions(Host) { AssemblyManifestPath = "relative.types.json" });
        Assert.AreEqual(Path.Combine(AppContext.BaseDirectory, "relative.types.json"), input.ManifestPath);
        var invalid = Assert.Throws<ArgumentException>(() => TypeManager.CreateAsync(new TypeManagerOptions(Host) { PrecomputedContracts = [typeof(int)] }));
        Assert.AreEqual("options", invalid.ParamName);
    }

    private unsafe sealed class FunctionPointerHolder
    {
        public delegate*<void> Pointer = null;
    }
}
