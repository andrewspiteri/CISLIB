namespace CIS.Logging.Runtime;
// One acquisition sequence and rollback owner for physical and controlled queued runtimes.
internal static class QueuedRuntimeFactory
{
    // Trusted callback seams share the production acquisition/rollback path.
    internal static CoreLogRuntime CreateQueued(EventMetadata metadata, LoggingOptions options, Func<AdmittedEvent, ValueTask>? consoleWrite, Func<AdmittedEvent, ValueTask>? fileWrite, TimeProvider? timeProvider = null, Func<ValueTask>? consoleFlush = null, Func<ValueTask>? fileFlush = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return ComposeQueued(metadata, CaptureQueueOptions(options), consoleWrite, fileWrite, timeProvider, consoleFlush: consoleFlush, fileFlush: fileFlush);
    }

    // Console settings are frozen before selecting the stream or starting any output worker.
    internal static CoreLogRuntime CreateConsoleQueued(EventMetadata metadata, LoggingOptions options, Func<AdmittedEvent, ValueTask>? fileWrite = null, TimeProvider? timeProvider = null, Func<ConsoleOutputStream, Stream>? acquireConsole = null, Func<Func<Task>, Task>? launchWorker = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        LoggingOptions selected = CaptureQueueOptions(options);
        CoreLogRuntime.ValidateSettings(selected.EffectiveConsoleLevel, selected.EffectiveFileLevel, selected.MaxEntryBytes);
        if (selected.QueueCapacity is < LoggingOptions.MinimumQueueCapacity or > LoggingOptions.MaximumQueueCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Use the configured queue-capacity range.");
        }

        if (!Enum.IsDefined(selected.ConsoleFormat) || !Enum.IsDefined(selected.HumanDetail) || !Enum.IsDefined(selected.ConsoleStream))
        {
            throw new ArgumentException("Use declared console presentation and stream values.", nameof(options));
        }

        ConsoleOutputWriter? writer = AcquireConsoleWriter(selected, acquireConsole);
        return ComposeQueued(metadata, selected, writer is null ? null : writer.WriteAsync, fileWrite, timeProvider, writer, launchWorker);
    }

    internal static CoreLogRuntime CreateConfiguredQueued(EventMetadata metadata, LoggingOptions options, TimeProvider? timeProvider = null, Func<ConsoleOutputStream, Stream>? acquireConsole = null, Func<Func<Task>, Task>? launchWorker = null, IEnumerable<LogWriterRegistration>? writers = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        LogWriterRegistration[] registrations = writers?.ToArray() ?? [];
        if (registrations.Any(registration => registration is null) || registrations.Select(registration => registration.Name).Distinct(StringComparer.Ordinal).Count() != registrations.Length ||
            registrations.Select(registration => registration.Writer).Distinct(ReferenceEqualityComparer.Instance).Count() != registrations.Length)
        { throw new ArgumentException("Writers and their ordinal names must be nonnull and unique.", nameof(writers)); }
        LoggingOptions selected = CaptureQueueOptions(options);
        CoreLogRuntime.ValidateSettings(selected.EffectiveConsoleLevel, selected.EffectiveFileLevel, selected.MaxEntryBytes);
        if (selected.QueueCapacity is < LoggingOptions.MinimumQueueCapacity or > LoggingOptions.MaximumQueueCapacity || selected.FileMaxBytes is < LoggingOptions.MinimumFileMaxBytes or > LoggingOptions.MaximumFileMaxBytes || selected.FileRetainedFiles is < LoggingOptions.MinimumFileRetainedFiles or > LoggingOptions.MaximumFileRetainedFiles || selected.FileEnabled && (selected.FileMaxBytes < selected.MaxEntryBytes || selected.FlushIntervalMilliseconds is < LoggingOptions.MinimumFlushIntervalMilliseconds or > LoggingOptions.MaximumFlushIntervalMilliseconds))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (!Enum.IsDefined(selected.ConsoleFormat) || !Enum.IsDefined(selected.ConsoleStream) || !Enum.IsDefined(selected.FileFormat) || !Enum.IsDefined(selected.HumanDetail))
        {
            throw new ArgumentException("Use declared output presentation values.", nameof(options));
        }

        if (selected.FileEnabled && !Path.IsPathFullyQualified(selected.FilePath!))
        {
            throw new ArgumentException("File output requires an already captured absolute path.", nameof(options));
        }

        ConsoleOutputWriter? console = AcquireConsoleWriter(selected, acquireConsole);
        FileOutputWriter? file = selected.FileEnabled ? new(selected.FilePath!, selected.FileFormat, selected.HumanDetail, selected.MaxEntryBytes, selected.FileMaxBytes, selected.FileRetainedFiles) : null;
        return ComposeQueued(metadata, selected, console is null ? null : console.WriteAsync, file is null ? null : file.WriteAsync, timeProvider, console, launchWorker, file, registrations: registrations);
    }

