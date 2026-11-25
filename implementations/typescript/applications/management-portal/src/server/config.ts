import { isAbsolute } from "node:path";
import { isIP } from "node:net";

export const providerId = "cis-management";
export const sessionCookieName = "__Host-cis-portal.session-token";
export const sessionMaximumSeconds = 3600;
export const tokenExpiryMarginSeconds = 30;
export interface PortalStoreConfig { readonly databaseUrl: string; readonly databaseSchema: string; readonly databaseTls: boolean; }

export interface PortalConfig {
  readonly origin: string;
  readonly managementUrl: string;
  readonly authorityOrigin: string;
  readonly tenantId: string;
  readonly clientId: string;
  readonly clientSecret: string;
  readonly managementScope: string;
  readonly allowedSubjects: ReadonlySet<string>;
  readonly authSecret: string;
  readonly databaseUrl: string;
  readonly databaseSchema: string;
  readonly databaseTls: boolean;
  readonly managementCaFile?: string;
  readonly serverCertificateFile?: string;
  readonly serverKeyFile?: string;
  readonly hostname: string;
  readonly port: number;
}

/** Capture explicit deployment inputs; perform no credential, file, database or network I/O. */
export function captureConfig(environment: NodeJS.ProcessEnv): PortalConfig {
  const originUrl = httpsUrl(required(environment, "NEXTAUTH_URL"), "NEXTAUTH_URL");
  if (originUrl.pathname !== "/" || originUrl.search || originUrl.hash) {
    throw new TypeError("NEXTAUTH_URL must be the fixed HTTPS portal origin without a path.");
  }
  if (environment.NEXTAUTH_URL_INTERNAL !== undefined) {
    throw new TypeError("Use one explicit portal origin; NEXTAUTH_URL_INTERNAL is not supported.");
  }
  const management = httpsUrl(required(environment, "CIS_PORTAL_MANAGEMENT_URL"), "CIS_PORTAL_MANAGEMENT_URL");
  if (management.search || management.hash || /%(?:2f|5c)/iu.test(management.pathname)) {
    throw new TypeError("Select one trusted Management HTTPS base prefix.");
  }
  const authority = httpsUrl(environment.CIS_PORTAL_AUTHORITY_ORIGIN ?? "https://login.microsoftonline.com/", "CIS_PORTAL_AUTHORITY_ORIGIN");
  if (authority.pathname !== "/" || authority.search || authority.hash) {
    throw new TypeError("The OIDC authority must be one fixed HTTPS origin.");
  }
  const tenantId = guid(required(environment, "CIS_PORTAL_TENANT_ID"));
  const clientId = guid(required(environment, "CIS_PORTAL_CLIENT_ID"));
  const audience = guid(required(environment, "CIS_PORTAL_MANAGEMENT_AUDIENCE"));
  const scope = environment.CIS_PORTAL_OPERATOR_SCOPE ?? "management.operator";
  if (!/^[A-Za-z][A-Za-z0-9._-]{0,127}$/u.test(scope)) {
    throw new TypeError("Configure one bounded delegated Management scope name.");
  }
  const subjects = required(environment, "CIS_PORTAL_ALLOWED_SUBJECTS").split(",").map(guid);
  if (subjects.length > 256 || new Set(subjects).size !== subjects.length) {
    throw new TypeError("Configure one to 256 distinct issuer-qualified operator object IDs.");
  }
  const { databaseUrl, databaseSchema, databaseTls } = captureStoreConfig(environment);
  const authSecret = required(environment, "NEXTAUTH_SECRET");
  if (!/^[A-Za-z0-9_-]{43,256}$/u.test(authSecret)) {
    throw new TypeError("Supply a newly provisioned high-entropy base64url NEXTAUTH_SECRET.");
  }
  const clientSecret = required(environment, "CIS_PORTAL_CLIENT_SECRET");
  if (clientSecret.length > 4096 || /[\u0000-\u001f\u007f]/u.test(clientSecret)) {
    throw new TypeError("Supply one bounded OIDC client secret.");
  }
  const serverCertificateFile = filePath(environment.CIS_PORTAL_SERVER_CERTIFICATE_FILE);
  const serverKeyFile = filePath(environment.CIS_PORTAL_SERVER_KEY_FILE);
  if ((serverCertificateFile === undefined) !== (serverKeyFile === undefined)) {
    throw new TypeError("Provide both owned portal HTTPS PEM files or terminate HTTPS at the trusted proxy.");
  }
  const hostname = environment.CIS_PORTAL_BIND_ADDRESS ?? "127.0.0.1";
  if (isIP(hostname) === 0) {
    throw new TypeError("Configure an explicit IP listener address.");
  }
  const portText = environment.CIS_PORTAL_PORT ?? "3000";
  const port = Number(portText);
  if (!/^[1-9][0-9]{0,4}$/u.test(portText) || port > 65535) {
    throw new TypeError("Configure a listener port from one through 65535.");
  }
  const managementCaFile = filePath(environment.CIS_PORTAL_MANAGEMENT_CA_FILE);
  return Object.freeze({
    origin: originUrl.origin, managementUrl: management.href, authorityOrigin: authority.origin,
    tenantId, clientId, clientSecret, managementScope: `api://${audience}/${scope}`,
    allowedSubjects: new Set(subjects), authSecret, databaseUrl, databaseSchema, databaseTls,
    ...(managementCaFile === undefined ? {} : { managementCaFile }),
    ...(serverCertificateFile === undefined ? {} : { serverCertificateFile, serverKeyFile: serverKeyFile! }),
    hostname, port
  });
}

