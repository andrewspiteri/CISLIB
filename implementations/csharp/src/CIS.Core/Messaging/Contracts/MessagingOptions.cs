namespace CIS.Messaging;

/// <summary>Immutable framing limits shared by staging, storage and native delivery admission.</summary>
public sealed class MessagingLimits
{
    /// <summary>The largest supported encoded payload, one MiB.</summary>
    public const int MaximumSupportedPayloadBytes = 1_048_576;
    /// <summary>The fixed envelope metadata bound.</summary>
    public const int MaximumMetadataBytes = 16_384;
    /// <summary>The fixed user-header count bound.</summary>
    public const int MaximumHeaders = 32;
    /// <summary>The fixed combined UTF-8 header key/value bound.</summary>
    public const int MaximumHeaderBytes = 8192;
    /// <summary>The largest supported JSON nesting depth.</summary>
    public const int MaximumSupportedJsonDepth = 32;

    /// <summary>Creates a finite framing profile, initially 256 KiB and depth sixteen.</summary>
    public MessagingLimits(int maximumPayloadBytes = 262_144, int maximumJsonDepth = 16)
    {
        MaximumPayloadBytes = MessageText.Range(maximumPayloadBytes, 1, MaximumSupportedPayloadBytes, nameof(maximumPayloadBytes));
        MaximumJsonDepth = MessageText.Range(maximumJsonDepth, 1, MaximumSupportedJsonDepth, nameof(maximumJsonDepth));
    }

    /// <summary>Gets the maximum encoded business payload.</summary>
    public int MaximumPayloadBytes
    {
        get;
    }
    /// <summary>Gets the configured payload nesting depth.</summary>
    public int MaximumJsonDepth
    {
        get;
    }
    /// <summary>Gets the whole-envelope admission size, including metadata.</summary>
    public int MaximumEnvelopeBytes => checked(MaximumPayloadBytes + MaximumMetadataBytes);
}

/// <summary>Explicit history policies; all durable history/protection defaults are indefinite.</summary>
public sealed class MessageRetentionPolicy
{
    /// <summary>Validates finite horizons together. Removing protection requires a finite admitted delivery and redrive horizon.</summary>
    public MessageRetentionPolicy(TimeSpan? publishedPayloadRetention = null, TimeSpan? inboxProtectionRetention = null,
        TimeSpan? terminalRetention = null, TimeSpan? supportedRedriveHorizon = null, TimeSpan? maximumDeliveryAge = null)
    {
        ValidateOptional(publishedPayloadRetention, nameof(publishedPayloadRetention));
        ValidateOptional(inboxProtectionRetention, nameof(inboxProtectionRetention));
        ValidateOptional(terminalRetention, nameof(terminalRetention));
        ValidateOptional(supportedRedriveHorizon, nameof(supportedRedriveHorizon));
        ValidateOptional(maximumDeliveryAge, nameof(maximumDeliveryAge));
        if (inboxProtectionRetention is { } protection
            && (maximumDeliveryAge is null || supportedRedriveHorizon is null
                || protection < maximumDeliveryAge || protection < supportedRedriveHorizon))
        {
            throw new ArgumentException("Finite protection must cover explicit finite delivery and supported redrive horizons.", nameof(inboxProtectionRetention));
        }
        if (terminalRetention is { } terminal
            && (supportedRedriveHorizon is null || terminal < supportedRedriveHorizon))
        {
            throw new ArgumentException("Finite terminal history must cover the supported redrive horizon.", nameof(terminalRetention));
        }
        PublishedPayloadRetention = publishedPayloadRetention;
        InboxProtectionRetention = inboxProtectionRetention;
        TerminalRetention = terminalRetention;
        SupportedRedriveHorizon = supportedRedriveHorizon;
        MaximumDeliveryAge = maximumDeliveryAge;
    }

