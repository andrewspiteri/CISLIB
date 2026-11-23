export class ManagementWireError extends Error {
  constructor(readonly code: "wire.invalid" | "wire.too_large" | "wire.unsupported_version" = "wire.invalid") {
    super("The value does not conform to the bounded management wire contract.");
    this.name = "ManagementWireError";
  }
}

const trustedRaw = Symbol("validated opaque JSON");

/** Opaque JSON object text; numbers and original strings are never coerced through JavaScript number. */
export class OpaqueJsonObject {
  readonly rawJson: string;
  constructor(rawJson: string, trusted?: symbol) {
    if (rawJson.length > 32 * 1024 * 1024) {
      throw new ManagementWireError("wire.too_large");
    }
    if (trusted !== trustedRaw) {
      const parsed = parseWireJson(rawJson, 32);
      if (typeof parsed.value !== "object" || parsed.value === null || Array.isArray(parsed.value)) {
        throw new ManagementWireError();
      }
    }
    this.rawJson = rawJson;
    Object.freeze(this);
  }
}

export interface ParsedJson {
  readonly value: unknown;
  readonly objects: ReadonlyMap<string, string>;
  readonly nonIntegerNumbers: ReadonlySet<string>;
}

export function validUnicode(text: string): boolean {
  for (let index = 0; index < text.length; index++) {
    const code = text.charCodeAt(index);
    if (code >= 0xd800 && code <= 0xdbff) {
      const next = text.charCodeAt(++index);
      if (!(next >= 0xdc00 && next <= 0xdfff)) {
        return false;
      }
    } else if (code >= 0xdc00 && code <= 0xdfff) {
      return false;
    }
  }
  return true;
}

// This is only the protocol's lexical duplicate/Unicode/depth pass. Native
// JSON.parse supplies JSON grammar; the fixed DTO validators supply semantics.
export function parseWireJson(text: string, maximumDepth: number): ParsedJson {
  if (!validUnicode(text)) {
    throw new ManagementWireError();
  }
  let index = 0;
  const objects = new Map<string, string>();
  const nonIntegerNumbers = new Set<string>();
  const skip = () => { while (index < text.length && /[\u0020\u0009\u000a\u000d]/u.test(text[index]!)) { index++; } };
  const string = (): string => {
    const start = index++;
    while (index < text.length) {
      const character = text[index++];
      if (character === "\\") {
        index++;
      } else if (character === '"') {
        let decoded: unknown;
        try { decoded = JSON.parse(text.slice(start, index)); } catch { throw new ManagementWireError(); }
        if (typeof decoded !== "string" || !validUnicode(decoded)) {
          throw new ManagementWireError();
        }
        return decoded;
      }
    }
    throw new ManagementWireError();
  };
  const value = (depth: number, path: string): void => {
    skip();
    const start = index;
    const character = text[index];
    if (character === "{" || character === "[") {
      if (depth > maximumDepth) {
        throw new ManagementWireError();
      }
      index++;
      skip();
      const close = character === "{" ? "}" : "]";
      const keys = new Set<string>();
      let item = 0;
      if (text[index] !== close) {
        for (;;) {
          skip();
          let segment = String(item++);
          if (character === "{") {
            if (text[index] !== '"') {
              throw new ManagementWireError();
            }
            segment = string();
            if (keys.has(segment)) {
              throw new ManagementWireError();
            }
            keys.add(segment);
            skip();
            if (text[index++] !== ":") {
              throw new ManagementWireError();
            }
          }
          value(depth + 1, path + "/" + segment.replaceAll("~", "~0").replaceAll("/", "~1"));
          skip();
          if (text[index] === close) {
            break;
          }
          if (text[index++] !== ",") {
            throw new ManagementWireError();
          }
        }
      }
      index++;
      if (character === "{" && path.endsWith("/details")) {
        objects.set(path, text.slice(start, index));
      }
    } else if (character === '"') {
      string();
    } else {
      while (index < text.length && !/[\u0020\u0009\u000a\u000d,}\]]/u.test(text[index]!)) {
        index++;
      }
      if (start === index) {
        throw new ManagementWireError();
      }
      if ((character === "-" || character !== undefined && character >= "0" && character <= "9")
        && /[.eE]/u.test(text.slice(start, index)) && integerTokenPath(path)) {
        nonIntegerNumbers.add(path);
      }
    }
  };
  value(1, "");
  skip();
  if (index !== text.length) {
    throw new ManagementWireError();
  }
  try {
    return { value: JSON.parse(text) as unknown, objects, nonIntegerNumbers };
  } catch {
    throw new ManagementWireError();
  }
}

