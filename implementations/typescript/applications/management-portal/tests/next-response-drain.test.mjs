import assert from "node:assert/strict";
import http from "node:http";
import { createRequire } from "node:module";
import test from "node:test";

const require = createRequire(import.meta.url);
const { pipeToNodeResponse } = require("next/dist/server/pipe-readable.js");
const payload = Buffer.from('{"large":9007199254740993,"unicode":"日本語 🌍","enabled":false}', "utf8");

test("the installed Next response pipe joins an ordinary finished HTTP response with exact bytes", { timeout: 5000 }, async () => {
  const probe = await nativeResponseProbe(false);
  const outcome = await probe.pipe;
  if ("error" in outcome) { throw outcome.error; }
  assert.equal(probe.finished, true);
  assert.equal(probe.endIntercepted, false);
  assert.equal(probe.clientError, undefined);
  assert.deepEqual(probe.received, payload);
});

test("the installed Next response pipe settles after actual close during its in-flight end without a finish event", { timeout: 5000 }, async () => {
  const probe = await nativeResponseProbe(true);
  // On the uncorrected pinned module, all native owners above are already
  // closed but this actual pipe promise remains pending. Retain that failed
  // test outcome. Its test deadline can only fail, never stand in for a join.
  const outcome = await probe.pipe;
  if ("error" in outcome) { throw outcome.error; }
  assert.equal(probe.endIntercepted, true, "The real WritableStream close must reach the response's public end method.");
  assert.equal(probe.finished, false, "A closed transport is not a fabricated successful finish event.");
  assert.equal(probe.responseClosed, true);
  assert.equal(probe.nativeJoined, true);
});

async function nativeResponseProbe(destroyBeforeEnd) {
  const handlerEntered = deferred();
  const accepted = new Map();
  const serverCloses = [];
  const chunks = [];
  let request;
  let requestClosed = Promise.resolve();
  let clientSocketClosed = Promise.resolve();
  let incomingClosed = Promise.resolve();
  let responseClosed = Promise.resolve();
  let pipe;
  let finished = false;
  let responseDidClose = false;
  let endIntercepted = false;
  let clientError;
  let nativeJoined = false;
  let serverClose;
  const server = http.createServer((_request, response) => {
    response.once("finish", () => { finished = true; });
    responseClosed = new Promise(resolve => response.once("close", () => { responseDidClose = true; resolve(); }));
    if (destroyBeforeEnd) {
      const end = response.end;
      response.end = function (...args) {
        endIntercepted = true;
        this.destroy();
        return end.apply(this, args);
      };
    }
    const body = new ReadableStream({
      start(controller) { controller.enqueue(payload); controller.close(); }
    });
    pipe = pipeToNodeResponse(body, response).then(value => ({ value }), error => ({ error }));
    handlerEntered.resolve();
  });
  server.on("connection", socket => {
    const closed = new Promise(resolve => socket.once("close", () => { accepted.delete(socket); resolve(); }));
    accepted.set(socket, closed);
    serverCloses.push(closed);
  });
  function closeServer() {
    serverClose ??= server.listening ? new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve())) : Promise.resolve();
    return serverClose;
  }
  await new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(0, "127.0.0.1", () => { server.off("error", reject); resolve(); });
  });
  try {
    const address = server.address();
    assert.ok(address !== null && typeof address !== "string");
    request = http.request({ hostname: "127.0.0.1", port: address.port, method: "GET", agent: false,
      headers: { Connection: "close" } });
    requestClosed = new Promise(resolve => request.once("close", resolve));
    request.once("socket", socket => { clientSocketClosed = new Promise(resolve => socket.once("close", resolve)); });
    request.once("response", response => {
      incomingClosed = new Promise(resolve => response.once("close", resolve));
      response.on("data", chunk => chunks.push(Buffer.from(chunk)));
      response.on("error", error => { clientError ??= error; });
    });
    request.on("error", error => { clientError ??= error; });
    request.end();
    await handlerEntered.promise;
    await responseClosed;
    await requestClosed;
    await Promise.all([clientSocketClosed, incomingClosed]);
    await closeServer();
    await Promise.all(serverCloses);
    assert.equal(accepted.size, 0);
    nativeJoined = true;
    // Fixed booleans only: evidence of actual native closure is retained even
    // when the old framework's remaining promise makes this test cancel.
    process.stdout.write("[NextResponseDrain] " + JSON.stringify({ destroyBeforeEnd, endIntercepted,
      responseClosed: responseDidClose, finished, nativeJoined }) + "\n");
    return { pipe, finished, endIntercepted, responseClosed: responseDidClose, nativeJoined,
      clientError, received: Buffer.concat(chunks) };
  } finally {
    if (!nativeJoined) {
      request?.destroy();
      for (const socket of accepted.keys()) { socket.destroy(); }
      await closeServer();
      await Promise.all([requestClosed, clientSocketClosed, incomingClosed, ...serverCloses]);
      if (pipe !== undefined) { await pipe; }
    }
  }
}

function deferred() {
  let resolve;
  const promise = new Promise(done => { resolve = done; });
  return { promise, resolve };
}
