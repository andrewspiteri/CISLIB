namespace CIS.Jobs;

/// <summary>Injectable bounded catalog lookup; registration eligibility remains distinct from authorization.</summary>
public interface IJobCatalog
{
    /// <summary>Gets immutable finite catalog/runtime limits.</summary>
    JobRuntimeLimits Limits
    {
        get;
    }
    /// <summary>Gets the explicit registered job count.</summary>
    int Count
    {
        get;
    }
    /// <summary>Gets immutable registered categories by exact scope/category.</summary>
    IReadOnlyDictionary<(string Scope, string Id), JobCategoryDefinition> Categories
    {
        get;
    }
    /// <summary>Gets exact passive bindings without constructing handlers.</summary>
    IReadOnlyDictionary<(string Scope, string Id), JobHandlerBinding> Bindings
    {
        get;
    }
    /// <summary>Finds only the exact scope/job binding, or null for an unknown target.</summary>
    JobHandlerBinding? Find(string executionScope, string jobId);
    /// <summary>Copies one deterministic bounded definition page without constructing handlers.</summary>
    IReadOnlyList<JobDefinition> Inspect(int offset = 0, int? pageSize = null);
    /// <summary>Checks a complete detached settings revision before any runtime adoption.</summary>
    void ValidateConfiguration(JobConfigurationSnapshot configuration);
    /// <summary>Checks enabled scheduling capabilities and actual available IANA zones during activation.</summary>
    void ValidateActivation(JobConfigurationSnapshot configuration, IJobRunSource? source = null,
        IJobScheduleEvaluator? evaluator = null);
}

/// <summary>An immutable bounded catalog of explicit definitions and exact local handler eligibility.</summary>
public sealed class JobCatalog : IJobCatalog
{
    private readonly IReadOnlyDictionary<(string Scope, string Id), JobHandlerBinding> bindings;
    private readonly IReadOnlyDictionary<(string Scope, string Id), JobCategoryDefinition> categories;
    private readonly JobDefinition[] orderedDefinitions;

