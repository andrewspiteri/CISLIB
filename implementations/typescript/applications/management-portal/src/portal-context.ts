/** Nonsecret request associations. These are neither credentials nor native grants/backend authority. */
export interface PortalSessionContext {
  readonly operatorId: string;
  readonly sessionAssociation: string;
  readonly serviceAssociation: string;
  readonly expiresAtMs: number;
}

export const portalContextHeader = "x-cis-portal-context";

export function capturePortalContext(value: unknown): PortalSessionContext | undefined {
  if (value === null || typeof value !== "object" || Array.isArray(value)
    || Object.getPrototypeOf(value) !== Object.prototype && Object.getPrototypeOf(value) !== null) { return undefined; }
  const fields = ["operatorId", "sessionAssociation", "serviceAssociation", "expiresAtMs"];
  const keys = Reflect.ownKeys(value);
  if (keys.length !== fields.length || keys.some(key => typeof key !== "string" || !fields.includes(key))
    || Object.values(Object.getOwnPropertyDescriptors(value)).some(property => property.get !== undefined || property.set !== undefined)) {
    return undefined;
  }
  const input = value as Record<string, unknown>;
  if (!association(input.operatorId) || !association(input.sessionAssociation) || !association(input.serviceAssociation)
    || typeof input.expiresAtMs !== "number" || !Number.isSafeInteger(input.expiresAtMs)
    || input.expiresAtMs <= 0 || input.expiresAtMs > 8640000000000000) { return undefined; }
  return Object.freeze({ operatorId: input.operatorId, sessionAssociation: input.sessionAssociation,
    serviceAssociation: input.serviceAssociation, expiresAtMs: input.expiresAtMs });
}

export function portalContextKey(context: PortalSessionContext): string {
  return `${context.operatorId}.${context.sessionAssociation}.${context.serviceAssociation}`;
}

export function samePortalSession(left: PortalSessionContext | undefined, right: PortalSessionContext | undefined): boolean {
  return left === undefined || right === undefined ? left === right : portalContextKey(left) === portalContextKey(right);
}

function association(value: unknown): value is string { return typeof value === "string" && /^[a-f0-9]{64}$/u.test(value); }
