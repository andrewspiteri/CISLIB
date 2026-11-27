import assert from "node:assert/strict";
import http from "node:http";

// Credential-free isolation of the short-lived native TCP connect path.
const requestCount = boundedInteger(process.argv[2], 16_384, "request count");
const concurrency = boundedInteger(process.argv[3], 8, "concurrency");
assert.equal(process.argv.length, 4, "Only request count and concurrency are accepted.");
assert.ok(concurrency <= requestCount, "Concurrency cannot exceed request count.");

await writeReport({
  kind: "node-http-connect-started",
  node: process.version,
  uv: process.versions.uv,
  platform: process.platform,
  arch: process.arch,
  requestCount,
  concurrency,
});

const counts = {
  requestsCreated: 0,
  requestsClosed: 0,
  responsesReceived: 0,
  responsesEnded: 0,
  responsesClosed: 0,
  clientSocketsOpened: 0,
  clientSocketsClosed: 0,
  serverRequestsReceived: 0,
  serverRequestsEnded: 0,
  serverRequestsClosed: 0,
  serverResponsesFinished: 0,
  serverResponsesClosed: 0,
  serverSocketsOpened: 0,
  serverSocketsClosed: 0,
};
const failures = [];
const activeRequests = new Set();
const clientSockets = new Map();
const serverSockets = new Map();
const nativeClosures = [];
let listenersOutstanding = 0;
let maximumClientSockets = 0;
let listenerStarted = false;
let listenerClosed = false;
let agentDestroyed = false;
let nextRequest = 0;

const agent = new http.Agent({
  keepAlive: false,
  maxSockets: concurrency,
  maxTotalSockets: concurrency,
});
const server = http.createServer();
const serverOwner = observe(server, "listener", () => { listenerClosed = true; });
serverOwner.on("connection", socket => {
  counts.serverSocketsOpened += 1;
  const socketOwner = observe(socket, "server socket", () => {
    counts.serverSocketsClosed += 1;
    serverSockets.delete(socket);
  });
  serverSockets.set(socket, socketOwner);
  nativeClosures.push(socketOwner.closed);
});
serverOwner.on("request", (request, response) => {
  counts.serverRequestsReceived += 1;
  const requestOwner = observe(request, "server request", () => {
    counts.serverRequestsClosed += 1;
  });
  requestOwner.on("end", () => { counts.serverRequestsEnded += 1; }, true);
  const responseOwner = observe(response, "server response", () => {
    counts.serverResponsesClosed += 1;
  });
  responseOwner.on("finish", () => { counts.serverResponsesFinished += 1; }, true);
  nativeClosures.push(requestOwner.closed, responseOwner.closed);
  request.resume();
  response.writeHead(200, { "Content-Length": "2", Connection: "close" });
  response.end("ok");
});

try {
  await new Promise((resolve, reject) => {
    serverOwner.on("error", reject, true);
    serverOwner.on("listening", () => {
      listenerStarted = true;
      resolve();
    }, true);
    server.listen(0, "127.0.0.1");
  });
  const address = server.address();
  assert.ok(address && typeof address === "object");
  await Promise.all(Array.from({ length: concurrency }, async () => {
    while (nextRequest < requestCount && failures.length === 0) {
      nextRequest += 1;
      try {
        await sendRequest(address.port);
      } catch (error) {
        failures.push(error);
      }
    }
  }));
} catch (error) {
  failures.push(error);
} finally {
  if (failures.length > 0) {
    for (const request of activeRequests) {
      try { request.destroy(); } catch (error) { failures.push(error); }
    }
    for (const socket of [...clientSockets.keys(), ...serverSockets.keys()]) {
      try { socket.destroy(); } catch (error) { failures.push(error); }
    }
  }
  try {
    agent.destroy();
    agentDestroyed = true;
  } catch (error) {
    failures.push(error);
  }
  if (listenerStarted) {
    await new Promise(resolve => {
      try {
        server.close(error => {
          if (error) { failures.push(error); }
          resolve();
        });
      } catch (error) {
        failures.push(error);
        resolve();
      }
    });
    await serverOwner.closed;
  } else {
    serverOwner.release();
  }
  await Promise.all(nativeClosures);
}

