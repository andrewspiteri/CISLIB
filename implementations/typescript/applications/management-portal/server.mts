import { createServer as createHttpServer } from "node:http";
import type { RequestListener } from "node:http";
import type { Duplex } from "node:stream";
import { createServer as createHttpsServer } from "node:https";
import { fileURLToPath } from "node:url";
import { captureConfig } from "./src/server/config.ts";
import { requireNextResponseDrain } from "./src/server/next-response-drain.ts";
import { createRuntime, publishRuntime, readOwnedPem, unpublishRuntime } from "./src/server/runtime.ts";

// This is the ordinary Next custom-server entry, not an alternate runtime framework.
const config = captureConfig(process.env);
const development = process.argv.includes("--development");
const supervised = typeof process.send === "function";
Object.assign(process.env, { NODE_ENV: development ? "development" : "production" });
await requireNextResponseDrain();
const { default: next } = await import("next");
const runtime = await createRuntime(config);
const app = next({ dev: development, dir: fileURLToPath(new URL(".", import.meta.url)), hostname: config.hostname, port: config.port });
let server: ReturnType<typeof createHttpServer> | ReturnType<typeof createHttpsServer> | undefined;
let published = false;
let accepting = false;
const handlers = new Set<Promise<void>>();
interface HandlerObservation { kind: string; method: string; finished: boolean; closed: boolean; aborted: boolean; rsc: boolean; readonly status: number; readonly headers: boolean; }
const handlerObservations = new Map<Promise<void>, HandlerObservation>();
const connections = new Map<Duplex, Promise<void>>();
let stopping: Promise<void> | undefined;
let serverPrivateKey: Buffer | undefined;
let stopRequested = false;
let finishPreparation!: () => void;
const preparation = new Promise<void>(resolve => { finishPreparation = resolve; });

async function stop(): Promise<void> {
  accepting = false;
  runtime.beginStop();
  const errors: unknown[] = [];
  observeShutdown("transport");
  let listenerClosed = Promise.resolve();
  if (server?.listening) {
    listenerClosed = new Promise<void>((resolve, reject) => {
      server!.close(error => error ? reject(error) : resolve());
    }).catch(error => { errors.push(error); });
    try { server.closeIdleConnections(); } catch (error) { errors.push(error); }
  }
  // HTTP idle closure does not own connections which have not completed TLS.
  // Cancel inbound transport as well as the runtime, then retain every actual
  // close and handler completion before disposing their dependencies.
  for (const connection of connections.keys()) { connection.destroy(); }
  await listenerClosed;
  await Promise.all([...connections.values()]);
  observeShutdown("handlers");
  for (const observation of handlerObservations.values()) {
    console.log(`[Portal] Pending handler kind=${observation.kind} method=${observation.method} finished=${observation.finished} closed=${observation.closed} aborted=${observation.aborted} rsc=${observation.rsc} status=${observation.status} headers=${observation.headers}`);
  }
  const requestsClosed = await Promise.allSettled([...handlers]);
  for (const result of requestsClosed) { if (result.status === "rejected") { errors.push(result.reason); } }
  observeShutdown("next");
  try { await app.close(); } catch (error) { errors.push(error); }
  observeShutdown("runtime");
  try { await runtime.dispose(); } catch (error) { errors.push(error); }
  if (published) { unpublishRuntime(runtime); published = false; }
  serverPrivateKey?.fill(0);
  if (process.connected) { process.disconnect(); }
  if (errors.length > 0) { throw new AggregateError(errors, "Portal shutdown did not fully settle."); }
  observeShutdown("complete");
}
function observeShutdown(phase: "transport" | "handlers" | "next" | "runtime" | "complete"): void {
  console.log(`[Portal] Shutdown phase=${phase} handlers=${handlers.size} connections=${connections.size} requests=${runtime.activeRequestCount}`);
}
function ownConnection(connection: Duplex): void {
  if (connection.closed) { return; }
  const closed = new Promise<void>(resolve => {
    connection.once("close", () => { connections.delete(connection); resolve(); });
  });
  connections.set(connection, closed);
  // A connection accepted just before listener closure cannot escape custody.
  if (stopRequested) { connection.destroy(); }
}
function handlerKind(url: string | undefined): string {
  let path: string;
  try { path = new URL(url ?? "/", "http://portal.invalid").pathname; } catch { return "other"; }
  if (path === "/healthz") { return "health"; }
  for (const action of ["callback", "signin", "session", "signout", "csrf"]) {
    if (path === `/api/auth/${action}` || path.startsWith(`/api/auth/${action}/`)) { return `auth-${action}`; }
  }
  if (path.startsWith("/api/auth/")) { return "auth-other"; }
  if (path.startsWith("/api/portal/")) { return "portal-api"; }
  if (path.startsWith("/applications/") && path.includes("/configuration")) { return "configuration-page"; }
  if (path.startsWith("/applications")) { return "page"; }
  if (path.startsWith("/_next/") || path === "/favicon.ico") { return "asset"; }
  return "other";
}
function requestStop(): void {
  stopRequested = true;
  accepting = false;
  runtime.beginStop();
  if (stopping !== undefined) { return; }
  // Retain the ordinary Node stop driver while opaque library teardown awaits.
  // An unresolved Promise alone must not let Node report a successful exit.
  const keepAlive = setInterval(() => undefined, 1000);
  stopping = preparation.then(stop).finally(() => clearInterval(keepAlive));
  void stopping.catch(() => { console.error("[Portal] Shutdown did not fully settle."); process.exitCode = 1; });
}
function requireAdmission(): void {
  if (stopRequested) { throw new Error("Portal preparation was stopped by its owner."); }
}
process.once("SIGTERM", requestStop);
process.once("SIGINT", requestStop);
if (supervised) {
  process.once("disconnect", requestStop);
  if (!process.connected) { requestStop(); }
}

