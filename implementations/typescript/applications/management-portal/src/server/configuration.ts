import type { ConfigurationAssignmentCommand, ConfigurationCallResult, ConfigurationCandidatePreviewCommand, ConfigurationCreationCommand, ConfigurationEditorContext, ConfigurationPublication,
  ConfigurationPublicationCommand, ConfigurationRelease, ConfigurationReleaseCommand, ConfigurationSource, ConfigurationValidationCommand,
  ConfigurationPreviewCommand, PageRequest } from "@cislib/management-client";
import { ConfigurationWireError, decodeConfiguration, isCursor, isOperationKey, isOrderedInteger, isStrongETag, isUuid } from "@cislib/management-client";
import type { ConfigurationSchema } from "@cislib/management-client";
import type { ConfigurationEditorActions, ConfigurationEditorContent, ConfigurationEditorSnapshot, ConfigurationSharedOverrideSeed } from "../configuration-editor-model.ts";
import { projectConfigurationActions, projectConfigurationAssignment, projectConfigurationCandidate, projectConfigurationCohortPage,
  projectConfigurationEditor, projectConfigurationPreview, projectConfigurationPublication, projectConfigurationRelease,
  projectConfigurationReleasePage, projectConfigurationSource, projectConfigurationValidation } from "../configuration-editor-model.ts";
import { projectConfigurationCommand } from "../configuration-command-results.ts";
import { projectConfigurationAdoptionPage } from "../configuration-adoption-model.ts";
import { assignedConfigurationLayer, configurationLayerAccess, projectConfigurationDetail, projectConfigurationRowActions, projectConfigurationSummaryPage } from "../configuration-list-model.ts";
import type { ConfigurationDetailContent, ConfigurationDetailSnapshot } from "../configuration-list-model.ts";
import { projectConfigurationCreationContext } from "../configuration-creation-model.ts";
import { configurationVersionMatches, projectConfigurationVersion } from "../configuration-versions.ts";
import type { ConfigurationVersionContent } from "../configuration-versions.ts";
import { PortalRequestInvalid, readPortalJson, withPortalCall } from "./portal-call.ts";
import type { PortalCallScope } from "./portal-call.ts";

export function isConfigurationPath(path: readonly string[]): boolean {
  return path[0] === "configuration" || path[0] === "applications" && ["configurations", "configuration-summaries", "configuration-creations", "configuration-creation-operations"].includes(path[2] ?? "");
}