    /// <summary>Gets eligible published payload history duration; unresolved dependencies still prevent cleanup.</summary>
    public TimeSpan? PublishedPayloadRetention
    {
        get;
    }
    /// <summary>Gets full successful protection deletion eligibility, or indefinite.</summary>
    public TimeSpan? InboxProtectionRetention
    {
        get;
    }
    /// <summary>Gets resolved terminal history eligibility, or indefinite.</summary>
    public TimeSpan? TerminalRetention
    {
        get;
    }
    /// <summary>Gets the explicitly supported administrative redrive horizon.</summary>
    public TimeSpan? SupportedRedriveHorizon
    {
        get;
    }
    /// <summary>Gets the admitted historical delivery horizon required before finite protection deletion.</summary>
    /// <remarks>This does not silently expire accepted pending durable work or a command at its caller's wait deadline.</remarks>
    public TimeSpan? MaximumDeliveryAge
    {
        get;
    }

    private static void ValidateOptional(TimeSpan? value, string name)
    {
        if (value is { } selected)
        {
            MessageText.Duration(selected, TimeSpan.FromTicks(1), TimeSpan.FromDays(36500), name);
        }
    }
}

/// <summary>Mutable startup setup copied into one immutable snapshot before any runtime is created.</summary>
public sealed class MessagingOptions
{
    /// <summary>Gets or sets the selected registered store alias.</summary>
    public string StoreAlias { get; set; } = "Messaging";
    /// <summary>Gets or sets encoded business-payload size, initially 256 KiB and at most one MiB.</summary>
    public int MaximumPayloadBytes { get; set; } = 262_144;
    /// <summary>Gets or sets JSON payload depth, initially sixteen and at most thirty-two.</summary>
    public int MaximumJsonDepth { get; set; } = 16;
    /// <summary>Gets or sets one metadata reservation batch, at most thirty-two.</summary>
    public int PublisherBatchSize { get; set; } = 32;
    /// <summary>Gets or sets actual concurrent native publications, initially four.</summary>
    public int PublisherConcurrency { get; set; } = 4;
    /// <summary>Gets or sets actual native publication's cooperative deadline.</summary>
    public TimeSpan PublicationTimeout { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>Gets or sets the database-time fencing lease.</summary>
    public TimeSpan ReservationDuration { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets process-wide admitted consumer work, initially thirty-two.</summary>
    public int MaximumConcurrentDeliveries { get; set; } = 32;
    /// <summary>Gets or sets shared module-owned encoded-buffer capacity, initially 32 MiB.</summary>
    public long MaximumEncodedBufferBytes { get; set; } = 33_554_432;
    /// <summary>Gets or sets process-local waiter capacity, initially 256.</summary>
    public int MaximumOutstandingRequests { get; set; } = 256;
    /// <summary>Gets or sets an admitted request's default whole-entry wait deadline.</summary>
    public TimeSpan DefaultRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets the largest permitted request wait, at most five minutes.</summary>
    public TimeSpan MaximumRequestTimeout { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>Gets or sets unused requester endpoint expiry and maximum obsolete reply age.</summary>
    public TimeSpan ReplyEndpointLifetime { get; set; } = TimeSpan.FromMinutes(10);
    /// <summary>Gets or sets the delivery margin beyond maximum wait.</summary>
    public TimeSpan ReplyDeliveryMargin { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>Gets or sets idle outbox polling; a wake signal may interrupt it.</summary>
    public TimeSpan IdlePollInterval { get; set; } = TimeSpan.FromSeconds(1);
    /// <summary>Gets or sets maximum transient publication backoff.</summary>
    public TimeSpan MaximumPublicationBackoff { get; set; } = TimeSpan.FromSeconds(60);
    /// <summary>Gets or sets the independent cooperative shutdown/drain cancellation budget.</summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets safe optional normal-call debug summaries; false keeps steady state quiet.</summary>
    public bool LogCalls
    {
        get; set;
    }
    /// <summary>Gets or sets whether subscriptions and typed requests require a live broker for readiness after startup.</summary>
    /// <remarks>Initial transport startup and store readiness remain required. Staging-only readiness does not require a live broker.</remarks>
    public bool RequireLiveBrokerForReadiness { get; set; } = true;
    /// <summary>Gets or sets explicit immutable retention policy, initially indefinite.</summary>
    public MessageRetentionPolicy Retention { get; set; } = new();
    /// <summary>Gets or sets capabilities for bounded metadata inspection; empty disables this administrative entry point.</summary>
    public IReadOnlyList<string> InspectionCapabilities { get; set; } = [];
    /// <summary>Gets or sets capabilities for explicit bounded retention maintenance; empty disables this entry point.</summary>
    public IReadOnlyList<string> MaintenanceCapabilities { get; set; } = [];

    /// <summary>Validates and captures this setup without resolving services or performing I/O.</summary>
    public MessagingSettings Capture() => new(this);
}

/// <summary>One validated restart-required resource snapshot, never a live configuration callback.</summary>
public sealed class MessagingSettings
{
    internal MessagingSettings(MessagingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        StoreAlias = MessageText.Alias(options.StoreAlias, nameof(options.StoreAlias));
        Limits = new(options.MaximumPayloadBytes, options.MaximumJsonDepth);
        PublisherBatchSize = MessageText.Range(options.PublisherBatchSize, 1, 32, nameof(options.PublisherBatchSize));
        PublisherConcurrency = MessageText.Range(options.PublisherConcurrency, 1, 32, nameof(options.PublisherConcurrency));
        PublicationTimeout = MessageText.Duration(options.PublicationTimeout, TimeSpan.FromMilliseconds(1), TimeSpan.FromMinutes(5), nameof(options.PublicationTimeout));
        ReservationDuration = MessageText.Duration(options.ReservationDuration, TimeSpan.FromMilliseconds(2), TimeSpan.FromMinutes(15), nameof(options.ReservationDuration));
        if (ReservationDuration <= PublicationTimeout)
        {
            throw new ArgumentException("The reservation must exceed the publication deadline; fencing still handles late settlement.", nameof(options));
        }
        MaximumConcurrentDeliveries = MessageText.Range(options.MaximumConcurrentDeliveries, 1, 256, nameof(options.MaximumConcurrentDeliveries));
        if (options.MaximumEncodedBufferBytes < checked(4L * Limits.MaximumEnvelopeBytes)
            || options.MaximumEncodedBufferBytes > 1_073_741_824)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaximumEncodedBufferBytes), "Provide bounded capacity for conservative four-buffer staging overlap; runtime admission precedes copies.");
        }
        MaximumEncodedBufferBytes = options.MaximumEncodedBufferBytes;
        MaximumOutstandingRequests = MessageText.Range(options.MaximumOutstandingRequests, 1, 4096, nameof(options.MaximumOutstandingRequests));
        MaximumRequestTimeout = MessageText.Duration(options.MaximumRequestTimeout, TimeSpan.FromMilliseconds(1), TimeSpan.FromMinutes(5), nameof(options.MaximumRequestTimeout));
        DefaultRequestTimeout = MessageText.Duration(options.DefaultRequestTimeout, TimeSpan.FromMilliseconds(1), MaximumRequestTimeout, nameof(options.DefaultRequestTimeout));
        ReplyDeliveryMargin = MessageText.Duration(options.ReplyDeliveryMargin, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), nameof(options.ReplyDeliveryMargin));
        ReplyEndpointLifetime = MessageText.Duration(options.ReplyEndpointLifetime, TimeSpan.FromSeconds(1), TimeSpan.FromHours(1), nameof(options.ReplyEndpointLifetime));
        if (ReplyEndpointLifetime < MaximumRequestTimeout + ReplyDeliveryMargin)
        {
            throw new ArgumentException("Requester endpoint lifetime must cover maximum wait plus delivery margin.", nameof(options));
        }
        IdlePollInterval = MessageText.Duration(options.IdlePollInterval, TimeSpan.FromMilliseconds(10), TimeSpan.FromMinutes(1), nameof(options.IdlePollInterval));
        MaximumPublicationBackoff = MessageText.Duration(options.MaximumPublicationBackoff, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(60), nameof(options.MaximumPublicationBackoff));
        DrainTimeout = MessageText.Duration(options.DrainTimeout, TimeSpan.FromMilliseconds(1), TimeSpan.FromMinutes(5), nameof(options.DrainTimeout));
        ArgumentNullException.ThrowIfNull(options.Retention);
        Retention = options.Retention;
        InspectionCapabilities = MessageText.Capabilities(options.InspectionCapabilities, nameof(options.InspectionCapabilities), required: false);
        MaintenanceCapabilities = MessageText.Capabilities(options.MaintenanceCapabilities, nameof(options.MaintenanceCapabilities), required: false);
        LogCalls = options.LogCalls;
        RequireLiveBrokerForReadiness = options.RequireLiveBrokerForReadiness;
    }

