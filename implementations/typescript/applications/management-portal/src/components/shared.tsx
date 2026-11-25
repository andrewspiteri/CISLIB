"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import * as Dialog from "@radix-ui/react-dialog";
import type { PortalReply } from "../portal-model";

const pagingStorage = "cis.management.portal.pages";
export interface ReadState<T> { readonly url: string | null; readonly busy: boolean; readonly reply?: PortalReply<T> }

export function usePortalRead<T>(url: string | null, revision = 0): ReadState<T> {
  const [state, setState] = useState<ReadState<T>>({ url, busy: url !== null });
  const generation = useRef(0);
  useEffect(() => {
    const id = ++generation.current;
    if (url === null) { setState({ url, busy: false }); return; }
    const controller = new AbortController();
    setState(previous => ({ url, busy: true, ...(previous.url === url && previous.reply !== undefined ? { reply: previous.reply } : {}) }));
    void (async () => {
      let reply: PortalReply<T>;
      try {
        const response = await fetch(url, { signal: controller.signal, credentials: "same-origin", cache: "no-store", redirect: "error" });
        const body: unknown = await response.json();
        if (typeof body !== "object" || body === null || !("state" in body)
          || !["ready", "unauthenticated", "denied", "not-found", "invalid", "unavailable"].includes(String(body.state))) {
          throw new Error("Invalid portal reply.");
        }
        reply = body as PortalReply<T>;
      } catch {
        if (controller.signal.aborted) { return; }
        reply = { state: "unavailable", message: "The service could not be reached. Try Refresh." };
      }
      if (controller.signal.aborted || id !== generation.current) { return; }
      if (reply.state === "unauthenticated") { clearPaging(); }
      setState({ url, busy: false, reply });
    })();
    return () => { ++generation.current; controller.abort(); };
  }, [url, revision]);
  return state.url === url ? state : { url, busy: url !== null };
}

export function ready<T>(state: ReadState<T>): T | undefined { return state.reply?.state === "ready" ? state.reply.value : undefined; }
export function isUnauthenticated(...states: readonly ReadState<unknown>[]): boolean {
  return states.some(state => state.reply?.state === "unauthenticated");
}

export function Shell({ children, signedIn = true }: { children: ReactNode; signedIn?: boolean }) {
  return <div className="portal-shell">
    <aside className="rail"><Link className="brand" href="/applications">CIS<span>Management</span></Link>
      <nav aria-label="Main navigation"><Link href="/applications" className="rail-link" aria-current="page">Applications</Link></nav>
      <span className="rail-footer">Read-only inspection</span>
    </aside>
    <div className="workspace"><header className="topbar"><strong>Management</strong>
      <a className="account-link" href={signedIn ? "/api/auth/signout" : "/api/auth/signin"} onClick={clearPaging}>{signedIn ? "Sign out" : "Sign in"}</a>
    </header><main id="main-content" className="canvas">{children}</main></div>
  </div>;
}

export function ReadNotice({ state, restart }: { state: ReadState<unknown>; restart?: () => void }) {
  const reply = state.reply;
  if (reply === undefined) { return <div className="notice" role="status">Loading…</div>; }
  if (reply.state === "ready") { return state.busy ? <p className="muted" role="status">Refreshing…</p> : null; }
  return <div className="notice" role="alert"><strong>{({ unauthenticated: "Sign in required", denied: "Access unavailable", "not-found": "Item unavailable", invalid: "The list request changed", unavailable: "Service unavailable" })[reply.state]}</strong>
    <p>{reply.message}</p>{reply.state === "unauthenticated" ? <a className="button primary" href="/api/auth/signin">Sign in</a> : null}
    {reply.state === "invalid" && restart !== undefined ? <button onClick={restart}>Start from the first page</button> : null}
  </div>;
}

export function Empty({ title, children }: { title: string; children: ReactNode }) {
  return <div className="empty"><h3>{title}</h3><p>{children}</p></div>;
}
export function Badge({ children, tone = "neutral" }: { children: ReactNode; tone?: "neutral" | "good" | "warning" }) {
  return <span className={`badge ${tone}`}>{children}</span>;
}
export function Facts({ children }: { children: ReactNode }) { return <dl className="facts">{children}</dl>; }
export function Fact({ label, children, wide = false }: { label: string; children: ReactNode; wide?: boolean }) {
  return <div className={wide ? "fact wide" : "fact"}><dt>{label}</dt><dd>{children ?? "Not reported"}</dd></div>;
}
export function Timestamp({ value }: { value: string | undefined }) {
  if (value === undefined) { return <>Not reported</>; }
  return <time dateTime={value}>{new Intl.DateTimeFormat("en-GB", { dateStyle: "medium", timeStyle: "medium", timeZone: "UTC" }).format(new Date(value))} UTC<span className="raw-value">{value}</span></time>;
}