/** Finite typed routes only. Recovery never prefetches an editor or borrows its read permission. */
export function handleConfiguration(request: Request, path: readonly string[]): Promise<Response> {
  const submitting = request.method !== "GET";
  return withPortalCall(request, { mutating: submitting, requireContext: submitting }, async scope => {
    if (request.method === "GET" && request.body !== null) { throw new PortalRequestInvalid(); }
    const query = new URL(request.url).searchParams;
    if (path[0] === "applications" && isUuid(path[1]) && ["configuration-creations", "configuration-creation-operations"].includes(path[2] ?? "")) {
      onlyQuery(query, []);
      const applicationId = path[1]!;
      if (path[2] === "configuration-creations" && path.length === 4 && path[3] === "context" && request.method === "GET") {
        const value = clean(scope, await scope.runtime.configurationAdministration.getCreationContext(applicationId, scope.context));
        if (value instanceof Response) { return value; }
        if (value.applicationId !== applicationId) { return scope.failure("unavailable", "The creation scope could not be verified.", 503); }
        return scope.ready(projectConfigurationCreationContext(value));
      }
      if (path[2] === "configuration-creations" && path.length === 3 && request.method === "POST") {
        const { key, ifMatch } = commandHeaders(request);
        const command = await nativeCommand<ConfigurationCreationCommand>(request, scope, "ConfigurationCreationCommand");
        const result = await scope.runtime.configurationAdministration.createConfiguration(applicationId, key, ifMatch, command, scope.context);
        if (result.status === 401) { return scope.failure("unauthenticated", "Sign in to continue.", 401); }
        return scope.ready(projectConfigurationCommand(result, { family: "management.configuration.create", target: { applicationId } }, key));
      }
      if (path[2] === "configuration-creation-operations" && path.length === 4 && isOperationKey(path[3]) && request.method === "GET") {
        const result = await scope.runtime.configurationAdministration.getCreationOperation(applicationId, path[3]!, scope.context);
        if (result.status === 401) { return scope.failure("unauthenticated", "Sign in to continue.", 401); }
        return scope.ready(projectConfigurationCommand(result, { family: "management.configuration.create", target: { applicationId } }, path[3]!));
      }
      return scope.failure("not-found", "This Configuration creation operation is unavailable.", 404);
    }
    if (path[0] === "applications" && isUuid(path[1]) && path[2] === "configuration-summaries" && path.length === 3 && request.method === "GET") {
      const page = pageQuery(query);
      const value = clean(scope, await scope.runtime.configurationAdministration.getConfigurationSummaries(path[1]!, page, scope.context));
      if (value instanceof Response) { return value; }
      if (value.items.length > page.pageSize! || value.items.some(item => item.applicationId !== path[1])) {
        return scope.failure("unavailable", "The Configuration list scope could not be verified.", 503);
      }
      return scope.ready(projectConfigurationSummaryPage(value));
    }
    if (path[0] === "configuration") {
      if (path.length === 2 && request.method === "POST") {
        if (path[1] === "validate") {
          onlyQuery(query, []);
          const command = await nativeCommand<ConfigurationValidationCommand>(request, scope, "ConfigurationValidationCommand");
          const value = clean(scope, await scope.runtime.configuration.validate(command, scope.context));
          return value instanceof Response ? value : scope.ready(projectConfigurationValidation(value));
        }
        if (path[1] === "candidate-preview") {
          const page = pageQuery(query);
          const command = await nativeCommand<ConfigurationCandidatePreviewCommand>(request, scope, "ConfigurationCandidatePreviewCommand");
          const value = clean(scope, await scope.runtime.configurationAdministration.previewCandidate(command, page, scope.context));
          return value instanceof Response ? value : scope.ready(projectConfigurationCandidate(value));
        }
        if (path[1] === "preview") {
          const page = pageQuery(query);
          const command = await nativeCommand<ConfigurationPreviewCommand>(request, scope, "ConfigurationPreviewCommand");
          const value = clean(scope, await scope.runtime.configuration.preview(command, page, scope.context));
          return value instanceof Response ? value : scope.ready(projectConfigurationPreview(value));
        }
      }
      if (path.length === 3 && path[1] === "releases" && request.method === "GET" && isUuid(path[2])) {
        onlyQuery(query, []);
        const value = clean(scope, await scope.runtime.configuration.getRelease(path[2]!, scope.context));
        return value instanceof Response ? value : scope.ready(projectConfigurationRelease(value));
      }
      if (path[1] === "sources" && isUuid(path[2])) {
        onlyQuery(query, []);
        const sourceId = path[2]!;
        if (path.length === 4 && path[3] === "publications" && request.method === "POST") {
          const { key, ifMatch } = commandHeaders(request);
          const command = await nativeCommand<ConfigurationPublicationCommand>(request, scope, "ConfigurationPublicationCommand");
          const result = await scope.runtime.configuration.publishSource(sourceId, key, ifMatch, command, scope.context);
          if (result.status === 401) { return scope.failure("unauthenticated", "Sign in to continue.", 401); }
          return scope.ready(projectConfigurationCommand(result, { family: "management.configuration.source.publish", target: { sourceId } }, key));
        }
        if (path.length === 5 && path[3] === "publication-operations" && request.method === "GET" && isOperationKey(path[4])) {
          const result = await scope.runtime.configuration.getPublicationOperation(sourceId, path[4]!, scope.context);
          if (result.status === 401) { return scope.failure("unauthenticated", "Sign in to continue.", 401); }
          return scope.ready(projectConfigurationCommand(result, { family: "management.configuration.source.publish", target: { sourceId } }, path[4]!));
        }
      }
      return scope.failure("not-found", "This Configuration operation is unavailable.", 404);
    }
    if (path[0] !== "applications" || !isUuid(path[1]) || path[2] !== "configurations" || !isOperationKey(path[3])) {
      throw new PortalRequestInvalid("Select the exact application and Configuration.");
    }
    const applicationId = path[1]!, configurationId = path[3]!;
    const schemaVersion = query.get("schemaVersion");
    if (!isOrderedInteger(schemaVersion)) { throw new PortalRequestInvalid("Select the exact Configuration schema."); }
    if (path.length === 5 && path[4] === "releases" && request.method === "POST") {
      onlyQuery(query, ["schemaVersion"]);
      const { key, ifMatch } = commandHeaders(request);
      const command = await nativeCommand<ConfigurationReleaseCommand>(request, scope, "ConfigurationReleaseCommand");
      const result = await scope.runtime.configuration.createRelease(applicationId, configurationId, schemaVersion!, key, ifMatch, command, scope.context);
      if (result.status === 401) { return scope.failure("unauthenticated", "Sign in to continue.", 401); }
      return scope.ready(projectConfigurationCommand(result, { family: "management.configuration.release.create",
        target: { applicationId, configurationId, schemaVersion: schemaVersion! } }, key));
    }
    if (path.length === 6 && path[4] === "release-operations" && request.method === "GET" && isOperationKey(path[5])) {
      onlyQuery(query, ["schemaVersion"]);
      const result = await scope.runtime.configuration.getReleaseOperation(applicationId, configurationId, schemaVersion!, path[5]!, scope.context);
      if (result.status === 401) { return scope.failure("unauthenticated", "Sign in to continue.", 401); }
      return scope.ready(projectConfigurationCommand(result, { family: "management.configuration.release.create",
        target: { applicationId, configurationId, schemaVersion: schemaVersion! } }, path[5]!));
    }
    if (path.length === 5 && path[4] === "assignments" && request.method === "GET") {
      onlyQuery(query, ["schemaVersion"]);
      const value = clean(scope, await scope.runtime.configuration.getAssignments(applicationId, configurationId, schemaVersion!, scope.context));
      return value instanceof Response ? value : scope.ready(projectConfigurationAssignment(value));
    }
    if (path.length === 5 && path[4] === "assignments" && request.method === "PUT") {
      onlyQuery(query, ["schemaVersion"]);
      const { key, ifMatch } = commandHeaders(request);
      const command = await nativeCommand<ConfigurationAssignmentCommand>(request, scope, "ConfigurationAssignmentCommand");
      const result = await scope.runtime.configuration.replaceAssignments(applicationId, configurationId, schemaVersion!, key, ifMatch, command, scope.context);
      if (result.status === 401) { return scope.failure("unauthenticated", "Sign in to continue.", 401); }
      return scope.ready(projectConfigurationCommand(result, { family: "management.configuration.assignment.replace",
        target: { applicationId, configurationId, schemaVersion: schemaVersion! } }, key));
    }
    if (path.length === 6 && path[4] === "assignment-operations" && request.method === "GET" && isOperationKey(path[5])) {
      onlyQuery(query, ["schemaVersion"]);
      // Native recovery owns exact resource resolution; read/write permission is not a prefetch prerequisite.
      const result = await scope.runtime.configuration.getAssignmentOperation(applicationId, configurationId, schemaVersion!, path[5]!, scope.context);
      if (result.status === 401) { return scope.failure("unauthenticated", "Sign in to continue.", 401); }
      return scope.ready(projectConfigurationCommand(result, { family: "management.configuration.assignment.replace",
        target: { applicationId, configurationId, schemaVersion: schemaVersion! } }, path[5]!));
    }
    if (path.length === 5 && path[4] === "binding-actions" && request.method === "GET") {
      onlyQuery(query, ["schemaVersion"]);
      const value = clean(scope, await scope.runtime.configurationAdministration.getBindingActions(applicationId, configurationId, schemaVersion!, scope.context));
      return value instanceof Response ? value : scope.ready(projectConfigurationActions(value));
    }
    if (path.length === 5 && path[4] === "releases" && request.method === "GET") {
      const value = clean(scope, await scope.runtime.configurationAdministration.getReleases(applicationId, configurationId, schemaVersion!, pageQuery(query, ["schemaVersion"]), scope.context));
      return value instanceof Response ? value : scope.ready(projectConfigurationReleasePage(value));
    }
    if (path.length === 5 && path[4] === "cohorts" && request.method === "GET") {
      const page = pageQuery(query, ["schemaVersion"]);
      const value = clean(scope, await scope.runtime.configurationAdministration.getCohorts(applicationId, configurationId, schemaVersion!, page, scope.context));
      if (value instanceof Response) { return value; }
      if (value.items.length > page.pageSize!) { return scope.failure("unavailable", "Current cohort choices could not be verified.", 503); }
      return scope.ready(projectConfigurationCohortPage(value));
    }
    if (path.length === 5 && path[4] === "adoption" && request.method === "GET") {
      const value = clean(scope, await scope.runtime.configurationAdministration.getBindingStatus(applicationId, configurationId, schemaVersion!, pageQuery(query, ["schemaVersion"]), scope.context));
      // The native read independently authorizes and resolves desired state; no map/editor/application prefetch.
      return value instanceof Response ? value : scope.ready(projectConfigurationAdoptionPage(value));
    }
    if (request.method !== "GET") { return scope.failure("invalid", "This Configuration method is unavailable.", 405); }
    if (path.length === 5 && path[4] === "row-actions") {
      onlyQuery(query, ["schemaVersion"]);
      const context = clean(scope, await scope.runtime.configurationAdministration.getBindingEditorContext(applicationId, configurationId, schemaVersion!, "application", scope.context));
      if (context instanceof Response) { return context; }
      const actions = await detailActions(scope, context);
      return actions instanceof Response ? actions : scope.ready(projectConfigurationRowActions({ applicationId, configurationId, schemaVersion: schemaVersion!, bindingId: context.bindingId, actions }));
    }
    if (path.length === 5 && path[4] === "details") {
      onlyQuery(query, ["schemaVersion", "source"]);
      const selected = query.get("source");
      if (selected !== null && selected !== "common" && selected !== "application") { throw new PortalRequestInvalid(); }
      const context = clean(scope, await scope.runtime.configurationAdministration.getBindingEditorContext(applicationId, configurationId, schemaVersion!, "application", scope.context));
      if (context instanceof Response) { return context; }
      return details(scope, context, selected ?? undefined);
    }
    if (path.length === 6 && path[4] === "versions" && isUuid(path[5])) {
      onlyQuery(query, ["schemaVersion"]);
      const release = clean(scope, await scope.runtime.configuration.getRelease(path[5]!, scope.context));
      if (release instanceof Response) { return release; }
      if (release.releaseId !== path[5] || !configurationVersionMatches(release, applicationId, configurationId, schemaVersion!)) {
        return scope.failure("unavailable", "The saved version scope could not be verified.", 503);
      }
      return savedVersion(scope, release);
    }
    const historical = path[4] === "source-history" || path[4] === "publication";
    onlyQuery(query, historical && path[4] === "source-history" ? ["schemaVersion", "source", "pageSize", "cursor"] : ["schemaVersion", "source"]);
    const selected = query.get("source");
    if (selected !== "common" && selected !== "application") { throw new PortalRequestInvalid("Choose Common or Application source."); }
    const context = clean(scope, await scope.runtime.configurationAdministration.getBindingEditorContext(applicationId, configurationId, schemaVersion!, selected, scope.context));
    if (context instanceof Response) { return context; }
    if (path.length === 5 && path[4] === "editor") { return editor(scope, context); }
    const slot = selected === "common" ? context.commonSource : context.applicationSource;
    const sourceId = slot.state === "available" || slot.state === "unavailable" ? slot.sourceId : undefined;
    if (sourceId === undefined) { return scope.failure("denied", "This source is unavailable in the selected scope.", 403); }
    if (path.length === 5 && path[4] === "source-history") {
      const value = clean(scope, await scope.runtime.configuration.getSource(sourceId, pageQuery(query, ["schemaVersion", "source"]), scope.context));
      if (value instanceof Response) { return value; }
      if (!sourceMatches(context, value)) { return scope.failure("unavailable", "The source scope could not be verified.", 503); }
      return scope.ready(projectConfigurationSource(value));
    }
    if (path.length === 6 && path[4] === "publication" && isUuid(path[5])) {
      const value = clean(scope, await scope.runtime.configuration.getPublication(path[5]!, scope.context));
      if (value instanceof Response) { return value; }
      if (!sourceMatches(context, value)) { return scope.failure("unavailable", "The publication scope could not be verified.", 503); }
      return scope.ready(projectConfigurationPublication(value));
    }
    return scope.failure("not-found", "This Configuration view is unavailable.", 404);
  });
}

