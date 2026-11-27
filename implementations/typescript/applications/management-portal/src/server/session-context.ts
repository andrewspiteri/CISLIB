import { createHmac } from "node:crypto";
import { captureBaseUrl } from "@cislib/management-client";
import { capturePortalContext } from "../portal-context.ts";
import type { PortalSessionContext } from "../portal-context.ts";
import type { PortalConfig } from "./config.ts";

type ContextConfig = Pick<PortalConfig, "authSecret" | "managementUrl" | "managementScope">;

/** Pure server-side association of an already validated database session; performs no authentication or I/O. */
export function createPortalSessionContext(config: ContextConfig, userId: string, sessionToken: string,
  expiresAtMs: number, nowMs = Date.now()): PortalSessionContext | undefined {
  if (typeof config.authSecret !== "string" || !/^[A-Za-z0-9_-]{43,256}$/u.test(config.authSecret)
    || typeof userId !== "string" || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/u.test(userId)
    || userId === "00000000-0000-0000-0000-000000000000"
    || typeof sessionToken !== "string" || sessionToken.length < 1 || sessionToken.length > 4096
    || !/^[\u0021-\u007e]+$/u.test(sessionToken)
    || !Number.isSafeInteger(nowMs) || nowMs < 0 || nowMs > 8640000000000000
    || !Number.isSafeInteger(expiresAtMs) || expiresAtMs <= nowMs || expiresAtMs > 8640000000000000
    || typeof config.managementScope !== "string" || config.managementScope.length < 1 || config.managementScope.length > 512
    || /[\u0000-\u0020\u007f-\u009f]/u.test(config.managementScope)) { return undefined; }
  let endpoint: string;
  try { endpoint = captureBaseUrl(config.managementUrl); }
  catch { return undefined; }
  return capturePortalContext({
    operatorId: association(config.authSecret, "cis.portal.operator.v1", userId),
    sessionAssociation: association(config.authSecret, "cis.portal.session.v1", userId, sessionToken),
    // This identifies fixed portal configuration, never the backend's native authority ID or recovery epoch.
    serviceAssociation: association(config.authSecret, "cis.portal.configured-management.v1", endpoint, config.managementScope),
    expiresAtMs
  });
}

function association(secret: string, domain: string, ...values: readonly string[]): string {
  return createHmac("sha256", secret).update(JSON.stringify([domain, ...values]), "utf8").digest("hex");
}
