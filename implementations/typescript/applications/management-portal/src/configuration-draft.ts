import { configurationSourceBytes, defaultConfigurationWireLimits, isUtf8Text } from "@cislib/management-client";
import type { ConfigurationShape } from "@cislib/management-client";

export const configurationDraftLimits = Object.freeze({ maximumBytes: 262144, maximumDepth: 16, maximumControls: 128, maximumFormDepth: 4 });
export interface ConfigurationField {
  readonly path: readonly string[];
  readonly shape: ConfigurationShape;
  readonly present: boolean;
  readonly blocked: boolean;
  readonly rawValue?: string;
}
interface Token { readonly start: number; end: number; readonly kind: string; readonly properties: Property[]; }
interface Property { readonly name: string; readonly start: number; readonly value: Token; }

/** Only the existing Configuration lexical validator decides whether the exact bounded JSON is admissible. */
export function captureConfigurationDraft(raw: string): string {
  configurationSourceBytes(raw, defaultConfigurationWireLimits);
  return raw;
}
/** Reformat whitespace only. Every string, number, property order and other value token is copied verbatim. */
export function formatConfigurationJson(input: string): { readonly valid: true; readonly json: string } | { readonly valid: false; readonly message: string } {
  try {
    captureConfigurationDraft(input);
    const output: string[] = [];
    let index = 0, depth = 0, bytes = 0;
    const append = (text: string) => {
      bytes += configurationDraftBytes(text);
      if (bytes > configurationDraftLimits.maximumBytes) { throw new TypeError("Formatted JSON exceeds the source byte limit."); }
      output.push(text);
    };
    const whitespace = (value: string | undefined) => value !== undefined && /[\u0020\u0009\u000a\u000d]/u.test(value);
    const next = () => { let cursor = index; while (whitespace(input[cursor])) { cursor++; } return input[cursor]; };
    const newline = () => append("\n" + "  ".repeat(depth));
    while (index < input.length) {
      const character = input[index]!;
      if (whitespace(character)) { index++; continue; }
      if (character === '"') {
        const start = index++;
        while (index < input.length) {
          const current = input[index++];
          if (current === "\\") { index++; }
          else if (current === '"') { break; }
        }
        append(input.slice(start, index));
      } else if (character === "{" || character === "[") {
        append(character); index++; depth++;
        if (next() !== (character === "{" ? "}" : "]")) { newline(); }
      } else if (character === "}" || character === "]") {
        depth--; index++;
        if (output.at(-1) !== (character === "}" ? "{" : "[")) { newline(); }
        append(character);
      } else if (character === ",") { append(","); index++; newline(); }
      else if (character === ":") { append(": "); index++; }
      else {
        const start = index++;
        while (index < input.length && !whitespace(input[index]) && !/[,}\]]/u.test(input[index]!)) { index++; }
        append(input.slice(start, index));
      }
    }
    return Object.freeze({ valid: true, json: captureConfigurationDraft(output.join("")) });
  } catch {
    return Object.freeze({ valid: false, message: "Only complete valid JSON within the 256 KiB formatted source limit can be formatted. The original text is unchanged." });
  }
}
export function configurationDraftBytes(raw: string): number { return new TextEncoder().encode(raw).length; }
export function captureConfigurationReason(raw: string): string {
  if (!isUtf8Text(raw, 1024)) { throw new TypeError("Provide a nonempty reason within 1,024 UTF-8 bytes, without control characters."); }
  return raw;
}
export function configurationNumberToken(raw: string): string {
  if (raw.length > 1024 || !/^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$/u.test(raw)) {
    throw new TypeError("Enter one JSON number token. Native structural validation checks its declared numeric kind.");
  }
  return raw;
}

/** Declared scalar/group controls only. Collections and excessive forms retain the exact JSON fallback. */
export function configurationFields(shape: ConfigurationShape, raw: string): { readonly fields: readonly ConfigurationField[]; readonly fallback: boolean } {
  const root = tokens(captureConfigurationDraft(raw));
  const fields: ConfigurationField[] = [];
  let fallback = false;
  function visit(current: ConfigurationShape, path: readonly string[], depth: number, parentBlocked: boolean) {
    if (depth > configurationDraftLimits.maximumFormDepth || fields.length >= configurationDraftLimits.maximumControls) { fallback = true; return; }
    const token = find(root, path);
    const blocked = parentBlocked || ancestorBlocked(root, path);
    if (path.length > 0) {
      fields.push(Object.freeze({ path: Object.freeze([...path]), shape: current, present: token !== undefined, blocked,
        ...(token === undefined ? {} : { rawValue: raw.slice(token.start, token.end) }) }));
    }
    if (current.kind === "object") {
      for (const member of current.members ?? []) { visit(member.shape, [...path, member.name], depth + 1, blocked || token?.kind === "null"); }
    } else if (current.kind === "array" || current.kind === "dictionary") { fallback = true; }
  }
  visit(shape, [], 0, false);
  return Object.freeze({ fields: Object.freeze(fields), fallback });
}