if (failures.length > 0) {
  throw new AggregateError(failures, "The native HTTP connect probe or its cleanup failed.");
}
for (const [name, value] of Object.entries(counts)) {
  assert.equal(value, requestCount, `${name} must include every native request.`);
}
assert.equal(activeRequests.size, 0);
assert.equal(clientSockets.size, 0);
assert.equal(serverSockets.size, 0);
assert.equal(listenersOutstanding, 0);
assert.ok(maximumClientSockets > 0 && maximumClientSockets <= concurrency);
const agentEmpty = [agent.sockets, agent.freeSockets, agent.requests]
  .every(collection => Object.values(collection).every(items => items.length === 0));
assert.ok(agentEmpty && agentDestroyed && listenerClosed);
await writeReport({
  kind: "node-http-connect-completed",
  requestCount,
  concurrency,
  ...counts,
  maximumClientSockets,
  listenersOutstanding,
  agentDestroyed,
  agentEmpty,
  listenerClosed,
  nativeJoined: true,
});

async function sendRequest(port) {
  let admitResponse;
  const responseAdmitted = new Promise(resolve => { admitResponse = resolve; });
  let responseOwner;
  let responseEnded = false;
  let responseBytes = "";
  let socketOwner;
  const request = http.request({
    host: "127.0.0.1",
    port,
    path: "/native-connect-probe",
    method: "GET",
    agent,
  });
  counts.requestsCreated += 1;
  activeRequests.add(request);
  const requestOwner = observe(request, "client request", () => {
    counts.requestsClosed += 1;
    activeRequests.delete(request);
    admitResponse();
  });
  nativeClosures.push(requestOwner.closed);
  requestOwner.on("socket", socket => {
    socketOwner = clientSockets.get(socket);
    if (!socketOwner) {
      counts.clientSocketsOpened += 1;
      socketOwner = observe(socket, "client socket", () => {
        counts.clientSocketsClosed += 1;
        clientSockets.delete(socket);
      });
      clientSockets.set(socket, socketOwner);
      nativeClosures.push(socketOwner.closed);
      maximumClientSockets = Math.max(maximumClientSockets, clientSockets.size);
    }
  }, true);
  requestOwner.on("response", response => {
    counts.responsesReceived += 1;
    responseOwner = observe(response, "client response", () => {
      counts.responsesClosed += 1;
    });
    nativeClosures.push(responseOwner.closed);
    responseOwner.on("end", () => {
      responseEnded = true;
      counts.responsesEnded += 1;
    }, true);
    responseOwner.on("data", chunk => {
      responseBytes += chunk.toString("ascii");
      if (responseBytes.length > 2) {
        failures.push(new Error("The native HTTP probe response exceeded two bytes."));
        response.destroy();
      }
    });
    if (response.statusCode !== 200) {
      failures.push(new Error("The native HTTP probe returned an unexpected status."));
    }
    admitResponse();
  }, true);
  request.end();
  await responseAdmitted;
  await Promise.all([requestOwner.closed, responseOwner?.closed, socketOwner?.closed]);
  assert.ok(responseOwner && socketOwner, "Each request must own a native response and socket.");
  assert.ok(responseEnded, "Each response must reach its actual end event.");
  assert.equal(responseBytes, "ok");
}

function observe(emitter, stage, onClose) {
  const subscriptions = [];
  let resolveClosed;
  const closed = new Promise(resolve => { resolveClosed = resolve; });
  function on(event, handler, once = false) {
    subscriptions.push({ event, handler });
    listenersOutstanding += 1;
    if (once) { emitter.once(event, handler); } else { emitter.on(event, handler); }
  }
  function release() {
    for (const { event, handler } of subscriptions.splice(0)) {
      emitter.removeListener(event, handler);
      listenersOutstanding -= 1;
    }
  }
  on("error", error => {
    failures.push(new Error(`Native ${stage} emitted an error.`, { cause: error }));
  });
  on("close", () => {
    onClose?.();
    release();
    resolveClosed();
  }, true);
  return { closed, on, release };
}

function boundedInteger(value, maximum, name) {
  assert.match(value ?? "", /^[1-9][0-9]*$/, `${name} must be a positive decimal integer.`);
  const parsed = Number(value);
  assert.ok(Number.isSafeInteger(parsed) && parsed <= maximum, `${name} exceeds its hard bound.`);
  return parsed;
}

async function writeReport(report) {
  await new Promise((resolve, reject) => {
    process.stdout.write(`${JSON.stringify(report)}\n`, error => {
      if (error) { reject(error); } else { resolve(); }
    });
  });
}
