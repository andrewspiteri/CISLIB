"use client";

import Link from "next/link";
import { useRef, useState } from "react";
import type { ConfigurationAdoptionEntry, ConfigurationRelease, Page } from "@cislib/management-client";
import { adoptionPageMatches } from "../configuration-adoption-model.ts";
import { applicationRoute } from "../portal-routes.ts";
import type { ListContext } from "./application-detail";
import { Pager, ReadNotice, Shell, TechnicalDetails, isUnauthenticated, queryUrl, ready, reportedLabel, usePages, usePortalRead } from "./shared";
import { ConfigurationInstanceStatusTable } from "./configuration-instance-status";

export function ConfigurationAdoption({ applicationId, configurationId, schemaVersion, initialCursor, context }: {
  applicationId: string; configurationId: string; schemaVersion: string; initialCursor?: string; context: ListContext
}) {
  const [revision, refresh] = useState(0);
  const heading = useRef<HTMLHeadingElement>(null);
  const pages = usePages(JSON.stringify([applicationId, configurationId, schemaVersion, "adoption"]), initialCursor);
  const base = `/api/portal/applications/${applicationId}/configurations/${encodeURIComponent(configurationId)}`;
  const url = queryUrl(base + "/adoption", { schemaVersion, pageSize: "20", cursor: pages.cursor });
  const state = usePortalRead<Page<ConfigurationAdoptionEntry>>(url, revision,
    { requestKey: url, applicationId, definitionId: configurationId, schemaVersion });
  // Optional, independently authorized labels never gate the status read or cause a per-row scan.
  const metadata = usePortalRead<Page<ConfigurationRelease>>(queryUrl(base + "/releases", { schemaVersion, pageSize: "100" }), revision);
  const received = ready(state);
  const page = received !== undefined && adoptionPageMatches(received, applicationId, configurationId, schemaVersion) ? received : undefined;
  const releases = !metadata.busy && !metadata.stale ? ready(metadata)?.items : undefined;
  const mismatched = received !== undefined && page === undefined;
  const environment = page?.items[0]?.environment;
  const configurationLabel = reportedLabel(configurationId, "Configuration");
  return <Shell signedIn={!isUnauthenticated(state)} footer="Instance status">
    <Link className="back-link" href={applicationRoute({ applicationId, tab: "configuration", context })}>← Back to Configuration list</Link>
    <div className="page-heading"><div><div className="eyebrow">{environment ?? "Application Configuration"}</div>
      <h1 ref={heading} tabIndex={-1}>Instance status</h1><p>{configurationLabel}</p>
    </div><button onClick={() => refresh(value => value + 1)} disabled={state.busy}>Refresh</button></div>
    <div className="section-heading"><div><h2>Last reported Configuration per instance</h2>
      <p>Reports describe each instance's last observation. Stale or missing reports remain unknown.</p></div></div>
    <ReadNotice state={state} restart={() => pages.reset()} />
    {mismatched ? <div className="notice" role="alert"><strong>Instance status unavailable</strong><p>The returned observations do not match this Configuration. Refresh to read current state.</p></div> : null}
    {page === undefined ? null : <>
      <ConfigurationInstanceStatusTable rows={page.items} releases={releases} scope={JSON.stringify([url, revision])}
        earlierPage={state.stale === true} configurationLabel={configurationLabel} fallback={heading.current} />
      <Pager pages={pages} nextCursor={page.nextCursor} busy={state.busy} label={`${page.items.filter(row => row.registrationId !== undefined).length} instances on this page`} />
    </>}
    <TechnicalDetails fields={[{ label: "Application ID", value: applicationId }, { label: "Configuration ID", value: configurationId }, { label: "Schema version", value: schemaVersion }]} />
  </Shell>;
}
