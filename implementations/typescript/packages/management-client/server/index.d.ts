import type { ClientTransport } from "@cislib/client-core";
import { ManagementClient } from "../dist/index.js";
import type { ManagementClientOptions } from "../dist/index.js";

/** Native TLS validation stays enabled. No accepting validation callback is supported. */
export interface NodeHttpsTransportOptions {
  readonly origin: string;
  readonly ca?: string | Uint8Array;
}
export interface NodeMutualTlsCredential {
  readonly kind: "mtls";
  readonly certificate: string | Uint8Array;
  readonly privateKey: string | Uint8Array;
}
export interface NodeManagementClientOptions extends Omit<ManagementClientOptions, "baseUrl"> {
  readonly baseUrl?: string;
  readonly ca?: string | Uint8Array;
}
export function createNodeHttpsTransport(options: NodeHttpsTransportOptions): ClientTransport;
export function createNodeManagementClient(options: NodeManagementClientOptions): ManagementClient;
