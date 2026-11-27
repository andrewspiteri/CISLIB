import { decodeManagement, encodeManagement, isStrongETag, isUuid } from "@cislib/management-client";
import type { Application, ApplicationMetadata, MetadataCommand } from "@cislib/management-client";
import type { MetadataCommandObservation } from "./command-results.ts";

/** One exact currently readable application; permission observations are advisory, never write authority. */
export interface MetadataEditorSnapshot {
  readonly applicationId: string;
  readonly application: string;
  readonly environment: string;
  readonly metadata: ApplicationMetadata;
  readonly metadataRevision: string;
  readonly metadataETag: string;
  readonly canReplace: boolean;
}
export interface MetadataDraft { readonly displayName: string; readonly description: string; }
/** Native additive fields are deliberately excluded from the private browser projection. */
export function projectMetadataEditor(value: Application, canReplace: boolean): MetadataEditorSnapshot {
  return Object.freeze({ applicationId: value.applicationId, application: value.application, environment: value.environment,
    metadataRevision: value.metadataRevision, metadataETag: value.metadataETag, canReplace,
    metadata: Object.freeze({ displayName: value.metadata.displayName,
      ...(value.metadata.description === undefined ? {} : { description: value.metadata.description }) }) });
}
export interface MetadataReview {
  readonly applicationId: string;
  readonly ifMatch: string;
  readonly before: ApplicationMetadata;
  readonly command: MetadataCommand;
}
/** The component enforces its current target even when a router reuses its mounted instance. */
export function metadataTargetMatches(applicationId: string, requestKey: string, snapshot: MetadataEditorSnapshot | undefined,
  target: { readonly requestKey: string; readonly applicationId?: string; readonly environment?: string } | undefined): boolean {
  return snapshot !== undefined && target !== undefined && snapshot.applicationId === applicationId
    && target.applicationId === applicationId && target.requestKey === requestKey && target.environment === snapshot.environment;
}
/** A later lookup must not replace the sole established original fact before explicit dismissal. */
export function resolvedMetadataOriginal(value: MetadataCommandObservation | undefined): boolean {
  return value !== undefined && (value.originalCompletion === "committed" && ["applied", "rejected", "conflict"].includes(value.disposition)
    || value.originalCompletion === "notCommitted" && value.disposition === "notCommitted");
}

/** Fresh PUT confirmation only. Known commit and current cleanup remain separate UI decisions. */
export function metadataConfirmationAction(review: MetadataReview, value: MetadataCommandObservation | undefined, key: string,
  currentDraft: string | undefined): "return" | "show-commit" | "stay" {
  if (value?.family !== "management.metadata.replace" || value.key !== key || value.target.applicationId !== review.applicationId
    || value.disposition !== "applied" || value.originalCompletion !== "committed" || !["prepared", "replayed"].includes(value.resultSource ?? "")
    || value.decision?.decision !== "applied" || value.decision.applicationId !== review.applicationId || currentDraft === undefined) { return "stay"; }
  let command: MetadataCommand;
  try { command = metadataCommand(readMetadataDraft(currentDraft)); } catch { return "stay"; }
  if (command.displayName !== review.command.displayName || command.description !== review.command.description
    || value.decision.metadata.displayName !== review.command.displayName || value.decision.metadata.description !== review.command.description) { return "stay"; }
  return value.transport.kind === "http" && value.transport.status === 200 && value.transport.cleanupSucceeded && !value.transport.mutationUncertain
    && value.attemptCompletion?.cleanupSucceeded !== false ? "return" : "show-commit";
}

/** The empty description deliberately maps to native omission-to-clear, never JSON null. */
export function metadataCommand(draft: MetadataDraft): MetadataCommand {
  if (!closed(draft, ["displayName", "description"]) || typeof draft.description !== "string") { throw new TypeError("Invalid metadata draft."); }
  return decodeManagement<MetadataCommand>("MetadataCommand", encodeManagement("MetadataCommand", {
    commandVersion: 1, displayName: draft.displayName, ...(draft.description === "" ? {} : { description: draft.description })
  }, "request"), "request");
}
export function metadataDraftText(metadata: ApplicationMetadata): string {
  return JSON.stringify({ displayName: metadata.displayName, description: metadata.description ?? "" });
}
export function readMetadataDraft(text: string): MetadataDraft {
  if (typeof text !== "string" || text.length > 16384) { throw new TypeError("Invalid metadata draft."); }
  const value: unknown = JSON.parse(text);
  if (!closed(value, ["displayName", "description"]) || typeof value.displayName !== "string" || typeof value.description !== "string") {
    throw new TypeError("Invalid metadata draft.");
  }
  return Object.freeze({ displayName: value.displayName, description: value.description });
}
export function reviewMetadata(snapshot: MetadataEditorSnapshot, text: string, applicationId = snapshot.applicationId): MetadataReview {
  if (!isUuid(snapshot.applicationId) || snapshot.applicationId !== applicationId || !isStrongETag(snapshot.metadataETag) || !snapshot.canReplace) {
    throw new TypeError("Refresh current metadata and permission before review.");
  }
  const command = metadataCommand(readMetadataDraft(text));
  return Object.freeze({ applicationId: snapshot.applicationId, ifMatch: snapshot.metadataETag,
    before: Object.freeze({ ...snapshot.metadata }), command });
}
function closed(value: unknown, fields: readonly string[]): value is Record<string, unknown> {
  if (value === null || typeof value !== "object" || Array.isArray(value)
    || Object.getPrototypeOf(value) !== Object.prototype && Object.getPrototypeOf(value) !== null) { return false; }
  const keys = Reflect.ownKeys(value);
  return keys.length === fields.length && keys.every(key => typeof key === "string" && fields.includes(key))
    && Object.values(Object.getOwnPropertyDescriptors(value)).every(property => property.get === undefined && property.set === undefined);
}
