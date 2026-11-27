import type { ConfigurationPublication } from "@cislib/management-client";
import type { ConfigurationEditorSnapshot } from "./configuration-editor-model.ts";
import type { ConfigurationInitialDraft } from "./configuration-creation-model.ts";
import { initialConfigurationDraftMatches } from "./configuration-creation-model.ts";
import { captureConfigurationDraft } from "./configuration-draft.ts";
import type { ProtectedStateOwner } from "./protected-state.ts";

/** Local text editing is independent of permission to dispatch another native operation. */
export function configurationEditingAccess(value: ConfigurationEditorSnapshot | undefined, state: {
  readonly ownerState: string; readonly blocked: boolean; readonly busy: boolean; readonly fresh: boolean;
  readonly originalsRetained: boolean; readonly receiptStorageReady: boolean;
}): { readonly canEdit: boolean; readonly canPublish: boolean } {
  const slot = value?.context.applicationSource;
  const allowed = value?.actions.state === "available" && value.actions.source.resourceId === slot?.sourceId
    && value.actions.source.actions.some(item => item.operationId === "publishConfigurationSource" && item.supported && item.allowed);
  const canEdit = value?.context.selectedSource === "application" && slot?.state === "available"
    && value.source?.sourceId === slot.sourceId && value.context.defaults.state === "available" && value.content.state !== "unavailable"
    && allowed === true && !state.blocked && !state.busy && ["ready", "loading", "stale"].includes(state.ownerState);
  const release = value?.baselineRelease;
  const sharedCopyReady = release === undefined || release.applicationSource.state !== "absent" || release.commonSource.state !== "present"
    || value?.sharedOverrideSeed?.state === "present";
  return Object.freeze({ canEdit, canPublish: canEdit && state.fresh && sharedCopyReady && !state.originalsRetained && state.receiptStorageReady });
}

/** Initialize once from exact native text; refresh never overwrites a retained draft or its review intent. */
export function initializeConfigurationEditorDraft(owner: ProtectedStateOwner<ConfigurationEditorSnapshot>,
  initial?: ConfigurationInitialDraft, sessionKey?: string): { readonly kind: "started" | "retained" } | { readonly kind: "blocked"; readonly message: string } {
  if (owner.view.draft !== undefined || owner.view.intentDirty) { return Object.freeze({ kind: "retained" }); }
  const value = owner.view.snapshot?.value;
  if (!owner.view.canWrite || value === undefined || value.context.selectedSource !== "application"
    || value.context.defaults.state !== "available" || value.content.state === "unavailable") {
    return Object.freeze({ kind: "blocked", message: "Current Application editing context is unavailable." });
  }
  let raw: string;
  let dirty = false;
  if (initial !== undefined) {
    if (sessionKey === undefined || !initialConfigurationDraftMatches(initial, value, sessionKey)) {
      return Object.freeze({ kind: "blocked", message: "The retained JSON no longer matches this session and source." });
    }
    raw = initial.raw;
    dirty = true;
  } else if (value.content.state === "present") {
    if (!publicationMatches(value.content.publication, value, "application") || value.baselineRelease?.applicationSource.state !== "present"
      || value.content.publication.publicationId !== value.baselineRelease.applicationSource.publicationId) {
      return Object.freeze({ kind: "blocked", message: "The assigned Application JSON scope could not be verified." });
    }
    raw = value.content.publication.payloadJson;
  } else if (value.baselineRelease?.applicationSource.state === "absent" && value.baselineRelease.commonSource.state === "present") {
    const seed = value.sharedOverrideSeed;
    if (seed?.state !== "present") {
      return Object.freeze({ kind: "blocked", message: seed?.message ?? "The assigned Shared JSON could not be copied. No empty override was substituted." });
    }
    if (seed.publication.publicationId !== value.baselineRelease.commonSource.publicationId || !publicationMatches(seed.publication, value, "common")) {
      return Object.freeze({ kind: "blocked", message: "The assigned Shared JSON scope could not be verified. Nothing was copied." });
    }
    raw = seed.publication.payloadJson;
    dirty = true;
  } else {
    raw = "{}";
  }
  try { captureConfigurationDraft(raw); }
  catch { return Object.freeze({ kind: "blocked", message: "The exact initial JSON exceeds the editable source limits." }); }
  if (owner.startDraft(raw).kind !== "changed") {
    return Object.freeze({ kind: "blocked", message: "The selected JSON could not become an editable draft." });
  }
  if (dirty) { owner.setIntentDirty(true); }
  return Object.freeze({ kind: "started" });
}

function publicationMatches(publication: ConfigurationPublication, value: ConfigurationEditorSnapshot, kind: "common" | "application"): boolean {
  const context = value.context, slot = kind === "common" ? context.commonSource : context.applicationSource;
  return slot.state === "available" && publication.sourceId === slot.sourceId && publication.kind === kind
    && publication.configurationId === context.configurationId && publication.schemaVersion === context.schemaVersion
    && publication.environment === context.environment && (kind === "common" ? publication.applicationId === undefined : publication.applicationId === context.applicationId);
}