    private static ConsoleOutputWriter? AcquireConsoleWriter(LoggingOptions selected, Func<ConsoleOutputStream, Stream>? acquireConsole)
    {
        ConsoleOutputWriter? writer = null;
        if (selected.ConsoleEnabled)
        {
            try
            {
                Stream stream = acquireConsole is null ? selected.ConsoleStream == ConsoleOutputStream.StandardError ? Console.OpenStandardError() : Console.OpenStandardOutput() : acquireConsole(selected.ConsoleStream);
                if (stream is null)
                {
                    throw new CoreLogInitializationException("console", "ConsoleInitializationFailed");
                }

                writer = new ConsoleOutputWriter(stream, selected.ConsoleFormat, selected.HumanDetail, selected.MaxEntryBytes);
            }
            catch (CoreLogInitializationException)
            {
                throw;
            }
            catch
            {
                throw new CoreLogInitializationException("console", "ConsoleInitializationFailed");
            }
        }

        return writer;
    }

    private static LoggingOptions CaptureQueueOptions(LoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        CoreLogRuntime.ValidateShutdownTimeout(options.ShutdownTimeoutMilliseconds);
        return new LoggingOptions
        {
            ConsoleLevel = options.EffectiveConsoleLevel,
            FileLevel = options.EffectiveFileLevel,
            FilePath = options.FilePath,
            MaxEntryBytes = options.MaxEntryBytes,
            QueueCapacity = options.QueueCapacity,
            ConsoleFormat = options.ConsoleFormat,
            ConsoleStream = options.ConsoleStream,
            HumanDetail = options.HumanDetail,
            FileFormat = options.FileFormat,
            FileMaxBytes = options.FileMaxBytes,
            FileRetainedFiles = options.FileRetainedFiles,
            FlushIntervalMilliseconds = options.FlushIntervalMilliseconds,
            ShutdownTimeoutMilliseconds = options.ShutdownTimeoutMilliseconds
        };
    }

    private static CoreLogRuntime ComposeQueued(EventMetadata metadata, LoggingOptions selected, Func<AdmittedEvent, ValueTask>? consoleWrite, Func<AdmittedEvent, ValueTask>? fileWrite, TimeProvider? timeProvider, ConsoleOutputWriter? writer = null, Func<Func<Task>, Task>? launchWorker = null, FileOutputWriter? fileWriter = null, Func<ValueTask>? consoleFlush = null, Func<ValueTask>? fileFlush = null, IReadOnlyList<LogWriterRegistration>? registrations = null)
    {
        CoreLogRuntime.ValidateSettings(selected.EffectiveConsoleLevel, selected.EffectiveFileLevel, selected.MaxEntryBytes);
        var gate = new object();
        var queues = new OutputQueues(capacity: selected.QueueCapacity, consoleEnabled: selected.ConsoleEnabled, fileEnabled: selected.FileEnabled, consoleWrite: consoleWrite, fileWrite: fileWrite, sharedAdmissionGate: gate, launchWorker: launchWorker, fileResource: fileWriter, consoleFlush: writer is null ? consoleFlush : writer.FlushAsync, fileFlush: fileWriter is null ? fileFlush : fileWriter.FlushAsync, consoleCancellableWrite: writer is null ? null : writer.WriteAsync, fileCancellableWrite: fileWriter is null ? null : fileWriter.WriteAsync, consoleCancellableFlush: writer is null ? null : writer.FlushAsync, fileCancellableFlush: fileWriter is null ? null : fileWriter.FlushAsync, timeProvider: timeProvider);
        bool ownershipTransferred = false;
        try
        {
            foreach (var registration in registrations ?? [])
            {
                async ValueTask Write(AdmittedEvent admitted, CancellationToken cancellation) =>
                    await registration.Writer.WriteAsync(System.Text.Encoding.UTF8.GetString(admitted.Event.JsonLine.Span), cancellation).ConfigureAwait(false);
                async ValueTask Cleanup()
                {
                    if (Volatile.Read(ref ownershipTransferred) && registration.OwnsWriter)
                    { await registration.Writer.DisposeAsync().ConfigureAwait(false); }
                }
                OutputEventQueue queue;
                try
                {
                    queue = new OutputEventQueue(selected.QueueCapacity, admitted => Write(admitted, CancellationToken.None), gate, launchWorker,
                        cancellableWrite: Write, cancellableFlush: registration.Writer.FlushAsync, timeProvider: timeProvider, cleanup: Cleanup);
                }
                catch { throw new CoreLogInitializationException(registration.Name, "WorkerInitializationFailed"); }
                queues.Add(registration.Name, queue);
            }
            writer?.Initialize();
            fileWriter?.Initialize();
            var runtime = new CoreLogRuntime(metadata: metadata, options: selected, console: queues.Console, file: queues.File, timeProvider: timeProvider, sharedAdmissionGate: gate, queuedOutputs: queues, consoleWriter: writer, fileWriter: fileWriter, writers: registrations);
            if (fileWriter is not null)
            {
                try
                {
                    queues.File!.StartFileFlushTimer(timeProvider ?? TimeProvider.System, selected.FlushInterval);
                }
                catch
                {
                    throw new CoreLogInitializationException("file", "FileFlushTimerInitializationFailed");
                }
            }

            Volatile.Write(ref ownershipTransferred, true);
            return runtime;
        }
        catch
        {
            queues.Complete();
            // Observe terminal cleanup failures without masking the safe initialization category.
            try
            {
                queues.Completion.GetAwaiter().GetResult();
            }
            catch
            {
            }

            try
            {
                fileWriter?.Dispose();
            }
            catch
            {
            }

            throw;
        }
    }
}