try {
  // Ownership and dependency preparation remain sequential.
  requireAdmission();
  await runtime.prepare();
  requireAdmission();
  publishRuntime(runtime);
  published = true;
  await app.prepare();
  requireAdmission();
  const handle = app.getRequestHandler();
  const listener: RequestListener = (request, response) => {
    response.setHeader("Cache-Control", "no-store");
    response.setHeader("X-Content-Type-Options", "nosniff");
    response.setHeader("Referrer-Policy", "same-origin");
    // The maintained sign-in form redirects to this independently configured
    // issuer. Keep form destinations bounded without blocking that real flow.
    response.setHeader("Content-Security-Policy", `base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self' ${config.authorityOrigin}`);
    if (!accepting || handlers.size >= 64) { response.writeHead(503); response.end("Portal unavailable."); return; }
    const observation: HandlerObservation = { kind: handlerKind(request.url), method: request.method !== undefined
      && ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"].includes(request.method) ? request.method : "OTHER",
      finished: response.writableFinished, closed: response.closed, aborted: request.aborted, rsc: request.headers.rsc === "1",
      get status() { return response.statusCode; }, get headers() { return response.headersSent; } };
    response.once("finish", () => { observation.finished = true; });
    response.once("close", () => { observation.closed = true; });
    request.once("aborted", () => { observation.aborted = true; });
    const timer = setTimeout(() => { response.destroy(); request.destroy(); }, 30000);
    timer.unref();
    response.once("close", () => clearTimeout(timer));
    const work = (async () => {
      try { await handle(request, response); }
      catch {
        if (!response.headersSent) { response.writeHead(500); }
        response.end("Portal request failed.");
        console.error("[Portal] Request failed.");
      }
    })();
    handlers.add(work);
    handlerObservations.set(work, observation);
    void work.then(() => { handlers.delete(work); handlerObservations.delete(work); }, () => {
      handlers.delete(work);
      handlerObservations.delete(work);
      console.error("[Portal] Request failed.");
      process.exitCode = 1;
      requestStop();
    });
  };
  if (config.serverCertificateFile === undefined) {
    server = createHttpServer(listener);
  } else {
    const certificate = await readOwnedPem(config.serverCertificateFile);
    serverPrivateKey = await readOwnedPem(config.serverKeyFile!);
    requireAdmission();
    server = createHttpsServer({ cert: certificate, key: serverPrivateKey, minVersion: "TLSv1.2" }, listener);
  }
  server.on("connection", ownConnection);
  server.headersTimeout = 10000;
  server.requestTimeout = 15000;
  server.keepAliveTimeout = 5000;
  server.maxHeadersCount = 64;
  server.maxConnections = 64;
  server.maxRequestsPerSocket = 100;
  await new Promise<void>((resolve, reject) => {
    server!.once("error", reject);
    server!.listen(config.port, config.hostname, () => { server!.removeListener("error", reject); resolve(); });
  });
  requireAdmission();
  accepting = true;
  console.log("[Portal] Ready.");
} catch {
  if (!stopRequested) { console.error("[Portal] Startup failed."); process.exitCode = 1; }
  requestStop();
} finally {
  finishPreparation();
}
if (stopping !== undefined) {
  try { await stopping; } catch { process.exitCode = 1; }
}
