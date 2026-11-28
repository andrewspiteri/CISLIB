using System.Text;

namespace CIS.RuleEngine;

internal static class RuleMatcher
{
    internal static RuleEvaluationTrace Evaluate(
        PreparedRuleDefinition prepared,
        RuleInputSnapshot input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(input);

        List<int> selected = [];
        List<RuleEvaluationError> diagnostics = [];
        RuleRowTrace[] rows = new RuleRowTrace[prepared.Rows.Count];
        RuleEvaluationError? error = null;
        RuleExecutionStatus status = RuleExecutionStatus.Completed;
        bool stopped = false;

        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            PreparedRuleRow row = prepared.Rows[rowIndex];
            RuleRowTrace trace;
            if (stopped || cancellationToken.IsCancellationRequested)
            {
                stopped = true;
                trace = UnevaluatedRow(row, rowIndex, RuleRowOutcome.NotReached);
            }
            else if (row.Rule.State == RuleState.Disabled)
            {
                trace = UnevaluatedRow(row, rowIndex, RuleRowOutcome.Disabled);
            }
            else
            {
                trace = EvaluateRow(row, rowIndex, input, cancellationToken, out bool cancelled);
                if (cancelled)
                {
                    stopped = true;
                }
                else if (trace.Error is { } rowError)
                {
                    if (row.Rule.State == RuleState.Test)
                    {
                        diagnostics.Add(rowError);
                    }
                    else
                    {
                        status = RuleExecutionStatus.Failed;
                        error = rowError;
                        stopped = true;
                    }
                }
                else if (trace.Outcome == RuleRowOutcome.Matched && row.Rule.State == RuleState.Enabled)
                {
                    selected.Add(rowIndex);
                    stopped = prepared.Definition.Mode == RuleExecutionMode.FirstMatch;
                }
            }

            rows[rowIndex] = trace;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            if (error is not null)
            {
                diagnostics.Add(error);
            }

            status = RuleExecutionStatus.Cancelled;
            error = new RuleEvaluationError("Cancelled", "The rule evaluation was cancelled.");
        }

        // The host still owns terminal selection and durable acceptance before releasing these candidates.
        int[] selectedIndices = status == RuleExecutionStatus.Completed
            ? selected
                .OrderByDescending(index => prepared.Rows[index].Rule.Weight)
                .ThenBy(index => prepared.Rows[index].Rule.Ordinal)
                .ToArray()
            : [];
        RuleRow[] matches = selectedIndices.Select(index => prepared.Rows[index].Rule).ToArray();

