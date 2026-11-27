"use client";

import { useEffect, useRef, useState } from "react";
import type { ConfigurationShape } from "@cislib/management-client";
import { configurationFields, configurationNumberToken, configurationStringValue, omitConfigurationValue, setConfigurationValue } from "../configuration-draft.ts";
import type { ConfigurationField } from "../configuration-draft.ts";

export function ConfigurationFields({ shape, raw, disabled, change, pendingChanged }: {
  shape: ConfigurationShape; raw: string; disabled: boolean; change(raw: string): void; pendingChanged(path: string, pending: boolean): void
}) {
  let form: ReturnType<typeof configurationFields>;
  try { form = configurationFields(shape, raw); }
  catch { return <p className="notice" role="status">Complete valid bounded JSON to use the declared fields. Your exact draft is retained below.</p>; }
  return <><p className="muted">Omitted fields inherit through native composition. Values shown here belong only to this source; no Common/default merge is performed in the browser.</p>
    {form.fields.map(field => <ConfigurationFieldControl key={JSON.stringify(field.path)} field={field} raw={raw} disabled={disabled} change={change} pendingChanged={pendingChanged} />)}
    {form.fallback ? <p className="notice">Collections or fields beyond the finite form limits remain available in the exact JSON editor. No duration units or unreported editor hints are inferred.</p> : null}</>;
}
function ConfigurationFieldControl({ field, raw, disabled, change, pendingChanged }: { field: ConfigurationField; raw: string; disabled: boolean; change(raw: string): void; pendingChanged(path: string, pending: boolean): void }) {
  const [error, setError] = useState<string>();
  const label = field.path.join(" / ");
  const fieldKey = JSON.stringify(field.path);
  const id = "configuration-field-" + encodeURIComponent(fieldKey);
  const blocked = disabled || field.blocked;
  function patch(token?: string) {
    try { change(token === undefined ? omitConfigurationValue(raw, field.path) : setConfigurationValue(raw, field.path, token)); setError(undefined); return true; }
    catch { setError("This field cannot be changed safely here. Check its containing value and the draft byte limit, or use exact JSON."); return false; }
  }
  let text: string | undefined;
  if (field.shape.kind === "string") { try { text = configurationStringValue(field.rawValue); } catch { /* Unsupported current token stays in JSON. */ } }
  return <section className="configuration-field" style={{ marginLeft: Math.min(field.path.length - 1, 3) * 16 }}>
    <label htmlFor={id}>{label}</label><p className="field-origin">{field.blocked ? "Containing value must first be explicitly reset or replaced."
      : !field.present ? "Not supplied by this source — inherited by native composition."
        : field.rawValue === "null" ? "Explicit null in this source." : "Explicit value in this source."}</p>
    {field.shape.kind === "object" ? <div className="configuration-field-actions"><span>Grouped fields</span>
      <button type="button" disabled={blocked || field.rawValue !== "null" && field.present} onClick={() => patch("{}")}>Use explicit object</button></div>
      : field.shape.kind === "array" || field.shape.kind === "dictionary" ? <p className="muted">Edit this collection in exact JSON.</p>
        : field.shape.kind === "boolean" || field.shape.allowedValues !== undefined ? <select id={id} disabled={blocked}
          value={field.rawValue ?? "@inherit"} onChange={event => patch(event.target.value === "@inherit" ? undefined : event.target.value)}>
          <option value="@inherit">Inherited — omitted</option>{field.shape.kind === "boolean" ? <><option value="true">True</option><option value="false">False</option></>
            : field.shape.allowedValues!.map(value => <option key={value} value={JSON.stringify(value)}>{value}</option>)}
          {field.shape.nullable ? <option value="null">Explicit null</option> : null}</select>
          : field.shape.kind === "string" && text !== undefined ? <><input id={id} disabled={blocked || field.rawValue === "null"} value={text} maxLength={65536}
            onChange={event => patch(JSON.stringify(event.target.value))} />{!field.present ? <button type="button" disabled={blocked} onClick={() => patch('""')}>Set empty string</button> : null}</>
            : field.shape.kind === "number" || field.shape.kind === "integer" ? <NumericField id={id} label={label} token={field.rawValue} disabled={blocked || field.rawValue === "null"} apply={patch} pendingChanged={pending => pendingChanged(fieldKey, pending)} />
              : <p className="notice">This token requires the exact JSON editor.</p>}
    <div className="configuration-field-actions"><button type="button" disabled={blocked || !field.present} onClick={() => patch()}>Reset override — omit</button>
      {field.shape.nullable ? <button type="button" disabled={blocked || field.rawValue === "null"} onClick={() => patch("null")}>Set explicit null</button> : null}</div>
    {error === undefined ? null : <p className="validation" role="alert">{error}</p>}
  </section>;
}
function NumericField({ id, label, token, disabled, apply, pendingChanged }: { id: string; label: string; token: string | undefined; disabled: boolean; apply(token: string): boolean; pendingChanged(pending: boolean): void }) {
  const [text, setText] = useState(token === undefined || token === "null" ? "" : token);
  const [error, setError] = useState<string>();
  const pendingCallback = useRef(pendingChanged);
  pendingCallback.current = pendingChanged;
  useEffect(() => { setText(token === undefined || token === "null" ? "" : token); setError(undefined); pendingCallback.current(false); }, [token]);
  return <><div className="configuration-number"><input id={id} type="text" inputMode="decimal" value={text} disabled={disabled} maxLength={1024}
    onChange={event => { setText(event.target.value); setError(undefined); pendingChanged(event.target.value !== (token ?? "")); }} aria-invalid={error !== undefined} />
    <button type="button" disabled={disabled || text === ""} onClick={() => {
      try { if (apply(configurationNumberToken(text))) { pendingChanged(false); } setError(undefined); }
      catch { setError("Enter a JSON number token. Its exact digits are retained; native validation checks the declared numeric kind."); }
    }}>Apply {label}</button></div>{error === undefined ? null : <p className="validation" role="alert">{error}</p>}</>;
}
