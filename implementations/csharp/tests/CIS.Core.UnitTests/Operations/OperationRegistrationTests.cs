using System.Text;
using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Operations;

[TestClass]
public sealed class OperationRegistrationTests
{
    [TestMethod]
    public void DefinitionCopiesCapabilitiesVersionsReadersLimitsAndRetention()
    {
        string[] execute = ["ledger.submit", "ledger.read"];
        int[] comparisons = [1, 2];
        OperationContract[] readers = [new("receipt", 1), new("receipt", 2)];
        var codec = new Codec { CurrentContract = readers[1], Readers = readers };
        var options = new OperationDefinitionOptions
        {
            ExecuteCapabilities = execute,
            LookupCapabilities = ["ledger.read"],
            FingerprintVersions = comparisons,
            Retention = new(TimeSpan.FromDays(30), TimeSpan.FromDays(365))
        };
        OperationDefinition<int, string> definition = Define("Debit", options, codec, version: 2);
        execute[0] = "other.submit";
        comparisons[0] = 3;
        readers[0] = new("receipt", 3);
        options.MaximumCanonicalBytes = 1;
        options.Retention = new();
        codec.CurrentContract = new("changed", 99);
        Assert.AreEqual("ledger.submit", definition.ExecuteCapabilities[0]);
        CollectionAssert.AreEqual(new[] { 1, 2 }, definition.FingerprintVersions.ToArray());
        Assert.AreEqual(1, definition.ReadableResultContracts[0].Version);
        Assert.AreEqual("receipt", definition.ResultContract.Identifier);
        Assert.AreEqual(2, definition.ResultContract.Version);
        Assert.AreEqual(65_536, definition.MaximumCanonicalBytes);
        Assert.AreEqual(262_144, definition.MaximumResultBytes);
        Assert.AreEqual(TimeSpan.FromDays(365), definition.Retention.DuplicateProtectionRetention);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)definition.ExecuteCapabilities).Clear());
    }

    [TestMethod]
    public void PassiveRegistrationAndProviderBuildDoNotResolveOrRunApplicationDependencies()
    {
        int resolved = 0;
        var normalizer = new Normalizer();
        var codec = new Codec();
        var definition = new OperationDefinition<int, string>("Debit", "Ledger", new("request", 1), normalizer, codec,
            _ =>
            {
                resolved++;
                throw new AssertFailedException("Passive registration cannot resolve the scoped application mapper.");
            }, Options());
        var services = new ServiceCollection();
        services.AddCisOperation(definition);
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        OperationsComposition composition = provider.GetRequiredService<OperationsComposition>();
        CollectionAssert.AreEqual(new[] { "Debit" }, composition.OperationNames.ToArray());
        Assert.AreEqual(0, resolved);
        Assert.AreEqual(0, normalizer.Calls);
        Assert.AreEqual(0, codec.Encodes);
        Assert.IsNull(provider.GetService<IDurableOperationService>(), "The foundation registers no throwing service placeholder.");
    }

    [TestMethod]
    public void NamedRegistrationIsOrdinalFiniteAndRetainsOneCompositionIdentity()
    {
        var services = new ServiceCollection();
        services.AddCisOperation(Define("first"));
        var first = (OperationsComposition)services.Single(item => item.ServiceType == typeof(OperationsComposition)).ImplementationInstance!;
        services.AddCisOperation(Define("First"));
        for (int index = 2; index < 16; index++)
        {
            services.AddCisOperation(Define("operation" + index));
        }
        var last = (OperationsComposition)services.Single(item => item.ServiceType == typeof(OperationsComposition)).ImplementationInstance!;
        Assert.AreSame(first.RegistrationIdentity, last.RegistrationIdentity);
        Assert.AreEqual(1, first.OperationNames.Count);
        Assert.AreEqual(16, last.OperationNames.Count);
        Assert.ThrowsExactly<ArgumentException>(() => services.AddCisOperation(Define("first")));
        Assert.ThrowsExactly<InvalidOperationException>(() => services.AddCisOperation(Define("seventeenth")));
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)last.OperationNames).Clear());
    }

    [TestMethod]
    public void MissingAuthorizationOrUnsupportedDefinitionBoundsFailDuringCapture()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Define("Debit", new OperationDefinitionOptions()));
        Assert.ThrowsExactly<ArgumentException>(() => Define("Debit", new OperationDefinitionOptions
        {
            ExecuteCapabilities = ["ledger.submit"],
            LookupCapabilities = []
        }));
        Assert.ThrowsExactly<ArgumentException>(() => Define("Debit", new OperationDefinitionOptions
        {
            ExecuteCapabilities = ["Ledger.Submit"],
            LookupCapabilities = ["ledger.read"]
        }));
        OperationDefinitionOptions options = Options();
        options.MaximumResultBytes = 1_048_577;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Define("Debit", options));
        options = Options();
        options.FingerprintVersions = [2];
        Assert.ThrowsExactly<ArgumentException>(() => Define("Debit", options));
        options = Options();
        options.FingerprintVersions = [1, 2, 3, 4, 5];
        Assert.ThrowsExactly<ArgumentException>(() => Define("Debit", options));
        var codec = new Codec { Readers = [new("other", 1)] };
        Assert.ThrowsExactly<ArgumentException>(() => Define("Debit", codec: codec));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationScope("scope", "", "resource"));
        Assert.ThrowsExactly<ArgumentNullException>(() => new OperationScope("scope", null!, null!));
    }

    [TestMethod]
    public void ManualOverflowDuplicateAndOpaqueDefinitionsCannotBypassPassiveMetadata()
    {
        var overflow = new ServiceCollection();
        for (int index = 0; index < 17; index++)
        {
            overflow.AddSingleton<OperationDefinition>(Define("prefilled" + index));
        }
        int before = overflow.Count;
        Assert.ThrowsExactly<InvalidOperationException>(() => overflow.AddCisOperation(Define("extra")));
        Assert.AreEqual(before, overflow.Count);

        var duplicate = new ServiceCollection();
        duplicate.AddSingleton<OperationDefinition>(Define("same"));
        duplicate.AddSingleton<OperationDefinition>(Define("same"));
        Assert.ThrowsExactly<InvalidOperationException>(() => duplicate.AddCisOperation(Define("extra")));

        bool invoked = false;
        var opaque = new ServiceCollection();
        opaque.AddSingleton<OperationDefinition>(_ =>
        {
            invoked = true;
            return Define("opaque");
        });
        Assert.ThrowsExactly<InvalidOperationException>(() => opaque.AddCisOperation(Define("extra")));
        Assert.IsFalse(invoked);

        var keyed = new ServiceCollection();
        keyed.AddKeyedSingleton<OperationDefinition>("key", Define("keyed"));
        Assert.ThrowsExactly<InvalidOperationException>(() => keyed.AddCisOperation(Define("extra")));
    }

    [TestMethod]
    public void OpaqueDuplicateAndMismatchedCompositionFailWithoutResolvingAnything()
    {
        bool invoked = false;
        var opaque = new ServiceCollection();
        opaque.AddSingleton<OperationsComposition>(_ =>
        {
            invoked = true;
            return new(new object(), []);
        });
        Assert.ThrowsExactly<InvalidOperationException>(() => opaque.AddCisOperation(Define("extra")));
        Assert.IsFalse(invoked);

        var duplicate = new ServiceCollection();
        duplicate.AddSingleton(new OperationsComposition(new object(), []));
        duplicate.AddSingleton(new OperationsComposition(new object(), []));
        Assert.ThrowsExactly<InvalidOperationException>(() => duplicate.AddCisOperation(Define("extra")));

        var mismatched = new ServiceCollection();
        mismatched.AddCisOperation(Define("first"));
        mismatched.AddSingleton<OperationDefinition>(Define("manual"));
        Assert.ThrowsExactly<InvalidOperationException>(() => mismatched.AddCisOperation(Define("extra")));
    }

    private static OperationDefinitionOptions Options() => new()
    {
        ExecuteCapabilities = ["ledger.submit", "ledger.read"],
        LookupCapabilities = ["ledger.read"]
    };

    private static OperationDefinition<int, string> Define(string name, OperationDefinitionOptions? options = null,
        Codec? codec = null, int version = 1)
        => new(name, "Ledger", new("request", version), new Normalizer(), codec ?? new Codec(), _ => new Resolver(), options ?? Options());

    private sealed class Normalizer : IOperationRequestNormalizer<int>
    {
        internal int Calls;
        public NormalizedOperationInput<int> Normalize(int input, OperationScope scope)
        {
            Calls++;
            return new(input, [new(new("request", 1), OperationCanonicalValue.Integer(input))]);
        }
    }

    private sealed class Codec : IOperationResultCodec<string>
    {
        internal int Encodes;
        internal OperationContract[] Readers = [new("receipt", 1)];
        public OperationContract CurrentContract { get; set; } = new("receipt", 1);
        public IReadOnlyList<OperationContract> ReadableContracts => Readers;
        public byte[] Encode(string? value)
        {
            Encodes++;
            return Encoding.UTF8.GetBytes(value ?? "");
        }
        public bool TryDecode(OperationContract contract, ReadOnlyMemory<byte> value, out string? decoded)
        {
            decoded = Encoding.UTF8.GetString(value.Span);
            return true;
        }
    }

    private sealed class Resolver : IOperationScopeResolver
    {
        public ValueTask<OperationScope> ResolveAsync(string operationName, string requestedScope, CallerContext? caller,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(new OperationScope(requestedScope, "account", requestedScope));
    }
}