/** A selected row resolves one actual default layer. It never manufactures merged/effective JSON. */
async function details(scope: PortalCallScope, context: ConfigurationEditorContext, selected?: "common" | "application"): Promise<Response> {
  const assignment = clean(scope, await scope.runtime.configuration.getAssignments(context.applicationId, context.configurationId, context.schemaVersion, scope.context));
  if (assignment instanceof Response) { return assignment; }
  if (assignment.bindingId !== context.bindingId || assignment.applicationId !== context.applicationId || assignment.configurationId !== context.configurationId
    || assignment.schemaVersion !== context.schemaVersion || assignment.environment !== context.environment) {
    return scope.failure("unavailable", "The assignment scope could not be verified.", 503);
  }
  const actions = await detailActions(scope, context);
  if (actions instanceof Response) { return actions; }
  let origin = "unassigned";
  let content: ConfigurationDetailContent = { state: "unassigned", message: "No default release is assigned. There is no assigned layer JSON to show." };
  if (assignment.defaultReleaseId !== undefined) {
    const release = clean(scope, await scope.runtime.configuration.getRelease(assignment.defaultReleaseId, scope.context));
    if (release instanceof Response) {
      if (release.status === 401) { return release; }
      origin = release.status === 403 ? "protected" : "unavailable";
      content = { state: release.status === 403 ? "redacted" : "unavailable", message: "The assigned release is protected or unavailable. Its layers are not inferred from history." };
    } else {
      if (release.releaseId !== assignment.defaultReleaseId || !releaseMatches(context, release)) { return scope.failure("unavailable", "The assigned release scope could not be verified.", 503); }
      const chosen = assignedConfigurationLayer(release, selected);
      origin = chosen.origin;
      const { layer, reference } = chosen;
      const slot = layer === "application" ? context.applicationSource : context.commonSource;
      if (reference.state === "absent") { content = { state: "absent", message: "The assigned release has no publication in this layer. Declared defaults and other layers are separate." }; }
      else if (slot.state !== "available") { content = { state: slot.state === "redacted" ? "redacted" : "unavailable", message: "This assigned layer is protected or unavailable." }; }
      else {
        let access = configurationLayerAccess(context, layer);
        if (access === undefined) {
          const selectedContext = clean(scope, await scope.runtime.configurationAdministration.getBindingEditorContext(context.applicationId,
            context.configurationId, context.schemaVersion, layer, scope.context));
          if (selectedContext instanceof Response) {
            if (selectedContext.status === 401) { return selectedContext; }
            access = selectedContext.status === 403 ? "redacted" : "unavailable";
          } else {
            if (!sameDetailBinding(context, selectedContext) || selectedContext.selectedSource !== layer) {
              return scope.failure("unavailable", "The selected layer scope could not be verified.", 503);
            }
            access = configurationLayerAccess(selectedContext, layer) ?? "unavailable";
          }
        }
        if (access !== "available") { content = { state: access, message: access === "redacted" ? "This assigned layer's JSON is protected." : "This assigned layer's JSON is unavailable." }; }
        else {
          const publication = clean(scope, await scope.runtime.configuration.getPublication(reference.publicationId, scope.context));
          if (publication instanceof Response) {
            if (publication.status === 401) { return publication; }
            content = { state: publication.status === 403 ? "redacted" : "unavailable", message: "Assigned JSON is protected or unavailable. It has not been replaced with an empty object." };
          } else {
            if (publication.publicationId !== reference.publicationId || !sourceMatches({ ...context, selectedSource: layer }, publication)) {
              return scope.failure("unavailable", "The assigned publication scope could not be verified.", 503);
            }
            content = { state: "present", layer, publication };
          }
        }
      }
    }
  }
  return scope.ready(projectConfigurationDetail({ applicationId: context.applicationId, application: context.application, environment: context.environment,
    configurationId: context.configurationId, schemaVersion: context.schemaVersion, bindingId: context.bindingId,
    hostApplicationName: context.hostApplicationName, ...(context.description === undefined ? {} : { description: context.description }), origin,
    hasCohortOverrides: assignment.overrides.length > 0, ...(assignment.defaultReleaseId === undefined ? {} : { defaultReleaseId: assignment.defaultReleaseId }),
    ...(assignment.generation === undefined ? {} : { generation: assignment.generation }), assignmentETag: assignment.eTag,
    ...(context.applicationSource.state === "available" ? { applicationSourceId: context.applicationSource.sourceId } : {}),
    ...(context.commonSource.state === "available" ? { commonSourceId: context.commonSource.sourceId } : {}), content, actions }));
}

