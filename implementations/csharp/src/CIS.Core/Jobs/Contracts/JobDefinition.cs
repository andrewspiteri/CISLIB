namespace CIS.Jobs;

/// <summary>The two explicit execution modes supported by a registered job.</summary>
public enum JobExecutionMode
{
    /// <summary>Finite iterations with retained ownership between iterations.</summary>
    Continuous,
    /// <summary>One externally admitted occurrence with a stable business identity.</summary>
    Scheduled
}

/// <summary>Passive immutable management metadata; a category never selects a handler or grants permission.</summary>
public sealed record JobCategoryDefinition
{
    /// <summary>Copies a scope/category identity and bounded human-readable metadata without activating work.</summary>
    public JobCategoryDefinition(string executionScope, string categoryId, string displayName, string description = "")
    {
        ArgumentNullException.ThrowIfNull(description);
        ExecutionScope = JobText.Text(executionScope, 256, nameof(executionScope));
        CategoryId = JobText.Text(categoryId, 128, nameof(categoryId));
        DisplayName = JobText.Text(displayName, 256, nameof(displayName));
        Description = description.Length == 0 ? string.Empty : JobText.Text(description, 2048, nameof(description));
    }

    /// <summary>Gets the exact responsible application or explicitly authorized shared scope.</summary>
    public string ExecutionScope
    {
        get;
    }
    /// <summary>Gets the ordinal category identifier, unique within the execution scope.</summary>
    public string CategoryId
    {
        get;
    }
    /// <summary>Gets bounded display text, excluded from coordination identity.</summary>
    public string DisplayName
    {
        get;
    }
    /// <summary>Gets bounded descriptive text.</summary>
    public string Description
    {
        get;
    }
}

/// <summary>Portable immutable job metadata, separate from handler CLR types and runtime state.</summary>
public sealed class JobDefinition
{
    /// <summary>Copies one explicit registered identity, supported modes and required execution permissions.</summary>
    public JobDefinition(string executionScope, string jobId, string displayName, string categoryId,
        IEnumerable<JobExecutionMode> supportedModes, IEnumerable<string> executeCapabilities,
        string description = "", string resourceScopeDescription = "default")
    {
        ArgumentNullException.ThrowIfNull(description);
        ExecutionScope = JobText.Text(executionScope, 256, nameof(executionScope));
        JobId = JobText.Text(jobId, 128, nameof(jobId));
        DisplayName = JobText.Text(displayName, 256, nameof(displayName));
        CategoryId = JobText.Text(categoryId, 128, nameof(categoryId));
        Description = description.Length == 0 ? string.Empty : JobText.Text(description, 2048, nameof(description));
        ResourceScopeDescription = JobText.Text(resourceScopeDescription, 1024, nameof(resourceScopeDescription));
        ExecuteCapabilities = JobText.Capabilities(executeCapabilities);
        ArgumentNullException.ThrowIfNull(supportedModes);
        var modes = new List<JobExecutionMode>();
        foreach (JobExecutionMode mode in supportedModes)
        {
            if (!Enum.IsDefined(mode) || modes.Count == 2 || modes.Contains(mode))
            {
                throw new ArgumentException("Declare one or both distinct supported execution modes.", nameof(supportedModes));
            }

            modes.Add(mode);
        }

        if (modes.Count == 0)
        {
            throw new ArgumentException("Declare at least one supported execution mode.", nameof(supportedModes));
        }

        SupportedModes = modes.AsReadOnly();
    }

    /// <summary>Gets the responsible application or explicitly authorized shared scope.</summary>
    public string ExecutionScope
    {
        get;
    }
    /// <summary>Gets the stable ordinal duty identifier, independent of category and schedule.</summary>
    public string JobId
    {
        get;
    }
    /// <summary>Gets human-readable display text.</summary>
    public string DisplayName
    {
        get;
    }
    /// <summary>Gets bounded explanatory text.</summary>
    public string Description
    {
        get;
    }
    /// <summary>Gets the primary category in this execution scope.</summary>
    public string CategoryId
    {
        get;
    }
    /// <summary>Gets one or two copied supported modes; definition alone enables neither mode.</summary>
    public IReadOnlyList<JobExecutionMode> SupportedModes
    {
        get;
    }
    /// <summary>Gets the meaning of the explicit resource/partition identifier.</summary>
    public string ResourceScopeDescription
    {
        get;
    }
    /// <summary>Gets the copied complete permissions required against the resolved execution resource.</summary>
    public IReadOnlyList<string> ExecuteCapabilities
    {
        get;
    }
}
