"use client";

import { useRef, useState } from "react";
import type { MouseEvent } from "react";
import type { ConfigurationRow, ConfigurationRowActionsSnapshot } from "../configuration-list-model.ts";
import { configurationAction, configurationOrigin } from "../configuration-list-model.ts";
import { configurationRoute } from "../portal-routes.ts";
import type { ListContext } from "./application-detail";
import { useProtectedPortal } from "./protected-context";
import { Badge, ReadNotice, Timestamp, queryUrl, ready, reportedLabel, usePortalRead } from "./shared";

export type { ConfigurationRow } from "../configuration-list-model.ts";

export function ConfigurationRows({ applicationId, applicationName, items, context, onOpen }: {
  applicationId: string; applicationName?: string | undefined; items: readonly ConfigurationRow[]; context: ListContext;
  onOpen(definition: ConfigurationRow, event: MouseEvent<HTMLButtonElement>): void;
}) {
  const { snapshot: session } = useProtectedPortal();
  const [menu, setMenu] = useState<{ scope: string; key: string }>();
  const [filter, setFilter] = useState({ scope: "", text: "" });
  const scope = JSON.stringify([applicationId, session.epoch, items.map(row => [row.configurationId, row.schemaVersion])]);
  const text = filter.scope === scope ? filter.text : "";
  const query = text.trim().toLocaleLowerCase();
  const visible = query === "" ? items : items.filter(item => `${item.configurationId} ${item.description ?? ""}`.toLocaleLowerCase().includes(query));
  return <><div className="configuration-list-filter"><label htmlFor="configuration-page-filter">Filter configurations on this page</label>
    <input id="configuration-page-filter" type="search" value={text} maxLength={256} onChange={event => { setFilter({ scope, text: event.target.value }); setMenu(undefined); }} /></div>
    <div className="table-frame configuration-table-frame"><table><thead><tr><th>Configuration</th><th>Source</th><th>Modified</th><th>Status</th><th><span className="sr-only">Actions</span></th></tr></thead><tbody>
    {visible.map(item => {
      const key = JSON.stringify([item.configurationId, item.schemaVersion]);
      const origin = configurationOrigin("origin" in item ? item.origin : undefined);
      const opened = menu?.scope === scope && menu.key === key;
      return <tr key={key}><td><button className="item-link" onClick={event => onOpen(item, event)}>{reportedLabel(item.configurationId, "Configuration")}</button>
        {item.description === undefined ? null : <span className="row-description">{item.description}</span>}</td>
        <td><Badge>{origin.label}</Badge></td><td>{"modifiedAtUtc" in item && typeof item.modifiedAtUtc === "string" ? <Timestamp value={item.modifiedAtUtc} compact /> : "Not available"}</td><td>{origin.status}</td><td><ConfigurationRowActions applicationId={applicationId} applicationName={applicationName}
          item={item} context={context} opened={opened} toggle={() => setMenu(opened ? undefined : { scope, key })} close={() => setMenu(undefined)} onOpen={onOpen} /></td></tr>;
    })}
  </tbody></table>{visible.length > 0 ? null : <p className="notice">No configurations match this filter on the current page.</p>}</div>
    <p className="footnote">Modified shows when the assigned JSON version was published.</p></>;
}

function ConfigurationRowActions({ applicationId, applicationName, item, context, opened, toggle, close, onOpen }: {
  applicationId: string; applicationName: string | undefined; item: ConfigurationRow; context: ListContext; opened: boolean;
  toggle(): void; close(): void; onOpen(definition: ConfigurationRow, event: MouseEvent<HTMLButtonElement>): void;
}) {
  const trigger = useRef<HTMLButtonElement>(null);
  const path = queryUrl(`/api/portal/applications/${applicationId}/configurations/${encodeURIComponent(item.configurationId)}/row-actions`, { schemaVersion: item.schemaVersion });
  const read = usePortalRead<ConfigurationRowActionsSnapshot>(opened ? path : null);
  const observed = ready(read);
  const data = observed !== undefined && observed.applicationId === applicationId && observed.configurationId === item.configurationId
    && observed.schemaVersion === item.schemaVersion && observed.bindingId === item.bindingId ? observed : undefined;
  const current = data !== undefined && !read.busy && !read.stale;
  const edit = configurationRoute({ applicationId, applicationName, configurationId: item.configurationId, schemaVersion: item.schemaVersion, view: "edit", source: "application", context });
  const route = (view: "assignment" | "adoption") => configurationRoute({ applicationId, applicationName, configurationId: item.configurationId, schemaVersion: item.schemaVersion, view, context });
  const canPublish = current && configurationAction(data, "application", "publishConfigurationSource");
  return <div className="configuration-row-menu" onKeyDown={event => { if (event.key === "Escape" && opened) { event.preventDefault(); event.stopPropagation(); close(); trigger.current?.focus(); } }}>
    <button type="button" ref={trigger} aria-label={`Actions for ${reportedLabel(item.configurationId, "Configuration")}`} aria-expanded={opened} onClick={toggle}>…</button>
    {!opened ? null : <div className="configuration-row-menu-items"><button className="item-link" onClick={event => { close(); onOpen(item, event); }}>View JSON</button>
      <ReadNotice state={read} />
      {canPublish ? <a href={edit}>{"origin" in item && (item.origin === "shared" || item.origin === "defaultsOnly") ? "Override" : "Edit"}</a> : <span className="muted">Edit unavailable</span>}
      {canPublish ? <a href={edit}>Publish…</a> : <span className="muted">Publish unavailable</span>}
      {current && configurationAction(data, "binding", "replaceConfigurationAssignments") ? <a href={route("assignment")}>Assign…</a> : <span className="muted">Assign unavailable</span>}
      <a href={route("adoption")}>View adoption</a>
    </div>}
  </div>;
}