/** Inspect one exact immutable version. Content denial does not add a read gate to independent deployment commands. */
async function savedVersion(scope: PortalCallScope, release: ConfigurationRelease): Promise<Response> {
  const layer = release.applicationSource.state === "present" ? "application" : "common";
  const reference = layer === "application" ? release.applicationSource : release.commonSource;
  let content: ConfigurationVersionContent;
  if (reference.state === "absent") {
    content = { state: "absent", message: "This saved version contains no JSON layer. Declared defaults are separate." };
  } else {
    const context = clean(scope, await scope.runtime.configurationAdministration.getBindingEditorContext(release.applicationId, release.configurationId,
      release.schemaVersion, layer, scope.context));
    if (context instanceof Response) {
      if (context.status === 401) { return context; }
      content = { state: context.status === 403 ? "redacted" : "unavailable", message: "This saved version's JSON is protected or unavailable." };
    } else if (!configurationVersionMatches(release, context.applicationId, context.configurationId, context.schemaVersion, context.environment)
      || context.selectedSource !== layer) {
      return scope.failure("unavailable", "The saved JSON source scope could not be verified.", 503);
    } else {
      const access = configurationLayerAccess(context, layer);
      if (access !== "available") {
        content = { state: access === "redacted" ? "redacted" : "unavailable", message: "This saved version's JSON is protected or unavailable." };
      } else {
        const publication = clean(scope, await scope.runtime.configuration.getPublication(reference.publicationId, scope.context));
        if (publication instanceof Response) {
          if (publication.status === 401) { return publication; }
          content = { state: publication.status === 403 ? "redacted" : "unavailable", message: "The exact saved publication could not be read." };
        } else if (publication.publicationId !== reference.publicationId || !sourceMatches(context, publication)) {
          return scope.failure("unavailable", "The saved publication scope could not be verified.", 503);
        } else { content = { state: "present", layer, publication }; }
      }
    }
  }
  return scope.ready(projectConfigurationVersion({ release, content }));
}

