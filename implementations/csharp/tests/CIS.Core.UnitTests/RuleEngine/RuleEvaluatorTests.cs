using System.Text.Json;
using CIS.RuleEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.RuleEngine;

[TestClass]
public sealed class RuleEvaluatorTests
{
    [TestMethod]
    public void PortableFixturePreservesExactDecimalAndSuppliedValueKinds()
    {
        using JsonDocument fixture = ReadPortableFixture();
        foreach (JsonElement example in fixture.RootElement.GetProperty("decimal").EnumerateArray())
        {
            string text = example.GetProperty("text").GetString()!;
            if (example.GetProperty("valid").GetBoolean())
            {
                RuleValue value = RuleValue.FromDecimal(text);
                Assert.AreEqual(text, value.DecimalText);
            }
            else
            {
                Assert.ThrowsExactly<ArgumentException>(() => RuleValue.FromDecimal(text));
            }
        }

        RuleInputSnapshot input = new(fixture.RootElement.GetProperty("input").EnumerateArray().Select(entry =>
        {
            RuleValue value = entry.GetProperty("kind").GetString() switch
            {
                "string" => RuleValue.FromString(entry.GetProperty("value").GetString()!),
                "boolean" => RuleValue.FromBoolean(entry.GetProperty("value").GetBoolean()),
                "decimal" => RuleValue.FromDecimal(entry.GetProperty("value").GetString()!),
                "null" => RuleValue.Null,
                _ => throw new AssertFailedException("Unknown portable fixture value kind.")
            };
            return KeyValuePair.Create(entry.GetProperty("columnId").GetString()!, value);
        }));

        Assert.HasCount(5, input.Values);
        Assert.AreEqual("MLT", input.Values["residence-country"].StringValue);
        Assert.IsFalse(input.Values["pensioner"].BooleanValue);
        Assert.AreEqual(9007199254740993.000000000001m, input.Values["monthly-income"].DecimalValue);
        Assert.AreEqual(RuleValueKind.Null, input.Values["referral"].Kind);
        Assert.AreEqual("Café 🌍", input.Values["external-label"].StringValue);
        Assert.IsFalse(input.Values.ContainsKey(fixture.RootElement.GetProperty("omitted").GetString()!));
    }

    [TestMethod]
    public void CapturedValuesEnforceUtf8ScalarListAndOpaqueJsonBounds()
    {
        string maximumString = string.Concat(Enumerable.Repeat("🌍", 128));
        Assert.AreEqual(maximumString, RuleValue.FromString(maximumString).StringValue);
        Assert.ThrowsExactly<ArgumentException>(() => RuleValue.FromString(maximumString + "a"));
        Assert.ThrowsExactly<ArgumentException>(() => RuleValue.FromString("\uD800"));

        Assert.HasCount(32, RuleValue.FromStringList(Enumerable.Repeat("MLT", 32)).StringListValue);
        Assert.ThrowsExactly<ArgumentException>(() => RuleValue.FromStringList(Enumerable.Repeat("MLT", 33)));

        using JsonDocument collision = JsonDocument.Parse("""{"name":1,"n\u0061me":2}""");
        Assert.ThrowsExactly<ArgumentException>(() => RuleValue.FromJson(collision.RootElement));
    }

    [TestMethod]
    public void InputSnapshotEnforcesTheCompleteEncodedByteLimitWithoutDroppingUnknownInputs()
    {
        const int maximumInputBytes = 24 * 1024;
        const string prefix = """[{"columnId":"unknown","kind":"json","value":""";
        const string suffix = "}]";
        int contentLength = maximumInputBytes - prefix.Length - suffix.Length - 2;

        using JsonDocument exact = JsonDocument.Parse("\"" + new string('x', contentLength) + "\"");
        using JsonDocument over = JsonDocument.Parse("\"" + new string('x', contentLength + 1) + "\"");

        RuleInputSnapshot accepted = Input(("unknown", RuleValue.FromJson(exact.RootElement)));

        Assert.AreEqual(maximumInputBytes, accepted.EncodedSizeBytes);
        Assert.AreEqual(contentLength, accepted.Values["unknown"].JsonValue.GetString()!.Length);
        Assert.ThrowsExactly<ArgumentException>(() => Input(("unknown", RuleValue.FromJson(over.RootElement))));
    }

