"use client";

import { createContext, useContext, useEffect, useState, useSyncExternalStore } from "react";
import type { ReactNode } from "react";
import { PortalSessionCoordinator } from "../portal-session.ts";

const ProtectedContext = createContext<PortalSessionCoordinator | undefined>(undefined);

export function ProtectedPortalProvider({ children }: { children: ReactNode }) {
  const [owner] = useState(() => new PortalSessionCoordinator());
  const snapshot = useSyncExternalStore(owner.subscribe, owner.getSnapshot, owner.getSnapshot);
  useEffect(() => {
    if (snapshot.context === undefined) { return; }
    const remaining = Math.max(0, snapshot.context.expiresAtMs - Date.now());
    const timer = window.setTimeout(() => owner.checkExpiry(), Math.min(remaining, 2147483647));
    return () => window.clearTimeout(timer);
  }, [owner, snapshot.context]);
  useEffect(() => {
    const focus = () => owner.checkExpiry();
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
    window.addEventListener("beforeunload", unload);
    document.addEventListener("click", navigate, true);
    return () => {
      window.removeEventListener("focus", focus);
      window.removeEventListener("beforeunload", unload);
      document.removeEventListener("click", navigate, true);
    };
  }, [owner]);
  return <ProtectedContext.Provider value={owner}>{children}</ProtectedContext.Provider>;
}

export function useProtectedPortal() {
  const owner = useContext(ProtectedContext);
  if (owner === undefined) { throw new Error("A protected portal owner is required."); }
  const snapshot = useSyncExternalStore(owner.subscribe, owner.getSnapshot, owner.getSnapshot);
  return { owner, snapshot };
}
