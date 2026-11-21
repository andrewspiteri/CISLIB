using System.Data.Common;
using System.Text;
using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Security;
using CIS.TestSupport.Transactions;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Tests.UnitTests.Operations;

// Controlled orchestration seams, not a SQL engine or proof of database durability.
internal sealed class OperationRuntimeFixture : IDisposable
{
    internal readonly RecordingTransactionDataSource Source = new();
    internal readonly RuntimeJournal Journal = new();
    internal readonly RuntimeNormalizer Normalizer = new();
    internal readonly RuntimeCodec Codec = new();
    internal readonly RuntimeResolver Resolver = new();
    internal readonly RuntimeAuthorizer Authorizer = new();
    internal readonly RuntimeCaller Caller = new();
    internal readonly ServiceProvider Provider;
    internal readonly IServiceScope Scope;
    internal readonly OperationDefinition<RuntimeInput, string> Definition;
    internal readonly ServiceCollection Services;

    internal OperationRuntimeFixture(Action<OperationDefinitionOptions>? configure = null,
        Action<IServiceCollection>? services = null, int version = 1)
    {
        var options = new OperationDefinitionOptions
        {
            ExecuteCapabilities = ["ledger.submit", "ledger.read"],
            LookupCapabilities = ["ledger.read"]
        };
        configure?.Invoke(options);
        Normalizer.Versions = options.FingerprintVersions?.ToArray() ?? [version];
        Definition = new("Debit", "Ledger", new("debit-request", version), Normalizer, Codec, _ => Resolver, options);
        var collection = new ServiceCollection();
        Services = collection;
        collection.AddCisAdoNetDatabase("Ledger", _ => Source, new AdoNetDatabaseOptions
        {
            DefaultIsolation = TransactionIsolation.ReadCommitted,
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted, TransactionIsolation.RepeatableRead, TransactionIsolation.Serializable]
        });
        collection.AddSingleton<IOperationJournal>(Journal);
        collection.AddSingleton<ICapabilityAuthorizer>(Authorizer);
        collection.AddSingleton<ICallerContextAccessor>(Caller);
        collection.AddCisOperations();
        collection.AddCisOperation(Definition);
        services?.Invoke(collection);
        Provider = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Scope = Provider.CreateScope();
    }

    internal IDurableOperationService Service => Scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
    internal ILocalTransactionFactory Transactions => Provider.GetRequiredService<ILocalTransactionFactory>();
    internal Task<ILocalTransaction> Begin() => Transactions.BeginAsync("Ledger");
    internal static OperationRequest<RuntimeInput> Request(int amount = 10, string key = "key") => new("Debit", "account-alias", key, new(amount));
    internal static Task<OperationDecision<string>> Success(ILocalTransactionContext _, RuntimeInput input, CancellationToken token)
        => Task.FromResult(OperationDecision<string>.Succeed("receipt:" + input.Amount));

    public void Dispose()
    {
        Scope.Dispose();
        Provider.Dispose();
        Source.Dispose();
    }
}

internal sealed class RuntimeInput(int amount)
{
    internal int Amount = amount;
    internal string Trace = "ignored";
}

internal sealed class RuntimeNormalizer : IOperationRequestNormalizer<RuntimeInput>
{
    internal int Calls;
    internal int[] Versions = [1];
    internal Func<RuntimeInput, OperationScope, NormalizedOperationInput<RuntimeInput>>? Handler;

    public NormalizedOperationInput<RuntimeInput> Normalize(RuntimeInput input, OperationScope scope)
    {
        Calls++;
        return Handler?.Invoke(input, scope) ?? new(new(input.Amount), Versions.Select(version =>
            new OperationFingerprintInput(new("debit-request", version), OperationCanonicalValue.Sequence(
                [OperationCanonicalValue.Text(scope.Scope), OperationCanonicalValue.Integer(input.Amount)]))));
    }
}

internal sealed class RuntimeCodec : IOperationResultCodec<string>
{
    internal int Encodes;
    internal int Decodes;
    internal Func<string?, byte[]>? Encoder;
    internal Func<OperationContract, ReadOnlyMemory<byte>, string?>? Decoder;
    internal bool DecodeSupported = true;
    public OperationContract CurrentContract { get; set; } = new("debit-result", 1);
    public IReadOnlyList<OperationContract> ReadableContracts { get; set; } = [new("debit-result", 1)];

    public byte[] Encode(string? value)
    {
        Encodes++;
        return Encoder?.Invoke(value) ?? Encoding.UTF8.GetBytes(value ?? "");
    }

    public bool TryDecode(OperationContract contract, ReadOnlyMemory<byte> value, out string? decoded)
    {
        Decodes++;
        decoded = Decoder?.Invoke(contract, value) ?? Encoding.UTF8.GetString(value.Span);
        return DecodeSupported;
    }
}

internal sealed class RuntimeResolver : IOperationScopeResolver
{
    internal int Calls;
    internal CallerContext? ObservedCaller;
    internal OperationScope Scope = new("trusted-account", "account", "actual-account");
    internal Func<CancellationToken, ValueTask<OperationScope>>? Handler;

    public ValueTask<OperationScope> ResolveAsync(string operationName, string requestedScope, CallerContext? caller,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        ObservedCaller = caller;
        return Handler?.Invoke(cancellationToken) ?? ValueTask.FromResult(Scope);
    }
}

internal sealed class RuntimeAuthorizer : ICapabilityAuthorizer
{
    internal int Calls;
    internal CapabilityTarget? Target;
    internal IReadOnlyList<string>? Capabilities;
    internal Func<CancellationToken, ValueTask<AuthorizationDecision>>? Handler;

