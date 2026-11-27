using System.Text.Json.Serialization;
using CIS.Messaging;

namespace CIS.Management;

/// <summary>Inert presentation for one declared object member, addressed by an exact JSON pointer.</summary>
public sealed record ManagementFormFieldHint(string Path, string Editor, string Label, string? Description = null,
    string? GroupId = null, int Order = 0, string? Unit = null, bool ReadOnly = false, string? LookupId = null)
{
    /// <summary>Gets whether this client implements the named fixed editor; unknown editors never activate.</summary>
    [JsonIgnore]
    public bool IsKnownEditor => Editor is "text" or "number" or "boolean" or "enum" or "duration" or "group" or "calendar";
}

/// <summary>One finite display group, without executable layout or markup.</summary>
public sealed record ManagementFormGroup(string Id, string Label, int Order = 0);

/// <summary>A trusted named lookup contract; resolution and actual resource authorization remain server-owned.</summary>
public sealed record ManagementNamedLookupReference(string Id, string OwnerApplication, string ExecutionScope,
    string LookupName, MessageContract Contract);

/// <summary>A detached finite fixed-component form description, never a script or a configuration value store.</summary>
public sealed class ManagementFormDescription
{
    /// <summary>Copies and validates bounded inert hints; the containing definition validates their actual field paths.</summary>
    public ManagementFormDescription(IReadOnlyList<ManagementFormFieldHint> fields,
        IReadOnlyList<ManagementFormGroup>? groups = null, IReadOnlyList<ManagementNamedLookupReference>? lookups = null)
    {
        Fields = ManagementCatalogValidation.Copy(fields, ManagementCapabilityLimits.MaximumFields, nameof(fields));
        Groups = ManagementCatalogValidation.Copy(groups ?? [], ManagementCapabilityLimits.MaximumGroups, nameof(groups));
        Lookups = ManagementCatalogValidation.Copy(lookups ?? [], ManagementCapabilityLimits.MaximumLookups, nameof(lookups));
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ManagementFormGroup group in Groups)
        {
            ManagementCatalogValidation.Token(group.Id, nameof(groups));
            ManagementCatalogValidation.Text(group.Label, 256, nameof(groups));
            Order(group.Order);
            if (!groupIds.Add(group.Id))
            {
                throw ManagementCatalogValidation.Invalid(nameof(groups));
            }
        }
        var lookupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ManagementNamedLookupReference lookup in Lookups)
        {
            ManagementCatalogValidation.Token(lookup.Id, nameof(lookups));
            ManagementCatalogValidation.Scalar(lookup.OwnerApplication, 255, nameof(lookups));
            ManagementCatalogValidation.Text(lookup.ExecutionScope, 256, nameof(lookups));
            ManagementCatalogValidation.Token(lookup.LookupName, nameof(lookups));
            ArgumentNullException.ThrowIfNull(lookup.Contract);
            if (!lookupIds.Add(lookup.Id))
            {
                throw ManagementCatalogValidation.Invalid(nameof(lookups));
            }
        }
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (ManagementFormFieldHint field in Fields)
        {
            ManagementCatalogValidation.Text(field.Path, 1024, nameof(fields));
            ManagementCatalogValidation.Token(field.Editor, nameof(fields));
            ManagementCatalogValidation.Text(field.Label, 256, nameof(fields));
            if (field.Description is not null)
            {
                ManagementCatalogValidation.Text(field.Description, 2048, nameof(fields), empty: true);
            }
            Order(field.Order);
            if (!paths.Add(field.Path) || field.GroupId is not null && !groupIds.Contains(field.GroupId)
                || field.LookupId is not null && !lookupIds.Contains(field.LookupId))
            {
                throw ManagementCatalogValidation.Invalid(nameof(fields));
            }
            if (field.Unit is not null && field.Unit is not ("milliseconds" or "seconds" or "minutes" or "hours" or "days"
                or "bytes" or "count" or "percent"))
            {
                throw ManagementCatalogValidation.Invalid(nameof(fields));
            }
        }
    }

    /// <summary>Gets immutable ordered field hints, not captured field values.</summary>
    public IReadOnlyList<ManagementFormFieldHint> Fields { get; }
    /// <summary>Gets distinct immutable display groups.</summary>
    public IReadOnlyList<ManagementFormGroup> Groups { get; }
    /// <summary>Gets registered names/contracts; none supplies a URI or permission.</summary>
    public IReadOnlyList<ManagementNamedLookupReference> Lookups { get; }
    /// <summary>Gets whether every fixed editor is known; unknown metadata stays unavailable.</summary>
    [JsonIgnore]
    public bool IsSupported => Fields.All(hint => hint.IsKnownEditor);

    internal void Validate(ManagementConfigurationShape root)
    {
        foreach (ManagementFormFieldHint field in Fields)
        {
            ManagementConfigurationShape shape = ManagementCatalogValidation.Resolve(root, field.Path);
            bool compatible = field.Editor switch
            {
                "text" => shape.Kind == "string" && shape.AllowedValues is null,
                "number" => shape.Kind is "number" or "integer",
                "boolean" => shape.Kind == "boolean",
                "enum" => shape.Kind == "string" && shape.AllowedValues is { Count: > 0 },
                "duration" => shape.Kind is "integer" or "number" or "string",
                "group" or "calendar" => shape.Kind == "object",
                _ => true // Retain bounded future metadata; IsSupported prevents activation.
            };
            if (!compatible || field.Editor == "duration" && field.Unit is not
                ("milliseconds" or "seconds" or "minutes" or "hours" or "days")
                || field.Unit is not null && field.Editor is not ("duration" or "number"))
            {
                throw ManagementCatalogValidation.Invalid(nameof(root));
            }
        }
    }

    private static void Order(int value)
    {
        if (value is < -10000 or > 10000)
        {
            throw ManagementCatalogValidation.Invalid(nameof(value));
        }
    }
}
