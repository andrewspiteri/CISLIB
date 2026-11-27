namespace CIS.Management;

/// <summary>One unpublished selected layer over an explicit immutable baseline, guarded by actual source/map preconditions.</summary>
public sealed record ManagementConfigurationCandidatePreviewCommand(int CommandVersion, string ApplicationId, string ConfigurationId,
    string SchemaVersion, string SelectedSource, string SourceId, string SourceETag, string AssignmentETag,
    ManagementConfigurationSourceReference CommonSource, ManagementConfigurationSourceReference ApplicationSource, string PayloadJson);

/// <summary>Side-effect-free candidate facts; source references remain the baseline and no candidate publication identity exists.</summary>
public sealed record ManagementConfigurationCandidatePreview(string BindingId, string ApplicationId, string Environment,
    string ConfigurationId, string SchemaVersion, string HostApplicationName, string SelectedSource, string SourceId,
    string SourceETag, string AssignmentETag, ManagementConfigurationSourceReference CommonSource,
    ManagementConfigurationSourceReference ApplicationSource, string DraftSha256, bool StructurallyValid, bool Redacted,
    IReadOnlyList<ManagementConfigurationChange> Changes, IReadOnlyList<ManagementConfigurationIssue> Issues,
    ManagementConfigurationAssignmentMap CurrentAssignment, ManagementPage<ManagementConfigurationPreviewRecipient> Recipients);