export function captureStoreConfig(environment: NodeJS.ProcessEnv): PortalStoreConfig {
  const databaseUrl = required(environment, "CIS_PORTAL_SESSION_DATABASE");
  let database: URL;
  try { database = new URL(databaseUrl); }
  catch { throw new TypeError("Supply a valid explicitly owned portal PostgreSQL URI."); }
  if (!["postgres:", "postgresql:"].includes(database.protocol) || !database.hostname || database.pathname.length <= 1
    || !database.username || !database.password || database.hash || /[\u0000-\u0020\u007f]/u.test(databaseUrl)) {
    throw new TypeError("Select the explicitly owned portal PostgreSQL session database.");
  }
  const modes = database.searchParams.getAll("sslmode");
  if ([...database.searchParams.keys()].some(key => key !== "sslmode") || modes.length > 1
    || modes.some(mode => mode !== "verify-full" && mode !== "disable")) {
    throw new TypeError("Only one explicit sslmode=verify-full or local sslmode=disable is supported.");
  }
  const databaseTls = modes[0] === "verify-full";
  if (!["localhost", "127.0.0.1", "[::1]"].includes(database.hostname) && !databaseTls) {
    throw new TypeError("Remote session database connections require sslmode=verify-full.");
  }
  // pg otherwise consults ambient PGPORT when the URI omits its port.
  if (!database.port) { database.port = "5432"; }
  const databaseSchema = environment.CIS_PORTAL_SESSION_SCHEMA ?? "cis_portal";
  if (!/^cis_portal(?:_[a-z0-9_]{1,48})?$/u.test(databaseSchema)) {
    throw new TypeError("Select a dedicated cis_portal schema, not a shared application schema.");
  }
  return Object.freeze({ databaseUrl: database.href, databaseSchema, databaseTls });
}

export function issuer(config: PortalConfig): string {
  return `${config.authorityOrigin}/${config.tenantId}/v2.0`;
}
export function guid(value: string): string {
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/u.test(value)
    || value === "00000000-0000-0000-0000-000000000000") {
    throw new TypeError("Supply a canonical nonempty UUID.");
  }
  return value;
}
function required(environment: NodeJS.ProcessEnv, key: string): string {
  const value = environment[key];
  if (value === undefined || value.length === 0 || value.length > 16384) {
    throw new TypeError(`Configure ${key} explicitly.`);
  }
  return value;
}
function httpsUrl(value: string, key: string): URL {
  let url: URL;
  try { url = new URL(value); }
  catch { throw new TypeError(`Configure a trusted HTTPS ${key}.`); }
  if (url.protocol !== "https:" || !url.hostname || url.username || url.password) {
    throw new TypeError(`Configure a trusted HTTPS ${key}.`);
  }
  return url;
}
function filePath(value: string | undefined): string | undefined {
  if (value !== undefined && (!isAbsolute(value) || value.length > 4096)) {
    throw new TypeError("Configure bounded absolute owned PEM paths.");
  }
  return value;
}
