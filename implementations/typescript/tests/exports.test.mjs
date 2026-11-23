import test from "node:test";
import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import { applicationView } from "../examples/next-server/dist/projection.js";

test("portable package export graphs have no Node, React or Next import", async () => {
  for (const packageName of ["client-core", "management-client"]) {
    const directory = new URL(`../packages/${packageName}/dist/`, import.meta.url);
    for (const file of await readdir(directory)) {
      if (!file.endsWith(".js") && !file.endsWith(".d.ts")) { continue; }
      const source = await readFile(new URL(file, directory), "utf8");
      assert.doesNotMatch(source, /(?:from\s*|import\s*\()\s*["'](?:node:|react(?:\/|["'])|next(?:\/|["']))/u, file);
    }
    const manifest = JSON.parse(await readFile(new URL(`../packages/${packageName}/package.json`, import.meta.url), "utf8"));
    assert.equal(manifest.type, "module"); assert.ok(manifest.exports["."].types.endsWith(".d.ts"));
    assert.deepEqual(Object.keys(manifest.dependencies ?? {}), packageName === "management-client" ? ["@cislib/client-core"] : []);
  }
});

test("Next server example projects only selected DTO fields", () => {
  const value = applicationView({ applicationId: "11111111-1111-1111-1111-111111111111", application: "private-workload", environment: "private-environment",
    metadata: { displayName: "Friendly", description: "Public description", unexpected: "hidden" }, metadataRevision: "9007199254740993", metadataETag: '"metadata-9007199254740993"',
    unexpected: { credential: "never send", moduleDetails: "never send" } });
  assert.deepEqual(Object.keys(value).sort(), ["description", "etag", "id", "name", "revision"]);
  assert.equal(value.revision, "9007199254740993"); assert.ok(Object.isFrozen(value));
  assert.doesNotMatch(JSON.stringify(value), /private-workload|private-environment|never send|unexpected/u);
});

test("server-only export retains native TLS and exposes no portable ambient Node types", async () => {
  const server = await import("@cislib/management-client/server");
  assert.equal(typeof server.createNodeHttpsTransport, "function");
  assert.equal(typeof server.createNodeManagementClient, "function");
  const declaration = await readFile(new URL("../packages/management-client/server/index.d.ts", import.meta.url), "utf8");
  assert.doesNotMatch(declaration, /node:|NodeJS|Buffer|rejectUnauthorized\??\s*:/u);
  assert.throws(() => server.createNodeHttpsTransport({ origin: "http://example.test/" }), TypeError);
});
