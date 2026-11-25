// Named native ownership proof; the existing C# TLS fixture supplies newly owned PEM files.
import assert from "node:assert/strict";
import https from "node:https";
import { readFile } from "node:fs/promises";
import { join } from "node:path";
import { OidcHttpAgent } from "../src/server/oidc-http.ts";

const directory = process.argv[2];
assert.ok(directory);
const ca = await readFile(join(directory, "root.pem"));
const sockets = new Set();
const timers = new Set();
const server = https.createServer({ cert: await readFile(join(directory, "server.pem")), key: await readFile(join(directory, "server.key")), minVersion: "TLSv1.2" });
server.on("connection", socket => { sockets.add(socket); socket.once("close", () => sockets.delete(socket)); });
const states = new Map();
server.on("request", (request, response) => {
  const state = states.get(request.url);
  assert.ok(state);
  state.closed = new Promise(resolve => response.once("close", () => { state.responseClosed = true; resolve(); }));
  if (state.kind === "headers") { return; }
  response.writeHead(200, { "Content-Type": "application/json" });
  response.flushHeaders();
  if (state.kind === "complete") { response.end('{"complete":true}'); return; }
  if (state.kind === "oversize") { response.end("x".repeat(4096)); return; }
  response.write('{"pending":"');
  const timer = setInterval(() => response.write("x"), 25);
  timers.add(timer);
  response.once("close", () => { clearInterval(timer); timers.delete(timer); });
});
await new Promise((resolve, reject) => { server.once("error", reject); server.listen(0, "127.0.0.1", resolve); });
const origin = `https://127.0.0.1:${server.address().port}`;
const cases = [];
try {
  for (const kind of ["complete", "headers", "body", "oversize", "stop"]) {
    const state = { kind, responseClosed: false, headersReceived: false };
    states.set("/" + kind, state);
    const agent = new OidcHttpAgent(1000, kind === "oversize" ? 1024 : 1048576);
    const started = performance.now();
    const operation = exchange("/" + kind, agent, state);
    if (kind === "stop") {
      await state.headers;
      await agent.dispose();
    }
    const result = await operation;
    await agent.dispose();
    await state.closed;
    if (kind === "complete") { assert.equal(result.body, '{"complete":true}'); }
    else {
      assert.ok(result.error);
      if (kind === "stop") { assert.ok(["OIDC_HTTP_STOPPING", "ECONNRESET"].includes(result.error.code)); }
      else { assert.equal(result.error.code, kind === "oversize" ? "OIDC_HTTP_RESPONSE_LIMIT" : "OIDC_HTTP_DEADLINE"); }
      assert.ok(performance.now() - started < 10000);
    }
    assert.equal(state.responseClosed, true);
    if (kind === "body" || kind === "stop") { assert.equal(state.headersReceived, true); }
    cases.push({ kind, responseClosed: state.responseClosed, headersReceived: state.headersReceived });
    if (kind === "stop") {
      await assert.rejects(async () => { https.get(origin + "/complete", { agent, ca }); }, { code: "OIDC_HTTP_STOPPING" });
    }
  }
  const saturated = new OidcHttpAgent(5000, 1048576);
  const held = [];
  try {
    for (let index = 0; index < 8; index++) {
      const state = { kind: "held", responseClosed: false, headersReceived: false };
      states.set("/held-" + index, state);
      held.push({ state, operation: exchange("/held-" + index, saturated, state) });
    }
    await Promise.all(held.map(item => item.state.headers));
    assert.throws(() => https.get(origin + "/complete", { agent: saturated, ca }), { code: "OIDC_HTTP_BUSY" });
    await saturated.dispose();
    for (const item of held) {
      assert.ok((await item.operation).error);
      await item.state.closed;
      assert.equal(item.state.responseClosed, true);
    }
    cases.push({ kind: "saturation", responseClosed: true, headersReceived: true, admitted: 8, rejected: 1 });
  } finally { await saturated.dispose(); }
} finally {
  for (const timer of timers) { clearInterval(timer); }
  const closed = new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  const drains = [...sockets].map(socket => new Promise(resolve => socket.once("close", resolve)));
  server.closeAllConnections();
  await Promise.all([closed, ...drains]);
}
assert.equal(sockets.size, 0);
process.stdout.write(JSON.stringify({ cases, socketsDrained: true, nativeHttps: true }));

function exchange(path, agent, state) {
  let headersResolve;
  let headersReject;
  state.headers = new Promise((resolve, reject) => { headersResolve = resolve; headersReject = reject; });
  void state.headers.catch(() => undefined);
  return new Promise(resolve => {
    let body = "";
    let finished = false;
    const finish = result => { if (!finished) { finished = true; resolve(result); } };
    const request = https.get(origin + path, { agent, ca }, response => {
      state.headersReceived = true;
      headersResolve();
      response.on("data", part => { body += part.toString(); });
      response.on("error", error => finish({ error }));
      response.on("end", () => finish({ body }));
    });
    request.on("error", error => { if (!state.headersReceived) { headersReject(error); } finish({ error }); });
  });
}