        return new RuleEvaluationTrace(
            status,
            Array.AsReadOnly(matches),
            Array.AsReadOnly(selectedIndices),
            Array.AsReadOnly(rows),
            diagnostics.AsReadOnly(),
            error);
    }

    private static RuleRowTrace EvaluateRow(
        PreparedRuleRow row,
        int rowIndex,
        RuleInputSnapshot input,
        CancellationToken cancellationToken,
        out bool cancelled)
    {
        RuleCellTrace[] cells = CreateUnevaluatedCells(row.Cells.Count);
        RuleRowOutcome outcome = RuleRowOutcome.Matched;
        RuleEvaluationError? error = null;
        cancelled = false;

        for (int columnIndex = 0; columnIndex < cells.Length; columnIndex++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                outcome = RuleRowOutcome.Error;
                error = new RuleEvaluationError("Cancelled", "The rule evaluation was cancelled before the cell was reached.", rowIndex, columnIndex);
                break;
            }

            PreparedRuleCell cell = row.Cells[columnIndex];
            input.Values.TryGetValue(cell.Column.Id, out RuleValue? value);
            RuleCellTrace trace = Compare(cell, value, rowIndex, columnIndex);
            cells[columnIndex] = trace;
            if (trace.Outcome == RuleCellOutcome.Error)
            {
                outcome = RuleRowOutcome.Error;
                error = trace.Error;
                break;
            }

            if (trace.Outcome == RuleCellOutcome.NotMatched)
            {
                outcome = RuleRowOutcome.NotMatched;
                break;
            }
        }

        return new RuleRowTrace(rowIndex, row.Rule.State, outcome, cells, error);
    }

    private static RuleCellTrace Compare(PreparedRuleCell cell, RuleValue? value, int rowIndex, int columnIndex)
    {
        bool missing = value is null || value.Kind == RuleValueKind.Null;
        if (cell.Condition.PresetKind == RulePresetKind.Any)
        {
            return Compared(columnIndex, matches: true);
        }

        if (cell.Condition.PresetKind == RulePresetKind.Missing)
        {
            return Compared(columnIndex, missing);
        }

        if (missing)
        {
            return Compared(columnIndex, matches: false);
        }

        if (value!.Kind != cell.Column.InputKind)
        {
            return new RuleCellTrace(
                columnIndex,
                RuleCellOutcome.Error,
                new RuleEvaluationError("WrongInputType", "The supplied value has the wrong type for the column.", rowIndex, columnIndex));
        }

        if (!SatisfiesConstraints(cell.Column, value))
        {
            return new RuleCellTrace(
                columnIndex,
                RuleCellOutcome.Error,
                new RuleEvaluationError("InputConstraintViolation", "The supplied value violates a declared column constraint.", rowIndex, columnIndex));
        }

        RuleValue preset = cell.Condition.Preset!;
        bool matches = cell.Condition.Operator switch
        {
            RuleOperator.Equals => AreEqual(value, preset),
            RuleOperator.NotEquals => !AreEqual(value, preset),
            RuleOperator.GreaterThan => value.DecimalValue > preset.DecimalValue,
            RuleOperator.GreaterThanOrEqual => value.DecimalValue >= preset.DecimalValue,
            RuleOperator.LessThan => value.DecimalValue < preset.DecimalValue,
            RuleOperator.LessThanOrEqual => value.DecimalValue <= preset.DecimalValue,
            RuleOperator.IsIn => preset.StringListValue.Contains(value.StringValue, StringComparer.Ordinal),
            RuleOperator.IsNotIn => !preset.StringListValue.Contains(value.StringValue, StringComparer.Ordinal),
            RuleOperator.WildcardMatch => cell.Wildcard!.IsMatch(value.StringValue),
            _ => throw new InvalidOperationException("A prepared rule contains an unsupported operator.")
        };

        return Compared(columnIndex, matches);
    }

    private static bool SatisfiesConstraints(RuleColumn column, RuleValue value)
    {
        if (value.Kind == RuleValueKind.String)
        {
            string text = value.StringValue;
            int bytes = Encoding.UTF8.GetByteCount(text);
            return !(bytes < column.MinimumUtf8Bytes || bytes > column.MaximumUtf8Bytes) &&
                (column.ExactScalarCount is not { } count || CountScalars(text) == count);
        }

        return value.Kind != RuleValueKind.Decimal ||
            !(value.DecimalValue < column.MinimumDecimal || value.DecimalValue > column.MaximumDecimal);
    }

    private static int CountScalars(string text)
    {
        int count = 0;
        foreach (Rune _ in text.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    private static bool AreEqual(RuleValue value, RuleValue preset) => value.Kind switch
    {
        RuleValueKind.String => string.Equals(value.StringValue, preset.StringValue, StringComparison.Ordinal),
        RuleValueKind.Decimal => value.DecimalValue == preset.DecimalValue,
        RuleValueKind.Boolean => value.BooleanValue == preset.BooleanValue,
        _ => throw new InvalidOperationException("A prepared equality comparison has an unsupported type.")
    };

    private static RuleRowTrace UnevaluatedRow(PreparedRuleRow row, int index, RuleRowOutcome outcome) =>
        new(index, row.Rule.State, outcome, CreateUnevaluatedCells(row.Cells.Count));

    private static RuleCellTrace[] CreateUnevaluatedCells(int count)
    {
        RuleCellTrace[] cells = new RuleCellTrace[count];
        for (int index = 0; index < cells.Length; index++)
        {
            cells[index] = new RuleCellTrace(index, RuleCellOutcome.NotEvaluated);
        }

        return cells;
    }

    private static RuleCellTrace Compared(int columnIndex, bool matches) =>
        new(columnIndex, matches ? RuleCellOutcome.Matched : RuleCellOutcome.NotMatched);
}

internal sealed record RuleEvaluationTrace(
    RuleExecutionStatus Status,
    IReadOnlyList<RuleRow> Matches,
    IReadOnlyList<int> SelectedRowIndices,
    IReadOnlyList<RuleRowTrace> Rows,
    IReadOnlyList<RuleEvaluationError> Diagnostics,
    RuleEvaluationError? Error);
