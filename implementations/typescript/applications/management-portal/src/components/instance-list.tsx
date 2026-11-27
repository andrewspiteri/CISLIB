import type { MouseEvent } from "react";
import type { PortalInstance } from "../portal-model";
import { groupReportedInstances, instancePresence } from "../instance-groups.ts";
import { Badge, Empty, Timestamp, reportedLabel } from "./shared";

export function InstanceGroups({ items, open }: {
  readonly items: readonly PortalInstance[];
  readonly open: (key: string, event: MouseEvent<HTMLButtonElement>) => void;
}) {
  const groups = groupReportedInstances(items);
  return <div className="instance-groups">
    <InstanceSection title="Current instances" items={groups.current} open={open} />
    {groups.current.some(item => item.presence !== "active")
      ? <p className="muted">A missed report does not confirm that the instance has stopped.</p> : null}
    <InstanceSection title="Past instances" items={groups.past} open={open} past />
  </div>;
}

function InstanceSection({ title, items, open, past = false }: {
  readonly title: string; readonly items: readonly PortalInstance[]; readonly past?: boolean;
  readonly open: (key: string, event: MouseEvent<HTMLButtonElement>) => void;
}) {
  return <section aria-label={title} className="instance-section">
    <div className="section-heading"><div><h2>{title}</h2><p>{items.length} reported instances on this page</p></div></div>
    <div className="table-frame"><table><thead><tr><th>Instance</th><th>Reported app version</th><th>Presence</th><th>{past ? "Closed at" : "Last seen"}</th></tr></thead>
      <tbody>{items.map(item => { const presence = instancePresence(item.presence); return <tr key={item.registrationId}>
        <td><button className="item-link" type="button" onClick={event => open(item.registrationId, event)}>{reportedLabel(item.reportedInstanceId, "Reported instance")}</button></td>
        <td>{item.applicationVersion}</td><td><Badge tone={presence.tone}>{presence.label}</Badge></td>
        <td><Timestamp value={past ? item.closedAtUtc : item.lastSeenAtUtc} /></td>
      </tr>; })}</tbody></table>
      {items.length === 0 ? <Empty title={past ? "No past instances on this page" : "No current instances on this page"}>
        {past ? "No explicitly closed retained instances are shown here." : "No unclosed instance reports are shown here."}
      </Empty> : null}
    </div>
  </section>;
}
