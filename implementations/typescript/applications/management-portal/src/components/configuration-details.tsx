"use client";

import { useState } from "react";
import type { ConfigurationDetailSnapshot } from "../configuration-list-model.ts";
import { configurationAction, configurationDetailMatches, configurationOrigin } from "../configuration-list-model.ts";
import { formatConfigurationJson } from "../configuration-draft.ts";
import type { PortalDefinition } from "../portal-model.ts";
import { configurationRoute } from "../portal-routes.ts";
import type { ListContext } from "./application-detail";
import { ConfigurationHistory } from "./configuration-history";
import { useProtectedPortal } from "./protected-context";
import { Badge, ReadNotice, TechnicalDetails, Timestamp, queryUrl, ready, usePortalRead } from "./shared";

/** Exact target read only; optional list metadata supplies labels, never authority or content. */
export function ConfigurationDetails({ applicationId, configurationId, schemaVersion, context, definition, source }: {
  applicationId: string; configurationId: string; schemaVersion: string; context: ListContext;
  definition?: Pick<PortalDefinition, "configurationId" | "schemaVersion" | "description"> | undefined; source?: "common" | "application" | undefined;
}) {
  const { snapshot: session } = useProtectedPortal();
  const [history, openHistory] = useState<{ scope: string; source: "common" | "application" }>();
  const url = queryUrl(`/api/portal/applications/${applicationId}/configurations/${encodeURIComponent(configurationId)}/details`, { schemaVersion, source });
  const read = usePortalRead<ConfigurationDetailSnapshot>(url, 0, { requestKey: url, applicationId, definitionId: configurationId, schemaVersion });
  const observed = ready(read);
  const data = observed !== undefined && configurationDetailMatches(observed, applicationId, configurationId, schemaVersion) ? observed : undefined;
  const current = data !== undefined && !read.busy && !read.stale;
  const scope = JSON.stringify([url, session.epoch, data?.commonSourceId, data?.applicationSourceId]);
  const historySource = history?.scope === scope ? history.source : undefined;
  const historySourceId = historySource === "common" ? data?.commonSourceId : historySource === "application" ? data?.applicationSourceId : undefined;
  const route = (view: "edit" | "assignment" | "adoption", selected?: "common" | "application") => configurationRoute({ applicationId,
    applicationName: data?.application, configurationId, schemaVersion, view, source: selected, context });
  const content = data?.content;
  const formatted = content?.state === "present" ? formatConfigurationJson(content.publication.payloadJson) : undefined;
  const description = data?.description ?? (definition?.configurationId === configurationId && definition.schemaVersion === schemaVersion ? definition.description : undefined);
  return <><ReadNotice state={read} />{data === undefined ? null : <>
    <Badge>{configurationOrigin(data.origin).label}</Badge><p className="muted">{data.application} / {data.environment}</p>
    {description === undefined ? null : <p>{description}</p>}
    {content?.state !== "present" ? <p className="notice">{content?.message}</p> : <section aria-label="Assigned layer JSON">
      <h3>{content.layer === "common" ? "Assigned shared JSON" : "Application override JSON"}</h3>
      <p className="muted">Modified: <Timestamp value={content.publication.createdAtUtc} /> · this assigned JSON version's publication time</p>
      <p className="muted">One assigned layer, separate from merged configuration and accepted runtime values.</p>
      <pre className="configuration-json-readonly">{formatted?.valid ? formatted.json : content.publication.payloadJson}</pre>
    </section>}
    {data.hasCohortOverrides ? <p className="muted">Cohort overrides are present. This panel shows the default assignment's selected layer.</p> : null}
    <div className="metadata-actions">
      {current && configurationAction(data, "application", "publishConfigurationSource") ? <a className="item-link" href={route("edit", "application")}>{data.origin === "shared" || data.origin === "defaultsOnly" ? "Create Application override" : "Edit Application JSON"}</a> : null}
      {current && configurationAction(data, "binding", "replaceConfigurationAssignments") ? <a className="item-link" href={route("assignment")}>Manage assignment</a> : null}
      <a className="item-link" href={route("adoption")}>View adoption</a>
    </div>
    {data.applicationSourceId === undefined && data.commonSourceId === undefined ? null : <details className="configuration-defaults" key={scope}
      onToggle={event => { if (!event.currentTarget.open) { openHistory(undefined); } }}><summary>Publication history</summary>
      <div className="metadata-actions">{data.applicationSourceId === undefined ? null : <button disabled={!current} aria-pressed={historySource === "application"}
        onClick={() => openHistory({ scope, source: "application" })}>Application publications</button>}
        {data.commonSourceId === undefined ? null : <button disabled={!current} aria-pressed={historySource === "common"}
          onClick={() => openHistory({ scope, source: "common" })}>Shared publications</button>}</div>
      {historySource === undefined || historySourceId === undefined ? null : <ConfigurationHistory key={`${scope}:${historySource}`} applicationId={applicationId} configurationId={configurationId}
        schemaVersion={schemaVersion} source={historySource} sourceId={historySourceId} environment={data.environment} disabled={!current} />}
    </details>}
    <TechnicalDetails fields={[{ label: "Application ID", value: applicationId }, { label: "Configuration ID", value: configurationId }, { label: "Schema version", value: schemaVersion },
      { label: "Binding ID", value: data.bindingId }, { label: "Configuration host", value: data.hostApplicationName }, { label: "Application source ID", value: data.applicationSourceId },
      { label: "Shared source ID", value: data.commonSourceId }, { label: "Default release ID", value: data.defaultReleaseId }, { label: "Assignment generation", value: data.generation },
      { label: "Assignment precondition", value: data.assignmentETag }, { label: "Publication ID", value: content?.state === "present" ? content.publication.publicationId : undefined },
      { label: "Payload SHA-256", value: content?.state === "present" ? content.publication.sha256 : undefined }]} />
  </>}</>;
}
