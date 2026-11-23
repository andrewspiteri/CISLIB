import { ManagementClient, knownOriginalCompletion } from "@cislib/management-client";
import type { Application, MetadataResult } from "@cislib/management-client";
import { createFetchTransport } from "@cislib/client-core";
import type { CredentialProvider } from "@cislib/client-core";

// This compile has DOM types and an empty ambient types list, including no Node types.
export function clientForBrowser(baseUrl: string, credential: CredentialProvider, signal: AbortSignal): Promise<readonly Application[]> {
  const client = new ManagementClient({ baseUrl, transport: createFetchTransport() });
  return client.listApplications({ pageSize: 20 }, { signal, credentials: credential }).then(result => result.value?.items ?? []);
}
export function persistenceLabel(result: MetadataResult): string {
  const completion = knownOriginalCompletion(result);
  return completion.isKnown ? completion.value : "unrecognized persistence status";
}
