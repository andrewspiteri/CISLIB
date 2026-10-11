namespace CIS.Configuration;

/// <summary>Reports independent startup configuration problems through an owned immutable collection.</summary>
public sealed class CoreConfigurationException : Exception
{
    /// <summary>Copies the supplied issues in enumeration order.</summary>
    /// <param name="issues">Immutable issue values; an empty sequence is permitted.</param>
    /// <exception cref="ArgumentNullException">The sequence is null.</exception>
    /// <exception cref="ArgumentException">The sequence contains a null issue.</exception>
    public CoreConfigurationException(IEnumerable<ConfigurationIssue> issues)
        : base("Core configuration is invalid. Inspect Issues for details.")
    {
        ArgumentNullException.ThrowIfNull(issues);
        var copy = new List<ConfigurationIssue>();
        foreach (ConfigurationIssue issue in issues)
        {
            if (issue is null)
            {
                throw new ArgumentException("The sequence must not contain null issues.", nameof(issues));
            }
            copy.Add(issue);
        }
        Issues = Array.AsReadOnly(copy.ToArray());
    }

    /// <summary>Gets the copied issue values in their original order.</summary>
    public IReadOnlyList<ConfigurationIssue> Issues { get; }
}
