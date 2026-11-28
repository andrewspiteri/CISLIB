using System.Text.Json;
using System.Text.Json.Serialization;
using CIS.Configuration;
using CIS.Management;

namespace CIS.RuleEngine;

/// <summary>A passive retained Configuration root for one explicitly enrolled Rule Engine binding.</summary>
/// <remarks>Declare a concrete application type for each ruleset and enroll it with the matching
/// native Configuration ID and HotReload policy. Provide only schemas supported by this application build.</remarks>
public abstract class RuleEngineConfiguration : ConfigurationObject
{
    private readonly Dictionary<string, byte[]> schemas = new(StringComparer.Ordinal);

    /// <summary>Captures bounded rowless schema declarations for a single ruleset.</summary>
    protected RuleEngineConfiguration(params RuleDefinition[] supportedSchemas)
    {
        ArgumentNullException.ThrowIfNull(supportedSchemas);
        if (supportedSchemas.Length is < 1 or > 16 || supportedSchemas[0] is not { } first)
        {
            throw new ArgumentException("Select between one and sixteen supported schema revisions.", nameof(supportedSchemas));
        }

        RulesetId = first.RulesetId;
        foreach (RuleDefinition schema in supportedSchemas)
        {
            if (schema is null || schema.RulesetId != RulesetId || schema.Rows.Count != 0
                || !schemas.TryAdd(schema.SchemaRevision, SchemaBytes(schema)))
            {
                throw new ArgumentException("Select distinct rowless schemas for the same ruleset.", nameof(supportedSchemas));
            }
        }
    }

    /// <summary>The RM08 carrier's data member. Evaluation captures the prepared Configuration version.</summary>
    [JsonPropertyName("definitionChunks")]
    public string[] DefinitionChunks { get; set; } = [];

    internal string RulesetId
    {
        get;
    }

    internal PreparedRuleDefinition PrepareSnapshot(JsonElement? common, JsonElement? application)
    {
        if (common.HasValue || application is not { } carrier)
        {
            throw new ArgumentException("A Rule Engine deployment requires one exact application source.");
        }
        return Prepare(ManagementRuleDefinitionCarrier.Decode(carrier.GetRawText()).Definition);
    }

    internal PreparedRuleDefinition Prepare(RuleDefinition definition)
    {
        if (definition.RulesetId != RulesetId || !schemas.TryGetValue(definition.SchemaRevision, out byte[]? registered)
            || !SchemaBytes(definition).AsSpan().SequenceEqual(registered))
        {
            throw new ArgumentException("The definition schema is unsupported by this application build.", nameof(definition));
        }
        return RuleDefinitionValidator.Prepare(definition);
    }

    private static byte[] SchemaBytes(RuleDefinition definition)
        => RuleDefinitionCodec.Encode(new(definition.RulesetId, definition.SchemaRevision, "host-schema",
            RuleExecutionMode.FirstMatch, definition.Columns, [], metadataShape: definition.MetadataShape));
}