/** Replace one value token, or insert one explicit partial-object path. Untouched bytes are copied verbatim. */
export function setConfigurationValue(raw: string, path: readonly string[], valueToken: string): string {
  const root = tokens(captureConfigurationDraft(raw));
  pathBound(path);
  configurationSourceBytes('{"value":' + valueToken + '}', defaultConfigurationWireLimits);
  let parent = root;
  for (let index = 0; index < path.length; index++) {
    if (parent.kind !== "object") { throw new TypeError("Reset or explicitly replace the containing value before editing its members."); }
    const name = path[index]!;
    const property = parent.properties.find(item => item.name === name);
    if (property === undefined) {
      let inserted = valueToken;
      for (let child = path.length - 1; child > index; child--) { inserted = "{" + JSON.stringify(path[child]) + ":" + inserted + "}"; }
      const addition = (parent.properties.length === 0 ? "" : ",") + JSON.stringify(name) + ":" + inserted;
      return captureConfigurationDraft(raw.slice(0, parent.end - 1) + addition + raw.slice(parent.end - 1));
    }
    if (index === path.length - 1) {
      return captureConfigurationDraft(raw.slice(0, property.value.start) + valueToken + raw.slice(property.value.end));
    }
    parent = property.value;
  }
  throw new TypeError("Select one declared field.");
}

/** Omission inherits through native composition; it neither copies defaults nor prunes an empty parent object. */
export function omitConfigurationValue(raw: string, path: readonly string[]): string {
  const root = tokens(captureConfigurationDraft(raw));
  pathBound(path);
  const parent = find(root, path.slice(0, -1));
  if (parent === undefined) { return raw; }
  if (parent.kind !== "object") { throw new TypeError("The containing value is not an editable object."); }
  const index = parent.properties.findIndex(item => item.name === path.at(-1));
  if (index < 0) { return raw; }
  const property = parent.properties[index]!;
  const start = index > 0 ? parent.properties[index - 1]!.value.end : property.start;
  const end = index === 0 && parent.properties.length > 1 ? parent.properties[1]!.start : property.value.end;
  return captureConfigurationDraft(raw.slice(0, start) + raw.slice(end));
}
export function configurationStringValue(rawValue: string | undefined): string {
  if (rawValue === undefined || rawValue === "null") { return ""; }
  if (!rawValue.startsWith('"')) { throw new TypeError("This value requires the exact JSON editor."); }
  const value: unknown = JSON.parse(rawValue);
  if (typeof value !== "string") { throw new TypeError("This value requires the exact JSON editor."); }
  return value;
}
function pathBound(path: readonly string[]) {
  if (path.length < 1 || path.length > configurationDraftLimits.maximumDepth || path.some(part => typeof part !== "string" || part.length > 512)) {
    throw new TypeError("Select one bounded declared field path.");
  }
}
function find(root: Token, path: readonly string[]): Token | undefined {
  let current: Token | undefined = root;
  for (const part of path) { current = current?.kind === "object" ? current.properties.find(item => item.name === part)?.value : undefined; }
  return current;
}
function ancestorBlocked(root: Token, path: readonly string[]): boolean {
  let current: Token | undefined = root;
  for (const part of path.slice(0, -1)) {
    if (current !== undefined && current.kind !== "object") { return true; }
    current = current?.properties.find(item => item.name === part)?.value;
  }
  return current !== undefined && current.kind !== "object";
}

// Span discovery over already validated JSON only. It does not interpret schema, merge layers or coerce numbers.
function tokens(raw: string): Token {
  let index = 0;
  const skip = () => { while (index < raw.length && /[\u0020\u0009\u000a\u000d]/u.test(raw[index]!)) { index++; } };
  function stringEnd() {
    index++;
    while (index < raw.length) {
      const character = raw[index++];
      if (character === "\\") { index++; }
      else if (character === '"') { return; }
    }
    throw new TypeError("Invalid validated string span.");
  }
  function value(): Token {
    skip();
    const start = index;
    const first = raw[index];
    const token: Token = { start, end: start, kind: first === "{" ? "object" : first === "[" ? "array" : first === '"' ? "string"
      : first === "n" ? "null" : first === "t" || first === "f" ? "boolean" : "number", properties: [] };
    if (first === "{") {
      index++; skip();
      while (raw[index] !== "}") {
        const propertyStart = index;
        stringEnd();
        const name = JSON.parse(raw.slice(propertyStart, index)) as string;
        skip(); index++;
        token.properties.push({ name, start: propertyStart, value: value() });
        skip();
        if (raw[index] !== ",") { break; }
        index++; skip();
      }
      index++;
    } else if (first === "[") {
      index++; skip();
      while (raw[index] !== "]") { value(); skip(); if (raw[index] !== ",") { break; } index++; skip(); }
      index++;
    } else if (first === '"') { stringEnd(); }
    else { while (index < raw.length && !/[\u0020\u0009\u000a\u000d,}\]]/u.test(raw[index]!)) { index++; } }
    token.end = index; return token;
  }
  return value();
}