function sameDetailBinding(left: ConfigurationEditorContext, right: ConfigurationEditorContext): boolean {
  return left.bindingId === right.bindingId && left.applicationId === right.applicationId && left.application === right.application && left.environment === right.environment
    && left.configurationId === right.configurationId && left.schemaVersion === right.schemaVersion && left.hostApplicationName === right.hostApplicationName
    && left.commonSource.state === right.commonSource.state && left.commonSource.sourceId === right.commonSource.sourceId
    && left.applicationSource.state === right.applicationSource.state && left.applicationSource.sourceId === right.applicationSource.sourceId;
}

async function detailActions(scope: PortalCallScope, context: ConfigurationEditorContext): Promise<ConfigurationDetailSnapshot["actions"] | Response> {
  const binding = clean(scope, await scope.runtime.configurationAdministration.getBindingActions(context.applicationId, context.configurationId, context.schemaVersion, scope.context));
  if (binding instanceof Response && binding.status === 401) { return binding; }
  let applicationActions;
  if (context.applicationSource.state === "available") {
    const value = clean(scope, await scope.runtime.configurationAdministration.getSourceActions(context.applicationSource.sourceId, scope.context));
    if (value instanceof Response && value.status === 401) { return value; }
    if (!(value instanceof Response) && value.resourceId === context.applicationSource.sourceId) { applicationActions = value; }
  }
  return binding instanceof Response || binding.resourceId !== context.bindingId ? { state: "unavailable" }
    : { state: "available", binding, ...(applicationActions === undefined ? {} : { application: applicationActions }) };
}