// Capture only fields that a declared DTO can use as a bounded JSON integer.
// Unknown numbers, including opaque module data, are not reinterpreted.
function integerTokenPath(path: string): boolean {
  return /\/(?:protocolMajor|commandVersion|status|version)$/u.test(path) || /\/protocolMajors\/[0-9]+$/u.test(path);
}

export function attachOpaqueObjects(value: unknown, path: string, objects: ReadonlyMap<string, string>): void {
  if (value !== null && typeof value === "object" && !Array.isArray(value)) {
    const object = value as Record<string, unknown>;
    for (const key of Object.keys(object)) {
      const next = path + "/" + key.replaceAll("~", "~0").replaceAll("/", "~1");
      const raw = objects.get(next);
      if (key === "details" && raw !== undefined) {
        object[key] = new OpaqueJsonObject(raw, trustedRaw);
      } else {
        attachOpaqueObjects(object[key], next, objects);
      }
    }
  } else if (Array.isArray(value)) {
    value.forEach((item, index) => attachOpaqueObjects(item, path + "/" + index, objects));
  }
}

export function stringifyWire(value: unknown, maximumDepth: number, maximumBytes: number): string {
  const chunks: string[] = [];
  const encoder = new TextEncoder();
  let remaining = maximumBytes;
  const append = (text: string) => {
    if (text.length > remaining) { throw new ManagementWireError("wire.too_large"); }
    remaining -= encoder.encode(text).length;
    if (remaining < 0) { throw new ManagementWireError("wire.too_large"); }
    chunks.push(text);
  };
  const write = (item: unknown, depth: number): void => {
    if (item instanceof OpaqueJsonObject) {
      if (item.rawJson.length > remaining) { throw new ManagementWireError("wire.too_large"); }
      parseWireJson(item.rawJson, maximumDepth - depth + 1);
      append(item.rawJson); return;
    }
    if (typeof item === "string") {
      if (!validUnicode(item)) { throw new ManagementWireError(); }
      if (item.length > remaining) { throw new ManagementWireError("wire.too_large"); }
      append(JSON.stringify(item)); return;
    }
    if (item === null || typeof item === "boolean") { append(JSON.stringify(item)); return; }
    if (typeof item === "number") {
      if (!Number.isFinite(item)) { throw new ManagementWireError(); }
      append(JSON.stringify(item)); return;
    }
    if (typeof item !== "object" || depth > maximumDepth) { throw new ManagementWireError(); }
    if (Array.isArray(item)) {
      append("[");
      for (let index = 0; index < item.length; index++) {
        if (index !== 0) { append(","); }
        write(item[index], depth + 1);
      }
      append("]"); return;
    }
    const prototype: unknown = Object.getPrototypeOf(item);
    if (prototype !== Object.prototype && prototype !== null) { throw new ManagementWireError(); }
    append("{");
    let first = true;
    for (const key in item) {
      if (!Object.hasOwn(item, key)) { continue; }
      const property = (item as Record<string, unknown>)[key];
      if (!validUnicode(key) || property === undefined) { throw new ManagementWireError(); }
      if (!first) { append(","); }
      first = false;
      append(JSON.stringify(key)); append(":"); write(property, depth + 1);
    }
    append("}");
  };
  write(value, 1);
  return chunks.join("");
}

export function freezeWire<T>(value: T): T {
  if (value !== null && typeof value === "object" && !Object.isFrozen(value)) {
    Object.values(value).forEach(freezeWire);
    Object.freeze(value);
  }
  return value;
}
