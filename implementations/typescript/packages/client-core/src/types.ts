export type CredentialAttachment =
  | { readonly kind: "bearer"; readonly token: string }
  | { readonly kind: "transport"; readonly value: unknown }
  | { readonly kind: "none" };

export interface CredentialRequest {
  readonly url: string;
  readonly method: string;
  readonly signal: AbortSignal;
}

export type CredentialProvider = (request: CredentialRequest) => Promise<CredentialAttachment>;

export interface ClientCallContext {
  readonly signal?: AbortSignal;
  readonly credentials?: CredentialProvider;
  readonly timeoutMs?: number;
  readonly readRetries?: number;
}

export interface TransportRequest {
  readonly url: string;
  readonly method: "GET" | "PUT" | "DELETE";
  readonly headers: Readonly<Record<string, string>>;
  readonly body?: Uint8Array;
  readonly authentication: CredentialAttachment;
  readonly signal: AbortSignal;
}

export interface TransportResponse {
  readonly status: number;
  readonly url: string;
  readonly redirected: boolean;
  readonly headers: Readonly<Record<string, string>>;
  readonly body: ReadableStream<Uint8Array> | null;
  dispose(): Promise<void>;
}

export interface ClientTransport {
  send(request: TransportRequest): Promise<TransportResponse>;
}

export interface RequestObservation {
  readonly operation: string;
  readonly method: string;
  readonly attempt: number;
  readonly status?: number;
  readonly durationMs: number;
  readonly outcome: "http" | "canceled" | "timeout" | "transportFailure" | "protocolFailure" | "faulted";
}

export interface ClientCallResult<T, TProblem = never> {
  readonly kind: "http" | "canceled" | "timeout" | "transportFailure" | "protocolFailure";
  readonly status?: number;
  readonly value?: T;
  readonly problem?: TProblem;
  readonly code?: string;
  readonly attempts: number;
  readonly durationMs: number;
  readonly cleanupSucceeded: boolean;
  readonly operationKey?: string;
  readonly mutationUncertain: boolean;
}

export class ClientTransportFailure extends Error {
  constructor(readonly code: string) {
    super("The selected transport did not complete the HTTP exchange.");
    this.name = "ClientTransportFailure";
  }
}

export class ClientProtocolFailure extends Error {
  constructor(readonly code: string) {
    super("The remote response does not match the selected client protocol.");
    this.name = "ClientProtocolFailure";
  }
}

export interface PreparedRequest {
  readonly url: string;
  readonly method: "GET" | "PUT" | "DELETE";
  readonly headers: Readonly<Record<string, string>>;
  readonly body?: Uint8Array;
  readonly maximumResponseBytes: number;
  readonly maximumErrorBytes?: number;
}

export interface DecodedBody<T, TProblem> {
  readonly value?: T;
  readonly problem?: TProblem;
}

export interface ExecuteRequestOptions<T, TProblem> {
  /** A typed endpoint can disable retries even when a caller selects a generic read policy. */
  readonly readRetriesAllowed?: boolean;
  readonly operation: string;
  readonly transport: ClientTransport;
  readonly prepare: (signal: AbortSignal) => PreparedRequest;
  readonly decode: (response: TransportResponse, bytes: Uint8Array) => DecodedBody<T, TProblem>;
  readonly context?: ClientCallContext;
  readonly credentials?: CredentialProvider;
  readonly timeoutMs?: number;
  readonly readRetries?: number;
  readonly operationKey?: string;
  readonly observe?: (observation: RequestObservation) => void;
}
