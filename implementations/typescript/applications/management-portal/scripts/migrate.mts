import { readFile } from "node:fs/promises";
import { captureStoreConfig } from "../src/server/config.ts";
import { createSessionStore, verifySessionSchema } from "../src/server/store.ts";

// Explicit schema installation. Ordinary host startup never executes DDL.
const config = captureStoreConfig(process.env);
const store = createSessionStore(config);
const pool = store.pool;
try {
  const client = await pool.connect();
  try {
    await client.query("BEGIN");
    await client.query("SELECT pg_advisory_xact_lock(hashtext($1))", [`cislib.portal.schema:${config.databaseSchema}`]);
    const tables = await client.query<{ table_name: string }>(
      "SELECT table_name FROM information_schema.tables WHERE table_schema=$1 ORDER BY table_name LIMIT 16", [config.databaseSchema]);
    if (tables.rowCount !== 0) {
      const expected = ["accounts", "portal_schema_version", "sessions", "users", "verification_token"];
      if (tables.rows.length !== expected.length || tables.rows.some((row, index) => row.table_name !== expected[index])) {
        throw new Error("The selected session schema is not empty or an installed portal schema.");
      }
      const version = await client.query<{ version: number }>("SELECT version FROM portal_schema_version");
      if (version.rowCount !== 1 || version.rows[0]?.version !== 1) { throw new Error("Unsupported portal schema version."); }
    } else {
      const namespace = await client.query<{ present: boolean }>(
        "SELECT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname=$1) AS present", [config.databaseSchema]);
      if (!namespace.rows[0]?.present) { await client.query(`CREATE SCHEMA "${config.databaseSchema}"`); }
      await client.query(`SET LOCAL search_path TO "${config.databaseSchema}", pg_catalog`);
      const sql = await readFile(new URL("../sql/001-sessions.sql", import.meta.url), "utf8");
      await client.query(sql);
    }
    await client.query("COMMIT");
  } catch (error) {
    await client.query("ROLLBACK");
    throw error;
  } finally {
    client.release();
  }
  await verifySessionSchema(pool, config);
  console.log("Portal session schema is installed at version one.");
} finally {
  await store.dispose();
}
