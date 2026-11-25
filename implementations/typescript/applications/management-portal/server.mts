import { createServer as createHttpServer } from "node:http";
import type { RequestListener } from "node:http";
import { createServer as createHttpsServer } from "node:https";
import { fileURLToPath } from "node:url";
import { captureConfig } from "./src/server/config.ts";
import { createRuntime, publishRuntime, readOwnedPem, unpublishRuntime } from "./src/server/runtime.ts";

// This is the ordinary Next custom-server entry, not an alternate runtime framework.
const config = captureConfig(process.env);
const development = process.argv.includes("--development");
const supervised = typeof process.send === "function";
Object.assign(process.env, { NODE_ENV: development ? "development" : "production" });
const { default: next } = await import("next");
const runtime = await createRuntime(config);
const app = next({ dev: development, dir: fileURLToPath(new URL(".", import.meta.url)), hostname: config.hostname, port: config.port });
let server: ReturnType<typeof createHttpServer> | ReturnType<typeof createHttpsServer> | undefined;
let published = false;
let accepting = false;
const handlers = new Set<Promise<void>>();
let stopping: Promise<void> | undefined;
let serverPrivateKey: Buffer | undefined;
let stopRequested = false;
let finishPreparation!: () => void;
const preparation = new Promise<void>(resolve => { finishPreparation = resolve; });

async function stop(): Promise<void> {
  accepting = false;
  runtime.beginStop();
  const errors: unknown[] = [];
  if (server?.listening) {
    try {
      await new Promise<void>((resolve, reject) => {
        server!.close(error => error ? reject(error) : resolve());
        server!.closeIdleConnections();
      });
    } catch (error) { errors.push(error); }
  }
  await Promise.all([...handlers]);
  try { await app.close(); } catch (error) { errors.push(error); }
  try { await runtime.dispose(); } catch (error) { errors.push(error); }
  if (published) { unpublishRuntime(runtime); published = false; }
  serverPrivateKey?.fill(0);
  if (process.connected) { process.disconnect(); }
  if (errors.length > 0) { throw new AggregateError(errors, "Portal shutdown did not fully settle."); }
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
    response.setHeader("Referrer-Policy", "no-referrer");
    if (!accepting || handlers.size >= 64) { response.writeHead(503); response.end("Portal unavailable."); return; }
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
    void work.finally(() => handlers.delete(work));
  };
  if (config.serverCertificateFile === undefined) {
    server = createHttpServer(listener);
  } else {
    const certificate = await readOwnedPem(config.serverCertificateFile);
    serverPrivateKey = await readOwnedPem(config.serverKeyFile!);
    requireAdmission();
    server = createHttpsServer({ cert: certificate, key: serverPrivateKey, minVersion: "TLSv1.2" }, listener);
  }
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
