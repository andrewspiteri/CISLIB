using System.Security.Cryptography;
using System.Text.Json.Serialization;
using CIS.Management.Wire;
using CIS.Messaging;

namespace CIS.Management;

/// <summary>An exact logical owner/definition/version identity, independent of instance presence or deployment labels.</summary>
public sealed record ManagementCapabilityKey
{
    /// <summary>Copies and validates passive description data.</summary>
    public ManagementCapabilityKey(string ownerApplication, string kind, string executionScope, string definitionId,
        string definitionVersion)
    {
        OwnerApplication = ManagementCatalogValidation.Scalar(ownerApplication, 255, nameof(ownerApplication));
        Kind = ManagementCatalogValidation.Token(kind, nameof(kind));
        ExecutionScope = ManagementCatalogValidation.Text(executionScope, 256, nameof(executionScope));
        DefinitionId = ManagementCatalogValidation.Text(definitionId, 128, nameof(definitionId));
        DefinitionVersion = ManagementCatalogValidation.Version(definitionVersion, nameof(definitionVersion));
    }

    /// <summary>Gets the immutable owner application.</summary>
    public string OwnerApplication { get; }
    /// <summary>Gets the immutable kind.</summary>
    public string Kind { get; }
    /// <summary>Gets the immutable execution scope.</summary>
    public string ExecutionScope { get; }
    /// <summary>Gets the immutable definition id.</summary>
    public string DefinitionId { get; }
    /// <summary>Gets the immutable definition version.</summary>
    public string DefinitionVersion { get; }
    /// <summary>Gets whether this version understands the kind; reported rule-engine metadata creates no engine.</summary>
    [JsonIgnore]
    public bool IsKnownKind => Kind is "job" or "triggerProvider" or "ruleEngine";
}

/// <summary>An exact existing Configuration contract and detached structural declaration, without values or defaults.</summary>
public sealed class ManagementConfigurationContractReference
{
    /// <summary>Copies and validates passive description data.</summary>
    public ManagementConfigurationContractReference(string configurationId, string schemaVersion,
        ManagementConfigurationShape shape)
    {
        ConfigurationId = ManagementWire.IsOperationKey(configurationId)
            ? configurationId : throw ManagementCatalogValidation.Invalid(nameof(configurationId));
        SchemaVersion = ManagementCatalogValidation.Version(schemaVersion, nameof(schemaVersion));
        Shape = ManagementCatalogValidation.Freeze(shape);
    }

    /// <summary>Gets the immutable configuration id.</summary>
    public string ConfigurationId { get; }
    /// <summary>Gets the immutable schema version.</summary>
    public string SchemaVersion { get; }
    /// <summary>Gets the immutable shape.</summary>
    public ManagementConfigurationShape Shape { get; }
}

