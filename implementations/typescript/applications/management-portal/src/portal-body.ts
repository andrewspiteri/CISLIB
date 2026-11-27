import { OpaqueJsonObject } from "@cislib/management-client";

export class PortalRequestInvalid extends Error {
  readonly status: number;
  constructor(message = "The request is invalid.", status = 400) {
    super(message);
    this.name = "PortalRequestInvalid";
    this.status = status;
  }
}

/** Return exact text so the selected native command codec can reject noncanonical numeric tokens. */
export async function readPortalJson(request: Request, signal: AbortSignal, maximumBytes = 65536): Promise<string> {
  if (!Number.isSafeInteger(maximumBytes) || maximumBytes < 1 || maximumBytes > 1048576) { throw new RangeError("Select a bounded portal body limit."); }
  if (request.body === null) { throw new PortalRequestInvalid("Supply a JSON object body."); }
  if (request.headers.get("content-type")?.split(";", 1)[0]?.trim().toLowerCase() !== "application/json") {
    await rejectBody(request.body, new PortalRequestInvalid("Supply a JSON object body."));
  }
  const length = request.headers.get("content-length");
  if (length !== null && (!/^(0|[1-9][0-9]*)$/u.test(length) || BigInt(length) > BigInt(maximumBytes))) {
    await rejectBody(request.body, new PortalRequestInvalid("The request body is too large or invalid.", 413));
  }
  const reader = request.body.getReader();
  let cancellation: Promise<void> | undefined;
  let primary: unknown;
  const abort = () => { cancellation ??= reader.cancel(signal.reason); };
  signal.addEventListener("abort", abort, { once: true });
  const chunks: Uint8Array[] = [];
  let count = 0;
  try {
    if (signal.aborted) { abort(); signal.throwIfAborted(); }
    for (;;) {
      const item = await reader.read();
      signal.throwIfAborted();
      if (item.done) { break; }
      count += item.value.byteLength;
      if (count > maximumBytes) { cancellation ??= reader.cancel(); throw new PortalRequestInvalid("The request body is too large.", 413); }
      chunks.push(item.value);
    }
    const bytes = new Uint8Array(count);
    let offset = 0;
    for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
    try {
      const text = new TextDecoder("utf-8", { fatal: true, ignoreBOM: true }).decode(bytes);
      new OpaqueJsonObject(text);
      return text;
    } catch { throw new PortalRequestInvalid("Supply one valid, bounded JSON object without duplicate fields."); }
  } catch (error) { primary = error; throw error; }
  finally {
    signal.removeEventListener("abort", abort);
    try { await cancellation; }
    catch (cleanup) { if (primary !== undefined) { throw new AggregateError([primary, cleanup], "The request body and its cleanup failed."); } throw cleanup; }
    finally { reader.releaseLock(); }
  }
}

async function rejectBody(body: ReadableStream<Uint8Array>, primary: PortalRequestInvalid): Promise<never> {
  try { await body.cancel(); }
  catch (cleanup) { throw new AggregateError([primary, cleanup], "The rejected request body cleanup failed."); }
  throw primary;
}

