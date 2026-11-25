import { open } from "node:fs/promises";
import type { Pool } from "pg";
import { createNodeManagementClient, createNodeManagementConfigurationClient } from "@cislib/management-client/server";
import type { ManagementClient, ManagementConfigurationClient } from "@cislib/management-client";
import type { PortalConfig } from "./config.ts";
import { OidcHttpAgent } from "./oidc-http.ts";
import { createSessionStore, verifySessionSchema } from "./store.ts";

const runtimeKey = Symbol.for("cislib.management.portal.runtime.v1");

/** Shared infrastructure only. No user, credential, page or authentication result is cached here. */
export class PortalRuntime {
  readonly config: PortalConfig;
  readonly pool: Pool;
  private readonly sessionStore: ReturnType<typeof createSessionStore>;
  readonly oidc: OidcHttpAgent;
  readonly management: ManagementClient;
  readonly configuration: ManagementConfigurationClient;
  private readonly stop = new AbortController();
  private disposal: Promise<void> | undefined;
  private activeReads = 0;

  constructor(config: PortalConfig, ca?: Uint8Array) {
    this.config = config;
    this.sessionStore = createSessionStore(config);
    this.pool = this.sessionStore.pool;
    this.oidc = new OidcHttpAgent();
    const options = { baseUrl: config.managementUrl, ...(ca === undefined ? {} : { ca }), timeoutMs: 30000 };
    this.management = createNodeManagementClient(options);
    this.configuration = createNodeManagementConfigurationClient(options);
    this.pool.on("error", () => { console.error("[Portal] Session store connection failed."); });
  }

  get stopping(): boolean { return this.stop.signal.aborted; }
  get signal(): AbortSignal { return this.stop.signal; }

  enterRead(): (() => void) | undefined {
    if (this.stopping || this.activeReads >= 8) { return undefined; }
    this.activeReads += 1;
    let released = false;
    return () => {
      if (!released) { released = true; this.activeReads -= 1; }
    };
  }

  async prepare(): Promise<void> {
    await verifySessionSchema(this.pool, this.config);
  }

  beginStop(): void {
    if (this.stopping) { return; }
    this.stop.abort();
    this.oidc.stopAdmission();
  }

  dispose(): Promise<void> {
    this.disposal ??= this.disposeCore();
    return this.disposal;
  }

  private async disposeCore(): Promise<void> {
    this.beginStop();
    const errors: unknown[] = [];
    try { await this.oidc.dispose(); } catch (error) { errors.push(error); }
    try { await this.sessionStore.dispose(); } catch (error) { errors.push(error); }
    if (errors.length > 0) { throw new AggregateError(errors, "Portal infrastructure did not fully settle."); }
  }
}

export async function createRuntime(config: PortalConfig): Promise<PortalRuntime> {
  const ca = config.managementCaFile === undefined ? undefined : await readOwnedPem(config.managementCaFile);
  return new PortalRuntime(config, ca);
}

/** Next's compiled server modules borrow the exact infrastructure created by server.mts. */
export function publishRuntime(runtime: PortalRuntime): void {
  if (Reflect.has(globalThis, runtimeKey)) { throw new Error("One portal infrastructure owner is already published."); }
  Reflect.set(globalThis, runtimeKey, runtime);
}
export function borrowRuntime(): PortalRuntime {
  const runtime = Reflect.get(globalThis, runtimeKey) as PortalRuntime | undefined;
  if (runtime === undefined || runtime.stopping) { throw new PortalStoppingError(); }
  return runtime;
}
export function unpublishRuntime(runtime: PortalRuntime): void {
  if (Reflect.get(globalThis, runtimeKey) !== runtime) { throw new Error("Cannot retire another portal owner."); }
  Reflect.deleteProperty(globalThis, runtimeKey);
}
export class PortalStoppingError extends Error {
  constructor() { super("The owned portal host is unavailable or stopping."); this.name = "PortalStoppingError"; }
}

/** Read one explicit owned PEM without an unbounded readFile allocation. */
export async function readOwnedPem(path: string): Promise<Buffer> {
  const handle = await open(path, "r");
  try {
    const buffer = Buffer.alloc(1048577);
    let length = 0;
    while (length < buffer.byteLength) {
      const read = await handle.read(buffer, length, buffer.byteLength - length, null);
      if (read.bytesRead === 0) { break; }
      length += read.bytesRead;
    }
    if (length === 0 || length > 1048576) {
      buffer.fill(0);
      throw new TypeError("An owned PEM file must contain at most one MiB.");
    }
    return buffer.subarray(0, length);
  } finally {
    await handle.close();
  }
}
