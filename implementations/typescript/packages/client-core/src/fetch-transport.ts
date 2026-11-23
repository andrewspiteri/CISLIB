import { ClientTransportFailure } from "./types.js";
import type { ClientTransport, TransportRequest } from "./types.js";

export function createFetchTransport(fetchImplementation: typeof fetch = globalThis.fetch): ClientTransport {
  if (typeof fetchImplementation !== "function") {
    throw new TypeError("Supply a native fetch implementation for this host.");
  }
  const nativeFetch = fetchImplementation === globalThis.fetch;
  return {
    async send(request: TransportRequest) {
      if (request.authentication.kind === "transport") {
        throw new TypeError("Portable fetch cannot use a server-only transport credential.");
      }
      const headers = new Headers(request.headers);
      if (request.authentication.kind === "bearer") {
        const token = request.authentication.token;
        if (token.length === 0 || token.length > 16384 || /[\u0000-\u0020\u007f]/u.test(token)) {
          throw new TypeError("Supply a bounded bearer credential without controls or whitespace.");
        }
        headers.set("Authorization", "Bearer " + token);
      }
      let response: Response;
      try {
        const init: RequestInit = {
          method: request.method,
          headers,
          redirect: "manual",
          credentials: "omit",
          signal: request.signal,
          cache: "no-store"
        };
        if (request.body !== undefined) {
          init.body = request.body as Uint8Array<ArrayBuffer>;
        }
        response = await fetchImplementation(request.url, init);
      } catch (error) {
        if (request.signal.aborted) {
          throw error;
        }
        if (error instanceof TypeError) {
          throw new ClientTransportFailure("transport.unavailable");
        }
        throw error;
      }
      const responseHeaders: Record<string, string> = Object.create(null) as Record<string, string>;
      response.headers.forEach((value, key) => { responseHeaders[key.toLowerCase()] = value; });
      const ownedBody = ownResponseBody(response.body, request.signal, nativeFetch);
      return {
        status: response.status,
        url: response.url || request.url,
        redirected: response.redirected || response.type === "opaqueredirect",
        headers: Object.freeze(responseHeaders),
        body: ownedBody.body,
        dispose: ownedBody.dispose
      };
    }
  };
}

function ownResponseBody(source: ReadableStream<Uint8Array> | null, signal: AbortSignal, nativeFetch: boolean): {
  body: ReadableStream<Uint8Array> | null;
  dispose(): Promise<void>;
} {
  if (source === null) {
    return { body: null, async dispose() {} };
  }
  const reader = source.getReader();
  let released = false;
  let canceling: Promise<void> | undefined;
  let cancellationRequested = false;
  let cancellationFailed = false;
  let cancellationFailure: unknown;
  let interruptedReadFailed = false;
  let interruptedReadFailure: unknown;
  let disposed: Promise<void> | undefined;
  const release = () => {
    if (!released) {
      reader.releaseLock();
      released = true;
    }
  };
  const body = new ReadableStream<Uint8Array>({
    async pull(controller) {
      let item: ReadableStreamReadResult<Uint8Array>;
      try {
        item = await reader.read();
      } catch (error) {
        const failure = classifyBodyFailure(error, signal, nativeFetch);
        if (cancellationRequested) {
          if (!isExpectedBodyFailure(error, signal, nativeFetch)) {
            interruptedReadFailed = true;
            interruptedReadFailure = failure;
          }
        } else {
          release();
          controller.error(failure);
        }
        return;
      }
      if (cancellationRequested) {
        return;
      }
      if (item.done) {
        release();
        controller.close();
      } else {
        controller.enqueue(item.value);
      }
    },
    cancel(reason) {
      cancellationRequested = true;
      canceling ??= (async () => {
        try {
          await reader.cancel(reason);
        } catch (error) {
          cancellationFailed = true;
          cancellationFailure = classifyCleanupFailure(error, signal, nativeFetch);
          throw classifyBodyFailure(error, signal, nativeFetch);
        } finally {
          try {
            release();
          } catch (error) {
            cancellationFailure = cancellationFailed
              ? new AggregateError([cancellationFailure, error], "The native response cleanup and reader release faulted.") : error;
            cancellationFailed = true;
            throw cancellationFailure;
          }
        }
      })();
      return canceling;
    }
  }, { highWaterMark: 0 });
  return {
    body,
    dispose() {
      disposed ??= (async () => {
        let failed = false;
        let failure: unknown;
        try {
          if (!body.locked) {
            await body.cancel();
          }
        } catch (error) {
          failed = true;
          // Raw native failures were classified where read/cancel actually ran.
          // Never relabel a wrapper state/release TypeError as a network failure.
          failure = error instanceof ClientTransportFailure || isAbortedBodyFailure(error, signal)
            ? new ClientTransportFailure("transport.cleanup_failed") : error;
        }
        if (canceling !== undefined) {
          // A canceled stream is already closed, even if its native cleanup rejected.
          // Join the original cancellation and report that retained fact separately.
          try { await canceling; } catch { /* Report the retained cancellation failure below. */ }
        }
        if (interruptedReadFailed) {
          if (cancellationFailed && cancellationFailure !== interruptedReadFailure) {
            throw new AggregateError([interruptedReadFailure, cancellationFailure], "The native response read and cleanup faulted.");
          }
          throw interruptedReadFailure;
        }
        if (cancellationFailed) {
          throw cancellationFailure;
        }
        if (failed) {
          throw failure;
        }
      })();
      return disposed;
    }
  };
}

function classifyBodyFailure(error: unknown, signal: AbortSignal, nativeFetch: boolean): unknown {
  if (!isExpectedBodyFailure(error, signal, nativeFetch)) {
    return error;
  }
  if (signal.aborted) {
    return new DOMException("The native response was aborted.", "AbortError");
  }
  return error instanceof ClientTransportFailure ? error : new ClientTransportFailure("transport.response_failed");
}

function classifyCleanupFailure(error: unknown, signal: AbortSignal, nativeFetch: boolean): unknown {
  return isExpectedBodyFailure(error, signal, nativeFetch)
    ? new ClientTransportFailure("transport.cleanup_failed") : error;
}

function isExpectedBodyFailure(error: unknown, signal: AbortSignal, nativeFetch: boolean): boolean {
  if (error instanceof ClientTransportFailure) {
    return true;
  }
  if (isAbortedBodyFailure(error, signal)) {
    return true;
  }
  if (error instanceof DOMException && error.name === "NetworkError") {
    return true;
  }
  if (!(error instanceof TypeError)) {
    return false;
  }
  if (nativeFetch) {
    // A native fetch body rejects network loss with TypeError, including browsers
    // that expose no native cause. Explicitly injected streams need a native marker.
    return true;
  }
  const cause = error.cause;
  const code = cause !== null && typeof cause === "object" && "code" in cause ? cause.code : undefined;
  return typeof code === "string" && /^(?:UND_ERR_|E(?:CONN|PIPE|HOST|NET|AI_|NOTFOUND|TIMEDOUT)|ERR_(?:TLS|SSL|STREAM_PREMATURE_CLOSE))/u.test(code);
}

function isAbortedBodyFailure(error: unknown, signal: AbortSignal): boolean {
  return error instanceof Error && signal.aborted && (error.name === "AbortError" || error.name === "TimeoutError");
}