    /// <summary>Gets the selected finite store alias.</summary>
    public string StoreAlias
    {
        get;
    }
    /// <summary>Gets immutable framing bounds.</summary>
    public MessagingLimits Limits
    {
        get;
    }
    /// <summary>Gets metadata-only reservation batch size.</summary>
    public int PublisherBatchSize
    {
        get;
    }
    /// <summary>Gets maximum actual native publication concurrency.</summary>
    public int PublisherConcurrency
    {
        get;
    }
    /// <summary>Gets cooperative publication deadline, without timeout-abandon semantics.</summary>
    public TimeSpan PublicationTimeout
    {
        get;
    }
    /// <summary>Gets the database-time lease duration.</summary>
    public TimeSpan ReservationDuration
    {
        get;
    }
    /// <summary>Gets process-wide delivery work capacity.</summary>
    public int MaximumConcurrentDeliveries
    {
        get;
    }
    /// <summary>Gets shared encoded-buffer byte capacity.</summary>
    public long MaximumEncodedBufferBytes
    {
        get;
    }
    /// <summary>Gets process-local wait capacity.</summary>
    public int MaximumOutstandingRequests
    {
        get;
    }
    /// <summary>Gets the default entry-based wait.</summary>
    public TimeSpan DefaultRequestTimeout
    {
        get;
    }
    /// <summary>Gets the maximum admitted wait.</summary>
    public TimeSpan MaximumRequestTimeout
    {
        get;
    }
    /// <summary>Gets finite unused-endpoint expiry and maximum reply age.</summary>
    public TimeSpan ReplyEndpointLifetime
    {
        get;
    }
    /// <summary>Gets the reply delivery margin.</summary>
    public TimeSpan ReplyDeliveryMargin
    {
        get;
    }
    /// <summary>Gets bounded idle polling.</summary>
    public TimeSpan IdlePollInterval
    {
        get;
    }
    /// <summary>Gets finite maximum transient backoff.</summary>
    public TimeSpan MaximumPublicationBackoff
    {
        get;
    }
    /// <summary>Gets cooperative drain cancellation time; actual cleanup is still awaited.</summary>
    public TimeSpan DrainTimeout
    {
        get;
    }
    /// <summary>Gets normal diagnostic selection.</summary>
    public bool LogCalls
    {
        get;
    }
    /// <summary>Gets required broker readiness selection.</summary>
    public bool RequireLiveBrokerForReadiness
    {
        get;
    }
    /// <summary>Gets immutable explicit history policy.</summary>
    public MessageRetentionPolicy Retention
    {
        get;
    }
    /// <summary>Gets immutable metadata inspection capabilities; empty means disabled.</summary>
    public IReadOnlyList<string> InspectionCapabilities
    {
        get;
    }
    /// <summary>Gets immutable maintenance capabilities; empty means disabled.</summary>
    public IReadOnlyList<string> MaintenanceCapabilities
    {
        get;
    }
}
