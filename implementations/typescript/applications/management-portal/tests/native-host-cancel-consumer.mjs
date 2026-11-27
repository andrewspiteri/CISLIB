// Existing fixture only: a real session-table lock controls an ordinary authenticated GET.
import assert from "node:assert/strict";
import https from "node:https";

export async function holdCanceledAuthenticatedRead({ owner, schema, origin, ca, context, applicationId }) {
  assert.match(schema, /^cis_portal_[a-f0-9]{32}$/u);
  const cookie = (await context.storageState()).cookies.find(row => row.name === "__Host-cis-portal.session-token");
  assert.ok(cookie?.secure && cookie.httpOnly);
  const gate = await owner.connect();
  let released = false;
  let request;
  let socketClosed = Promise.resolve();
  let requestClosed = Promise.resolve();
  let incomingClosed = Promise.resolve();
  let responseReceived = false;
  let unexpectedError;
  let canceling = false;
  async function release() {
    if (!released) {
      released = true;
      try { await gate.query("ROLLBACK"); } finally { gate.release(); }
    }
    if (request !== undefined && !request.closed) { canceling = true; request.destroy(); }
    await Promise.all([requestClosed, socketClosed, incomingClosed]);
    if (unexpectedError !== undefined) { throw unexpectedError; }
  }
  try {
    await gate.query("BEGIN");
    await gate.query(`LOCK TABLE "${schema}".sessions IN ACCESS EXCLUSIVE MODE`);
    request = https.request(new URL(`/api/portal/applications/${applicationId}/configurations/shared.settings/releases?schemaVersion=1&pageSize=2`, origin), { ca, agent: false,
      headers: { Cookie: `${cookie.name}=${cookie.value}` } });
    requestClosed = new Promise(resolve => request.once("close", resolve));
    request.once("socket", socket => { socketClosed = new Promise(resolve => socket.once("close", resolve)); });
    request.once("response", response => {
      responseReceived = true;
      incomingClosed = new Promise(resolve => response.once("close", resolve));
      response.resume();
    });
    request.on("error", error => { if (!canceling) { unexpectedError = error; } });
    request.end();
    const until = Date.now() + 3000;
    let held = false;
    while (Date.now() < until) {
      const locks = await owner.query("SELECT count(*)::int AS count FROM pg_locks WHERE relation=to_regclass($1) AND NOT granted", [`${schema}.sessions`]);
      if (locks.rows[0].count > 0) { held = true; break; }
      if (unexpectedError !== undefined) { throw unexpectedError; }
      await new Promise(resolve => setTimeout(resolve, 10));
    }
    assert.equal(held, true, "The actual authenticated request must enter its session SQL before cancellation.");
    assert.equal(responseReceived, false);
    canceling = true;
    request.destroy();
    await Promise.all([requestClosed, socketClosed]);
    return { release };
  } catch (error) {
    try { await release(); } catch (cleanup) { throw new AggregateError([error, cleanup], "Canceled read probe failed and cleanup did not settle."); }
    throw error;
  }
}