    [TestMethod]
    [DataRow(RuleOperator.Equals, "12.00", "12", true)]
    [DataRow(RuleOperator.NotEquals, "12.000", "12", false)]
    [DataRow(RuleOperator.NotEquals, "9007199254740993.000000000001", "9007199254740993.000000000002", true)]
    [DataRow(RuleOperator.GreaterThan, "9007199254740993.000000000002", "9007199254740993.000000000001", true)]
    [DataRow(RuleOperator.GreaterThanOrEqual, "-1.25", "-1.2500", true)]
    [DataRow(RuleOperator.LessThan, "9007199254740993.000000000002", "9007199254740993.000000000001", false)]
    [DataRow(RuleOperator.LessThan, "-0.0000000000000000000000000001", "0", true)]
    [DataRow(RuleOperator.LessThanOrEqual, "-1.2500000", "-1.25", true)]
    public void DecimalOperatorsUseExactNumericComparison(RuleOperator operation, string supplied, string preset, bool matches)
    {
        RuleColumn column = new("input", "Number", RuleValueKind.Decimal, [RulePresetKind.Decimal]);
        RuleCondition condition = new(column.Id, RulePresetKind.Decimal, operation, RuleValue.FromDecimal(preset));

        AssertSingleCondition(column, condition, RuleValue.FromDecimal(supplied), matches);
    }

    [TestMethod]
    [DataRow(RuleOperator.Equals, "ANY", "ANY", true)]
    [DataRow(RuleOperator.Equals, "MISSING", "missing", false)]
    [DataRow(RuleOperator.Equals, "MISSING", "MISSING", true)]
    [DataRow(RuleOperator.Equals, "", "", true)]
    [DataRow(RuleOperator.Equals, "MLT ", "MLT", false)]
    [DataRow(RuleOperator.NotEquals, "Café", "Cafe", true)]
    [DataRow(RuleOperator.WildcardMatch, "Café 🌍", "Café ?", true)]
    [DataRow(RuleOperator.WildcardMatch, "👩‍💻", "?", false)]
    [DataRow(RuleOperator.WildcardMatch, "xa🌍z", "a?z", false)]
    [DataRow(RuleOperator.WildcardMatch, "az", "a*z", true)]
    [DataRow(RuleOperator.WildcardMatch, "a🌍🍋z", "a*z", true)]
    [DataRow(RuleOperator.WildcardMatch, "a*?\\z", "a\\*\\?\\\\z", true)]
    public void StringOperatorsAreOrdinalAndWildcardsConsumeUnicodeScalars(
        RuleOperator operation, string supplied, string preset, bool matches)
    {
        RuleColumn column = new("input", "Text", RuleValueKind.String, [RulePresetKind.String]);
        RuleCondition condition = new(column.Id, RulePresetKind.String, operation, RuleValue.FromString(preset));

        AssertSingleCondition(column, condition, RuleValue.FromString(supplied), matches);
    }

    [TestMethod]
    [DataRow(RuleOperator.Equals, false, false, true)]
    [DataRow(RuleOperator.Equals, false, true, false)]
    [DataRow(RuleOperator.NotEquals, true, false, true)]
    [DataRow(RuleOperator.NotEquals, false, false, false)]
    public void BooleanOperatorsPreserveFalseAsAnOrdinaryValue(RuleOperator operation, bool supplied, bool preset, bool matches)
    {
        RuleColumn column = new("input", "Flag", RuleValueKind.Boolean, [RulePresetKind.Boolean]);
        RuleCondition condition = new(column.Id, RulePresetKind.Boolean, operation, RuleValue.FromBoolean(preset));

        AssertSingleCondition(column, condition, RuleValue.FromBoolean(supplied), matches);
    }