interface PageTrail { readonly current: string | null; readonly previous: readonly (string | null)[] }
function clearPaging() { try { sessionStorage.removeItem(pagingStorage); } catch { /* Browser context storage is optional. */ } }
function readTrail(scope: string, current: string | undefined): readonly (string | undefined)[] {
  try {
    const raw = sessionStorage.getItem(pagingStorage);
    if (raw === null || raw.length > 131072) { return []; }
    const all = JSON.parse(raw) as Record<string, PageTrail>;
    const item = all[scope];
    if (item === undefined || item.current !== (current ?? null) || !Array.isArray(item.previous) || item.previous.length > 32
      || item.previous.some(value => value !== null && (typeof value !== "string" || value.length > 2048 || /[^\x21-\x7e]/u.test(value)))) { return []; }
    return item.previous.map(value => value ?? undefined);
  } catch { return []; }
}
function saveTrail(scope: string, current: string | undefined, previous: readonly (string | undefined)[]) {
  try {
    const raw = sessionStorage.getItem(pagingStorage);
    const existing: unknown = raw !== null && raw.length <= 131072 ? JSON.parse(raw) : {};
    const entries = typeof existing === "object" && existing !== null && !Array.isArray(existing) ? Object.entries(existing).slice(-7) : [];
    const all = Object.fromEntries(entries);
    all[scope] = { current: current ?? null, previous: previous.map(value => value ?? null) };
    const encoded = JSON.stringify(all);
    if (encoded.length <= 131072) { sessionStorage.setItem(pagingStorage, encoded); }
  } catch { /* Paging still works in memory when storage is unavailable. */ }
}

/** Bounded navigation context only; no records, credentials or permission hints are stored. */
export function usePages(scope: string, initial: string | undefined) {
  const [page, setPage] = useState<{ scope: string; cursor: string | undefined; previous: readonly (string | undefined)[] }>({ scope, cursor: initial, previous: [] });
  useEffect(() => {
    setPage(previous => {
      const cursor = previous.scope === scope ? previous.cursor : undefined;
      return { scope, cursor, previous: readTrail(scope, cursor) };
    });
  }, [scope]);
  const value = page.scope === scope ? page : { scope, cursor: undefined, previous: [] };
  function change(cursor: string | undefined, previous: readonly (string | undefined)[]) { saveTrail(scope, cursor, previous); setPage({ scope, cursor, previous }); }
  return { cursor: value.cursor, hasPrevious: value.previous.length > 0,
    next(cursor: string) { change(cursor, [...value.previous.slice(-31), value.cursor]); },
    previous() { if (value.previous.length > 0) { change(value.previous.at(-1), value.previous.slice(0, -1)); } },
    reset() { change(undefined, []); }
  };
}
export function Pager({ pages, nextCursor, busy, label = "Current page", changed }: {
  pages: ReturnType<typeof usePages>; nextCursor: string | undefined; busy: boolean; label?: string; changed?: () => void
}) {
  return <nav className="pager" aria-label={`${label} pagination`}><span>{label}</span>
    <button disabled={busy || !pages.hasPrevious} onClick={() => { changed?.(); pages.previous(); }}>Previous</button>
    <button disabled={busy || nextCursor === undefined} onClick={() => { if (nextCursor !== undefined) { changed?.(); pages.next(nextCursor); } }}>Next</button>
    {pages.cursor !== undefined && !pages.hasPrevious ? <button disabled={busy} onClick={() => { changed?.(); pages.reset(); }}>First page</button> : null}
  </nav>;
}

export function queryUrl(path: string, values: Record<string, string | undefined>) {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(values)) { if (value !== undefined && value !== "") { query.set(key, value); } }
  return path + (query.size === 0 ? "" : `?${query.toString()}`);
}
export function Panel({ open, close, title, eyebrow, subtitle, children, footer, origin, fallback }: {
  open: boolean; close: () => void; title: string; eyebrow: string; subtitle: string; children: ReactNode; footer?: ReactNode;
  origin: HTMLElement | null; fallback: HTMLElement | null
}) {
  return <Dialog.Root open={open} onOpenChange={value => { if (!value) { close(); } }}>
    <Dialog.Portal><Dialog.Overlay className="panel-shade" /><Dialog.Content className="detail-panel" onCloseAutoFocus={event => {
      event.preventDefault(); (origin?.isConnected ? origin : fallback)?.focus();
    }}>
      <header className="panel-header"><span className="eyebrow">{eyebrow}</span><Dialog.Close asChild><button className="close-panel">Close details</button></Dialog.Close>
        <Dialog.Title asChild><h2>{title}</h2></Dialog.Title><Dialog.Description>{subtitle}</Dialog.Description></header>
      <div className="panel-content">{children}</div><footer className="panel-footer">{footer ?? "Read-only details"}</footer>
    </Dialog.Content></Dialog.Portal>
  </Dialog.Root>;
}
