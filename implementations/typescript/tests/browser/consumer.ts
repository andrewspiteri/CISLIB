import { ManagementClient, ManagementConfigurationClient, knownOriginalCompletion, knownConfigurationConvergence } from "@cislib/management-client";
import type { Application, ConfigurationSnapshot, ConfigurationSnapshotTarget, ControlConfirmation, MetadataResult, ManagementConfigurationClientPort } from "@cislib/management-client";
import { createFetchTransport, executeRequest } from "@cislib/client-core";
import type { CredentialProvider, ExecuteRequestOptions } from "@cislib/client-core";

// This compile has DOM types and an empty ambient types list, including no Node types.
export function clientForBrowser(baseUrl: string, credential: CredentialProvider, signal: AbortSignal): Promise<readonly Application[]> {
  const client = new ManagementClient({ baseUrl, transport: createFetchTransport() });
  return client.listApplications({ pageSize: 20 }, { signal, credentials: credential }).then(result => result.value?.items ?? []);
}
export function persistenceLabel(result: MetadataResult): string {
  const completion = knownOriginalCompletion(result);
  return completion.isKnown ? completion.value : "unrecognized persistence status";
}

export async function readControl(baseUrl: string, registrationId: string, attemptId: string,
  credential: CredentialProvider, signal: AbortSignal): Promise<ControlConfirmation | undefined> {
  const client = new ManagementClient({ baseUrl, transport: createFetchTransport(), readRetries: 2 });
  return (await client.getControl(registrationId, attemptId, { signal, credentials: credential, readRetries: 2 })).value;
}

export async function readCentralConfiguration(baseUrl: string, target: ConfigurationSnapshotTarget,
  credential: CredentialProvider, signal: AbortSignal): Promise<ConfigurationSnapshot | undefined> {
  const client: ManagementConfigurationClientPort = new ManagementConfigurationClient({ baseUrl });
  return (await client.getRegistrationConfiguration(target, undefined, { signal, credentials: credential })).value;
}
export function convergenceLabel(value: string): string { return knownConfigurationConvergence(value).isKnown ? value : "unrecognized"; }

// Existing synchronous two-argument decoders/preparation remain assignable to the additive async owner.
export function existingRequest(options: ExecuteRequestOptions<string, never>): Promise<unknown> {
  return executeRequest({ ...options, prepare: () => ({ url: "https://example.test/api", method: "GET", headers: {}, maximumResponseBytes: 1024 }),
    decode: (_response, bytes) => ({ value: new TextDecoder().decode(bytes) }) });
}
