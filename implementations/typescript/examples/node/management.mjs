import { readFile } from "node:fs/promises";
import { createNodeManagementClient } from "@cislib/management-client/server";

// The owning server supplies these paths. Nothing is installed in a trust store.
const certificate = await readFile(required("CIS_CLIENT_CERTIFICATE_FILE"));
const privateKey = await readFile(required("CIS_CLIENT_PRIVATE_KEY_FILE"));
try {
  const client = createNodeManagementClient({ ca: await readFile(required("CIS_SERVER_CA_FILE")) });
  const result = await client.listApplications({ pageSize: 20 }, {
    credentials: async ({ signal }) => {
      signal.throwIfAborted();
      return { kind: "transport", value: { kind: "mtls", certificate, privateKey } };
    }
  });
  if (result.kind !== "http" || result.status !== 200 || result.value === undefined) {
    throw new Error("The authenticated management read did not complete.");
  }
  // Select fields explicitly. Do not serialize a full SDK result, credential or module details.
  process.stdout.write(JSON.stringify({ applications: result.value.items.map(item => ({
    id: item.applicationId, name: item.metadata.displayName, revision: item.metadataRevision
  })) }) + "\n");
} finally { privateKey.fill(0); }

function required(name) {
  const value = process.env[name];
  if (value === undefined || value.length === 0) { throw new Error("Configure " + name + " for this explicit server example."); }
  return value;
}
