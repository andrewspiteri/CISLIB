import assert from "node:assert/strict";
import test from "node:test";
import { CommandReceiptStore } from "../src/command-receipts.ts";
import { PortalSessionCoordinator } from "../src/portal-session.ts";
import { ProtectedStateOwner } from "../src/protected-state.ts";

test("the protected coordinator retains its sixteen-owner bound and releases a retired slot", () => {
  const receipts = new CommandReceiptStore({ context: undefined, storage: () => undefined });
  const portal = new PortalSessionCoordinator(receipts);
  const unregister = [];
  try {
    for (let index = 0; index < 16; index++) {
      unregister.push(portal.register(new ProtectedStateOwner({ context: undefined, receipts })));
    }
    const next = new ProtectedStateOwner({ context: undefined, receipts });
    assert.throws(() => portal.register(next), /protected portal owner limit/u);
    unregister.pop()();
    unregister.push(portal.register(next));
  } finally {
    for (const release of unregister) { release(); }
  }
});