    public ValueTask<AuthorizationDecision> AuthorizeAsync(IReadOnlyList<string> capabilities, CapabilityTarget target,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        Target = target;
        Capabilities = capabilities;
        return Handler?.Invoke(cancellationToken) ?? ValueTask.FromResult(AuthorizationDecision.Allow);
    }
}

internal sealed class RuntimeCaller : ICallerContextAccessor
{
    public CallerContext? Current
    {
        get; set;
    }
}

internal sealed class RuntimeJournal : IOperationJournal
{
    internal int Claims;
    internal int Reads;
    internal int Writes;
    internal OperationIdentity? Identity;
    internal OperationFingerprint? Fingerprint;
    internal OperationStoredResult? Stored;
    internal ILocalTransactionContext? ClaimedContext;
    internal ILocalTransactionContext? ReadContext;
    internal ILocalTransactionContext? WrittenContext;
    internal Func<ILocalTransactionContext, OperationIdentity, OperationFingerprint, CancellationToken, Task<OperationClaim>>? Claim;
    internal Func<ILocalTransactionContext, OperationIdentity, CancellationToken, Task<OperationJournalRecord?>>? Read;
    internal Func<ILocalTransactionContext, CancellationToken, Task>? Write;

    public Task<OperationClaim> TryClaimAsync(ILocalTransactionContext context, OperationIdentity identity,
        OperationFingerprint fingerprint, CancellationToken cancellationToken = default)
    {
        Claims++;
        Identity = identity;
        Fingerprint = fingerprint;
        ClaimedContext = context;
        return Claim?.Invoke(context, identity, fingerprint, cancellationToken)
            ?? context.UseAdoNetAsync((_, _, _) => Task.FromResult(new OperationClaim(OperationClaimDisposition.Acquired)), cancellationToken);
    }

    public Task<OperationJournalRecord?> ReadAsync(ILocalTransactionContext context, OperationIdentity identity,
        CancellationToken cancellationToken = default)
    {
        Reads++;
        ReadContext = context;
        Identity = identity;
        return Read?.Invoke(context, identity, cancellationToken)
            ?? context.UseAdoNetAsync<OperationJournalRecord?>((_, _, _) => Task.FromResult<OperationJournalRecord?>(null), cancellationToken);
    }

    public async Task<OperationJournalTimes> WriteCompletedAsync(ILocalTransactionContext context, OperationIdentity identity,
        OperationFingerprint fingerprint, OperationStoredResult result, OperationRetentionPolicy retention,
        CancellationToken cancellationToken = default)
    {
        Writes++;
        WrittenContext = context;
        Stored = result;
        if (Write is { } handler)
        {
            await handler(context, cancellationToken);
        }
        else
        {
            await context.UseAdoNetAsync((_, _, _) => Task.CompletedTask, cancellationToken);
        }
        return new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }

    internal static OperationJournalRecord Row(OperationIdentity identity, OperationFingerprint fingerprint,
        OperationStoredResult? result = null, bool expired = false, bool corrupt = false, bool finalized = true)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new(identity, fingerprint,
            result ?? new OperationStoredResult(new("debit-result", 1), OperationBusinessDecision.Succeeded, Encoding.UTF8.GetBytes("original")),
            new(now, now.AddDays(-2), expired ? now.AddDays(-1) : null), finalized, corrupt);
    }
}

internal sealed class RuntimeDbException(bool transient) : DbException("controlled native failure")
{
    public override bool IsTransient => transient;
}

internal sealed class RuntimeReadFactory(RuntimeReadOwner owner) : ILocalTransactionFactory
{
    internal Exception? BeginError;
    internal int Calls;
    internal LocalTransactionOptions? Options;
    public Task<ILocalTransaction> BeginAsync(string databaseName, CancellationToken cancellationToken = default)
        => BeginAsync(databaseName, new LocalTransactionOptions(), cancellationToken);

    public Task<ILocalTransaction> BeginAsync(string databaseName, LocalTransactionOptions options, CancellationToken cancellationToken = default)
    {
        Calls++;
        Options = options;
        return BeginError is { } error ? Task.FromException<ILocalTransaction>(error) : Task.FromResult<ILocalTransaction>(owner);
    }
}

internal sealed class RuntimeReadOwner : ILocalTransaction
{
    internal readonly RuntimeContext Participant = new();
    internal int Rollbacks;
    internal int Disposals;
    internal Exception? RollbackError;
    internal Exception? DisposeError;
    internal TransactionCompletion? Published;
    public ILocalTransactionContext Context => Participant;
    public LocalTransactionStatus Status => Completion is null ? LocalTransactionStatus.Active : LocalTransactionStatus.Completed;
    public TransactionCompletion? Completion => Published;
    public Task<TransactionCompletion> CommitAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Lookup cannot commit.");

    public Task<TransactionCompletion> RollbackAsync()
    {
        Rollbacks++;
        if (RollbackError is { } error)
        {
            return Task.FromException<TransactionCompletion>(error);
        }
        Published ??= new(TransactionOutcome.NotCommitted, TransactionEndReason.RollbackRequested, false, true, true);
        return Task.FromResult(Published);
    }

    public ValueTask DisposeAsync()
    {
        Disposals++;
        return DisposeError is { } error ? ValueTask.FromException(error) : ValueTask.CompletedTask;
    }
}

internal sealed class RuntimeContext : ILocalTransactionContext
{
    public string DatabaseName { get; set; } = "Ledger";
    public TransactionIsolation Isolation { get; set; } = TransactionIsolation.ReadCommitted;
    public bool IsRollbackOnly => RollbackReasonCode is not null;
    public string? RollbackReasonCode
    {
        get; private set;
    }
    public void MarkRollbackOnly(string reasonCode) => RollbackReasonCode ??= reasonCode;
}
