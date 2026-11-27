"use client";

import { createContext, useContext, useEffect, useRef, useState, useSyncExternalStore } from "react";
import type { ReactNode } from "react";
import { PortalSessionCoordinator } from "../portal-session.ts";
import { portalSessionInvalidationChannel, portalSessionInvalidationMessage, publishPortalSessionInvalidation,
  readPortalSessionCheck } from "../portal-session-check.ts";

const ProtectedContext = createContext<PortalSessionCoordinator | undefined>(undefined);

export function ProtectedPortalProvider({ children }: { children: ReactNode }) {
  const [owner] = useState(() => new PortalSessionCoordinator());
  const snapshot = useSyncExternalStore(owner.subscribe, owner.getSnapshot, owner.getSnapshot);
  const broadcast = useRef<() => void>(() => {});
  useEffect(() => {
    if (snapshot.context === undefined) { return; }
    const remaining = Math.max(0, snapshot.context.expiresAtMs - Date.now());
    const timer = window.setTimeout(() => owner.checkExpiry(), Math.min(remaining, 2147483647));
    return () => window.clearTimeout(timer);
  }, [owner, snapshot.context]);
  useEffect(() => {
    let channel: BroadcastChannel | undefined;
    try { channel = new BroadcastChannel(portalSessionInvalidationChannel); } catch { /* Focus validation remains authoritative. */ }
    const invalidate = () => owner.signOut();
    const message = (event: MessageEvent<unknown>) => { if (event.data === portalSessionInvalidationMessage) { invalidate(); } };
    const storage = (event: StorageEvent) => {
      if (event.key === portalSessionInvalidationChannel && event.newValue === portalSessionInvalidationMessage) { invalidate(); }
    };
    channel?.addEventListener("message", message);
    window.addEventListener("storage", storage);
    broadcast.current = () => publishPortalSessionInvalidation(channel, () => window.localStorage);
    const focus = () => {
      owner.checkExpiry();
      if (document.visibilityState !== "hidden") { void owner.revalidateSession(readPortalSessionCheck); }
    };
    const visible = () => { if (document.visibilityState === "visible") { focus(); } };
    const unload = (event: BeforeUnloadEvent) => {
      if (owner.navigationBlocked) { event.preventDefault(); event.returnValue = ""; }
    };
    const navigate = (event: MouseEvent) => {
      if (!(event.target instanceof Element)) { return; }
      const anchor = event.target.closest("a[href]");
      if (!(anchor instanceof HTMLAnchorElement)) { return; }
      if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey
        || anchor.target !== "" && anchor.target !== "_self" || anchor.hasAttribute("download")) { return; }
      const target = new URL(anchor.href, window.location.href);
      if (target.origin === window.location.origin && target.pathname === "/api/auth/signout") {
        const destination = target.href;
        event.preventDefault();
        broadcast.current();
        owner.signOut();
        window.location.assign(destination);
        return;
      }
      if (target.origin === window.location.origin && target.pathname === window.location.pathname && target.search === window.location.search
        || !owner.navigationBlocked) { return; }
      if (!window.confirm("Discard unsaved changes and leave this page?")) { event.preventDefault(); event.stopPropagation(); }
      else { owner.discardLocalDrafts(); }
    };
    window.addEventListener("focus", focus);
    document.addEventListener("visibilitychange", visible);
    window.addEventListener("beforeunload", unload);
    document.addEventListener("click", navigate, true);
    return () => {
      window.removeEventListener("focus", focus);
      document.removeEventListener("visibilitychange", visible);
      window.removeEventListener("beforeunload", unload);
      document.removeEventListener("click", navigate, true);
      channel?.removeEventListener("message", message);
      channel?.close();
      window.removeEventListener("storage", storage);
      broadcast.current = () => {};
      void owner.cancelSessionCheck();
    };
  }, [owner]);
  useEffect(() => {
    if (snapshot.blocked && snapshot.blockReason === "session") { broadcast.current(); }
  }, [snapshot.blocked, snapshot.blockReason]);
  const masked = snapshot.verification !== undefined;
  return <ProtectedContext.Provider value={owner}>
    <div className="protected-document" data-session-masked={masked ? "true" : undefined} inert={masked}>{children}</div>
    {masked ? <div className="notice session-check-notice" role="status">
      {snapshot.verification === "checking" ? "Checking the signed-in session…" : <>
        <p>The signed-in session could not be verified. Protected content remains hidden.</p>
        <button onClick={() => { void owner.revalidateSession(readPortalSessionCheck); }}>Retry session check</button>
      </>}
    </div> : null}
  </ProtectedContext.Provider>;
}

export function useProtectedPortal() {
  const owner = useContext(ProtectedContext);
  if (owner === undefined) { throw new Error("A protected portal owner is required."); }
  const snapshot = useSyncExternalStore(owner.subscribe, owner.getSnapshot, owner.getSnapshot);
  return { owner, snapshot };
}
