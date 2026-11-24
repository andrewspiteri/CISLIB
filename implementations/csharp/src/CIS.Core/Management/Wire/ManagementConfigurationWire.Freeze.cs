namespace CIS.Management.Wire;

public static partial class ManagementConfigurationWire
{
    private static object Freeze(object value) => value switch
    {
        ManagementConfigurationDefinition definition => DefinitionCopy(definition),
        ManagementConfigurationSource source => source with
        {
            Publications = source.Publications with
            {
                Items = Copy(source.Publications.Items)
            }
        },
        ManagementConfigurationAssignmentCommand command => command with { Overrides = Copy(command.Overrides) },
        ManagementConfigurationAssignmentMap map => MapCopy(map),
        ManagementConfigurationPreviewCommand previewCommand => previewCommand with
        {
            Assignment = previewCommand.Assignment is null ? null : previewCommand.Assignment with
            {
                Overrides = Copy(previewCommand.Assignment.Overrides)
            }
        },
        ManagementConfigurationPreview preview => preview with
        {
            Changes = Copy(preview.Changes),
            Issues = Copy(preview.Issues),
            CurrentAssignment = MapCopy(preview.CurrentAssignment),
            Recipients = preview.Recipients with
            {
                Items = Copy(preview.Recipients.Items)
            },
            ProposedAssignment = preview.ProposedAssignment is null ? null : preview.ProposedAssignment with
            {
                Overrides = Copy(preview.ProposedAssignment.Overrides)
            }
        },
        ManagementConfigurationValidationResult validation => validation with { Issues = Copy(validation.Issues) },
        ManagementConfigurationStatusReport status => status with { Entries = Copy(status.Entries) },
        ManagementConfigurationDecision decision => DecisionCopy(decision),
        ManagementConfigurationOperationResult result => ResultCopy(result),
        ManagementConfigurationProblem problem => problem with
        {
            Result = problem.Result is null ? null : ResultCopy(problem.Result)
        },
        ManagementPage<ManagementConfigurationDefinition> page => page with
        {
            Items = Array.AsReadOnly(page.Items.Select(DefinitionCopy).ToArray())
        },
        ManagementPage<ManagementConfigurationPublicationMetadata> page => page with { Items = Copy(page.Items) },
        ManagementPage<ManagementConfigurationAdoptionEntry> page => page with { Items = Copy(page.Items) },
        _ => value
    };

    private static ManagementConfigurationDefinition DefinitionCopy(ManagementConfigurationDefinition value)
        => value with
        {
            Shape = ShapeCopy(value.Shape)
        };

    private static ManagementConfigurationShape ShapeCopy(ManagementConfigurationShape value)
        => value with
        {
            Members = value.Members is null ? null : Array.AsReadOnly(value.Members.Select(member => member with
            {
                Shape = ShapeCopy(member.Shape)
            }).ToArray()),
            Element = value.Element is null ? null : ShapeCopy(value.Element),
            AllowedValues = value.AllowedValues is null ? null : Copy(value.AllowedValues)
        };

    private static ManagementConfigurationAssignmentMap MapCopy(ManagementConfigurationAssignmentMap value)
        => value with
        {
            Overrides = Copy(value.Overrides)
        };

    private static ManagementConfigurationDecision DecisionCopy(ManagementConfigurationDecision value)
        => value with
        {
            Assignment = value.Assignment is null ? null : MapCopy(value.Assignment)
        };

    private static ManagementConfigurationOperationResult ResultCopy(ManagementConfigurationOperationResult value)
        => value with
        {
            Result = value.Result is null ? null : DecisionCopy(value.Result),
            AttemptCompletion = value.AttemptCompletion is null ? null : value.AttemptCompletion with
            {
                CleanupCodes = value.AttemptCompletion.CleanupCodes is null ? null : Copy(value.AttemptCompletion.CleanupCodes)
            }
        };

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values) => Array.AsReadOnly(values.ToArray());
}