    /// <summary>Copies a complete passive catalog; no handlers, services, providers or scheduling are started.</summary>
    public JobCatalog(IEnumerable<JobCategoryDefinition> categories, IEnumerable<JobHandlerBinding> bindings,
        JobRuntimeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(bindings);
        Limits = limits ?? new();
        var categoryMap = new Dictionary<(string Scope, string Id), JobCategoryDefinition>();
        foreach (JobCategoryDefinition category in categories)
        {
            ArgumentNullException.ThrowIfNull(category);
            if (categoryMap.Count == Limits.MaximumCategories
                || !categoryMap.TryAdd((category.ExecutionScope, category.CategoryId), category))
            {
                throw new ArgumentException("Registered categories must be distinct and fit the catalog bound.", nameof(categories));
            }
        }

        var bindingMap = new Dictionary<(string Scope, string Id), JobHandlerBinding>();
        foreach (JobHandlerBinding binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            JobDefinition definition = binding.Definition;
            if (!categoryMap.ContainsKey((definition.ExecutionScope, definition.CategoryId)))
            {
                throw new ArgumentException("Every job requires its category in the same execution scope.", nameof(bindings));
            }

            if (bindingMap.Count == Limits.MaximumDefinitions
                || !bindingMap.TryAdd((definition.ExecutionScope, definition.JobId), binding))
            {
                throw new ArgumentException("Each scope/job has exactly one binding and must fit the catalog bound.", nameof(bindings));
            }
        }

        this.categories = new System.Collections.ObjectModel.ReadOnlyDictionary<(string Scope, string Id), JobCategoryDefinition>(categoryMap);
        this.bindings = new System.Collections.ObjectModel.ReadOnlyDictionary<(string Scope, string Id), JobHandlerBinding>(bindingMap);
        orderedDefinitions = bindingMap.Values.Select(binding => binding.Definition)
            .OrderBy(definition => definition.ExecutionScope, StringComparer.Ordinal)
            .ThenBy(definition => definition.JobId, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Gets finite catalog and runtime bounds.</summary>
    public JobRuntimeLimits Limits
    {
        get;
    }
    /// <summary>Gets the number of explicitly bound jobs.</summary>
    public int Count => bindings.Count;
    /// <summary>Gets immutable registered categories, keyed by exact ordinal scope/category.</summary>
    public IReadOnlyDictionary<(string Scope, string Id), JobCategoryDefinition> Categories => categories;
    /// <summary>Gets immutable explicit bindings; inspecting a Type does not resolve it.</summary>
    public IReadOnlyDictionary<(string Scope, string Id), JobHandlerBinding> Bindings => bindings;

    /// <summary>Finds only the exact registered scope/job; unknown targets have no handler fallback.</summary>
    public JobHandlerBinding? Find(string executionScope, string jobId)
    {
        string scope = JobText.Text(executionScope, 256, nameof(executionScope));
        string id = JobText.Text(jobId, 128, nameof(jobId));
        return bindings.GetValueOrDefault((scope, id));
    }

    /// <summary>Copies a bounded deterministic definition page; disabled scheduled definitions require no providers.</summary>
    public IReadOnlyList<JobDefinition> Inspect(int offset = 0, int? pageSize = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        int count = pageSize ?? Limits.MaximumSnapshotPageSize;
        if (count < 1 || count > Limits.MaximumSnapshotPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        return Array.AsReadOnly(orderedDefinitions.Skip(offset).Take(count).ToArray());
    }

    /// <summary>Validates a complete detached revision before a runtime applies any of it.</summary>
    public void ValidateConfiguration(JobConfigurationSnapshot configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (Count > configuration.Limits.MaximumDefinitions || categories.Count > configuration.Limits.MaximumCategories)
        {
            throw new ArgumentException("Configuration limits cannot exclude the registered catalog.", nameof(configuration));
        }

        foreach (((string scope, string id), JobCategorySettings _) in configuration.Categories)
        {
            if (!categories.ContainsKey((scope, id)))
            {
                throw new ArgumentException("Category settings require an exact registered scope/category.", nameof(configuration));
            }
        }

        foreach (((string scope, string id), JobSettings settings) in configuration.Jobs)
        {
            JobHandlerBinding binding = Find(scope, id)
                ?? throw new ArgumentException("Job settings require an exact registered scope/job.", nameof(configuration));
            if (!binding.Definition.SupportedModes.Contains(settings.Execution.Mode)
                || !categories.ContainsKey((scope, settings.CategoryId ?? binding.Definition.CategoryId)))
            {
                throw new ArgumentException("Settings require an implemented mode and a category in the same execution scope.", nameof(configuration));
            }
        }
    }

    /// <summary>Checks selected scheduling capabilities only for effectively enabled scheduled jobs.</summary>
    public void ValidateActivation(JobConfigurationSnapshot configuration, IJobRunSource? source = null,
        IJobScheduleEvaluator? evaluator = null)
    {
        ValidateConfiguration(configuration);
        foreach (JobSettings settings in configuration.Jobs.Values)
        {
            JobHandlerBinding binding = Find(settings.ExecutionScope, settings.JobId)!;
            string category = settings.CategoryId ?? binding.Definition.CategoryId;
            bool categoryEnabled = configuration.Categories.GetValueOrDefault((settings.ExecutionScope, category))?.Enabled ?? true;
            if (!configuration.Enabled || !settings.Enabled || !categoryEnabled || settings.Execution.Mode != JobExecutionMode.Scheduled)
            {
                continue;
            }

            if (settings.Execution.Schedule is not { } schedule || source is null
                || source.Capabilities != JobRunSourceCapabilities.ExternallyAdmittedOccurrences)
            {
                throw new InvalidOperationException("Enabled scheduled work requires a descriptor and an explicitly selected trusted admitted-run source.");
            }

            _ = JobText.Text(source.SourceId, 128, nameof(source));
            ValidateTimeZone(schedule);
            if (settings.Execution.ScheduledActivation == JobScheduledActivation.AutomaticRecurrence
                && (evaluator is null || !evaluator.CanEvaluate(schedule)))
            {
                throw new InvalidOperationException("AutomaticRecurrence requires a separate evaluator supporting the complete schedule contract.");
            }
        }
    }

    private static void ValidateTimeZone(JobScheduleDescriptor schedule)
    {
        if (schedule.TimeZoneId is null or "UTC")
        {
            return;
        }

        try
        {
            if (!TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId).HasIanaId)
            {
                throw new InvalidOperationException("Calendar activation requires an available explicit IANA time zone.");
            }
        }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException("The configured IANA time zone is unavailable or invalid for this host.");
        }
    }
}
