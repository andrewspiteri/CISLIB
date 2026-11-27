import assert from "node:assert/strict";
import test from "node:test";
import { groupReportedInstances, instancePresence } from "../src/instance-groups.ts";

function row(registrationId, presence, extra = {}) {
  return Object.freeze({ registrationId, presence, reportedInstanceId: "same-reported-name", applicationVersion: registrationId, ...extra });
}

test("only native closed presence enters past; age and lifecycle are not process-death proof", () => {
  const active = row("active", "active", { lastSeenAtUtc: "2000-01-01T00:00:00.0000000Z" });
  const stale = row("stale", "stale", { observation: { lifecycle: "Stopped" } });
  const future = row("future", "future-presence", { closedAtUtc: "2026-11-26T00:00:00.0000000Z" });
  const closed = row("closed", "closed");
  const result = groupReportedInstances(Object.freeze([active, closed, stale, future]));
  assert.deepEqual(result.current, [active, stale, future]);
  assert.deepEqual(result.past, [closed]);
  assert.ok(Object.isFrozen(result) && Object.isFrozen(result.current) && Object.isFrozen(result.past));
});

test("duplicate reported labels keep each exact registration and version in native page order", () => {
  const rows = Object.freeze([row("version-a", "active"), row("version-b", "active"), row("version-c", "closed")]);
  const groups = groupReportedInstances(rows);
  assert.deepEqual(groups.current.map(item => item.applicationVersion), ["version-a", "version-b"]);
  assert.equal(groups.past[0], rows[2]);
  assert.deepEqual(groupReportedInstances([]), { current: [], past: [] });
});

test("fresh, stale and unknown observations have distinct human labels without a running guarantee", () => {
  assert.deepEqual(instancePresence("active"), { label: "Recently seen", tone: "good" });
  assert.deepEqual(instancePresence("stale"), { label: "Not recently seen", tone: "warning" });
  assert.deepEqual(instancePresence("closed"), { label: "Closed", tone: "neutral" });
  assert.deepEqual(instancePresence("future-success"), { label: "Unknown", tone: "neutral" });
});
