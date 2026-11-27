import { Client, Pool } from "pg";
import type { ClientConfig } from "pg";
import type { PortalStoreConfig } from "./config.ts";

/** The pool is owned by the custom host; requests and the maintained adapter borrow it. */
export function createSessionStore(config: PortalStoreConfig): { readonly pool: Pool; dispose(): Promise<void> } {
  const clients = new Map<Client, Promise<void>>();
  class OwnedClient extends Client {
    constructor(options?: string | ClientConfig) {
      super(options);
      let closed!: () => void;
      const drained = new Promise<void>(resolve => { closed = resolve; });
      clients.set(this, drained);
      // A pool removal/query rejection is earlier than the native client's end.
      // Register before connect so failed connection attempts are owned as well.
      this.once("end", () => { clients.delete(this); closed(); });
    }
  }
  const pool = new Pool({
    Client: OwnedClient,
    connectionString: config.databaseUrl,
    ssl: config.databaseTls ? { rejectUnauthorized: true } : false,
    max: 4,
    connectionTimeoutMillis: 3000,
    idleTimeoutMillis: 10000,
    query_timeout: 5000,
    statement_timeout: 5000,
    idle_in_transaction_session_timeout: 5000,
    options: `-c search_path=${config.databaseSchema},pg_catalog`
  });
  let disposal: Promise<void> | undefined;
  return {
    pool,
    dispose() {
      disposal ??= (async () => {
        const errors: unknown[] = [];
        try { await pool.end(); } catch (error) { errors.push(error); }
        // Failed connects can be removed without the pool asking the client to
        // end. The owner still requests public shutdown and joins native end.
        const endings = await Promise.allSettled([...clients.keys()].map(client => Promise.resolve().then(() => client.end())));
        for (const ending of endings) { if (ending.status === "rejected") { errors.push(ending.reason); } }
        await Promise.all([...clients.values()]);
        if (errors.length !== 0) { throw new AggregateError(errors, "The owned session clients did not fully settle."); }
      })();
      return disposal;
    }
  };
}

export async function verifySessionSchema(pool: Pool, config: PortalStoreConfig): Promise<void> {
  const result = await pool.query<{ version: number; schema: string }>(
    "SELECT version, current_schema() AS schema FROM portal_schema_version");
  if (result.rowCount !== 1 || result.rows[0]?.version !== 1 || result.rows[0].schema !== config.databaseSchema) {
    throw new Error("The explicitly provisioned portal session schema is not version one.");
  }
  await pool.query('SELECT s."sessionToken", a.access_token, u.id FROM sessions s JOIN users u ON u.id=s."userId" JOIN accounts a ON a."userId"=u.id WHERE false');
}
