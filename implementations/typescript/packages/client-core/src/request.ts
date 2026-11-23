import { ClientProtocolFailure, ClientTransportFailure } from "./types.js";
import type {
  ClientCallResult, DecodedBody, ExecuteRequestOptions, PreparedRequest, RequestObservation, TransportResponse
} from "./types.js";

class CallAborted extends Error { }

const transientStatuses = new Set([408, 429, 502, 503, 504]);

export function validateTimeout(value: number): number {
  if (!Number.isSafeInteger(value) || value < 1 || value > 300000) {
    throw new RangeError("Select a positive client timeout of at most 300000 milliseconds.");
  }
  return value;
}

export function validateReadRetries(value: number): number {
  if (!Number.isSafeInteger(value) || value < 0 || value > 2) {
    throw new RangeError("Select zero, one or two explicit read retries.");
  }
  return value;
}

export async function executeRequest<T, TProblem>(options: ExecuteRequestOptions<T, TProblem>): Promise<ClientCallResult<T, TProblem>> {
  const started = performance.now();
  const timeout = validateTimeout(options.context?.timeoutMs ?? options.timeoutMs ?? 30000);
  const retries = validateReadRetries(options.context?.readRetries ?? options.readRetries ?? 0);
  const controller = new AbortController();
  let abortKind: "canceled" | "timeout" | undefined;
  const caller = options.context?.signal;
  const cancel = () => {
    if (!controller.signal.aborted) {
      abortKind = "canceled";
      controller.abort(new DOMException("The client call was canceled.", "AbortError"));
    }
  };
  caller?.addEventListener("abort", cancel, { once: true });
  if (caller?.aborted) {
    cancel();
  }
  const timer = setTimeout(() => {
    if (!controller.signal.aborted) {
      abortKind = "timeout";
      controller.abort(new DOMException("The client deadline elapsed.", "TimeoutError"));
    }
  }, timeout);
  let attempts = 0;
  let cleanupSucceeded = true;
  let status: number | undefined;
  let method = "GET";
  let sentMutation = false;
  const finish = (kind: ClientCallResult<T, TProblem>["kind"], decoded: DecodedBody<T, TProblem> = {}, code?: string): ClientCallResult<T, TProblem> => ({
    kind,
    ...(status === undefined ? {} : { status }),
    ...decoded,
    ...(code === undefined ? {} : { code }),
    attempts,
    durationMs: Math.max(0, performance.now() - started),
    cleanupSucceeded,
    ...(options.operationKey === undefined ? {} : { operationKey: options.operationKey }),
    mutationUncertain: sentMutation && kind !== "http"
  });
  const aborted = () => {
    if (!controller.signal.aborted && performance.now() - started >= timeout) {
      abortKind = "timeout";
      controller.abort(new DOMException("The client deadline elapsed.", "TimeoutError"));
    }
    if (controller.signal.aborted) {
      throw new CallAborted();
    }
  };
  try {
    aborted();
    const prepared = options.prepare(controller.signal);
    if (!Number.isSafeInteger(prepared.maximumResponseBytes) || prepared.maximumResponseBytes < 1 || prepared.maximumResponseBytes > 33554432) {
      throw new RangeError("Select a response byte bound of at most 32 MiB.");
    }
    method = prepared.method;
    const destination = new URL(prepared.url);
    if (destination.protocol !== "https:" || destination.username !== "" || destination.password !== "") {
      throw new TypeError("The typed endpoint must use HTTPS without URI credentials.");
    }
    for (let index = 0; ; index++) {
      aborted();
      const credential = options.context?.credentials ?? options.credentials;
      const authentication = credential === undefined ? { kind: "none" as const }
        : await credential({ url: prepared.url, method: prepared.method, signal: controller.signal });
      aborted();
      let response: TransportResponse | undefined;
      let decoded: DecodedBody<T, TProblem> | undefined;
      let primary: unknown;
      let failed = false;
      const attemptStarted = performance.now();
      attempts++;
      sentMutation ||= method !== "GET";
      try {
        response = await options.transport.send({ ...prepared, authentication, signal: controller.signal });
        status = response.status;
        aborted();
        const final = new URL(response.url);
        if (response.redirected || response.status >= 300 && response.status < 400
          || final.origin !== destination.origin || final.pathname !== destination.pathname || final.search !== destination.search) {
          throw new ClientProtocolFailure("http.redirect_blocked");
        }
        if (!Number.isSafeInteger(response.status) || response.status < 100 || response.status > 599) {
          throw new ClientProtocolFailure("http.invalid_status");
        }
        const bytes = await readBody(response, prepared, controller.signal);
        aborted();
        decoded = options.decode(response, bytes);
        aborted();
      } catch (error) {
        primary = error;
        failed = true;
      } finally {
        if (response !== undefined) {
          try {
            await response.dispose();
          } catch (cleanup) {
            cleanupSucceeded = false;
            if (!(cleanup instanceof ClientTransportFailure)) {
              if (failed) {
                throw new AggregateError([primary, cleanup], "The client operation and owned cleanup faulted.");
              }
              throw cleanup;
            }
          }
        }
      }
      if (failed) {
        throw primary;
      }
      observe(options.observe, {
        operation: options.operation, method, attempt: attempts, ...(status === undefined ? {} : { status }),
        durationMs: Math.max(0, performance.now() - attemptStarted), outcome: "http"
      });
      // A decoded HTTP result survives later deadline/cancellation during real cleanup.
      const delay = response === undefined ? undefined : retryDelay(response, index);
      if (method !== "GET" || index >= retries || delay === undefined || controller.signal.aborted
        || delay >= timeout - (performance.now() - started)) {
        return finish("http", decoded);
      }
      await wait(delay, controller.signal);
    }
  } catch (error) {
    if (error instanceof CallAborted || controller.signal.aborted && isAbort(error)) {
      const kind = abortKind ?? "canceled";
      const result = finish(kind, {}, kind === "timeout" ? "request.timeout" : "request.canceled");
      observe(options.observe, { operation: options.operation, method, attempt: attempts, durationMs: result.durationMs, outcome: kind });
      return result;
    }
    if (error instanceof ClientTransportFailure || error instanceof ClientProtocolFailure) {
      const kind = error instanceof ClientTransportFailure ? "transportFailure" : "protocolFailure";
      const result = finish(kind, {}, error.code);
      observe(options.observe, { operation: options.operation, method, attempt: attempts, durationMs: result.durationMs, outcome: kind });
      return result;
    }
    observe(options.observe, { operation: options.operation, method, attempt: attempts, durationMs: performance.now() - started, outcome: "faulted" });
    throw error;
  } finally {
    clearTimeout(timer);
    caller?.removeEventListener("abort", cancel);
  }
}