/// <summary>A bounded immutable trusted definition; it does not instantiate a handler, install a schema or grant permission.</summary>
public sealed class ManagementCapabilityDefinition
{
    /// <summary>Copies and validates passive description data.</summary>
    public ManagementCapabilityDefinition(ManagementCapabilityKey key, string displayName,
        IReadOnlyList<string> supportedOperations, string? description = null, string? categoryId = null,
        ManagementConfigurationContractReference? configurationContract = null, MessageContract? inputContract = null,
        ManagementConfigurationShape? inputShape = null, MessageContract? eventContract = null,
        ManagementConfigurationShape? eventShape = null, MessageContract? resultSummaryContract = null,
        ManagementConfigurationShape? resultSummaryShape = null, ManagementFormDescription? form = null,
        ManagementJobExecutionSupport? executionSupport = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        Key = key;
        DisplayName = ManagementCatalogValidation.Text(displayName, 256, nameof(displayName));
        Description = description is null ? null : ManagementCatalogValidation.Text(description, 2048, nameof(description), empty: true);
        CategoryId = categoryId is null ? null : ManagementCatalogValidation.Text(categoryId, 128, nameof(categoryId));
        SupportedOperations = ManagementCatalogValidation.Copy(supportedOperations,
            ManagementCapabilityLimits.MaximumOperations, nameof(supportedOperations));
        if (SupportedOperations.Count == 0 || SupportedOperations.Distinct(StringComparer.Ordinal).Count() != SupportedOperations.Count)
        {
            throw ManagementCatalogValidation.Invalid(nameof(supportedOperations));
        }
        foreach (string operation in SupportedOperations)
        {
            ManagementCatalogValidation.Token(operation, nameof(supportedOperations));
        }
        if ((inputContract is null) != (inputShape is null) || (eventContract is null) != (eventShape is null)
            || (resultSummaryContract is null) != (resultSummaryShape is null)
            || configurationContract is null && SupportedOperations.Any(operation => operation is "configure" or "createConfigured")
            || key.Kind == "job" && inputContract is null && SupportedOperations.Contains("manual", StringComparer.Ordinal)
            || executionSupport is not null && key.Kind != "job"
            || key.Kind == "job" && (eventContract is not null || SupportedOperations.Contains("createConfigured", StringComparer.Ordinal))
            || key.Kind == "triggerProvider" && (eventContract is null || executionSupport is not null))
        {
            throw ManagementCatalogValidation.Invalid(nameof(key));
        }
        ConfigurationContract = configurationContract;
        InputContract = inputContract;
        InputShape = inputShape is null ? null : ManagementCatalogValidation.Freeze(inputShape);
        EventContract = eventContract;
        EventShape = eventShape is null ? null : ManagementCatalogValidation.Freeze(eventShape);
        ResultSummaryContract = resultSummaryContract;
        ResultSummaryShape = resultSummaryShape is null ? null : ManagementCatalogValidation.Freeze(resultSummaryShape);
        Form = form;
        if (form is not null)
        {
            form.Validate(configurationContract?.Shape ?? throw ManagementCatalogValidation.Invalid(nameof(form)));
        }
        ExecutionSupport = executionSupport is null ? null : ManagementCatalogValidation.Support(executionSupport);
        byte[] encoded = ManagementCatalogValidation.Encode(this);
        EncodedLength = encoded.Length;
        SemanticDigest = Convert.ToHexStringLower(SHA256.HashData(encoded));
    }

    /// <summary>Gets the immutable key.</summary>
    public ManagementCapabilityKey Key { get; }
    /// <summary>Gets the immutable display name.</summary>
    public string DisplayName { get; }
    /// <summary>Gets the immutable description.</summary>
    public string? Description { get; }
    /// <summary>Gets the immutable category id.</summary>
    public string? CategoryId { get; }
    /// <summary>Gets the immutable supported operations.</summary>
    public IReadOnlyList<string> SupportedOperations { get; }
    /// <summary>Gets the immutable configuration contract.</summary>
    public ManagementConfigurationContractReference? ConfigurationContract { get; }
    /// <summary>Gets the immutable input contract.</summary>
    public MessageContract? InputContract { get; }
    /// <summary>Gets the immutable input shape.</summary>
    public ManagementConfigurationShape? InputShape { get; }
    /// <summary>Gets the immutable event contract.</summary>
    public MessageContract? EventContract { get; }
    /// <summary>Gets the immutable event shape.</summary>
    public ManagementConfigurationShape? EventShape { get; }
    /// <summary>Gets the immutable result summary contract.</summary>
    public MessageContract? ResultSummaryContract { get; }
    /// <summary>Gets the immutable result summary shape.</summary>
    public ManagementConfigurationShape? ResultSummaryShape { get; }
    /// <summary>Gets the immutable form.</summary>
    public ManagementFormDescription? Form { get; }
    /// <summary>Gets the immutable execution support.</summary>
    public ManagementJobExecutionSupport? ExecutionSupport { get; }
    /// <summary>Gets fixed local metadata support, never deployment readiness, source authority or permission.</summary>
    [JsonIgnore]
    public bool IsSupported => Key.IsKnownKind && (Form?.IsSupported ?? true)
        && SupportedOperations.All(IsKnownOperation);
    /// <summary>Gets the bounded encoded description length, without publishing any configuration values.</summary>
    [JsonIgnore]
    public int EncodedLength { get; }
    [JsonIgnore]
    internal string SemanticDigest { get; }

    /// <summary>Checks only the declared known operation; actual owner support and native authorization remain separate.</summary>
    public bool DeclaresOperation(string operation)
        => IsSupported && IsKnownOperation(operation) && SupportedOperations.Contains(operation, StringComparer.Ordinal);

    private static bool IsKnownOperation(string operation)
        => operation is "inspect" or "configure" or "createConfigured" or "manual" or "cancel";
}
