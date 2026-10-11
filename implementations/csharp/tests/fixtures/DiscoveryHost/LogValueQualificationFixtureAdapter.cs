using System.Globalization;
using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class LogValueQualificationFixtureAdapter
{
    internal static JsonObject Execute(JsonObject row)
    {
        int factoryCalls = 0;
        LogValue? value = null;
        ArgumentException? failure = null;
        bool? unchanged = null;
        JsonObject input = row["input"]!.AsObject();
        try
        {
            switch (input["mode"]!.GetValue<string>())
            {
                case "typed":
                    value = LogValueFixtureAdapter.Construct(input, () => factoryCalls++);
                    break;
                case "unicode":
                    string text = input["codeUnits"] is JsonArray units
                        ? new string(units.Select(unit => checked((char)unit!.GetValue<int>())).ToArray())
                        : input["text"]!.GetValue<string>();
                    factoryCalls++;
                    value = input["target"]!.GetValue<string>() == "key"
                        ? LogValue.Map(new Dictionary<string, LogValue> { [text] = LogValue.Null() })
                        : LogValue.String(text);
                    break;
                case "null-argument":
                    factoryCalls++;
                    value = NullArgument(input["kind"]!.GetValue<string>());
                    break;
                case "mutation":
                    value = Mutate(input["operation"]!.GetValue<string>(), () => factoryCalls++, out bool owned);
                    unchanged = owned;
                    break;
                default:
                    throw new InvalidDataException("Unknown value qualification descriptor.");
            }
        }
        catch (ArgumentException error)
        {
            failure = error;
        }
        var result = new JsonObject
        {
            ["caseId"] = row["id"]!.DeepClone(),
            ["valid"] = value is not null,
            ["kind"] = value?.Kind.ToString(),
            ["isTruncated"] = value?.IsTruncated,
            ["json"] = value is null ? null : LogValueFixtureAdapter.Project(value),
            ["factoryCalls"] = factoryCalls,
            ["error"] = failure switch
            {
                ArgumentNullException => "argument-null",
                ArgumentOutOfRangeException => "argument-out-of-range",
                null => null,
                _ => "argument-invalid"
            },
            ["nativeException"] = failure?.GetType().Name,
            ["parameter"] = failure?.ParamName,
            ["rejectedInputNotEchoed"] = failure is null || !failure.Message.Contains("CIS_NO_VALUE_ECHO", StringComparison.Ordinal),
            ["rangeActualValueAbsent"] = failure is not ArgumentOutOfRangeException range || range.ActualValue is null,
            ["ownedGraphUnchanged"] = unchanged
        };
        if (value?.Kind == LogValueKind.Number)
        {
            result["numberBits"] = BitConverter.DoubleToInt64Bits(value.Scalar.Number).ToString("X16", CultureInfo.InvariantCulture);
        }
        Validate(row, result);
        return result;
    }

    internal static void Validate(JsonObject row, JsonObject actual)
    {
        foreach ((string key, JsonNode? expected) in row["expected"]!.AsObject())
        {
            if (!JsonNode.DeepEquals(expected, actual[key]))
            {
                throw new InvalidDataException("Typed qualification observation differs: " + key);
            }
        }
        if (!actual["rejectedInputNotEchoed"]!.GetValue<bool>() || !actual["rangeActualValueAbsent"]!.GetValue<bool>())
        {
            throw new InvalidDataException("Rejected scalar leaked into Core argument diagnostics.");
        }
    }

    private static LogValue NullArgument(string kind) => kind switch
    {
        "string" => LogValue.String(null!),
        "integer-string" => LogValue.IntegerString(null!),
        "decimal-string" => LogValue.DecimalString(null!),
        "array" => LogValue.Array(null!),
        "map" => LogValue.Map(null!),
        _ => throw new InvalidDataException("Unknown null-argument target.")
    };

    private static LogValue Mutate(string operation, Action beforeFactory, out bool unchanged)
    {
        if (operation == "add-snapshot-reference")
        {
            beforeFactory();
            var caller = new List<LogValue> { LogValue.Null() };
            beforeFactory();
            LogValue result = LogValue.Array(caller);
            caller[0] = result;
            unchanged = result.ArrayItems.Count == 1 && result.ArrayItems[0].Kind == LogValueKind.Null;
            return result;
        }
        beforeFactory();
        var leaf = new Dictionary<string, LogValue> { ["amount"] = LogValue.DecimalString("12.3400") };
        beforeFactory();
        LogValue ownedLeaf = LogValue.Map(leaf);
        beforeFactory();
        var items = new List<LogValue> { ownedLeaf, LogValue.Null() };
        beforeFactory();
        var root = new Dictionary<string, LogValue> { ["items"] = LogValue.Array(items) };
        beforeFactory();
        LogValue captured = LogValue.Map(root);
        string before = LogValueFixtureAdapter.Project(captured)!.ToJsonString();
        if (operation == "clear-nested")
        {
            leaf.Clear();
            items.Clear();
            root.Clear();
        }
        else
        {
            beforeFactory();
            leaf["amount"] = LogValue.String("changed");
            beforeFactory();
            items[0] = LogValue.Boolean(false);
            beforeFactory();
            root["items"] = LogValue.Array([]);
        }
        unchanged = before == LogValueFixtureAdapter.Project(captured)!.ToJsonString();
        return captured;
    }
}
