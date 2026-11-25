import test from "node:test";
import assert from "node:assert/strict";
import { ManagementClient, ManagementConfigurationClient, decodeManagement } from "@cislib/management-client";

const applicationId = "11111111-1111-4111-8111-111111111111";
const application = { applicationId, application: "Café Worker", environment: "Prod+Blue", metadata: { displayName: "Café%_ Worker" },
  metadataRevision: "1", metadataETag: '"metadata-1"' };

test("discovery clients preserve literal filters exact environments prefix and caller cancellation", async () => {
  const records = [];
  const controller = new AbortController();
  const client = new ManagementClient({ baseUrl: "https://example.test/platform", transport: {
    async send(request) { records.push(request); return response(request, { items: [application] }); }
  } });
  const result = await client.queryApplications({ query: " Café%_ ", environment: "Prod+Blue" },
    { pageSize: 1, cursor: "original+/" }, { signal: controller.signal });
  assert.equal(result.status, 200);
  const url = new URL(records[0].url);
  assert.equal(url.pathname, "/platform/management/v1/applications");
  assert.equal(url.searchParams.get("query"), " Café%_ ");
  assert.equal(url.searchParams.get("environment"), "Prod+Blue");
  assert.equal(url.searchParams.get("cursor"), "original+/");
  assert.equal(url.searchParams.get("pageSize"), "1");
  assert.equal(records.length, 1);
  for (const query of [{ query: "" }, { query: "x".repeat(129) }, { query: "\ud800" }, { query: "\0" }, { environment: "" }, { environment: "x".repeat(256) }]) {
    await assert.rejects(client.queryApplications(query), TypeError);
  }
  assert.equal(records.length, 1, "Invalid filters must fail before credentials/transport.");
});

test("environment discovery has its declared bounded response shape and compatible additions", async () => {
  const body = { items: [{ environment: "Prod+Blue", future: "retained" }], nextCursor: "opaque" };
  const client = new ManagementClient({ baseUrl: "https://example.test/platform", transport: {
    async send(request) {
      assert.equal(request.url, "https://example.test/platform/management/v1/environments?pageSize=1");
      return response(request, body);
    }
  } });
  assert.deepEqual((await client.listEnvironments({ pageSize: 1 })).value, body);
  assert.throws(() => decodeManagement("EnvironmentPage", '{"items":[{"environment":""}]}', "response"));
  assert.throws(() => decodeManagement("EnvironmentPage", '{"items":[{"environment":"\\ud800"}]}', "response"));
});

test("application definition discovery uses the exact selected application route and existing configuration schema", async () => {
  const client = new ManagementConfigurationClient({ baseUrl: "https://example.test/platform", transport: {
    async send(request) {
      assert.equal(request.url, `https://example.test/platform/management/v1/applications/${applicationId}/configuration-definitions?pageSize=2`);
      return response(request, { items: [] });
    }
  } });
  assert.deepEqual((await client.getApplicationDefinitions(applicationId, { pageSize: 2 })).value, { items: [] });
  await assert.rejects(client.getApplicationDefinitions("../other"), TypeError);
});

function response(request, body) {
  return { status: 200, url: request.url, redirected: false, headers: { "content-type": "application/json" },
    body: new ReadableStream({ start(controller) { controller.enqueue(new TextEncoder().encode(JSON.stringify(body))); controller.close(); } }),
    async dispose() {} };
}