async function editor(scope: PortalCallScope, context: ConfigurationEditorContext): Promise<Response> {
  const slot = context.selectedSource === "common" ? context.commonSource : context.applicationSource;
  const unavailable = (message: string) => scope.ready(projectConfigurationEditor({ context,
    content: { state: "unavailable", message }, actions: { state: "unavailable", message } }));
  if (slot.state !== "available") { return unavailable(slot.state === "redacted" ? "This source is protected." : "This source is unavailable for authoring."); }
  const source = clean(scope, await scope.runtime.configuration.getSource(slot.sourceId, { pageSize: 100 }, scope.context));
  if (source instanceof Response) { return source; }
  if (!sourceMatches(context, source)) { return scope.failure("unavailable", "The source scope could not be verified.", 503); }
  const assignment = clean(scope, await scope.runtime.configuration.getAssignments(context.applicationId, context.configurationId, context.schemaVersion, scope.context));
  if (assignment instanceof Response) { return assignment; }
  if (assignment.bindingId !== context.bindingId || assignment.environment !== context.environment) { return scope.failure("unavailable", "The assignment scope could not be verified.", 503); }
  const sourceActions = clean(scope, await scope.runtime.configurationAdministration.getSourceActions(slot.sourceId, scope.context));
  if (sourceActions instanceof Response) { return sourceActions; }
  const bindingActions = clean(scope, await scope.runtime.configurationAdministration.getBindingActions(context.applicationId, context.configurationId, context.schemaVersion, scope.context));
  if (bindingActions instanceof Response) { return bindingActions; }
  if (sourceActions.resourceId !== slot.sourceId || bindingActions.resourceId !== context.bindingId) { return scope.failure("unavailable", "Current action scope could not be verified.", 503); }
  const actions: ConfigurationEditorActions = { state: "available", source: sourceActions, binding: bindingActions };
  let baselineRelease: ConfigurationRelease | undefined;
  let sharedOverrideSeed: ConfigurationSharedOverrideSeed | undefined;
  let content: ConfigurationEditorContent = { state: "unselected", message: "No default release is assigned. Choose exact publication references or deliberately start a new override." };
  if (assignment.defaultReleaseId !== undefined) {
    const value = clean(scope, await scope.runtime.configuration.getRelease(assignment.defaultReleaseId, scope.context));
    if (value instanceof Response) { return value; }
    if (!releaseMatches(context, value)) { return scope.failure("unavailable", "The assigned release scope could not be verified.", 503); }
    baselineRelease = value;
    const reference = context.selectedSource === "common" ? value.commonSource : value.applicationSource;
    if (reference.state === "absent") {
      content = { state: "absent" };
      if (context.selectedSource === "application" && value.commonSource.state === "present") {
        const seed = await assignedSharedOverride(scope, context, value.commonSource.publicationId!);
        if (seed instanceof Response) { return seed; }
        sharedOverrideSeed = seed;
      }
    }
    else if (context.defaults.state !== "available") { content = { state: "unavailable", message: "Selected source values or declared defaults are protected or unavailable." }; }
    else {
      const publication = clean(scope, await scope.runtime.configuration.getPublication(reference.publicationId, scope.context));
      if (publication instanceof Response) { return publication; }
      if (!sourceMatches(context, publication)) { return scope.failure("unavailable", "The assigned publication scope could not be verified.", 503); }
      content = { state: "present", publication };
    }
  }
  const snapshot: ConfigurationEditorSnapshot = { context, source, assignment, content, actions,
    ...(baselineRelease === undefined ? {} : { baselineRelease }), ...(sharedOverrideSeed === undefined ? {} : { sharedOverrideSeed }) };
  return scope.ready(projectConfigurationEditor(snapshot));
}

