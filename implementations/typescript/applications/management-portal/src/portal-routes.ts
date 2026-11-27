import { isCursor, isOperationKey, isOrderedInteger, isUuid } from "@cislib/management-client";

export type ApplicationRouteTab = "instances" | "configuration" | "capabilities" | "jobs";
export interface PortalListContext { readonly fromQuery?: string | undefined; readonly fromEnvironment?: string | undefined; readonly fromCursor?: string | undefined; }
interface ApplicationRoute { readonly applicationId: string; readonly applicationName?: string | undefined; readonly context?: PortalListContext | undefined; }
export interface ReportedJobRouteTarget {
  readonly registration?: string | undefined; readonly report?: string | undefined; readonly scope?: string | undefined; readonly job?: string | undefined;
  readonly resource?: string | undefined; readonly mode?: string | undefined; readonly cursor?: string | undefined; readonly view?: string | undefined;
}

/** A bounded cosmetic label. Exact native selectors, never this lossy label, identify the resource. */
export function readableSlug(value: string | undefined, fallback: "application" | "configuration"): string {
  if (value === undefined || value.length > 1024 || !validText(value)) { return fallback; }
  const normalized = value.normalize("NFKC").toLowerCase().replace(/[^\p{L}\p{N}]+/gu, "-").replace(/^-+|-+$/gu, "");
  const slug = [...normalized].slice(0, 80).join("").replace(/-+$/u, "") || fallback;
  if (fallback === "configuration" && slug === "new") { return "configuration-new"; }
  // A cosmetic name that looks exactly like another UUID must not enter the legacy UUID-path grammar.
  return fallback === "application" && isUuid(slug) ? `application-${slug}` : slug;
}

export function applicationRoute(input: ApplicationRoute & { readonly tab: ApplicationRouteTab; readonly cursor?: string | undefined }): string {
  if (!["instances", "configuration", "capabilities", "jobs"].includes(input.tab)) { throw new TypeError("Select one application view."); }
  if (input.cursor !== undefined && !isCursor(input.cursor)) { throw new TypeError("Retain the exact page cursor."); }
  return route(`${applicationPath(input)}/${input.tab}`, { applicationId: input.applicationId, cursor: input.cursor, ...listValues(input.context) });
}

export function configurationRoute(input: ApplicationRoute & {
  readonly configurationId: string; readonly configurationName?: string | undefined; readonly schemaVersion: string;
  readonly view?: "details" | "edit" | "assignment" | "adoption" | undefined; readonly source?: "common" | "application" | undefined;
  readonly publicationId?: string | undefined; readonly releaseId?: string | undefined; readonly cursor?: string | undefined;
}): string {
  if (!isOperationKey(input.configurationId) || !isOrderedInteger(input.schemaVersion)
    || input.view !== undefined && !["details", "edit", "assignment", "adoption"].includes(input.view)
    || input.source !== undefined && input.source !== "common" && input.source !== "application"
    || input.publicationId !== undefined && !isUuid(input.publicationId) || input.releaseId !== undefined && !isUuid(input.releaseId)
    || input.cursor !== undefined && !isCursor(input.cursor)) { throw new TypeError("Select one exact Configuration view."); }
  const suffix = input.view === undefined || input.view === "details" ? "" : `/${input.view}`;
  return route(`${applicationPath(input)}/configuration/${encodeURIComponent(readableSlug(input.configurationName ?? input.configurationId, "configuration"))}${suffix}`,
    { applicationId: input.applicationId, configurationId: input.configurationId, schemaVersion: input.schemaVersion, source: input.source,
      publicationId: input.publicationId, releaseId: input.releaseId, cursor: input.cursor, ...listValues(input.context) });
}

export function jobDetailsRoute(input: ApplicationRoute & { readonly target: ReportedJobRouteTarget }): string {
  const target = input.target;
  return route(`${applicationPath(input)}/jobs/details`, { applicationId: input.applicationId, registration: target.registration, report: target.report,
    scope: target.scope, job: target.job, resource: target.resource, mode: target.mode, cursor: target.cursor, view: target.view, ...listValues(input.context) });
}

function listValues(context: PortalListContext | undefined): Readonly<Record<string, string | undefined>> {
  return { fromQuery: context?.fromQuery, fromEnvironment: context?.fromEnvironment, fromCursor: context?.fromCursor };
}

function applicationPath(input: ApplicationRoute): string {
  if (!isUuid(input.applicationId)) { throw new TypeError("Retain the exact application ID."); }
  return `/applications/${encodeURIComponent(readableSlug(input.applicationName, "application"))}`;
}
function route(path: string, values: Readonly<Record<string, string | undefined>>): string {
  const query = new URLSearchParams();
  for (const [name, value] of Object.entries(values)) {
    if (value === undefined || value === "") { continue; }
    if (value.length > 2048 || !validText(value) || /[\u0000-\u001f\u007f-\u009f]/u.test(value)) { throw new TypeError("Retain a bounded exact route selector."); }
    query.set(name, value);
  }
  return `${path}?${query.toString()}`;
}
function validText(value: string): boolean {
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index);
    if (code >= 0xd800 && code <= 0xdbff) {
      const next = value.charCodeAt(++index);
      if (!(next >= 0xdc00 && next <= 0xdfff)) { return false; }
    } else if (code >= 0xdc00 && code <= 0xdfff) { return false; }
  }
  return true;
}
