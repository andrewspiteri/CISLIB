"use client";

import Link from "next/link";
import { useState } from "react";
import type { FormEvent } from "react";
import type { PortalApplication, PortalPage } from "../portal-model";
import { Badge, Empty, Pager, ReadNotice, Shell, isUnauthenticated, queryUrl, ready, usePages, usePortalRead } from "./shared";

export function Applications({ initialQuery = "", initialEnvironment = "", initialCursor }: {
  initialQuery?: string; initialEnvironment?: string; initialCursor?: string
}) {
  const [query, setQuery] = useState(initialQuery);
  const [environment, setEnvironment] = useState(initialEnvironment);
  const [draft, setDraft] = useState(initialQuery);
  const [draftEnvironment, setDraftEnvironment] = useState(initialEnvironment);
  const [revision, refresh] = useState(0);
  const [validation, setValidation] = useState("");
  const scope = JSON.stringify(["applications", query, environment]);
  const pages = usePages(scope, initialCursor);
  const envPages = usePages("environments", undefined);
  const applications = usePortalRead<PortalPage<PortalApplication>>(queryUrl("/api/portal/applications", { query, environment, cursor: pages.cursor }), revision);
  const environments = usePortalRead<PortalPage<{ environment: string }>>(queryUrl("/api/portal/environments", { cursor: envPages.cursor }), revision);
  const blocked = isUnauthenticated(applications, environments);
  const data = blocked ? undefined : ready(applications);
  const options = blocked ? undefined : ready(environments);
  function apply(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if ([...draft].length > 128 || /[\u0000-\u001f\u007f-\u009f]/u.test(draft)
      || [...draft].some(value => value.codePointAt(0)! >= 0xd800 && value.codePointAt(0)! <= 0xdfff)) {
      setValidation("Use at most 128 characters without control characters."); return;
    }
    setValidation(""); pages.reset(); setQuery(draft); setEnvironment(draftEnvironment);
  }
  function clearFilters() {
    setDraft(""); setDraftEnvironment(""); setValidation("");
    pages.reset(); envPages.reset(); setQuery(""); setEnvironment("");
  }
  return <Shell signedIn={!blocked}><div className="page-heading"><div><h1>Applications</h1><p>Applications reported to Management</p></div>
    <button onClick={() => refresh(value => value + 1)} disabled={applications.busy}>Refresh</button></div>
    <form className="filter-bar" onSubmit={apply}><div className="filter-search"><label htmlFor="application-query">Application name or identity</label>
      <input id="application-query" name="query" value={draft} maxLength={256} onChange={event => setDraft(event.target.value)} placeholder="Search applications" aria-describedby="query-help" aria-invalid={validation !== ""} />
      <small id="query-help">Literal, case-sensitive match</small></div>
      <div><label htmlFor="application-environment">Environment</label><select id="application-environment" value={draftEnvironment} onChange={event => setDraftEnvironment(event.target.value)} disabled={options === undefined}>
        <option value="">All environments</option>{draftEnvironment !== "" && !options?.items.some(item => item.environment === draftEnvironment) ? <option value={draftEnvironment}>{draftEnvironment}</option> : null}
        {options?.items.map(item => <option value={item.environment} key={item.environment}>{item.environment}</option>)}
      </select><small>Authorized environments on this page</small></div><button className="primary filter-submit" type="submit">Apply filters</button>
      <button className="filter-submit" type="button" onClick={clearFilters}>Clear filters</button>
    </form>
    {validation !== "" ? <p role="alert" className="validation">{validation}</p> : null}
    {options?.nextCursor !== undefined || envPages.hasPrevious ? <div className="environment-pages"><Pager pages={envPages} nextCursor={options?.nextCursor} busy={environments.busy} label="Environment choices" /></div> : null}
    {environments.reply !== undefined && environments.reply.state !== "ready" && !blocked ? <ReadNotice state={environments} restart={envPages.reset} /> : null}
    <ReadNotice state={blocked ? (applications.reply?.state === "unauthenticated" ? applications : environments) : applications} restart={pages.reset} />
    {data !== undefined ? <><div className="table-frame"><table><thead><tr><th>Application</th><th>Environment</th><th>Application ID</th></tr></thead><tbody>
      {data.items.map(app => <tr key={app.applicationId}><td><Link className="item-link" href={queryUrl(`/applications/${app.applicationId}`, { fromQuery: query, fromEnvironment: environment, fromCursor: pages.cursor })}>{app.metadata.displayName || app.application}</Link>
        <span className="row-subtitle">{app.application}</span>{app.metadata.description !== undefined ? <span className="row-description">{app.metadata.description}</span> : null}</td>
        <td><Badge>{app.environment}</Badge></td><td className="identifier">{app.applicationId}</td></tr>)}
    </tbody></table>{data.items.length === 0 ? <Empty title="No matching applications">No applications match these filters within your access.</Empty> : null}</div>
      <Pager pages={pages} nextCursor={data.nextCursor} busy={applications.busy} label={`${data.items.length} applications on this page`} /></> : null}
    <p className="footnote">Application identity and environment are retained when you open an application. This is a bounded page, not a fleet total.</p>
  </Shell>;
}