    [TestMethod]
    [DataRow(RuleOperator.IsIn, "MLT", true)]
    [DataRow(RuleOperator.IsIn, "mlt", false)]
    [DataRow(RuleOperator.IsIn, "ITA", false)]
    [DataRow(RuleOperator.IsIn, "I*", true)]
    [DataRow(RuleOperator.IsNotIn, "MLT", false)]
    [DataRow(RuleOperator.IsNotIn, "ITA", true)]
    public void MembershipIsLiteralOrdinalAndDuplicateEntriesDoNotProduceDuplicateMatches(
        RuleOperator operation, string supplied, bool matches)
    {
        RuleColumn column = new("input", "Country", RuleValueKind.String, [RulePresetKind.StringList]);
        RuleCondition condition = new(column.Id, RulePresetKind.StringList, operation, RuleValue.FromStringList(["MLT", "MLT", "I*"]));

        AssertSingleCondition(column, condition, RuleValue.FromString(supplied), matches);
    }

    [TestMethod]
    [DataRow(RuleOperator.IsIn, false)]
    [DataRow(RuleOperator.IsNotIn, true)]
    public void EmptyStringListHasDefinedMembership(RuleOperator operation, bool matches)
    {
        RuleColumn column = new("input", "Country", RuleValueKind.String, [RulePresetKind.StringList]);
        RuleCondition condition = new(column.Id, RulePresetKind.StringList, operation, RuleValue.FromStringList([]));

        AssertSingleCondition(column, condition, RuleValue.FromString(""), matches);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MarkersDistinguishAbsentOrNullFromOrdinaryPredicates(bool explicitNull)
    {
        RuleColumn column = new("input", "Text", RuleValueKind.String, [RulePresetKind.String, RulePresetKind.StringList]);
        RuleRow[] rows =
        [
            new("equals", 0, RuleState.Enabled, [new(column.Id, RulePresetKind.String, RuleOperator.Equals, RuleValue.FromString(""))]),
            new("not-equals", 1, RuleState.Enabled, [new(column.Id, RulePresetKind.String, RuleOperator.NotEquals, RuleValue.FromString(""))]),
            new("not-in", 2, RuleState.Enabled, [new(column.Id, RulePresetKind.StringList, RuleOperator.IsNotIn, RuleValue.FromStringList([]))]),
            new("missing", 3, RuleState.Enabled, [new(column.Id, RulePresetKind.Missing)]),
            new("any", 4, RuleState.Enabled, [new(column.Id, RulePresetKind.Any)])
        ];
        RuleInputSnapshot input = explicitNull ? Input((column.Id, RuleValue.Null)) : Input();

        RuleEvaluationTrace result = Evaluate([column], rows, input, RuleExecutionMode.AllMatching);

        Assert.AreEqual(RuleExecutionStatus.Completed, result.Status);
        CollectionAssert.AreEqual(new[] { "missing", "any" }, result.Matches.Select(row => row.RuleId).ToArray());
        Assert.AreEqual(explicitNull, input.Values.ContainsKey(column.Id));
    }

    [TestMethod]
    public void ZeroIsPresentAndDoesNotSatisfyMissing()
    {
        RuleColumn column = new("input", "Number", RuleValueKind.Decimal, [RulePresetKind.Decimal]);
        RuleCondition missing = new(column.Id, RulePresetKind.Missing);

        AssertSingleCondition(column, missing, RuleValue.FromDecimal("0.0"), matches: false);
    }

    [TestMethod]
    public void FirstMatchEvaluatesOrdinalAndColumnOrderThenStopsOnlyForEnabledMatch()
    {
        RuleColumn country = new("country", "Residence country", RuleValueKind.String, [RulePresetKind.String]);
        RuleColumn flag = new("flag", "Pensioner", RuleValueKind.Boolean, [RulePresetKind.Boolean]);
        RuleCondition local = new(country.Id, RulePresetKind.String, RuleOperator.Equals, RuleValue.FromString("MLT"));
        RuleCondition other = new(country.Id, RulePresetKind.String, RuleOperator.Equals, RuleValue.FromString("SWE"));
        RuleCondition falseFlag = new(flag.Id, RulePresetKind.Boolean, RuleOperator.Equals, RuleValue.FromBoolean(false));
        RuleCondition anyFlag = new(flag.Id, RulePresetKind.Any);
        RuleRow[] rows =
        [
            new("unreached-test", 60, RuleState.Test, [local, falseFlag]),
            new("selected", 40, RuleState.Enabled, [local, anyFlag]),
            new("disabled-after", 50, RuleState.Disabled, [local, falseFlag]),
            new("short-circuit", 10, RuleState.Enabled, [other, falseFlag]),
            new("trial-match", 30, RuleState.Test, [local, anyFlag], weight: 500m),
            new("trial-error", 20, RuleState.Test, [local, falseFlag]),
            new("disabled-before", 0, RuleState.Disabled, [local, falseFlag])
        ];

        RuleEvaluationTrace result = Evaluate(
            [country, flag], rows, Input((country.Id, RuleValue.FromString("MLT")), (flag.Id, RuleValue.FromString("False"))));

        Assert.AreEqual(RuleExecutionStatus.Completed, result.Status);
        CollectionAssert.AreEqual(new[] { "selected" }, result.Matches.Select(row => row.RuleId).ToArray());
        CollectionAssert.AreEqual(new[] { 4 }, result.SelectedRowIndices.ToArray());
        CollectionAssert.AreEqual(new[]
        {
            RuleRowOutcome.Disabled, RuleRowOutcome.NotMatched, RuleRowOutcome.Error, RuleRowOutcome.Matched,
            RuleRowOutcome.Matched, RuleRowOutcome.NotReached, RuleRowOutcome.NotReached
        }, result.Rows.Select(row => row.Outcome).ToArray());
        CollectionAssert.AreEqual(new[] { RuleCellOutcome.NotMatched, RuleCellOutcome.NotEvaluated },
            result.Rows[1].Cells.Select(cell => cell.Outcome).ToArray());
        Assert.AreEqual(RuleState.Test, result.Rows[3].State);
        Assert.HasCount(1, result.Diagnostics);
        Assert.AreEqual("WrongInputType", result.Diagnostics[0].Code);
        Assert.AreEqual(2, result.Diagnostics[0].RowIndex);
        Assert.AreEqual(1, result.Diagnostics[0].ColumnIndex);
    }

    [TestMethod]
    public void AllMatchingSortsEnabledHitsByExactDescendingWeightThenAscendingOrdinal()
    {
        RuleColumn column = new("input", "Text", RuleValueKind.String, [RulePresetKind.String]);
        RuleCondition any = new(column.Id, RulePresetKind.Any);
        RuleRow[] rows =
        [
            new("late-tie", 30, RuleState.Enabled, [any], "9007199254740993.0000000000010"),
            new("highest", 20, RuleState.Enabled, [any], "9007199254740993.000000000002"),
            new("negative", 0, RuleState.Enabled, [any], -10m),
            new("early-tie", 10, RuleState.Enabled, [any], "9007199254740993.000000000001"),
            new("trial", 40, RuleState.Test, [any], decimal.MaxValue)
        ];

        RuleEvaluationTrace result = Evaluate([column], rows, Input(), RuleExecutionMode.AllMatching);

        Assert.AreEqual(RuleExecutionStatus.Completed, result.Status);
        CollectionAssert.AreEqual(new[] { "highest", "early-tie", "late-tie", "negative" },
            result.Matches.Select(row => row.RuleId).ToArray());
        CollectionAssert.AreEqual(new[] { 2, 1, 3, 0 }, result.SelectedRowIndices.ToArray());
        Assert.AreEqual("9007199254740993.0000000000010", result.Matches[2].WeightText);
        Assert.AreEqual(RuleRowOutcome.Matched, result.Rows[4].Outcome);
        Assert.AreEqual(RuleState.Test, result.Rows[4].State);
    }

    [TestMethod]
    public void EnabledConstraintFailureRetainsObservedHitsButWithholdsTheSelection()
    {
        RuleColumn column = new("input", "Code", RuleValueKind.String, [RulePresetKind.String], exactScalarCount: 1);
        RuleCondition any = new(column.Id, RulePresetKind.Any);
        RuleCondition ordinary = new(column.Id, RulePresetKind.String, RuleOperator.NotEquals, RuleValue.FromString("X"));
        RuleRow[] rows =
        [
            new("test-hit", 0, RuleState.Test, [any]),
            new("enabled-hit", 1, RuleState.Enabled, [any]),
            new("failure", 2, RuleState.Enabled, [ordinary]),
            new("unreached", 3, RuleState.Enabled, [any])
        ];

        RuleEvaluationTrace result = Evaluate(
            [column], rows, Input((column.Id, RuleValue.FromString("too long"))), RuleExecutionMode.AllMatching);

        Assert.AreEqual(RuleExecutionStatus.Failed, result.Status);
        Assert.HasCount(0, result.Matches);
        Assert.HasCount(0, result.SelectedRowIndices);
        Assert.AreEqual("InputConstraintViolation", result.Error?.Code);
        Assert.AreEqual(2, result.Error?.RowIndex);
        Assert.AreEqual(0, result.Error?.ColumnIndex);
        CollectionAssert.AreEqual(new[] { RuleRowOutcome.Matched, RuleRowOutcome.Matched, RuleRowOutcome.Error, RuleRowOutcome.NotReached },
            result.Rows.Select(row => row.Outcome).ToArray());
        Assert.AreEqual(RuleCellOutcome.Error, result.Rows[2].Cells[0].Outcome);
    }

    [TestMethod]
    public void SnapshotsDetachCollectionsAndKeepOpaqueJsonAfterTheCallerDisposesIt()
    {
        RuleColumn column = new("input", "Code", RuleValueKind.String, [RulePresetKind.StringList]);
        var presetItems = new List<string> { "MLT" };
        var conditions = new List<RuleCondition>
        {
            new(column.Id, RulePresetKind.StringList, RuleOperator.IsIn, RuleValue.FromStringList(presetItems))
        };
        RuleDefinition definition;
        RuleInputSnapshot input;
        using (JsonDocument ownedByCaller = JsonDocument.Parse("""{"exact":9007199254740993.000,"list":["x"]}"""))
        {
            var rows = new List<RuleRow> { new("selected", 0, RuleState.Enabled, conditions, metadata: ownedByCaller.RootElement) };
            definition = Definition([column], rows, RuleExecutionMode.FirstMatch);
            var values = new Dictionary<string, RuleValue>
            {
                [column.Id] = RuleValue.FromString("MLT"),
                ["unknown"] = RuleValue.FromJson(ownedByCaller.RootElement)
            };
            input = new(values);

            rows.Clear();
            values.Clear();
            presetItems[0] = "ITA";
            conditions.Clear();
        }

        RuleEvaluationTrace result = RuleMatcher.Evaluate(RuleDefinitionValidator.Prepare(definition), input);

        Assert.AreEqual(RuleExecutionStatus.Completed, result.Status);
        Assert.HasCount(1, result.Matches);
        Assert.AreEqual("9007199254740993.000", result.Matches[0].Metadata.GetValueOrDefault().GetProperty("exact").GetRawText());
        Assert.AreEqual("9007199254740993.000", input.Values["unknown"].JsonValue.GetProperty("exact").GetRawText());
    }

    [TestMethod]
    [DataRow("bad\\q")]
    [DataRow("bad\\")]
    public void ValidatorRejectsMalformedWildcardEscapesBeforeEvaluation(string pattern)
    {
        RuleColumn column = new("input", "Text", RuleValueKind.String, [RulePresetKind.String]);
        RuleCondition condition = new(column.Id, RulePresetKind.String, RuleOperator.WildcardMatch, RuleValue.FromString(pattern));
        RuleDefinition malformed = Definition([column], [new("one", 0, RuleState.Enabled, [condition])], RuleExecutionMode.FirstMatch);

        Assert.Throws<ArgumentException>(() => RuleDefinitionValidator.Validate(malformed));
    }

    [TestMethod]
    public void ValidatorRejectsDuplicateOrdinalAnImplicitCellAndAnIncompatibleOperator()
    {
        RuleColumn first = new("first", "First", RuleValueKind.String, [RulePresetKind.String]);
        RuleColumn second = new("second", "Second", RuleValueKind.Boolean, [RulePresetKind.Boolean]);
        RuleCondition firstAny = new(first.Id, RulePresetKind.Any);
        RuleCondition secondAny = new(second.Id, RulePresetKind.Any);
        RuleColumn[] columns = [first, second];
        RuleRow[] duplicateOrder =
        [
            new("one", 0, RuleState.Enabled, [firstAny, secondAny]),
            new("two", 0, RuleState.Test, [firstAny, secondAny])
        ];
        RuleRow[] implicitCell = [new("one", 0, RuleState.Enabled, [firstAny])];
        RuleCondition wrongOperation = new(second.Id, RulePresetKind.Boolean, RuleOperator.GreaterThan, RuleValue.FromBoolean(false));

        Assert.Throws<ArgumentException>(() => RuleDefinitionValidator.Validate(Definition(columns, duplicateOrder, RuleExecutionMode.FirstMatch)));
        Assert.Throws<ArgumentException>(() => RuleDefinitionValidator.Validate(Definition(columns, implicitCell, RuleExecutionMode.FirstMatch)));
        Assert.Throws<ArgumentException>(() => RuleDefinitionValidator.Validate(Definition(
            columns, [new("one", 0, RuleState.Enabled, [firstAny, wrongOperation])], RuleExecutionMode.FirstMatch)));
    }

    [TestMethod]
    public void ValidatorChecksDeclaredMetadataAndRetainsItsNumericSpelling()
    {
        using var metadata = JsonDocument.Parse("{\"rate\":1.00e+0}");
        RuleMetadataShape shape = new(RuleMetadataKind.Object,
            properties: [new("rate", true, new(RuleMetadataKind.Number))]);
        RuleColumn column = new("input", "Text", RuleValueKind.String, [RulePresetKind.String]);
        RuleCondition any = new(column.Id, RulePresetKind.Any);
        RuleDefinition definition = new("test-rules", "schema-1", "version-1", RuleExecutionMode.FirstMatch,
            [column], [new("one", 0, RuleState.Enabled, [any], "-0.000", metadata.RootElement)], metadataShape: shape);
        RuleDefinition missingMetadata = new("test-rules", "schema-1", "version-1", RuleExecutionMode.FirstMatch,
            [column], [new("one", 0, RuleState.Enabled, [any])], metadataShape: shape);

        RuleDefinition validated = RuleDefinitionValidator.Validate(definition);
        using var wire = JsonDocument.Parse(RuleDefinitionWire.Encode(validated));
        JsonElement row = wire.RootElement.GetProperty("rows")[0];

        Assert.AreEqual("1.00e+0", row.GetProperty("metadata").GetProperty("rate").GetRawText());
        Assert.AreEqual("-0.000", row.GetProperty("weight").GetString());
        Assert.Throws<ArgumentException>(() => RuleDefinitionValidator.Validate(missingMetadata));
    }

    [TestMethod]
    public void ValidatorCapsTheWholeEncodedDefinitionAcrossIndividuallyValidRows()
    {
        RuleColumn column = new("input", "Text", RuleValueKind.String, [RulePresetKind.StringList]);
        RuleValue preset = RuleValue.FromStringList(Enumerable.Repeat(new string('x', 512), 32));
        RuleCondition condition = new(column.Id, RulePresetKind.StringList, RuleOperator.IsIn, preset);
        RuleRow[] rows = Enumerable.Range(0, 9)
            .Select(ordinal => new RuleRow($"rule-{ordinal}", ordinal, RuleState.Enabled, [condition]))
            .ToArray();

        RuleDefinition accepted = RuleDefinitionValidator.Validate(Definition([column], rows[..7], RuleExecutionMode.FirstMatch));

        Assert.HasCount(7, accepted.Rows);
        Assert.Throws<ArgumentException>(() => RuleDefinitionValidator.Validate(Definition([column], rows, RuleExecutionMode.FirstMatch)));
    }

    [TestMethod]
    public void EmptySuccessAndCallerCancellationHaveDistinctTerminalTraces()
    {
        RuleColumn column = new("input", "Text", RuleValueKind.String, [RulePresetKind.String]);
        RuleRow[] rows = [new("trial", 0, RuleState.Test, [new(column.Id, RulePresetKind.Any)])];

        RuleEvaluationTrace noRows = Evaluate([column], [], Input());
        RuleEvaluationTrace onlyTest = Evaluate([column], rows, Input());
        RuleEvaluationTrace cancelled = Evaluate([column], rows, Input(), cancellationToken: new CancellationToken(true));

        Assert.AreEqual(RuleExecutionStatus.Completed, noRows.Status);
        Assert.HasCount(0, noRows.Matches);
        Assert.AreEqual(RuleExecutionStatus.Completed, onlyTest.Status);
        Assert.AreEqual(RuleRowOutcome.Matched, onlyTest.Rows[0].Outcome);
        Assert.HasCount(0, onlyTest.Matches);
        Assert.AreEqual(RuleExecutionStatus.Cancelled, cancelled.Status);
        Assert.AreEqual("Cancelled", cancelled.Error?.Code);
        Assert.AreEqual(RuleRowOutcome.NotReached, cancelled.Rows[0].Outcome);
        Assert.HasCount(0, cancelled.Matches);
        Assert.HasCount(0, cancelled.SelectedRowIndices);
    }

    private static void AssertSingleCondition(RuleColumn column, RuleCondition condition, RuleValue supplied, bool matches)
    {
        RuleEvaluationTrace result = Evaluate([column], [new("one", 0, RuleState.Enabled, [condition])], Input((column.Id, supplied)));

        Assert.AreEqual(RuleExecutionStatus.Completed, result.Status);
        Assert.AreEqual(matches ? RuleRowOutcome.Matched : RuleRowOutcome.NotMatched, result.Rows[0].Outcome);
        Assert.AreEqual(matches ? RuleCellOutcome.Matched : RuleCellOutcome.NotMatched, result.Rows[0].Cells[0].Outcome);
        Assert.HasCount(matches ? 1 : 0, result.Matches);
    }

    private static RuleDefinition Definition(IEnumerable<RuleColumn> columns, IEnumerable<RuleRow> rows, RuleExecutionMode mode)
        => new("test-rules", "schema-1", "version-1", mode, columns, rows);

    private static RuleEvaluationTrace Evaluate(
        RuleColumn[] columns, RuleRow[] rows, RuleInputSnapshot input,
        RuleExecutionMode mode = RuleExecutionMode.FirstMatch, CancellationToken cancellationToken = default)
        => RuleMatcher.Evaluate(RuleDefinitionValidator.Prepare(Definition(columns, rows, mode)), input, cancellationToken);

    private static RuleInputSnapshot Input(params (string Id, RuleValue Value)[] entries)
        => new(entries.Select(entry => KeyValuePair.Create(entry.Id, entry.Value)));

    private static JsonDocument ReadPortableFixture()
    {
        for (DirectoryInfo? root = new(AppContext.BaseDirectory); root is not null; root = root.Parent)
        {
            string path = Path.Combine(root.FullName, "conformance", "rule-engine", "v1", "portable-values.json");
            if (File.Exists(path))
            {
                return JsonDocument.Parse(File.ReadAllBytes(path));
            }
        }
        throw new AssertFailedException("The RM11 portable values fixture was not found under the repository.");
    }
}
