import { ClientProtocolFailure, ClientTransportFailure } from "./types.js";
import type { ClientTransport, TransportRequest } from "./types.js";

export function createFetchTransport(fetchImplementation: typeof fetch = globalThis.fetch): ClientTransport {
  if (typeof fetchImplementation !== "function") {
    throw new TypeError("Supply a native fetch implementation for this host.");
  }
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
      if (response.type === "opaqueredirect") {
        await response.body?.cancel();
        throw new ClientProtocolFailure("http.redirect_blocked");
      }
      const responseHeaders: Record<string, string> = Object.create(null) as Record<string, string>;
      response.headers.forEach((value, key) => { responseHeaders[key.toLowerCase()] = value; });
      return {
        status: response.status,
        url: response.url || request.url,
        redirected: response.redirected,
        headers: Object.freeze(responseHeaders),
        body: response.body,
        async dispose() {
          if (response.body !== null && !response.body.locked) {
            await response.body.cancel();
          }
        }
      };
    }
  };
}
