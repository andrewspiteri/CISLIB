import type { PortalInstance } from "./portal-model.ts";

/** Partition the authorized page; dates, lifecycle text and reported labels cannot prove process exit. */
export function groupReportedInstances(items: readonly PortalInstance[]): {
  readonly current: readonly PortalInstance[]; readonly past: readonly PortalInstance[];
} {
  const current: PortalInstance[] = [], past: PortalInstance[] = [];
  for (const item of items) { (item.presence === "closed" ? past : current).push(item); }
  return Object.freeze({ current: Object.freeze(current), past: Object.freeze(past) });
}

export function instancePresence(presence: string): { readonly label: string; readonly tone: "neutral" | "good" | "warning" } {
  switch (presence) {
    case "active": return { label: "Recently seen", tone: "good" };
    case "stale": return { label: "Not recently seen", tone: "warning" };
    case "closed": return { label: "Closed", tone: "neutral" };
    default: return { label: "Unknown", tone: "neutral" };
  }
}