/** Copying a Shared layer requires its own current native ContentRead; Application rights never stand in for it. */
async function assignedSharedOverride(scope: PortalCallScope, context: ConfigurationEditorContext, publicationId: string): Promise<ConfigurationSharedOverrideSeed | Response> {
  const unavailable = (message: string): ConfigurationSharedOverrideSeed => ({ state: "unavailable", message });
  const selected = clean(scope, await scope.runtime.configurationAdministration.getBindingEditorContext(context.applicationId,
    context.configurationId, context.schemaVersion, "common", scope.context));
  if (selected instanceof Response) {
    if (selected.status === 401) { return selected; }
    return selected.status === 403 ? { state: "redacted", message: "Assigned Shared JSON is protected and could not be copied into an override." }
      : unavailable("Assigned Shared JSON is unavailable. No empty override was substituted.");
  }
  if (!sameDetailBinding(context, selected) || selected.selectedSource !== "common") {
    return unavailable("The assigned Shared source no longer matches this application. Nothing was copied.");
  }
  const access = configurationLayerAccess(selected, "common");
  if (access !== "available") {
    return access === "redacted" ? { state: "redacted", message: "Assigned Shared JSON is protected and could not be copied into an override." }
      : unavailable("Assigned Shared JSON is unavailable. No empty override was substituted.");
  }
  const publication = clean(scope, await scope.runtime.configuration.getPublication(publicationId, scope.context));
  if (publication instanceof Response) {
    if (publication.status === 401) { return publication; }
    return publication.status === 403 ? { state: "redacted", message: "Assigned Shared JSON is protected and could not be copied into an override." }
      : unavailable("The assigned Shared publication is unavailable. Nothing was copied from history.");
  }
  if (publication.publicationId !== publicationId || !sourceMatches(selected, publication)) {
    return unavailable("The assigned Shared publication scope could not be verified. Nothing was copied.");
  }
  return { state: "present", publication };
}

