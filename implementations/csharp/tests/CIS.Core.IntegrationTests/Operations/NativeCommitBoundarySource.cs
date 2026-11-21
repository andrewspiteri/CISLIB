using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace CIS.Tests.IntegrationTests.Operations;

// A narrow test-only native seam: all SQL and commit work runs on the real selected provider.
// It can report one failure AFTER a real successful commit or transaction disposal.
// It neither fabricates TransactionCompletion nor simulates a network transport.
internal sealed class NativeCommitBoundarySource(DbDataSource inner) : DbDataSource
{
    private int failCommit;
    private int failDisposal;
    internal int CommitInvocations;
    internal int ConfirmedNativeCommits;
    internal int RollbackInvocations;
    internal int TransactionDisposals;
    internal int ConnectionDisposals;

    internal void FailAfterNextCommit() => Interlocked.Exchange(ref failCommit, 1);

    internal void FailAfterNextTransactionDisposal() => Interlocked.Exchange(ref failDisposal, 1);

    internal bool TakeCommitFailure() => Interlocked.Exchange(ref failCommit, 0) != 0;

    internal bool TakeDisposalFailure() => Interlocked.Exchange(ref failDisposal, 0) != 0;

    public override string ConnectionString => "owned-native-commit-boundary";

    protected override DbConnection CreateDbConnection() => new NativeCommitBoundaryConnection(inner.CreateConnection(), this);

    protected override async ValueTask<DbConnection> OpenDbConnectionAsync(CancellationToken cancellationToken = default)
        => new NativeCommitBoundaryConnection(await inner.OpenConnectionAsync(cancellationToken), this);

    // The fixture owns the real data source separately and drains it after every wrapper lease settles.
    protected override void Dispose(bool disposing)
    {
    }

    protected override ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;
}

internal sealed class NativeCommitBoundaryConnection(DbConnection inner, NativeCommitBoundarySource boundary) : DbConnection
{
    internal DbConnection Inner => inner;

    [AllowNull]
    public override string ConnectionString
    {
        get => "owned-native-commit-boundary";
        set => throw new NotSupportedException();
    }

    public override string Database => inner.Database;
    public override string DataSource => inner.DataSource;
    public override string ServerVersion => inner.ServerVersion;
    public override ConnectionState State => inner.State;
    public override void ChangeDatabase(string databaseName) => inner.ChangeDatabase(databaseName);
    public override void Close() => inner.Close();
    public override Task CloseAsync() => inner.CloseAsync();
    public override void Open() => inner.Open();
    public override Task OpenAsync(CancellationToken cancellationToken) => inner.OpenAsync(cancellationToken);

    protected override DbCommand CreateDbCommand() => new NativeCommitBoundaryCommand(inner.CreateCommand(), this);

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => new NativeCommitBoundaryTransaction(inner.BeginTransaction(isolationLevel), this, boundary);

    protected override async ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken)
        => new NativeCommitBoundaryTransaction(await inner.BeginTransactionAsync(isolationLevel, cancellationToken), this, boundary);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Interlocked.Increment(ref boundary.ConnectionDisposals);
            inner.Dispose();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref boundary.ConnectionDisposals);
        await inner.DisposeAsync();
    }
}

internal sealed class NativeCommitBoundaryTransaction(DbTransaction inner, NativeCommitBoundaryConnection connection,
    NativeCommitBoundarySource boundary) : DbTransaction
{
    internal DbTransaction Inner => inner;

    public override IsolationLevel IsolationLevel => inner.IsolationLevel;

    protected override DbConnection DbConnection => connection;

    public override void Commit()
    {
        Interlocked.Increment(ref boundary.CommitInvocations);
        inner.Commit();
        Committed();
    }

    public override async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref boundary.CommitInvocations);
        await inner.CommitAsync(cancellationToken);
        Committed();
    }

    private void Committed()
    {
        Interlocked.Increment(ref boundary.ConfirmedNativeCommits);
        if (boundary.TakeCommitFailure())
        {
            throw new IOException("Simulated acknowledgement loss after a real native commit.");
        }
    }

    public override void Rollback()
    {
        Interlocked.Increment(ref boundary.RollbackInvocations);
        inner.Rollback();
    }

    public override Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref boundary.RollbackInvocations);
        return inner.RollbackAsync(cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
            Disposed();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        Disposed();
    }

    private void Disposed()
    {
        Interlocked.Increment(ref boundary.TransactionDisposals);
        if (boundary.TakeDisposalFailure())
        {
            throw new IOException("Controlled failure after real native transaction disposal.");
        }
    }
}

internal sealed class NativeCommitBoundaryCommand(DbCommand inner, NativeCommitBoundaryConnection connection) : DbCommand
{
    private DbTransaction? transaction;

    [AllowNull]
    public override string CommandText
    {
        get => inner.CommandText; set => inner.CommandText = value;
    }
    public override int CommandTimeout
    {
        get => inner.CommandTimeout; set => inner.CommandTimeout = value;
    }
    public override CommandType CommandType
    {
        get => inner.CommandType; set => inner.CommandType = value;
    }
    public override bool DesignTimeVisible
    {
        get => inner.DesignTimeVisible; set => inner.DesignTimeVisible = value;
    }
    public override UpdateRowSource UpdatedRowSource
    {
        get => inner.UpdatedRowSource; set => inner.UpdatedRowSource = value;
    }

    protected override DbConnection? DbConnection
    {
        get => connection;
        set => inner.Connection = value is NativeCommitBoundaryConnection wrapped ? wrapped.Inner : value;
    }

    protected override DbTransaction? DbTransaction
    {
        get => transaction;
        set
        {
            transaction = value;
            inner.Transaction = value is NativeCommitBoundaryTransaction wrapped ? wrapped.Inner : value;
        }
    }

    protected override DbParameterCollection DbParameterCollection => inner.Parameters;
    protected override DbParameter CreateDbParameter() => inner.CreateParameter();
    public override void Cancel() => inner.Cancel();
    public override void Prepare() => inner.Prepare();
    public override Task PrepareAsync(CancellationToken cancellationToken = default) => inner.PrepareAsync(cancellationToken);
    public override int ExecuteNonQuery() => inner.ExecuteNonQuery();
    public override object? ExecuteScalar() => inner.ExecuteScalar();
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => inner.ExecuteNonQueryAsync(cancellationToken);
    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) => inner.ExecuteScalarAsync(cancellationToken);
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => inner.ExecuteReader(behavior);
    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        => inner.ExecuteReaderAsync(behavior, cancellationToken);
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }
    }
    public override ValueTask DisposeAsync() => inner.DisposeAsync();
}