async function readBody(response: TransportResponse, prepared: PreparedRequest, signal: AbortSignal): Promise<Uint8Array> {
  if (response.body === null) {
    return new Uint8Array();
  }
  const reader = response.body.getReader();
  let canceled: Promise<void> | undefined;
  const abort = () => {
    canceled ??= reader.cancel();
    // Observe promptly and join in finally, including the source's real cleanup.
    void canceled.catch(() => undefined);
  };
  signal.addEventListener("abort", abort, { once: true });
  if (signal.aborted) { abort(); }
  const maximum = response.status >= 400 ? Math.min(prepared.maximumResponseBytes, prepared.maximumErrorBytes ?? prepared.maximumResponseBytes)
    : prepared.maximumResponseBytes;
  let buffer = new Uint8Array(Math.min(maximum, 65536));
  let length = 0;
  try {
    for (;;) {
      const item = await reader.read();
      if (signal.aborted) {
        await reader.cancel();
        throw new CallAborted();
      }
      if (item.done) {
        break;
      }
      if (item.value.length > maximum - length) {
        await reader.cancel();
        throw new ClientProtocolFailure("response.too_large");
      }
      if (length + item.value.length > buffer.length) {
        const grown = new Uint8Array(Math.min(maximum, Math.max(length + item.value.length, buffer.length * 2)));
        grown.set(buffer);
        buffer = grown;
      }
      buffer.set(item.value, length);
      length += item.value.length;
    }
    return buffer.slice(0, length);
  } finally {
    signal.removeEventListener("abort", abort);
    try { if (canceled !== undefined) { await canceled; } }
    finally { reader.releaseLock(); }
  }
}

function retryDelay(response: TransportResponse, index: number): number | undefined {
  if (!transientStatuses.has(response.status)) {
    return undefined;
  }
  const header = response.headers["retry-after"];
  if (header === undefined) {
    return Math.min(5000, 200 * 2 ** index);
  }
  const seconds = /^[0-9]+$/u.test(header) ? Number(header) * 1000 : Date.parse(header) - Date.now();
  return Number.isFinite(seconds) && seconds >= 0 && seconds <= 5000 ? seconds : undefined;
}

function wait(milliseconds: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const abort = () => {
      clearTimeout(timer);
      signal.removeEventListener("abort", abort);
      reject(new CallAborted());
    };
    const timer = setTimeout(() => {
      signal.removeEventListener("abort", abort);
      resolve();
    }, milliseconds);
    signal.addEventListener("abort", abort, { once: true });
    if (signal.aborted) {
      abort();
    }
  });
}

function isAbort(error: unknown): boolean {
  return error instanceof Error && (error.name === "AbortError" || error.name === "TimeoutError");
}

function observe(observer: ((event: RequestObservation) => void) | undefined, event: RequestObservation): void {
  try {
    observer?.(Object.freeze(event));
  } catch {
    // Optional observations cannot replace operation, cancellation or fault facts.
  }
}