function sourceMatches(context: ConfigurationEditorContext, value: ConfigurationSource | ConfigurationPublication): boolean {
  const slot = context.selectedSource === "common" ? context.commonSource : context.applicationSource;
  return (slot.state === "available" || slot.state === "unavailable") && slot.sourceId !== undefined
    && value.sourceId === slot.sourceId && value.kind === context.selectedSource && value.configurationId === context.configurationId
    && value.schemaVersion === context.schemaVersion && value.environment === context.environment
    && (value.kind === "common" ? value.applicationId === undefined : value.applicationId === context.applicationId);
}
function releaseMatches(context: ConfigurationEditorContext, value: ConfigurationRelease): boolean {
  return value.applicationId === context.applicationId && value.configurationId === context.configurationId
    && value.schemaVersion === context.schemaVersion && value.environment === context.environment;
}
function clean<T>(scope: PortalCallScope, result: ConfigurationCallResult<T>): T | Response {
  if (result.kind === "http" && result.status === 200 && result.value !== undefined && result.cleanupSucceeded) { return result.value; }
  if (result.status === 401) { return scope.failure("unauthenticated", "Sign in to continue.", 401); }
  if (result.status === 403) { return scope.failure("denied", "This Configuration operation is protected.", 403); }
  if (result.status === 404) { return scope.failure("not-found", "This Configuration target is unavailable.", 404); }
  if (result.status === 501) { return scope.failure("unavailable", "This Configuration operation is not supported by the service.", 501); }
  return scope.failure("unavailable", "Current Configuration data could not be verified. Try Refresh.", result.kind === "timeout" ? 504 : 503);
}
function commandHeaders(request: Request): { key: string; ifMatch: string } {
  const key = request.headers.get("Idempotency-Key"), ifMatch = request.headers.get("If-Match");
  if (!isOperationKey(key) || !isStrongETag(ifMatch)) { throw new PortalRequestInvalid("Supply the original key and exact current precondition."); }
  return { key: key!, ifMatch: ifMatch! };
}
async function nativeCommand<T>(request: Request, scope: PortalCallScope, schema: ConfigurationSchema): Promise<T> {
  const raw = await readPortalJson(request, scope.signal, 1048576);
  try { return await decodeConfiguration<T>(schema, raw, "request", {}, scope.signal); }
  catch (error) {
    if (error instanceof ConfigurationWireError) { scope.signal.throwIfAborted(); throw new PortalRequestInvalid("The Configuration command is invalid."); }
    throw error;
  }
}
function onlyQuery(query: URLSearchParams, fields: readonly string[]): void {
  for (const name of query.keys()) { if (!fields.includes(name) || query.getAll(name).length !== 1) { throw new PortalRequestInvalid(); } }
}
function pageQuery(query: URLSearchParams, extra: readonly string[] = []): PageRequest {
  onlyQuery(query, [...extra, "pageSize", "cursor"]);
  const size = query.get("pageSize"), cursor = query.get("cursor");
  if (size !== null && (!/^[1-9][0-9]{0,2}$/u.test(size) || Number(size) > 100) || cursor !== null && !isCursor(cursor)) { throw new PortalRequestInvalid(); }
  return { pageSize: size === null ? 100 : Number(size), ...(cursor === null ? {} : { cursor }) };
}
