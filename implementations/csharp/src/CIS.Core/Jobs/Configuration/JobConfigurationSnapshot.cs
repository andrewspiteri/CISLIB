using System.Collections.ObjectModel;

namespace CIS.Jobs;

/// <summary>Immutable application-scoped category control; its hard ceiling cannot be overridden by a job.</summary>
public sealed class JobCategorySettings
{
    /// <summary>Copies explicit category enablement and optional capacity/default timeout.</summary>
    public JobCategorySettings(string executionScope, string categoryId, bool enabled = true,
        int? maximumInvocations = null, TimeSpan? defaultTimeout = null)
    {
        ExecutionScope = JobText.Text(executionScope, 256, nameof(executionScope));
        CategoryId = JobText.Text(categoryId, 128, nameof(categoryId));
        if (maximumInvocations is < 1 or > 65536)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumInvocations));
        }

        Enabled = enabled;
        MaximumInvocations = maximumInvocations;
        DefaultTimeout = defaultTimeout is { } timeout ? JobText.Duration(timeout, nameof(defaultTimeout)) : null;
    }

    /// <summary>Gets the category's exact execution scope.</summary>
    public string ExecutionScope
    {
        get;
    }
    /// <summary>Gets the ordinal category identifier.</summary>
    public string CategoryId
    {
        get;
    }
    /// <summary>Gets the category enablement gate.</summary>
    public bool Enabled
    {
        get;
    }
    /// <summary>Gets the hard local ceiling, or null to inherit the host ceiling.</summary>
    public int? MaximumInvocations
    {
        get;
    }
    /// <summary>Gets an optional inherited callback timeout.</summary>
    public TimeSpan? DefaultTimeout
    {
        get;
    }
}

/// <summary>Immutable control for one registered job; new definitions remain disabled by default.</summary>
public sealed class JobSettings
{
    /// <summary>Copies explicit execution behavior, enablement and optional management-category reassignment.</summary>
    public JobSettings(string executionScope, string jobId, JobExecutionSettings execution, bool enabled = false,
        string? categoryId = null)
    {
        ExecutionScope = JobText.Text(executionScope, 256, nameof(executionScope));
        JobId = JobText.Text(jobId, 128, nameof(jobId));
        ArgumentNullException.ThrowIfNull(execution);
        Execution = execution;
        Enabled = enabled;
        CategoryId = categoryId is null ? null : JobText.Text(categoryId, 128, nameof(categoryId));
    }

    /// <summary>Gets the exact execution scope.</summary>
    public string ExecutionScope
    {
        get;
    }
    /// <summary>Gets the unchanged stable job identity.</summary>
    public string JobId
    {
        get;
    }
    /// <summary>Gets explicit job enablement, initially false.</summary>
    public bool Enabled
    {
        get;
    }
    /// <summary>Gets an optional category reassignment within the same execution scope.</summary>
    public string? CategoryId
    {
        get;
    }
    /// <summary>Gets immutable invocation settings.</summary>
    public JobExecutionSettings Execution
    {
        get;
    }
}

/// <summary>A detached complete locally accepted settings revision. Its revision is not authoritative scheduler admission.</summary>
public sealed class JobConfigurationSnapshot
{
    /// <summary>Copies bounded category/job settings without activating definitions or mutating retained Configuration roots.</summary>
    public JobConfigurationSnapshot(long revision, bool enabled = false,
        IEnumerable<JobCategorySettings>? categories = null, IEnumerable<JobSettings>? jobs = null,
        JobRuntimeLimits? limits = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        Revision = revision;
        Enabled = enabled;
        Limits = limits ?? new();
        var categoryMap = new Dictionary<(string Scope, string Id), JobCategorySettings>();
        foreach (JobCategorySettings category in categories ?? [])
        {
            ArgumentNullException.ThrowIfNull(category);
            if (categoryMap.Count == Limits.MaximumCategories
                || !categoryMap.TryAdd((category.ExecutionScope, category.CategoryId), category))
            {
                throw new ArgumentException("Category settings must be distinct and fit the configured catalog bound.", nameof(categories));
            }
        }

        var jobMap = new Dictionary<(string Scope, string Id), JobSettings>();
        foreach (JobSettings job in jobs ?? [])
        {
            ArgumentNullException.ThrowIfNull(job);
            if (jobMap.Count == Limits.MaximumDefinitions || !jobMap.TryAdd((job.ExecutionScope, job.JobId), job))
            {
                throw new ArgumentException("Job settings must be distinct and fit the configured catalog bound.", nameof(jobs));
            }
        }

        Categories = new ReadOnlyDictionary<(string Scope, string Id), JobCategorySettings>(categoryMap);
        Jobs = new ReadOnlyDictionary<(string Scope, string Id), JobSettings>(jobMap);
    }

    /// <summary>Gets the local accepted Configuration revision, with zero reserved for initial defaults.</summary>
    public long Revision
    {
        get;
    }
    /// <summary>Gets explicit host enablement, initially false.</summary>
    public bool Enabled
    {
        get;
    }
    /// <summary>Gets finite per-instance limits.</summary>
    public JobRuntimeLimits Limits
    {
        get;
    }
    /// <summary>Gets detached settings keyed by exact scope/category, without cross-scope inheritance.</summary>
    public IReadOnlyDictionary<(string Scope, string Id), JobCategorySettings> Categories
    {
        get;
    }
    /// <summary>Gets detached settings keyed by exact scope/job; absent jobs remain disabled.</summary>
    public IReadOnlyDictionary<(string Scope, string Id), JobSettings> Jobs
    {
        get;
    }
}
