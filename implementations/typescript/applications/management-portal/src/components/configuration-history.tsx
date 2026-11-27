"use client";

import { useState } from "react";
import type { ConfigurationPublication, ConfigurationSource } from "@cislib/management-client";
import type { ConfigurationEditorSnapshot } from "../configuration-editor-model.ts";
import { Badge, Pager, ReadNotice, TechnicalDetails, Timestamp, queryUrl, ready, usePages, usePortalRead } from "./shared";

/** Existing definition-panel history is opt-in and performs its own scoped reads. */
export function ConfigurationPanelHistory({ applicationId, configurationId, schemaVersion }: { applicationId: string; configurationId: string; schemaVersion: string }) {
  const [open, setOpen] = useState(false), [source, setSource] = useState<"common" | "application">("application");
  const base = `/api/portal/applications/${applicationId}/configurations/${encodeURIComponent(configurationId)}`;
  const state = usePortalRead<ConfigurationEditorSnapshot>(open ? queryUrl(base + "/editor", { schemaVersion, source }) : null);
  const observed = ready(state);
  const value = observed?.context.applicationId === applicationId && observed.context.configurationId === configurationId
    && observed.context.schemaVersion === schemaVersion && observed.context.selectedSource === source ? observed : undefined;
  const slot = value?.context[source === "common" ? "commonSource" : "applicationSource"];
  const releaseAllowed = value?.actions.state === "available" && value.actions.binding.actions.some(action => action.operationId === "createConfigurationRelease" && action.supported && action.allowed);
  return <section><button onClick={() => setOpen(value => !value)}>{open ? "Close publication history" : "Publication history"}</button>
    {!open ? null : <><div className="metadata-actions"><button onClick={() => setSource("common")}>Common source history</button><button onClick={() => setSource("application")}>Application source history</button></div>
      <ReadNotice state={state} />{slot?.state !== "available" || value === undefined ? <p className="muted">This source history is unavailable or protected.</p>
        : <ConfigurationHistory applicationId={applicationId} configurationId={configurationId} schemaVersion={schemaVersion} source={source}
          sourceId={slot.sourceId} environment={value.context.environment} disabled={state.busy || state.stale === true}
          {...(!releaseAllowed ? {} : { choose: (publication: ConfigurationPublication) => window.location.assign(queryUrl(`/applications/${applicationId}/configurations/${encodeURIComponent(configurationId)}/edit`,
            { schemaVersion, source, publicationId: publication.publicationId })) })} />}</>}
  </section>;
}

export function ConfigurationHistory({ applicationId, configurationId, schemaVersion, source, sourceId, environment, choose, disabled = false }: {
  applicationId: string; configurationId: string; schemaVersion: string; source: "common" | "application"; sourceId: string; environment: string;
  choose?(publication: ConfigurationPublication): void; disabled?: boolean
}) {
  const [selected, select] = useState<string>();
  const pages = usePages(JSON.stringify(["configuration-history", applicationId, configurationId, schemaVersion, source, sourceId]), undefined);
  const base = `/api/portal/applications/${applicationId}/configurations/${encodeURIComponent(configurationId)}`;
  const history = usePortalRead<ConfigurationSource>(queryUrl(`${base}/source-history`, { schemaVersion, source, pageSize: "10", cursor: pages.cursor }));
  const content = usePortalRead<ConfigurationPublication>(selected === undefined ? null : queryUrl(`${base}/publication/${selected}`, { schemaVersion, source }));
  const observed = ready(history);
  const rows = observed !== undefined && observed.sourceId === sourceId && observed.configurationId === configurationId
    && observed.schemaVersion === schemaVersion && observed.environment === environment && observed.kind === source
    && (source === "common" ? observed.applicationId === undefined : observed.applicationId === applicationId) ? observed.publications : undefined;
  const value = rows === undefined || history.stale ? undefined : ready(content);
  const publication = value !== undefined && value.publicationId === selected && value.sourceId === sourceId
    && value.configurationId === configurationId && value.schemaVersion === schemaVersion && value.environment === environment
    && value.kind === source && (source === "common" ? value.applicationId === undefined : value.applicationId === applicationId) ? value : undefined;
  return <section className="card configuration-history" aria-label={`${source === "common" ? "Common" : "Application"} publication history`}>
    <h2>{source === "common" ? "Common" : "Application"} publication history</h2><p>Choose an exact immutable reference. UUID order is not currentness; opening history never publishes or assigns.</p>
    <ReadNotice state={history} restart={() => { select(undefined); pages.reset(); }} />
    {rows === undefined ? null : <><div className="table-frame"><table><thead><tr><th>Created</th><th>Actor</th><th>Reason</th><th>Content</th></tr></thead><tbody>
      {rows.items.map(item => <tr key={item.publicationId}><td><Timestamp value={item.createdAtUtc} /></td><td>{item.actor}</td><td>{item.reason}</td>
        <td><button className="item-link" disabled={disabled || history.busy || history.stale} onClick={() => select(item.publicationId)}>View immutable publication</button>
          <TechnicalDetails fields={[{ label: "Publication ID", value: item.publicationId }, { label: "Payload SHA-256", value: item.sha256 }]} /></td></tr>)}
    </tbody></table>{rows.items.length === 0 ? <p className="notice">No immutable publications on this source history page. This does not select source absence.</p> : null}</div>
      <Pager pages={pages} nextCursor={rows.nextCursor} busy={history.busy} changed={() => select(undefined)} label={`${source} publications`} /></>}
    {selected === undefined ? null : <><ReadNotice state={content} />{publication === undefined ? null : <section className="configuration-publication">
      <Badge>Immutable publication</Badge><p>{publication.kind} / {publication.environment} / schema {publication.schemaVersion}</p>
      <TechnicalDetails fields={[{ label: "Publication ID", value: publication.publicationId }, { label: "Source ID", value: publication.sourceId }, { label: "Payload SHA-256", value: publication.sha256 }]} />
      <details><summary>Exact immutable payload</summary><pre>{publication.payloadJson}</pre></details>
      {choose === undefined ? null : <button disabled={disabled || history.busy || history.stale || content.busy || content.stale}
        onClick={() => choose(publication)}>Use this exact {source === "common" ? "Common" : "Application"} publication</button>}
    </section>}</>}
  </section>;
}
