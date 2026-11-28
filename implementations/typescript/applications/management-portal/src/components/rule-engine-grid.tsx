"use client";

import { useEffect, useRef, useState } from "react";
import type { KeyboardEvent, ReactNode } from "react";
import { addRule, modeLabels, moveRule, newRuleCell, operatorLabels, removeRule, ruleCellLabel, ruleOperators,
  rulePresetKinds, ruleRowLabel, ruleUiLimits, stateLabels, updateRule, updateRuleCell } from "../rule-engine-draft.ts";
import type { DraftIssue, PresetKind, RuleCell, RuleColumn, RuleDraft, RuleMode, RuleRow, RuleState } from "../rule-engine-draft.ts";
import { Badge, Empty, TechnicalDetails } from "./shared";

const presetLabels: Readonly<Record<PresetKind, string>> = { "": "Choose condition", string: "Compare text", decimal: "Compare value",
  boolean: "Compare Boolean", stringList: "Compare list", any: "Any", missing: "Missing" };
type Selection = { readonly ruleId: string; readonly columnId?: string };

export function RuleGrid({ draft, editable, change, issue, actions, hits, hitsStale }: {
  readonly draft: RuleDraft; readonly editable: boolean; readonly change: (next: RuleDraft) => void;
  readonly issue?: DraftIssue | undefined; readonly actions?: ReactNode;
  readonly hits?: readonly { readonly ruleId: string; readonly count?: string }[] | undefined; readonly hitsStale?: boolean;
}) {
  const [selection, select] = useState<Selection>();
  const condition = useRef<HTMLSelectElement>(null);
  const rowEditor = useRef<HTMLDivElement>(null);
  const cells = useRef(new Map<string, HTMLButtonElement>());
  const row = draft.rows.find(item => item.ruleId === selection?.ruleId) ?? draft.rows[0];
  const column = draft.columns.find(item => item.id === selection?.columnId);
  const cell = row?.conditions.find(item => item.columnId === column?.id);
  const rowIndex = row === undefined ? -1 : draft.rows.indexOf(row);

  useEffect(() => {
    if (issue?.ruleId === undefined) { return; }
    select({ ruleId: issue.ruleId, ...(issue.columnId === undefined ? {} : { columnId: issue.columnId }) });
    if (issue.columnId !== undefined) { cells.current.get(cellKey(issue.ruleId, issue.columnId))?.focus(); }
    else if (issue.field !== undefined) {
      const field = issue.field;
      requestAnimationFrame(() => rowEditor.current?.querySelector<HTMLElement>('[data-rule-field="' + field + '"]')?.focus());
    }
  }, [issue]);

  function add(basedOn?: string) {
    const ruleId = globalThis.crypto.randomUUID();
    change(addRule(draft, ruleId, basedOn));
    select({ ruleId, ...(draft.columns[0] === undefined ? {} : { columnId: draft.columns[0].id }) });
    requestAnimationFrame(() => condition.current?.focus());
  }
  function openCell(ruleId: string, columnId: string) {
    select({ ruleId, columnId });
    requestAnimationFrame(() => condition.current?.focus());
  }
  function moveFocus(event: KeyboardEvent<HTMLButtonElement>, rowIndex: number, columnIndex: number) {
    const offset = { ArrowRight: [0, 1], ArrowLeft: [0, -1], ArrowUp: [-1, 0], ArrowDown: [1, 0] }[event.key];
    if (offset === undefined) { return; }
    const destinationRow = draft.rows[rowIndex + offset[0]!] , destinationColumn = draft.columns[columnIndex + offset[1]!];
    if (destinationRow !== undefined && destinationColumn !== undefined) {
      event.preventDefault();
      cells.current.get(cellKey(destinationRow.ruleId, destinationColumn.id))?.focus();
    }
  }
  function editRow(edit: (row: RuleRow) => RuleRow) {
    if (row !== undefined) { change(updateRule(draft, row.ruleId, edit)); }
  }

  return <section aria-label="Rules grid">
    <div className="rm11-grid-toolbar">
      <div><label htmlFor="rule-engine-mode">Execution mode</label>
        <select id="rule-engine-mode" value={draft.mode} disabled={!editable}
          onChange={event => change({ ...draft, mode: event.target.value as RuleMode })}>
          {Object.entries(modeLabels).map(([mode, label]) => <option value={mode} key={mode}>{label}</option>)}
        </select>
      </div>
      <p className="muted">{draft.mode === "allMatching" ? "Enabled matches are returned in descending weight order." : "Evaluation stops at the first matching enabled rule."}</p>
      <div className="rm11-actions">{editable ? <button type="button" disabled={draft.rows.length >= ruleUiLimits.rows}
        onClick={() => add()}>Add rule</button> : null}{actions}</div>
    </div>
    {issue === undefined ? null : <p role="alert" className="notice">{issue.message}</p>}
    <div className="table-frame rm11-grid-frame" tabIndex={0} aria-label="Rules in schema order">
      <table className="rm11-grid-table"><thead><tr><th scope="col">Order</th><th scope="col">State</th>
        {draft.columns.map(item => <th scope="col" key={item.id}>{item.displayName}</th>)}
        <th scope="col">Weight</th><th scope="col">Hits</th><th scope="col">Rule</th></tr></thead>
        <tbody>{draft.rows.map((item, index) => {
          const count = hits?.find(counter => counter.ruleId === item.ruleId)?.count;
          return <tr key={item.ruleId}>
          <td>{item.ordinal}</td><td><Badge tone={item.state === "enabled" ? "good" : item.state === "test" ? "warning" : "neutral"}>{stateLabels[item.state]}</Badge></td>
          {draft.columns.map((input, columnIndex) => {
            const key = cellKey(item.ruleId, input.id);
            const preset = item.conditions.find(value => value.columnId === input.id) ?? newRuleCell(input.id, "");
            const selected = selection?.ruleId === item.ruleId && selection.columnId === input.id;
            const invalid = issue?.ruleId === item.ruleId && issue.columnId === input.id;
            return <td key={input.id}><button type="button" className="rm11-cell" data-selected={selected} aria-invalid={invalid}
              aria-label={"Rule " + item.ordinal + " " + input.displayName + ": " + ruleCellLabel(preset)}
              ref={node => { if (node === null) { cells.current.delete(key); } else { cells.current.set(key, node); } }}
              onKeyDown={event => moveFocus(event, index, columnIndex)} onClick={() => openCell(item.ruleId, input.id)}>{ruleCellLabel(preset)}</button></td>;
          })}
          <td>{item.weight}</td><td aria-label={count === undefined ? "Hit count unavailable" : undefined}>{count ?? "—"}
            {count !== undefined && hitsStale ? <small>Stale</small> : null}</td>
          <td><button type="button" className="rm11-cell" onClick={() => select({ ruleId: item.ruleId })}>{ruleRowLabel(item)}</button></td>
        </tr>; })}</tbody>
      </table>
      {draft.rows.length === 0 ? <Empty title="No rules">Add a rule to start editing this version.</Empty> : null}
    </div>
    {row === undefined ? null : <div className="rm11-inspectors">
      {column === undefined || cell === undefined ? <section className="card rm11-panel" aria-label="Condition inspector">
        <h2>Condition</h2><p>Select a condition to inspect its operator and preset.</p>
      </section> : <section className="card rm11-panel" aria-label="Condition inspector">
        <h2>{column.displayName}</h2><p>Rule {row.ordinal} · {presetLabels[column.inputKind].replace("Compare ", "")}</p>
        <RuleConditionEditor key={cellKey(row.ruleId, column.id)} column={column} cell={cell} editable={editable}
          conditionRef={condition} onChange={next => change(updateRuleCell(draft, row.ruleId, next))} />
      </section>}
      <div className="card rm11-panel" ref={rowEditor} aria-label="Row inspector">
        <h2>{ruleRowLabel(row)}</h2>
        <div className="rm11-fields">
          <label>State<select aria-label="State" value={row.state} disabled={!editable} onChange={event => editRow(current => ({ ...current, state: event.target.value as RuleState }))}>
            {Object.entries(stateLabels).map(([state, label]) => <option key={state} value={state}>{label}</option>)}
          </select></label>
          <label>Weight<input data-rule-field="weight" inputMode="decimal" readOnly={!editable} value={row.weight}
            aria-invalid={issue?.ruleId === row.ruleId && issue.field === "weight"}
            onChange={event => editRow(current => ({ ...current, weight: event.target.value }))} /></label>
        </div>
        <label>Comment<input data-rule-field="comment" readOnly={!editable} value={row.comment ?? ""}
          onChange={event => editRow(current => ({ ...current, comment: event.target.value }))} /></label>
        <label>Metadata<textarea data-rule-field="metadata" spellCheck={false} readOnly={!editable} rows={3} value={row.metadataText ?? ""}
          aria-invalid={issue?.ruleId === row.ruleId && issue.field === "metadata"}
          onChange={event => editRow(current => ({ ...current, metadataText: event.target.value }))} /></label>
        {editable ? <div className="rm11-actions">
          <button type="button" disabled={rowIndex < 1} onClick={() => change(moveRule(draft, row.ruleId, -1))}>Move up</button>
          <button type="button" disabled={rowIndex >= draft.rows.length - 1} onClick={() => change(moveRule(draft, row.ruleId, 1))}>Move down</button>
          <button type="button" disabled={draft.rows.length >= ruleUiLimits.rows} onClick={() => add(row.ruleId)}>Copy rule</button>
          <button type="button" onClick={() => { change(removeRule(draft, row.ruleId)); select(undefined); }}>Remove rule</button>
        </div> : null}
        <TechnicalDetails fields={[{ label: "Rule ID", value: row.ruleId }]} />
      </div>
    </div>}
  </section>;
}
function RuleConditionEditor({ column, cell, editable, conditionRef, onChange }: {
  readonly column: RuleColumn; readonly cell: RuleCell; readonly editable: boolean;
  readonly conditionRef: React.RefObject<HTMLSelectElement | null>; readonly onChange: (cell: RuleCell) => void;
}) {
  const [entry, setEntry] = useState("");
  const value = cell.preset?.value;
  const items: readonly string[] = Array.isArray(value) ? value : [];
  function presetValue(next: string | boolean | readonly string[]) {
    if (cell.preset !== undefined) { onChange({ ...cell, preset: { ...cell.preset, value: next } }); }
  }
  function addChip() {
    if (!editable || items.length >= ruleUiLimits.listItems) { return; }
    presetValue([...items, entry]); setEntry("");
  }
  return <>
    <div className="rm11-fields">
      <label>Condition<select aria-label="Condition" ref={conditionRef} value={cell.presetKind} disabled={!editable}
        onChange={event => { setEntry(""); onChange(newRuleCell(column.id, event.target.value as PresetKind)); }}>
        <option value="">Choose condition</option>
        {rulePresetKinds(column).map(kind => <option key={kind} value={kind}>{presetLabels[kind]}</option>)}
      </select></label>
      {cell.operator === undefined ? null : <label>Operator<select aria-label="Operator" value={cell.operator} disabled={!editable}
        onChange={event => onChange({ ...cell, operator: event.target.value })}>
        {ruleOperators(cell.presetKind).map(operator => <option key={operator} value={operator}>{operatorLabels[operator]}</option>)}
      </select></label>}
    </div>
    {cell.presetKind === "boolean" ? <label>Boolean value<select aria-label="Boolean value" value={typeof value === "boolean" ? String(value) : ""} disabled={!editable}
      onChange={event => {
        if (event.target.value === "") { onChange({ ...cell, preset: { kind: "boolean" } }); }
        else { presetValue(event.target.value === "true"); }
      }}><option value="">Choose true or false</option><option value="true">True</option><option value="false">False</option></select></label>
      : cell.presetKind === "decimal" || cell.presetKind === "string" ? <label>{cell.presetKind === "decimal" ? "Decimal value" : "Text value"}
        <input value={typeof value === "string" ? value : ""} readOnly={!editable} spellCheck={false}
          inputMode={cell.presetKind === "decimal" ? "decimal" : "text"} onChange={event => presetValue(event.target.value)} /></label>
        : cell.presetKind === "stringList" ? <>
          <ul className="rm11-chips" aria-label="Literal membership values">{items.map((item, index) => <li key={index}>{item === "" ? "Empty string" : item}
            {editable ? <button type="button" aria-label={"Remove list item " + (index + 1)}
              onClick={() => presetValue(items.filter((_, position) => position !== index))}>×</button> : null}</li>)}</ul>
          {editable ? <div className="rm11-list-entry"><label>List item<input value={entry} onChange={event => setEntry(event.target.value)}
            onKeyDown={event => { if (event.key === "Enter") { event.preventDefault(); addChip(); } }} /></label>
            <button type="button" disabled={items.length >= ruleUiLimits.listItems} onClick={addChip}>{entry === "" ? "Add empty string" : "Add item"}</button></div> : null}
        </> : null}
    <small>Any and Missing omit the operator and value.</small>
  </>;
}
function cellKey(ruleId: string, columnId: string) { return JSON.stringify([ruleId, columnId]); }
