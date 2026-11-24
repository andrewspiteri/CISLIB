import https from "node:https";
import { Readable } from "node:stream";
import { ClientProtocolFailure, ClientTransportFailure } from "@cislib/client-core";
import { ManagementClient, ManagementConfigurationClient, captureBaseUrl } from "../dist/index.js";

/** Node server only. Trust is host configuration; a client key is attached per call. */
export function createNodeHttpsTransport(options) {
  const approved = new URL(captureBaseUrl(options.origin));
  if (approved.pathname !== "/") { throw new TypeError("Select an HTTPS origin for the Node transport."); }
  const ca = options.ca === undefined ? undefined : copyMaterial(options.ca, "public CA");
  return Object.freeze({
    async send(request) {
      const url = new URL(request.url);
      if (url.protocol !== "https:" || url.origin !== approved.origin || url.username || url.password) {
        throw new ClientProtocolFailure("transport.origin_mismatch");
      }
      const headers = { ...request.headers };
      for (const name of Object.keys(headers)) {
        if (name.toLowerCase() === "authorization") { delete headers[name]; }
      }
      let cert;
      let key;
      switch (request.authentication.kind) {
        case "none": break;
        case "bearer": {
          const token = request.authentication.token;
          if (typeof token !== "string" || token.length < 1 || token.length > 16384 || /[\u0000-\u0020\u007f]/u.test(token)) {
            throw new TypeError("Supply one bounded bearer token through the per-call provider.");
          }
          headers.Authorization = "Bearer " + token;
          break;
        }
        case "transport": {
          const material = request.authentication.value;
          if (material === null || typeof material !== "object" || material.kind !== "mtls") {
            throw new TypeError("The selected Node transport accepts only explicit per-call mTLS material.");
          }
          cert = copyMaterial(material.certificate, "client certificate");
          key = copyMaterial(material.privateKey, "client private key");
          break;
        }
        default: throw new TypeError("Select an explicit supported per-call credential.");
      }
      return await nativeExchange(url, request, headers, { ca, cert, key });
    }
  });
}

/** Server-only environment fallback; an explicit endpoint always wins. */
export function createNodeManagementClient(options) {
  const baseUrl = options.baseUrl ?? process.env.CIS_MANAGEMENT_URL;
  if (baseUrl === undefined) { throw new TypeError("Configure CIS_MANAGEMENT_URL or an explicit management endpoint."); }
  const captured = captureBaseUrl(baseUrl);
  return new ManagementClient({ ...options, baseUrl: captured, transport: options.transport ?? createNodeHttpsTransport({
    origin: new URL(captured).origin, ...(options.ca === undefined ? {} : { ca: options.ca })
  }) });
}

/** The same explicit native HTTPS profile for the separate configuration port. */
export function createNodeManagementConfigurationClient(options) {
  const baseUrl = options.baseUrl ?? process.env.CIS_MANAGEMENT_URL;
  if (baseUrl === undefined) { throw new TypeError("Configure CIS_MANAGEMENT_URL or an explicit management endpoint."); }
  const captured = captureBaseUrl(baseUrl);
  return new ManagementConfigurationClient({ ...options, baseUrl: captured, transport: options.transport ?? createNodeHttpsTransport({
    origin: new URL(captured).origin, ...(options.ca === undefined ? {} : { ca: options.ca })
  }) });
}

function copyMaterial(value, label) {
  if (typeof value === "string" && value.length > 0 && value.length <= 1048576) { return value; }
  if (value instanceof Uint8Array && value.byteLength > 0 && value.byteLength <= 1048576) { return Buffer.from(value); }
  throw new TypeError("Supply bounded " + label + " material from the host's explicit credential configuration.");
}

async function nativeExchange(url, request, headers, tls) {
  let native;
  let response;
  let socketClosed = Promise.resolve();
  let requestClosed = Promise.resolve();
  let closeResponse = Promise.resolve();
  let abort;
  try {
    const ready = new Promise((resolve, reject) => {
      native = https.request(url, {
        method: request.method, headers, agent: false, rejectUnauthorized: true, minVersion: "TLSv1.2",
        ...(tls.ca === undefined ? {} : { ca: tls.ca }), ...(tls.cert === undefined ? {} : { cert: tls.cert }),
        ...(tls.key === undefined ? {} : { key: tls.key })
      });
      requestClosed = closed(native);
      native.once("socket", socket => { socketClosed = closed(socket); });
      native.once("response", value => {
        response = value;
        closeResponse = closed(value);
        // Readable.toWeb forwards native stream errors; retain an error handler through drain.
        value.on("error", () => undefined);
        resolve(value);
      });
      native.on("error", error => reject(classify(error, request.signal)));
      abort = () => native.destroy(abortError());
      request.signal.addEventListener("abort", abort, { once: true });
      if (request.signal.aborted) { abort(); }
      else { native.end(request.body); }
    });
    const incoming = await ready;
    const responseHeaders = {};
    for (const [name, value] of Object.entries(incoming.headers)) {
      if (value !== undefined) { responseHeaders[name.toLowerCase()] = Array.isArray(value) ? value.join(", ") : value; }
    }
    let disposed;
    return {
      status: incoming.statusCode ?? 0, url: url.href, redirected: false,
      headers: Object.freeze(responseHeaders), body: classifiedBody(incoming, request.signal),
      dispose() {
        disposed ??= (async () => {
          incoming.destroy();
          native.destroy();
          await Promise.all([closeResponse, requestClosed, socketClosed]);
          request.signal.removeEventListener("abort", abort);
          clearPrivate(tls);
        })();
        return disposed;
      }
    };
  } catch (error) {
    response?.destroy();
    native?.destroy();
    await Promise.all([closeResponse, requestClosed, socketClosed]);
    if (abort !== undefined) { request.signal.removeEventListener("abort", abort); }
    clearPrivate(tls);
    throw classify(error, request.signal);
  }
}

function classifiedBody(incoming, signal) {
  const reader = Readable.toWeb(incoming).getReader();
  return new ReadableStream({
    async pull(controller) {
      try {
        const item = await reader.read();
        if (item.done) { controller.close(); reader.releaseLock(); }
        else { controller.enqueue(item.value); }
      } catch (error) { controller.error(classify(error, signal)); }
    },
    async cancel(reason) { await reader.cancel(reason); reader.releaseLock(); }
  });
}

function closed(stream) {
  if (stream.closed) { return Promise.resolve(); }
  return new Promise(resolve => stream.once("close", resolve));
}
function clearPrivate(tls) { if (Buffer.isBuffer(tls.key)) { tls.key.fill(0); } }
function abortError() { return new DOMException("The client request was aborted.", "AbortError"); }
function classify(error, signal) {
  if (signal.aborted && error instanceof Error && (error.name === "AbortError" || error.code === "ABORT_ERR")) { return abortError(); }
  if (error instanceof ClientProtocolFailure || error instanceof ClientTransportFailure) { return error; }
  const code = error !== null && typeof error === "object" ? error.code : undefined;
  if (typeof code === "string" && (/^(?:E(?:CONN|PIPE|HOST|NET|AI_|NOTFOUND|TIMEDOUT)|ERR_(?:TLS|SSL|OSSL|STREAM|HTTP)|CERT_|DEPTH_|SELF_|UNABLE_|INVALID_)/u.test(code))) {
    return new ClientTransportFailure("transport.exchange_failed");
  }
  return error;
}
