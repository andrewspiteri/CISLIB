// Focused real-PostgreSQL ownership proof, called by the existing native portal fixture.
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { captureStoreConfig } from "../src/server/config.ts";
import { createSessionStore } from "../src/server/store.ts";

export async function verifySessionStoreDrain({ owner, databaseUrl, schema, reportPhase }) {
  const config = captureStoreConfig({ CIS_PORTAL_SESSION_DATABASE: databaseUrl, CIS_PORTAL_SESSION_SCHEMA: schema });
  reportPhase("session-store-owner-stop");
  await verifyHeldClient(config, owner, "owner-stop", reportPhase);
  reportPhase("session-store-query-timeout");
  await verifyHeldClient(config, owner, "query-timeout", reportPhase);
  reportPhase("session-store-failed-connect-admission");
  const invalid = new URL(databaseUrl);
  invalid.password = randomUUID();
  const failed = createSessionStore(captureStoreConfig({ CIS_PORTAL_SESSION_DATABASE: invalid.href, CIS_PORTAL_SESSION_SCHEMA: schema }));
  try { await assert.rejects(failed.pool.connect(), "The fresh PostgreSQL fixture must reject the wrong owned password."); }
  finally { reportPhase("session-store-failed-connect-native-join"); await failed.dispose(); }
  reportPhase("session-store-failed-connect-complete");
  return ["real-postgres-held-native-client-stop", "real-postgres-query-timeout-eviction-stop", "real-postgres-failed-connect-stop"];
}

async function verifyHeldClient(config, owner, mode, reportPhase) {
  const store = createSessionStore(config);
  let client;
  let released = false;
  let permitEnd = () => undefined;
  try {
    reportPhase("session-store-" + mode + "-connect");
    client = await store.pool.connect();
    reportPhase("session-store-" + mode + "-backend-identity");
    const backend = (await client.query("SELECT pg_backend_pid() AS pid")).rows[0].pid;
    let nativeEndObserved = false;
    client.once("end", () => { nativeEndObserved = true; });
    const nativeEnd = client.end.bind(client);
    let observeEnd;
    const entered = new Promise(resolve => { observeEnd = resolve; });
    const held = new Promise(resolve => { permitEnd = resolve; });
    // Hold this actual owned client's ordinary end call, never driver private state.
    // pg-pool has already removed it from its list before making this call.
    let nativeClosing;
    client.end = callback => {
      observeEnd();
      nativeClosing ??= held.then(() => nativeEnd());
      if (typeof callback === "function") {
        void nativeClosing.then(() => callback(), error => callback(error));
        return;
      }
      return nativeClosing;
    };
    client.release();
    released = true;
    if (mode === "query-timeout") {
      reportPhase("session-store-query-timeout-native-query");
      await assert.rejects(store.pool.query({ text: "SELECT pg_sleep(5)", query_timeout: 200 }), /Query read timeout/u,
        mode + ": the actual native query must reach its read timeout.");
    }
    let settled = false;
    reportPhase("session-store-" + mode + "-stop-requested");
    const closing = store.dispose();
    assert.equal(store.dispose(), closing, mode + ": repeated owner stop must join the same disposal.");
    void closing.then(() => { settled = true; });
    reportPhase("session-store-" + mode + "-held-end-admission");
    await entered;
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(settled, false, mode + ": pool removal cannot stand in for actual native client closure.");
    assert.equal(nativeEndObserved, false, mode + ": the held actual native client must not yet have ended.");
    reportPhase("session-store-" + mode + "-held-backend-observation");
    assert.equal((await owner.query("SELECT count(*)::int AS count FROM pg_stat_activity WHERE pid=$1", [backend])).rows[0].count, 1,
      mode + ": the controlled held native client must still have a real backend connection.");
    reportPhase("session-store-" + mode + "-release-native-end");
    permitEnd();
    reportPhase("session-store-" + mode + "-native-join");
    await closing;
    assert.equal(nativeEndObserved, true, mode + ": disposal must await the actual client's public native end event.");
    reportPhase("session-store-" + mode + "-native-joined");
    // This establishes local native connection settlement. It makes no claim
    // about remote SQL cancellation, rollback or backend-process disappearance.
  } finally {
    reportPhase("session-store-" + mode + "-cleanup-native-join");
    permitEnd();
    if (client !== undefined && !released) { client.release(); }
    await store.dispose();
  }
}
