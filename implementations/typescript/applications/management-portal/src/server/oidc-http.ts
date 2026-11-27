import { Agent } from "node:https";
import type { ClientRequest, IncomingMessage, RequestOptions } from "node:http";

// Node 24 invokes this native Agent method for each request. The declaration
// package omits it; the local augmentation describes the pinned runtime hook.
declare module "node:http" {
  interface Agent { addRequest(request: ClientRequest, options: RequestOptions): void; }
}

/** One native OIDC HTTP owner. It changes no process-global fetch or HTTP implementation. */
export class OidcHttpAgent extends Agent {
  private readonly active = new Map<ClientRequest, Promise<void>>();
  private stopped = false;
  private readonly deadlineMilliseconds: number;
  private readonly maximumResponseBytes: number;

  constructor(deadlineMilliseconds = 8000, maximumResponseBytes = 1048576) {
    super({ keepAlive: false, maxSockets: 8, maxTotalSockets: 8, minVersion: "TLSv1.2", rejectUnauthorized: true });
    if (!Number.isInteger(deadlineMilliseconds) || deadlineMilliseconds < 1 || deadlineMilliseconds > 15000
      || !Number.isInteger(maximumResponseBytes) || maximumResponseBytes < 1 || maximumResponseBytes > 1048576) {
      throw new TypeError("Select finite OIDC native HTTP limits.");
    }
    this.deadlineMilliseconds = deadlineMilliseconds;
    this.maximumResponseBytes = maximumResponseBytes;
  }

  override addRequest(request: ClientRequest, options: RequestOptions): void {
    if (this.stopped) { throw Object.assign(new Error("OIDC_HTTP_STOPPING"), { code: "OIDC_HTTP_STOPPING" }); }
    if (this.active.size >= 8) { throw Object.assign(new Error("OIDC_HTTP_BUSY"), { code: "OIDC_HTTP_BUSY" }); }
    let response: IncomingMessage | undefined;
    let resolveClosed!: () => void;
    const requestClosed = new Promise<void>(resolve => { resolveClosed = resolve; });
    let socketClosed: Promise<void> = Promise.resolve();
    let responseClosed: Promise<void> = Promise.resolve();
    const fail = (code: string) => {
      const error = Object.assign(new Error(code), { code });
      response?.destroy(error);
      request.destroy(error);
    };
    const timer = setTimeout(() => fail("OIDC_HTTP_DEADLINE"), this.deadlineMilliseconds);
    timer.unref();
    request.once("socket", socket => { socketClosed = closed(socket); });
    request.once("response", value => {
      response = value;
      responseClosed = closed(value);
      let bytes = value.readableLength;
      const push = value.push;
      const maximum = this.maximumResponseBytes;
      // Observe the owned stream's native ingress without switching it into flowing
      // mode or consuming bytes before openid-client's async iterator sees them.
      value.push = function (part: Uint8Array | string | null, encoding?: BufferEncoding): boolean {
        bytes += part === null ? 0 : typeof part === "string" ? Buffer.byteLength(part, encoding) : part.byteLength;
        if (bytes > maximum) {
          fail("OIDC_HTTP_RESPONSE_LIMIT");
          return false;
        }
        return push.call(this, part, encoding);
      };
      value.on("error", () => undefined);
    });
    request.once("close", () => {
      resolveClosed();
    });
    const drained = requestClosed.then(async () => {
      await Promise.all([socketClosed, responseClosed]);
      clearTimeout(timer);
      this.active.delete(request);
    });
    this.active.set(request, drained);
    // The library adds its own rejection listener after https.request returns.
    request.on("error", () => undefined);
    super.addRequest(request, options);
  }

  stopAdmission(): void {
    if (this.stopped) { return; }
    this.stopped = true;
    for (const request of this.active.keys()) {
      request.destroy(Object.assign(new Error("OIDC_HTTP_STOPPING"), { code: "OIDC_HTTP_STOPPING" }));
    }
    this.destroy();
  }

  async dispose(): Promise<void> {
    this.stopAdmission();
    await Promise.all([...this.active.values()]);
  }
}

function closed(stream: { readonly closed: boolean; once(event: "close", listener: () => void): unknown }): Promise<void> {
  return stream.closed ? Promise.resolve() : new Promise(resolve => { stream.once("close", resolve); });
}
