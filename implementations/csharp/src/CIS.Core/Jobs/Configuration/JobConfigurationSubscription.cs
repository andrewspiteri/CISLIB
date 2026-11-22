using System.Runtime.ExceptionServices;
using CIS.Configuration;

namespace CIS.Jobs;

internal interface IJobConfigurationBinding : IAsyncDisposable
{
    void Connect();
}

/// <summary>Adopts whole coordinated snapshots from one existing retained Configuration object.</summary>
/// <remarks>Construction is passive. Connect before starting the runtime. The capture delegate executes
/// synchronously under the existing value gate and must only read/copy: no mutation, I/O, awaiting or nested root reads.
/// The subscription borrows its runtime, reader and root; disposal closes admission and joins admitted reads.</remarks>
public sealed class JobConfigurationSubscription<TConfiguration> : IJobConfigurationBinding
    where TConfiguration : class, IConfigurationObject
{
    private readonly object gate = new();
    private readonly JobRuntime runtime;
    private readonly IConfigurationReader reader;
    private readonly TConfiguration configuration;
    private readonly Func<TConfiguration, long, JobConfigurationSnapshot> capture;
    private readonly TaskCompletionSource subscriptionSettled = Completion();
    private readonly TaskCompletionSource drained = Completion();
    private TaskCompletionSource? connection;
    private TaskCompletionSource? disposal;
    private int active;
    private bool closing;

    /// <summary>Captures borrowed services without subscribing, reading, creating scopes or starting activity.</summary>
    public JobConfigurationSubscription(JobRuntime runtime, IConfigurationReader reader, TConfiguration configuration,
        Func<TConfiguration, long, JobConfigurationSnapshot> capture)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(capture);
        this.runtime = runtime;
        this.reader = reader;
        this.configuration = configuration;
        this.capture = capture;
    }

    /// <summary>Subscribes once before the initial coordinated read and installs revision zero before runtime activation.</summary>
    /// <remarks>A concurrently observed newer event revision wins over initial defaults. Initial invalid configuration
    /// fails and cleans up the subscription; later invalid updates retain the last valid complete snapshot.</remarks>
    public void Connect()
    {
        TaskCompletionSource selected;
        bool ownsConnection;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            ownsConnection = connection is null;
            selected = connection ??= Completion();
            if (ownsConnection)
            {
                active++;
            }
        }
        if (!ownsConnection)
        {
            selected.Task.GetAwaiter().GetResult();
            return;
        }

        ExceptionDispatchInfo? failure = null;
        try
        {
            try
            {
                // User-provided event accessors run without the subscription's state gate.
                configuration.ConfigurationChanged += Changed;
            }
            finally
            {
                subscriptionSettled.TrySetResult();
            }
            if (CanApply())
            {
                CaptureAndApply(0, initial: true);
            }
        }
        catch (Exception error)
        {
            failure = ExceptionDispatchInfo.Capture(error);
        }
        finally
        {
            ReleaseAdmission();
        }

        if (failure is not null)
        {
            try
            {
                DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception cleanup)
            {
                var combined = new AggregateException("Initial job configuration and subscription cleanup both failed.",
                    failure.SourceException, cleanup);
                selected.TrySetException(combined);
                _ = selected.Task.Exception;
                throw combined;
            }
            selected.TrySetException(failure.SourceException);
            _ = selected.Task.Exception;
            failure.Throw();
        }
        selected.TrySetResult();
    }

    /// <summary>Unsubscribes and joins all admitted reads/captures without disposing borrowed runtime or Configuration owners.</summary>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        bool waitForSubscription;
        lock (gate)
        {
            if (disposal is not null)
            {
                return new(disposal.Task);
            }
            closing = true;
            completion = Completion();
            disposal = completion;
            waitForSubscription = connection is not null;
            if (active == 0)
            {
                drained.TrySetResult();
            }
        }
        _ = DisposeCoreAsync(completion, waitForSubscription);
        return new(completion.Task);
    }

    private void Changed(object? sender, ConfigurationChangedEventArgs change)
    {
        if (!ReferenceEquals(sender, configuration) || change.ConfigurationType != configuration.GetType())
        {
            return;
        }
        lock (gate)
        {
            if (closing)
            {
                return;
            }
            active++;
        }
        try
        {
            CaptureAndApply(change.Revision, initial: false);
        }
        finally
        {
            ReleaseAdmission();
        }
    }

    private void CaptureAndApply(long revision, bool initial)
    {
        try
        {
            // This is one pure whole-copy callback under the Configuration module's existing value gate.
            JobConfigurationSnapshot candidate = reader.Read(configuration, current => capture(current, revision))
                ?? throw new InvalidOperationException("The job configuration capture returned no snapshot.");
            if (candidate.Revision != revision)
            {
                throw new InvalidOperationException("A job snapshot must retain the supplied local Configuration revision.");
            }
            if (!CanApply())
            {
                return;
            }
            if (initial)
            {
                if (!runtime.InstallInitialConfiguration(candidate) && runtime.Configuration.Revision == 0)
                {
                    throw new InvalidOperationException("Connect initial job configuration before starting the runtime.");
                }
            }
            else
            {
                runtime.TryApplyConfiguration(candidate);
            }
        }
        catch (Exception error) when (Ordinary(error))
        {
            if (CanApply())
            {
                runtime.RejectConfiguration(revision, "jobs.configuration.capture_failed");
            }
            if (initial)
            {
                throw new InvalidOperationException("Initial job configuration could not be captured and validated.", error);
            }
        }
    }

    private bool CanApply()
    {
        lock (gate)
        {
            return !closing;
        }
    }

    private void ReleaseAdmission()
    {
        lock (gate)
        {
            active--;
            if (closing && active == 0)
            {
                drained.TrySetResult();
            }
        }
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion, bool waitForSubscription)
    {
        ExceptionDispatchInfo? failure = null;
        try
        {
            if (waitForSubscription)
            {
                await subscriptionSettled.Task.ConfigureAwait(false);
                configuration.ConfigurationChanged -= Changed;
            }
        }
        catch (Exception error)
        {
            failure = ExceptionDispatchInfo.Capture(error);
        }
        await drained.Task.ConfigureAwait(false);
        if (failure is null)
        {
            completion.TrySetResult();
        }
        else
        {
            completion.TrySetException(failure.SourceException);
        }
    }

    private static bool Ordinary(Exception error) => error is not OutOfMemoryException
        and not StackOverflowException and not AccessViolationException;

    private static TaskCompletionSource Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
