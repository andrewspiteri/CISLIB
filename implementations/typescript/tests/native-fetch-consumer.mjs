import assert from "node:assert/strict";
import https from "node:https";
import { readFile } from "node:fs/promises";
import { join } from "node:path";
import { ManagementClient } from "@cislib/management-client";

const directory = process.argv[2];
assert.ok(directory, "The parent supplies a newly owned private TLS directory.");
const server = https.createServer({
  cert: await readFile(join(directory, "server.pem")),
  key: await readFile(join(directory, "server.key")),
  minVersion: "TLSv1.2"
});
const sockets = new Set();
let intentionalResetAllowance = 0;
let observedIntentionalResets = 0;
let tlsFailure;
server.on("connection", socket => {
  sockets.add(socket);
  socket.once("close", () => { sockets.delete(socket); });
});
server.on("tlsClientError", error => {
  // This listener belongs only to this loopback child. After actual HTTPS headers,
  // each deliberate abort may also reset one native-fetch preconnect handshake.
  if (error.code === "ECONNRESET" && intentionalResetAllowance > 0) {
    intentionalResetAllowance--;
    observedIntentionalResets++;
  } else {
    tlsFailure ??= error;
  }
});
let current;
server.on("request", (request, response) => {
  const state = current;
  void receive(request, response, state).catch(error => {
    state.failure = error;
    response.destroy();
  });
});
await new Promise((resolve, reject) => {
  server.once("error", reject);
  server.listen(0, "127.0.0.1", resolve);
});

const nativeFetch = globalThis.fetch;
// Observe headers without replacing the native response, stream or abort behavior.
// ManagementClient still selects its default fetch transport with no custom adapter.
globalThis.fetch = async (...args) => {
  const response = await nativeFetch(...args);
  current.headersReceived.resolve();
  return response;
};
const applicationId = "11111111-1111-4111-8111-111111111111";
const baseUrl = `https://127.0.0.1:${server.address().port}/`;
const results = [];
let runFailed = false;
let runFailure;
try {
  for (const method of ["GET", "PUT"]) {
    for (const failure of ["disconnect", "timeout", "canceled"]) {
      results.push(await run(method, failure));
    }
  }
} catch (error) {
  runFailed = true;
  runFailure = error;
} finally {
  globalThis.fetch = nativeFetch;
  const closed = new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  const socketCloses = [...sockets].map(socket => new Promise(resolve => socket.once("close", resolve)));
  server.closeAllConnections();
  await Promise.all([closed, ...socketCloses]);
}
assert.equal(sockets.size, 0);
if (tlsFailure !== undefined) {
  if (runFailed) { throw new AggregateError([runFailure, tlsFailure], "The native call and TLS listener faulted."); }
  throw tlsFailure;
}
if (runFailed) { throw runFailure; }
assert.ok(observedIntentionalResets <= 6);
process.stdout.write(JSON.stringify({ cases: results, socketsDrained: true, nativeFetch: true, observedIntentionalResets }));

async function run(method, failure) {
  const state = {
    method, failure, count: 0, headersReceived: deferred(), responseClosed: deferred(), response: undefined,
    operationKey: method === "PUT" ? "native-original-" + failure : undefined
  };
  current = state;
  const controller = new AbortController();
  const client = new ManagementClient({ baseUrl, timeoutMs: 1000, readRetries: 2 });
  const call = method === "PUT"
    ? client.replaceMetadata(applicationId, state.operationKey, '"metadata-1"', { commandVersion: 1, displayName: "Native body failure" }, { signal: controller.signal })
    : client.getServiceInfo({ signal: controller.signal });
  // Observe faults immediately while the test waits for actual native response headers.
  void call.catch(() => undefined);
  try {
    await Promise.race([
      state.headersReceived.promise,
      call.then(() => { throw new Error("The call finished before native response headers."); })
    ]);
    assert.ok(state.response);
    intentionalResetAllowance++;
    if (failure === "disconnect") {
      state.response.destroy();
    } else if (failure === "canceled") {
      controller.abort();
    }
    const result = await call;
    assert.equal(result.kind, failure === "disconnect" ? "transportFailure" : failure);
    assert.equal(result.status, 200, "Actual response headers arrived before the response-body failure.");
    assert.equal(result.attempts, 1);
    assert.equal(result.operationKey, state.operationKey);
    assert.equal(result.mutationUncertain, method === "PUT");
    assert.equal(typeof result.cleanupSucceeded, "boolean");
    await state.responseClosed.promise;
    if (state.failure instanceof Error) { throw state.failure; }
    assert.equal(state.count, 1, "A response-body failure cannot resubmit the request.");
    return { method, failure, kind: result.kind, originalKeyReceived: method !== "PUT" || state.originalKeyReceived,
      headersReceived: true, attempts: result.attempts, mutationUncertain: result.mutationUncertain,
      cleanupSucceeded: result.cleanupSucceeded, responseDrained: true };
  } finally {
    controller.abort();
    state.response?.destroy();
    await call.catch(() => undefined);
  }
}

async function receive(request, response, state) {
  assert.ok(state, "Only an owned active call can reach this listener.");
  state.count++;
  response.once("close", () => { state.responseClosed.resolve(); });
  assert.equal(request.method, state.method);
  assert.equal(request.url, state.method === "PUT"
    ? `/management/v1/applications/${applicationId}/metadata` : "/management/v1/service-info");
  let bytes = 0;
  const chunks = [];
  for await (const chunk of request) {
    bytes += chunk.length;
    assert.ok(bytes <= 4096);
    chunks.push(chunk);
  }
  if (state.method === "PUT") {
    assert.equal(request.headers["idempotency-key"], state.operationKey);
    assert.equal(request.headers["if-match"], '"metadata-1"');
    assert.deepEqual(JSON.parse(Buffer.concat(chunks).toString("utf8")), { commandVersion: 1, displayName: "Native body failure" });
    state.originalKeyReceived = true;
  }
  state.response = response;
  response.writeHead(200, { "content-type": "application/json", "content-length": "1000", connection: "close" });
  response.flushHeaders();
  response.write('{"incomplete":');
}

function deferred() {
  let resolve;
  const promise = new Promise(complete => { resolve = complete; });
  return { promise, resolve };
}
