using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace CIS.TestSupport.Transactions;

// Native ownership/failure seams only. These doubles execute no SQL and cannot establish database atomicity.
internal sealed class RecordingTransactionDataSource : DbDataSource
{
    internal ConcurrentQueue<string> Events { get; } = new();
    internal ConcurrentQueue<RecordingTransactionConnection> Connections { get; } = new();
    internal Func<CancellationToken, ValueTask<DbConnection>>? OpenHandler
    {
        get; set;
    }
    internal Action<RecordingTransactionConnection>? ConnectionCreated
    {
        get; set;
    }
    internal int OpenCalls;
    internal int DisposeCalls;

    public override string ConnectionString => "fixture-private-connection";

    protected override DbConnection CreateDbConnection()
    {
        var connection = new RecordingTransactionConnection(Events);
        Connections.Enqueue(connection);
        ConnectionCreated?.Invoke(connection);
        return connection;
    }

    protected override ValueTask<DbConnection> OpenDbConnectionAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref OpenCalls);
        Events.Enqueue("open");
        if (OpenHandler is { } handler)
        {
            return handler(cancellationToken);
        }
        var connection = (RecordingTransactionConnection)CreateDbConnection();
        connection.Open();
        return ValueTask.FromResult<DbConnection>(connection);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Interlocked.Increment(ref DisposeCalls);
        }
    }

    protected override ValueTask DisposeAsyncCore()
    {
        Interlocked.Increment(ref DisposeCalls);
        return ValueTask.CompletedTask;
    }
}

internal sealed class RecordingTransactionConnection(ConcurrentQueue<string> events) : DbConnection
{
    private ConnectionState state;
    internal ConcurrentQueue<RecordingNativeTransaction> Transactions { get; } = new();
    internal Func<IsolationLevel, CancellationToken, ValueTask<DbTransaction>>? BeginHandler
    {
        get; set;
    }
    internal Action<RecordingNativeTransaction>? TransactionCreated
    {
        get; set;
    }
    internal Func<ValueTask>? DisposeHandler
    {
        get; set;
    }
    internal int BeginCalls;
    internal int DisposeCalls;
    internal IsolationLevel? RequestedIsolation
    {
        get; private set;
    }

    [AllowNull]
    public override string ConnectionString { get; set; } = "fixture-private-connection";
    public override string Database => "fixture";
    public override string DataSource => "fixture";
    public override string ServerVersion => "fixture";
    public override ConnectionState State => state;
    public override void Open() => state = ConnectionState.Open;
    public override void Close() => state = ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
    protected override DbCommand CreateDbCommand() => throw new NotSupportedException("The recording provider executes no SQL.");
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

    protected override ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref BeginCalls);
        RequestedIsolation = isolationLevel;
        events.Enqueue("begin");
        if (BeginHandler is { } handler)
        {
            return handler(isolationLevel, cancellationToken);
        }
        var transaction = new RecordingNativeTransaction(this, isolationLevel, events);
        Transactions.Enqueue(transaction);
        TransactionCreated?.Invoke(transaction);
        return ValueTask.FromResult<DbTransaction>(transaction);
    }

    public override async ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref DisposeCalls);
        events.Enqueue("connection.dispose");
        try
        {
            if (DisposeHandler is { } handler)
            {
                await handler();
            }
        }
        finally
        {
            state = ConnectionState.Closed;
        }
    }
}

internal sealed class RecordingNativeTransaction(RecordingTransactionConnection connection, IsolationLevel isolation,
    ConcurrentQueue<string> events) : DbTransaction
{
    internal Func<CancellationToken, Task>? CommitHandler
    {
        get; set;
    }
    internal Func<CancellationToken, Task>? RollbackHandler
    {
        get; set;
    }
    internal Func<ValueTask>? DisposeHandler
    {
        get; set;
    }
    internal int CommitCalls;
    internal int RollbackCalls;
    internal int DisposeCalls;
    internal CancellationToken CommitToken
    {
        get; private set;
    }
    internal CancellationToken RollbackToken
    {
        get; private set;
    }
    public override IsolationLevel IsolationLevel => isolation;
    protected override DbConnection DbConnection => connection;
    public override void Commit() => throw new NotSupportedException();
    public override void Rollback() => throw new NotSupportedException();

    public override Task CommitAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref CommitCalls);
        events.Enqueue("commit");
        CommitToken = cancellationToken;
        return CommitHandler?.Invoke(cancellationToken) ?? Task.CompletedTask;
    }

    public override Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref RollbackCalls);
        events.Enqueue("rollback");
        RollbackToken = cancellationToken;
        return RollbackHandler?.Invoke(cancellationToken) ?? Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref DisposeCalls);
        events.Enqueue("transaction.dispose");
        return DisposeHandler?.Invoke() ?? ValueTask.CompletedTask;
    }
}
